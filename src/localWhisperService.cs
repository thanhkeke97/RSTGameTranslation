using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using System.Text.Json;
using System.Threading.Channels;
using NAudio.CoreAudioApi;
using System.Windows;

namespace RSTGameTranslation
{
    public class localWhisperService
    {
        // Process loopback (all audio except this app's own TTS), or plain device loopback as fallback
        private IWaveIn? loopbackCapture;
        private BufferedWaveProvider? bufferedProvider;
        private ISampleProvider? processedProvider;
        private WaveFileWriter? debugWriter;
        private WaveFileWriter? debugWriterProcessed;
        // Minimum number of bytes that must sit in bufferedProvider (in *capture* format,
        // e.g. 48kHz/stereo/float) before we try to read one VAD frame out of the resampler.
        // Computed from the real capture format in StartServiceAsync — a hard-coded value is
        // wrong because the capture format varies per device.
        private int minSourceBytesPerFrame = 4096;
        public bool IsRunning => loopbackCapture switch
        {
            ProcessLoopbackCapture p => p.IsCapturing,
            WasapiLoopbackCapture w => w.CaptureState == CaptureState.Capturing,
            _ => false
        };
        // Last few emitted lines, used for exact-duplicate suppression.
        private readonly Queue<string> _recentTexts = new Queue<string>();
        private const int RecentTextHistory = 5;
        private ISpeechRecognitionEngine? _engine;
        // Neural speech detector; null means the RMS threshold is used instead.
        private SileroFrameVad? frameVad;
        private readonly List<float> audioBuffer = new List<float>();
        private readonly object bufferLock = new object();
        private CancellationTokenSource? _cancellationTokenSource;
        private float SilenceThreshold => ConfigManager.Instance.GetSilenceThreshold();
        private int SilenceDurationMs => ConfigManager.Instance.GetSilenceDurationMs();
        private bool isSpeaking = false;
        // Hard ceiling on one segment (samples @16kHz). Whisper's own window is 30s, so we allow
        // long sentences; the config value is clamped up to at least 10s because cutting earlier
        // than that splits ordinary speech mid-sentence.
        private int HardLimitSamples => 16000 * Math.Clamp(ConfigManager.Instance.GetMaxBufferSamples(), 10, 28);
        // Silence threshold in samples (converted from ms) — uses sample count, not wall clock,
        // so it stays accurate even when recognition blocks.
        private int SilenceDurationSamples => 16000 * SilenceDurationMs / 1000;

        // VAD frame = 30ms @16kHz. Small frames give accurate speech/silence boundaries; a 1s
        // frame averages speech and silence into a single RMS value and smears every boundary.
        private const int VadFrameSamples = 480;
        // Consecutive voiced frames required to declare speech. Rejects single-frame noise spikes.
        private const int MinVoiceFrames = 2;
        // Audio kept from *before* speech onset so the first syllable is not clipped.
        private const int PreRollSamples = 16000 * 300 / 1000;
        // Silence kept *after* speech so the last word is not clipped.
        private const int TrailingSilenceSamples = 16000 * 250 / 1000;
        // Never send less speech than this to the engine. Sub-300ms fragments (and pure silence)
        // are the main cause of Whisper hallucinating repeated filler phrases.
        private const int MinVoicedSamples = 16000 * 300 / 1000;
        private int voiceFrameCount = 0;

        // Lines Whisper/SenseVoice emit when fed silence or music rather than speech.
        // NOTE: deliberately does not blanket-drop text starting with "thank"/"please" — that
        // discarded ordinary speech. Only the known full-phrase hallucinations are matched.
        private static readonly System.Text.RegularExpressions.Regex NoisePattern =
            new System.Text.RegularExpressions.Regex(
                @"^\s*[\[\(][^\]\)]*[\]\)]\s*$"                    // [Music], (laughter)
                + @"|^[\s\.\-–—_·、。，,!?！？~♪]*$"                  // punctuation / music-only
                + @"|inaudible|blank_audio"
                + @"|thank(s| you) for watching|please subscribe|subscribe to (my|our) channel"
                + @"|ご視聴ありがとうございました|字幕(by|を提供)"
                + @"|请不吝点赞|订阅 ?转发|打赏支持|明镜与点点",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase
            );

        // new fields
        private Task? processingTask;
        // Single-consumer recognition queue. The capture loop only enqueues, so it never blocks
        // on a slow decode, and exactly one decode runs at a time — concurrent calls into the
        // engine reordered output and produced duplicated lines.
        private Channel<float[]>? segmentChannel;
        private Task? recognizeTask;
        private const int MaxQueuedSegments = 4;
        private MMDeviceEnumerator? deviceEnumerator;
        // Flag to indicate Stop() is in progress to avoid races with processing task
        private volatile bool _isStopping = false;
        // Bumped by every Stop(). A start whose model load finishes after a Stop() (user turned
        // the service off while the model was still loading) sees a changed value and backs out
        // instead of starting capture behind the user's back.
        private int _generation;
        // Serializes StartServiceAsync calls, which now await the model load.
        private readonly SemaphoreSlim _startLock = new SemaphoreSlim(1, 1);

        // Singleton
        private static localWhisperService? instance;
        public static localWhisperService Instance
        {
            get
            {
                if (instance == null)
                {
                    instance = new localWhisperService();
                }
                return instance;
            }
        }

        private localWhisperService()
        {
            AppDomain.CurrentDomain.ProcessExit += (s, e) => Stop();
            try
            {
                if (System.Windows.Application.Current != null)
                {
                    System.Windows.Application.Current.Exit += (s, e) => Stop();
                }
            }
            catch { }
            // NOTE: do not hook TaskScheduler.UnobservedTaskException here. It fires for any
            // faulted task anywhere in the app (e.g. a failed translation request), which
            // silently stopped audio capture while the UI still showed the service as on.
        }

        /// <summary>
        /// Create the speech recognition engine based on the configured provider.
        /// Defaults to Whisper for any legacy/unknown provider value.
        /// Private on purpose: Initialize() loads the full model, so the engine must only ever be
        /// built by StartServiceAsync. Calling this from UI code froze the window on a model load
        /// and leaked the engine, since nothing disposed it.
        /// </summary>
        private ISpeechRecognitionEngine CreateEngine()
        {
            string provider = ConfigManager.Instance.GetAudioProcessingProvider();
            if (provider.Contains("funasr", StringComparison.OrdinalIgnoreCase))
            {
                return new FunAsrSenseVoiceEngine();
            }
            return new WhisperEngine();
        }

        /// <summary>
        /// Load the speech engine and start capturing. Throws if the engine or the audio device
        /// cannot be started, so the caller can tell the user instead of showing the service as
        /// running. Returns without starting if Stop() was called while the model was loading —
        /// check <see cref="IsRunning"/> afterwards.
        /// </summary>
        public async Task StartServiceAsync(Action<string, string> onResult)
        {
            await _startLock.WaitAsync();
            try
            {
                // Ensure previous run is stopped
                Stop();
                int generation = Volatile.Read(ref _generation);

                // Prepare the translation service while the model loads, so the first line does
                // not also pay the connection setup (or, for Ollama, the model load).
                TranslationWarmUp.Start();

                // Loading a model takes seconds (up to ~1 GB for large Whisper / SenseVoice fp32),
                // so it must not run on the caller's (UI) thread.
                ISpeechRecognitionEngine engine = CreateEngine();
                Console.WriteLine($"[Audio] Speech engine: {engine.GetType().Name}");
                try
                {
                    await Task.Run(engine.Initialize);
                }
                catch
                {
                    try { engine.Dispose(); } catch { }
                    throw;
                }

                if (generation != Volatile.Read(ref _generation))
                {
                    Console.WriteLine("[Audio] Service was stopped while the model was loading, discarding engine");
                    try { engine.Dispose(); } catch { }
                    return;
                }
                _engine = engine;

                try
                {
                    await StartCaptureAsync(onResult, engine);
                }
                catch
                {
                    Stop();
                    throw;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"StartServiceAsync failed: {ex.Message}");
                Console.WriteLine($"[Audio] Stack trace: {ex.StackTrace}");
                throw;
            }
            finally
            {
                _startLock.Release();
            }
        }

        /// <summary>
        /// Capture everything the system plays except this process (so our own TTS is not
        /// transcribed back). Falls back to loopback of the default output device when process
        /// loopback is unavailable (Windows older than 10 2004) or disabled in config.
        /// </summary>
        private async Task<IWaveIn> CreateLoopbackCaptureAsync()
        {
            if (ConfigManager.Instance.GetAudioCaptureMode() != "device")
            {
                try
                {
                    var capture = await ProcessLoopbackCapture.CreateExcludingProcessAsync(Environment.ProcessId);
                    Console.WriteLine("[Audio] Capturing all system audio except this app (process loopback)");
                    return capture;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Audio] Process loopback unavailable ({ex.Message}), " +
                                      "falling back to device loopback — TTS output will be captured too");
                }
            }

            deviceEnumerator = new MMDeviceEnumerator();
            var devices = deviceEnumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);

            Console.WriteLine("=== Available Audio Devices ===");
            foreach (var device in devices)
            {
                Console.WriteLine($"Device: {device.FriendlyName}");
            }
            var defaultDevice = deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            Console.WriteLine($"Using default device: {defaultDevice.FriendlyName}");
            return new WasapiLoopbackCapture(defaultDevice);
        }

        private async Task StartCaptureAsync(Action<string, string> onResult, ISpeechRecognitionEngine engine)
        {
            loopbackCapture = await CreateLoopbackCaptureAsync();
            // debugWriter = new WaveFileWriter("debug_audio_raw.wav", loopbackCapture.WaveFormat);
            bufferedProvider = new BufferedWaveProvider(loopbackCapture.WaveFormat);
            bufferedProvider.DiscardOnBufferOverflow = true;
            bufferedProvider.BufferDuration = TimeSpan.FromSeconds(60);
            // CRITICAL: ReadFully defaults to true, which makes Read() pad the request with
            // zeroes whenever the capture buffer is short. That injected fabricated silence
            // into the middle of speech, diluted the VAD's RMS, and fed Whisper shredded
            // audio — the root cause of truncated and endlessly repeated transcriptions.
            bufferedProvider.ReadFully = false;

            // Build pipeline: Buffered -> Sample -> Mono -> Resample (16k)
            // Downmix first: NAudio's ToMono() throws on 5.1/7.1 devices, and resampling one
            // channel instead of six or eight is cheaper.
            var sampleProvider = bufferedProvider.ToSampleProvider();
            var mono = new MultiChannelToMonoSampleProvider(sampleProvider);
            processedProvider = new WdlResamplingSampleProvider(mono, 16000);

            // One 30ms 16kHz frame needs this many bytes of source audio. Reading before that
            // much has arrived yields short frames and a noisier VAD.
            minSourceBytesPerFrame = Math.Max(
                loopbackCapture.WaveFormat.BlockAlign,
                (int)((long)VadFrameSamples * loopbackCapture.WaveFormat.AverageBytesPerSecond / 16000));
            Console.WriteLine($"[Audio] Capture format: {loopbackCapture.WaveFormat}, " +
                              $"{minSourceBytesPerFrame} src bytes per 30ms frame");
            // Silero tells speech apart from the music/SFX under it; RMS is kept as the fallback
            // (vad mode "rms", or the model file is missing).
            frameVad = ConfigManager.Instance.GetAudioVadMode() == "rms"
                ? null
                : SileroFrameVad.TryCreate(ConfigManager.Instance.GetSileroVadThreshold());
            string vadDescription = frameVad != null
                ? $"Silero (threshold={ConfigManager.Instance.GetSileroVadThreshold():F2})"
                : $"RMS (threshold={SilenceThreshold:F4})";
            Console.WriteLine($"[Audio] VAD: {vadDescription}, " +
                              $"pause={SilenceDurationMs}ms, max segment={HardLimitSamples / 16000.0:F0}s " +
                              $"(config {ConfigManager.Instance.GetMaxBufferSamples()}s, clamped to 10-28s)");

            // Setup debug writer for 16k 16bit mono
            var targetFormat = new WaveFormat(16000, 16, 1);
            // debugWriterProcessed = new WaveFileWriter("debug_audio_16k.wav", targetFormat);

            loopbackCapture.DataAvailable += OnGameAudioReceived;
            loopbackCapture.StartRecording();

            _cancellationTokenSource = new CancellationTokenSource();
            var token = _cancellationTokenSource.Token;

            // Bounded queue with a single reader: decoding stays strictly sequential and in
            // order, while the capture loop is never blocked by a slow decode.
            segmentChannel = Channel.CreateBounded<float[]>(new BoundedChannelOptions(MaxQueuedSegments)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = true
            });

            var channel = segmentChannel;
            recognizeTask = Task.Run(() => RecognizeLoop(engine, channel, onResult, token));
            var vad = frameVad;
            processingTask = Task.Run(() => ProcessLoop(vad, token));
        }

        private void OnGameAudioReceived(object? sender, WaveInEventArgs e)
        {
            // Console.WriteLine($"[DEBUG] Audio received: {e.BytesRecorded} bytes");
            if (e.BytesRecorded == 0) return;

            // Write raw debug audio
            debugWriter?.Write(e.Buffer, 0, e.BytesRecorded);

            try
            {
                // Just add to buffer, let the Loop thread handle processing/resampling
                bufferedProvider?.AddSamples(e.Buffer, 0, e.BytesRecorded);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in OnGameAudioReceived: {ex.Message}");
            }
        }

        public void Stop()
        {
            try
            {
                _isStopping = true;
                Interlocked.Increment(ref _generation);

                // Cancel processing loop
                try { _cancellationTokenSource?.Cancel(); } catch { }

                // Signal the recognition consumer that no more segments are coming
                try { segmentChannel?.Writer.TryComplete(); } catch { }

                // Wait for both loops to finish. Must happen before _engine is disposed,
                // otherwise a decode in flight touches a disposed native processor.
                // The recognition loop gets a longer grace period because a decode already
                // running cannot be cancelled mid-call.
                foreach (var (task, timeoutMs) in new[] { (processingTask, 3000), (recognizeTask, 10000) })
                {
                    try
                    {
                        if (task != null && !task.IsCompleted)
                        {
                            task.Wait(timeoutMs);
                        }
                    }
                    catch (AggregateException) { }
                    catch (Exception) { }
                }
                var pendingRecognize = recognizeTask;
                var pendingProcessing = processingTask;
                processingTask = null;
                recognizeTask = null;
                segmentChannel = null;

                var engine = _engine;
                _engine = null;
                var cts = _cancellationTokenSource;
                _cancellationTokenSource = null;

                if (pendingRecognize != null && !pendingRecognize.IsCompleted)
                {
                    // A native decode cannot be interrupted (sherpa-onnx Decode ignores the token,
                    // and a long Whisper segment on CPU can outlast the timeout). Disposing the
                    // engine under it is an access violation, so hand the cleanup to the task.
                    Console.WriteLine("[Audio] A decode is still running, engine will be disposed when it finishes");
                    pendingRecognize.ContinueWith(_ =>
                    {
                        try { engine?.Dispose(); } catch { }
                        try { cts?.Dispose(); } catch { }
                    }, TaskScheduler.Default);
                }
                else
                {
                    try { engine?.Dispose(); } catch { }
                    try { cts?.Dispose(); } catch { }
                }

                // Same rule for the VAD: native, so only dispose once the capture loop is done with it
                var vad = frameVad;
                frameVad = null;
                if (pendingProcessing != null && !pendingProcessing.IsCompleted)
                {
                    pendingProcessing.ContinueWith(_ => vad?.Dispose(), TaskScheduler.Default);
                }
                else
                {
                    vad?.Dispose();
                }

                // Unregister event and stop loopback
                if (loopbackCapture != null)
                {
                    try { loopbackCapture.DataAvailable -= OnGameAudioReceived; } catch { }
                    try { loopbackCapture.StopRecording(); } catch { }
                    try { loopbackCapture.Dispose(); } catch { }
                    loopbackCapture = null;
                }

                // Dispose enumerator
                try { deviceEnumerator?.Dispose(); } catch { }
                deviceEnumerator = null;

                // Dispose writers
                try { debugWriter?.Dispose(); } catch { }
                debugWriter = null;
                try { debugWriterProcessed?.Dispose(); } catch { }
                debugWriterProcessed = null;

                // Clear providers
                try { bufferedProvider?.ClearBuffer(); } catch { }
                bufferedProvider = null;
                processedProvider = null;

                lock (bufferLock)
                {
                    audioBuffer.Clear();
                }
                isSpeaking = false;
                voiceFrameCount = 0;
                _recentTexts.Clear();
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error during Stop(): " + ex.Message);
            }
            finally
            {
                _isStopping = false;
            }
        }

        /// <summary>
        /// Capture/VAD loop. Reads 30ms frames out of the resampler, classifies each frame as
        /// voice or silence, accumulates an utterance, and enqueues it for recognition once the
        /// speaker pauses. Never calls the engine directly, so a slow decode cannot stall capture.
        /// </summary>
        private async Task ProcessLoop(SileroFrameVad? vad, CancellationToken cancellationToken)
        {
            float[] frame = new float[VadFrameSamples];
            // Silence is tracked in samples, not wall-clock time, so the measurement stays correct
            // regardless of how long a decode takes.
            int silenceSamples = 0;
            int framesSinceLog = 0;
            long lastFrameTick = Environment.TickCount64;
            // Speech frames in the current utterance, as judged by the active VAD. Used to drop
            // segments with too little speech without re-running the VAD over the segment.
            int voicedSamples = 0;

            while (loopbackCapture != null && !cancellationToken.IsCancellationRequested && !_isStopping)
            {
                bool cutNow = false;
                bool hardCut = false;

                // 1. Drain everything the resampler currently holds, one VAD frame at a time.
                // ReadFully is false, so Read() returns 0 once the capture buffer runs dry
                // instead of handing back zero-padded silence.
                while (processedProvider != null && bufferedProvider != null
                       && bufferedProvider.BufferedBytes >= minSourceBytesPerFrame
                       && !cancellationToken.IsCancellationRequested && !_isStopping)
                {
                    int samplesRead;
                    try
                    {
                        samplesRead = processedProvider.Read(frame, 0, VadFrameSamples);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error reading audio pipe: {ex.Message}");
                        break;
                    }

                    if (samplesRead <= 0) break;
                    lastFrameTick = Environment.TickCount64;

                    WriteDebugFrame(frame, samplesRead);

                    // RMS over a 30ms window is a meaningful loudness estimate for one frame.
                    double sumSq = 0;
                    for (int i = 0; i < samplesRead; i++)
                    {
                        float v = frame[i];
                        sumSq += v * v;
                    }
                    float rms = (float)Math.Sqrt(sumSq / samplesRead);
                    bool isVoice = vad != null ? vad.IsVoice(frame, samplesRead) : rms > SilenceThreshold;

                    if (isVoice)
                    {
                        voiceFrameCount++;
                        if (voiceFrameCount >= MinVoiceFrames)
                        {
                            // Sustained voice: this is speech, restart the silence window.
                            isSpeaking = true;
                            silenceSamples = 0;
                            voicedSamples += samplesRead;
                        }
                        else
                        {
                            // Possibly just a noise spike — decay rather than reset, so one loud
                            // frame in a pause cannot keep an utterance open forever.
                            silenceSamples = Math.Max(0, silenceSamples - samplesRead);
                        }
                    }
                    else
                    {
                        voiceFrameCount = 0;
                        if (isSpeaking) silenceSamples += samplesRead;
                    }

                    lock (bufferLock)
                    {
                        if (isSpeaking)
                        {
                            audioBuffer.AddRange(new ReadOnlySpan<float>(frame, 0, samplesRead));
                            if (audioBuffer.Count >= HardLimitSamples)
                            {
                                hardCut = true;
                                cutNow = true;
                            }
                        }
                        else
                        {
                            // Before speech starts, keep only a short pre-roll. Previously the
                            // buffer accumulated unbounded silence until the hard limit fired,
                            // which handed Whisper seconds of pure silence to hallucinate over.
                            audioBuffer.AddRange(new ReadOnlySpan<float>(frame, 0, samplesRead));
                            if (audioBuffer.Count > PreRollSamples)
                            {
                                audioBuffer.RemoveRange(0, audioBuffer.Count - PreRollSamples);
                            }
                        }
                    }

                    if (isSpeaking && silenceSamples >= SilenceDurationSamples)
                    {
                        cutNow = true;
                    }

                    // Periodic VAD log so the threshold can be tuned from the console
                    if (++framesSinceLog >= 33 && isSpeaking) // ~1s
                    {
                        framesSinceLog = 0;
                        int bufCount;
                        lock (bufferLock) bufCount = audioBuffer.Count;
                        string vadState = vad != null ? $"silero={(isVoice ? "speech" : "-")}" : $"thr={SilenceThreshold:F4}";
                        Console.WriteLine($"[VAD] rms={rms:F4} {vadState} " +
                                          $"silence={silenceSamples / 16000.0:F2}s/{SilenceDurationMs / 1000.0:F2}s " +
                                          $"buf={bufCount / 16000.0:F1}s");
                    }

                    // Cut immediately rather than draining more audio into this utterance.
                    if (cutNow) break;
                }

                // WASAPI loopback delivers no packets at all while nothing is playing. If the game
                // goes fully quiet right after a line, silenceSamples never advances and the line
                // would sit in the buffer until audio resumes — treat a stalled stream as silence.
                if (!cutNow && isSpeaking && Environment.TickCount64 - lastFrameTick >= SilenceDurationMs)
                {
                    Console.WriteLine("[Audio] Audio stream went idle, closing the pending utterance");
                    cutNow = true;
                }

                // 2. An utterance ended (pause detected) or hit the hard ceiling.
                if (cutNow)
                {
                    float[] segment = ExtractSegment(hardCut, ref silenceSamples);
                    if (segment.Length > 0) EnqueueSegment(segment, voicedSamples);
                    // After a hard cut the retained tail is mid-speech, so it counts as voiced;
                    // after a normal cut the buffer is empty.
                    lock (bufferLock) voicedSamples = audioBuffer.Count;
                }

                try
                {
                    await Task.Delay(5, cancellationToken);
                }
                catch (TaskCanceledException)
                {
                    break;
                }
            }
        }

        private void WriteDebugFrame(float[] frame, int samplesRead)
        {
            if (debugWriterProcessed == null) return;
            var byteBuffer = new byte[samplesRead * 2];
            for (int i = 0; i < samplesRead; i++)
            {
                short s = (short)Math.Max(short.MinValue, Math.Min(short.MaxValue, frame[i] * 32768f));
                BitConverter.GetBytes(s).CopyTo(byteBuffer, i * 2);
            }
            debugWriterProcessed.Write(byteBuffer, 0, byteBuffer.Length);
        }

        /// <summary>
        /// Take the finished utterance out of audioBuffer.
        /// On a normal (pause-triggered) cut the whole buffer is consumed, minus the trailing
        /// silence beyond <see cref="TrailingSilenceSamples"/>.
        /// On a hard cut the speaker is still talking, so the buffer is split at the quietest
        /// point near the end — cutting at an arbitrary sample chopped words in half, which is
        /// what produced half-sentences and made Whisper guess at the fragments.
        /// </summary>
        private float[] ExtractSegment(bool hardCut, ref int silenceSamples)
        {
            float[] segment;
            lock (bufferLock)
            {
                if (hardCut && audioBuffer.Count > 16000 * 2)
                {
                    int split = FindQuietestSplit(audioBuffer);
                    segment = audioBuffer.GetRange(0, split).ToArray();
                    audioBuffer.RemoveRange(0, split);
                    // Speech continues into the retained tail — stay in the speaking state.
                    silenceSamples = 0;
                    Console.WriteLine($"[Audio] Hard limit reached, split at {split / 16000.0:F1}s, " +
                                      $"kept {audioBuffer.Count / 16000.0:F1}s for the next segment");
                }
                else
                {
                    // Drop silence past the small tail we keep so the last word is not clipped.
                    int excessSilence = Math.Max(0, silenceSamples - TrailingSilenceSamples);
                    int keep = Math.Max(0, audioBuffer.Count - excessSilence);
                    segment = audioBuffer.GetRange(0, keep).ToArray();
                    audioBuffer.Clear();
                    isSpeaking = false;
                    silenceSamples = 0;
                    voiceFrameCount = 0;
                }
            }
            return segment;
        }

        /// <summary>
        /// Find the lowest-energy 30ms frame in the last ~2s of the buffer and return its start
        /// index, so a forced split lands in a gap between words rather than inside one.
        /// </summary>
        private static int FindQuietestSplit(List<float> buffer)
        {
            int searchStart = Math.Max(0, buffer.Count - 16000 * 2);
            // Leave at least 0.5s in the buffer so the next segment has some context.
            int searchEnd = buffer.Count - 16000 / 2;
            if (searchEnd - searchStart < VadFrameSamples) return buffer.Count;

            int bestIndex = buffer.Count;
            double bestEnergy = double.MaxValue;
            for (int start = searchStart; start + VadFrameSamples <= searchEnd; start += VadFrameSamples)
            {
                double sumSq = 0;
                for (int i = start; i < start + VadFrameSamples; i++)
                {
                    sumSq += (double)buffer[i] * buffer[i];
                }
                if (sumSq < bestEnergy)
                {
                    bestEnergy = sumSq;
                    bestIndex = start;
                }
            }
            return bestIndex;
        }

        /// <summary>
        /// Hand a finished utterance to the recognition consumer, dropping segments that hold
        /// too little actual speech (they only ever produced hallucinated filler).
        /// </summary>
        private void EnqueueSegment(float[] segment, int voicedSamples)
        {
            if (_engine == null || _isStopping || segmentChannel == null)
            {
                Console.WriteLine("[DEBUG] Skipping processing because engine is null or stopping");
                return;
            }

            int voiced = Math.Min(voicedSamples, segment.Length);
            if (voiced < MinVoicedSamples)
            {
                Console.WriteLine($"[Audio] Discarding {segment.Length / 16000.0:F1}s segment: " +
                                  $"only {voiced / 16000.0:F2}s of speech (min {MinVoicedSamples / 16000.0:F2}s)");
                return;
            }

            Console.WriteLine($"[DEBUG] Queueing {segment.Length} samples " +
                              $"({segment.Length / 16000.0:F1}s audio, {voiced / 16000.0:F1}s speech)");

            // DropOldest: if decoding cannot keep up, prefer recent audio over a growing backlog.
            if (segmentChannel.Reader.Count >= MaxQueuedSegments)
            {
                Console.WriteLine($"[Audio] Warning: recognition is behind ({segmentChannel.Reader.Count} " +
                                  "segments queued), dropping the oldest. Consider a smaller model or GPU runtime.");
            }
            segmentChannel.Writer.TryWrite(segment);
            AudioTiming.Log("1. Segment ready", $"{segment.Length / 16000.0:F1}s audio, {voiced / 16000.0:F1}s speech, " +
                                                $"{segmentChannel.Reader.Count} waiting for recognition");
        }

        /// <summary>
        /// Single consumer of the segment queue: decodes one segment at a time, in order.
        /// Running decodes concurrently (the previous fire-and-forget approach) interleaved
        /// results and raced the duplicate-suppression state, duplicating lines.
        /// The engine and channel are passed in rather than read from fields: a decode still
        /// running after Stop() must keep using its own engine, not one a later start created.
        /// </summary>
        private async Task RecognizeLoop(ISpeechRecognitionEngine engine, Channel<float[]>? channel,
                                         Action<string, string> onResult, CancellationToken cancellationToken)
        {
            if (channel == null) return;

            try
            {
                await foreach (var segment in channel.Reader.ReadAllAsync(cancellationToken))
                {
                    if (_isStopping || cancellationToken.IsCancellationRequested) break;
                    await ProcessAudioAsync(engine, segment, onResult, cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                // expected on Stop()
            }
            catch (Exception ex)
            {
                Console.WriteLine($"RecognizeLoop error: {ex.Message}");
            }
        }


        /// <summary>
        /// Last-resort check for a line that is almost entirely one repeated word.
        /// Only applied to reasonably long lines: the old threshold (any word making up >40% of
        /// a 3-word line) rejected ordinary short speech such as "I know, I know".
        /// </summary>
        private static bool IsRepetitiveText(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;

            string[] words = text.Split(new[] { ' ', ',', '.', '!', '?' },
                StringSplitOptions.RemoveEmptyEntries);

            if (words.Length < 8) return false;

            var wordCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var word in words)
            {
                string normalized = word.ToLower().Trim();
                if (normalized.Length < 2) continue;

                if (!wordCounts.ContainsKey(normalized))
                    wordCounts[normalized] = 0;
                wordCounts[normalized]++;
            }

            int totalWords = words.Length;
            foreach (var count in wordCounts.Values)
            {
                double ratio = (double)count / totalWords;
                if (ratio > 0.5)
                {
                    Console.WriteLine($"[REPETITION] Word repeats {ratio:P0} of text");
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Collapse a stuck decoder loop ("no no no no" / "そうそうそうそう") down to a single
        /// occurrence, keeping the rest of the sentence. Previously such a line was discarded
        /// whole, which lost the surrounding words along with the repetition.
        /// Works on words first, then on raw character n-grams for languages without spaces.
        /// </summary>
        private static string CollapseRepeats(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;

            // Word level: collapse runs of a phrase repeated 3+ times in a row. Covers both a
            // single stuck word ("no no no no") and a stuck phrase ("Get out of here!" x3),
            // which the character pass below cannot reach once the phrase exceeds 12 chars.
            // A run of 2 is normal emphasis ("I know, I know") and is left alone.
            if (text.Contains(' '))
            {
                var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                for (int n = 1; n <= 8 && n * 3 <= words.Length; n++)
                {
                    words = CollapseWordRuns(words, n, 3);
                }
                text = string.Join(' ', words);
            }

            // Character n-gram level, for CJK where there are no word boundaries.
            for (int n = 1; n <= 12; n++)
            {
                // Short n-grams need more repeats before we treat them as a loop, so that
                // legitimate doubling such as "ますます" survives.
                int minRepeats = n <= 2 ? 4 : 3;
                text = CollapseNGramRuns(text, n, minRepeats);
            }

            return text;
        }

        /// <summary>
        /// Collapse consecutive repetitions of an n-word phrase down to one occurrence.
        /// </summary>
        private static string[] CollapseWordRuns(string[] words, int n, int minRepeats)
        {
            var kept = new List<string>(words.Length);
            int i = 0;
            while (i < words.Length)
            {
                int repeats = 1;
                if (i + n * minRepeats <= words.Length)
                {
                    while (i + (repeats + 1) * n <= words.Length &&
                           PhrasesEqual(words, i, i + repeats * n, n))
                    {
                        repeats++;
                    }
                }

                if (repeats >= minRepeats)
                {
                    for (int k = 0; k < n; k++) kept.Add(words[i + k]);
                    i += repeats * n;
                }
                else
                {
                    kept.Add(words[i]);
                    i++;
                }
            }
            return kept.ToArray();
        }

        private static bool PhrasesEqual(string[] words, int a, int b, int n)
        {
            for (int k = 0; k < n; k++)
            {
                if (!string.Equals(TrimWord(words[a + k]), TrimWord(words[b + k]),
                                   StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }
            return true;
        }

        private static string TrimWord(string word) =>
            word.Trim(' ', ',', '.', '!', '?', '"', '\'', ';', ':', '、', '。', '！', '？');

        private static string CollapseNGramRuns(string text, int n, int minRepeats)
        {
            if (text.Length < n * minRepeats) return text;

            var sb = new System.Text.StringBuilder(text.Length);
            int i = 0;
            while (i < text.Length)
            {
                if (i + n * minRepeats <= text.Length)
                {
                    var unit = text.AsSpan(i, n);
                    int repeats = 1;
                    while (i + (repeats + 1) * n <= text.Length &&
                           text.AsSpan(i + repeats * n, n).SequenceEqual(unit))
                    {
                        repeats++;
                    }
                    if (repeats >= minRepeats)
                    {
                        sb.Append(unit);
                        i += repeats * n;
                        continue;
                    }
                }
                sb.Append(text[i]);
                i++;
            }
            return sb.ToString();
        }

        private static string NormalizeForCompare(string text)
        {
            var sb = new System.Text.StringBuilder(text.Length);
            foreach (char c in text)
            {
                if (char.IsLetterOrDigit(c)) sb.Append(char.ToLowerInvariant(c));
            }
            return sb.ToString();
        }

        /// <summary>
        /// True if this line was already emitted recently. Only exact (normalized) matches are
        /// suppressed — the old rule also dropped any line contained in the previous one, which
        /// silently swallowed short follow-up sentences.
        /// </summary>
        private bool IsDuplicate(string text)
        {
            string normalized = NormalizeForCompare(text);
            if (normalized.Length == 0) return true;

            string? last = _recentTexts.Count > 0 ? _recentTexts.Last() : null;

            // Back-to-back identical output is the same audio decoded twice.
            if (last == normalized) return true;

            if (normalized.Length >= 15)
            {
                // Only long lines are matched against the wider history. Short lines such as
                // "はい。" or "Yes." legitimately recur in dialogue, and suppressing every
                // recurrence within the last few utterances silently dropped real speech.
                foreach (var previous in _recentTexts)
                {
                    if (previous == normalized) return true;
                }

                // A long line fully contained in the previous one is a re-emission of that audio.
                if (last != null && last.Length > normalized.Length && last.Contains(normalized))
                {
                    return true;
                }
            }

            _recentTexts.Enqueue(normalized);
            while (_recentTexts.Count > RecentTextHistory) _recentTexts.Dequeue();
            return false;
        }

        private async Task ProcessAudioAsync(ISpeechRecognitionEngine engine, float[] samples,
                                             Action<string, string> onResult, CancellationToken token)
        {
            try
            {
                if (_isStopping)
                {
                    Console.WriteLine("ProcessAudioAsync: stopping, returning");
                    return;
                }

                var decodeTimer = System.Diagnostics.Stopwatch.StartNew();
                IReadOnlyList<string> segments = await engine.RecognizeAsync(samples, token);
                AudioTiming.Log("2. Recognized", $"{decodeTimer.ElapsedMilliseconds} ms for {samples.Length / 16000.0:F1}s audio, " +
                                                 $"{segments.Count} segment(s)");

                // Stopped while decoding: drop the result rather than emit a stale line or touch
                // the duplicate history a new run may already be using.
                if (token.IsCancellationRequested) return;

                var lines = new List<string>();
                foreach (var rawText in segments)
                {
                    if (string.IsNullOrWhiteSpace(rawText)) continue;

                    // Repair a stuck decoder loop instead of throwing the whole line away.
                    string originalText = CollapseRepeats(rawText).Trim();

                    // No length floor in characters: a 5-character minimum discarded ordinary
                    // short lines, which for Japanese/Chinese can be a whole sentence ("はい。").
                    // Require only that something readable is left.
                    if (!originalText.Any(char.IsLetterOrDigit)) continue;

                    if (NoisePattern.IsMatch(originalText)) continue;

                    if (IsRepetitiveText(originalText)) continue;

                    if (IsDuplicate(originalText)) continue;

                    lines.Add(originalText);
                }

                if (lines.Count == 0) return;

                // One utterance -> one line. Whisper can split a sentence into several segments;
                // translation now starts as soon as a line arrives, so emitting them separately
                // would translate the halves of a sentence on their own.
                string utterance = string.Join(" ", lines);
                AudioTiming.Log("3. Sent to translation", AudioTiming.Preview(utterance));
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    Logic.Instance.AddAudioTextObject(utterance);
                });

                onResult(utterance, "");
            }
            catch (OperationCanceledException)
            {
                // expected on cancellation
            }
            catch (ObjectDisposedException)
            {
                Console.WriteLine("Processor disposed during processing");
            }
            catch (InvalidOperationException ex)
            {
                Console.WriteLine("Invalid operation during processing: " + ex.Message);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Speech recognition error: " + ex.Message);
            }
        }
    }
}