using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using Whisper.net;
using Whisper.net.Ggml;
using Whisper.net.LibraryLoader;
using System.Text.Json;
using NAudio.CoreAudioApi;
using System.Windows;
using Windows.Globalization;

namespace RSTGameTranslation
{
    /// <summary>
    /// Enum to specify Whisper runtime types
    /// </summary>
    public enum WhisperRuntimeType
    {
        Cpu,
        Cuda,   // NVIDIA GPU
        Vulkan  // AMD/NVIDIA/Intel GPU
    }

    public class localWhisperService
    {
        private WasapiLoopbackCapture? loopbackCapture;
        private BufferedWaveProvider? bufferedProvider;
        private ISampleProvider? processedProvider;
        private WaveFileWriter? debugWriter;
        private WaveFileWriter? debugWriterProcessed;
        int minBytesToProcess = 192000;
        public bool IsRunning => loopbackCapture != null && loopbackCapture.CaptureState == CaptureState.Capturing;
        private string _lastTranslatedText = "";
        private bool forceProcessing = false;
        private WhisperProcessor? processor;
        private WhisperFactory? factory;
        private readonly List<float> audioBuffer = new List<float>();
        private readonly object bufferLock = new object();
        private CancellationTokenSource? _cancellationTokenSource;
        private float SilenceThreshold => ConfigManager.Instance.GetSilenceThreshold();
        private int SilenceDurationMs => ConfigManager.Instance.GetSilenceDurationMs();
        private DateTime lastVoiceDetected = DateTime.Now;
        private bool isSpeaking = false;
        private int MaxBufferSamples => 16000 * ConfigManager.Instance.GetMaxBufferSamples();
        private int voiceFrameCount = 0;
        private const int MinVoiceFrames = 1;
        private static readonly System.Text.RegularExpressions.Regex NoisePattern =
            new System.Text.RegularExpressions.Regex(
                @"^\[.*\]$|^\(.*\)$|^\.{3,}$|^thank|^please|inaudible|blank",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase
            );

        // new fields
        private Task? processingTask;
        private MMDeviceEnumerator? deviceEnumerator;
        // Flag to indicate Stop() is in progress to avoid races with processing task
        private volatile bool _isStopping = false;

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

            TaskScheduler.UnobservedTaskException += (s, e) => Stop();
        }

        private string MapLanguageToWhisper(string language)
        {
            return language.ToLower() switch
            {
                "japanese" or "japan" or "ja" => "ja",
                "english" or "en" => "en",
                "chinese" or "zh" or "ch_sim" => "zh",
                "korean" or "ko" => "ko",
                "vietnamese" or "vi" => "vi",
                "french" or "fr" => "fr",
                "german" or "de" => "de",
                "spanish" or "es" => "es",
                "italian" or "it" => "it",
                "portuguese" or "pt" => "pt",
                "russian" or "ru" => "ru",
                "hindi" or "hi" => "hi",
                "indonesian" or "id" => "id",
                "polish" or "pl" => "pl",
                "arabic" or "ar" => "ar",
                "dutch" or "nl" => "nl",
                "romanian" or "ro" => "ro",
                "persian" or "farsi" or "fa" => "fa",
                "czech" or "cs" => "cs",
                "thai" or "th" or "thailand" => "th",
                "traditional chinese" or "ch_tra" => "zh",
                "croatian" or "hr" => "hr",
                "turkish" or "tr" => "tr",
                _ => language
            };
        }

        public async Task StartServiceAsync(Action<string, string> onResult)
        {
            // Ensure previous run is stopped
            Stop();

            try
            {
                string modelPath = ConfigManager.Instance.GetAudioProcessingModel() + ".bin";
                string fullPath = Path.Combine(ConfigManager.Instance._audioProcessingModelFolderPath, modelPath);

                // Get runtime from config
                string runtimeSetting = ConfigManager.Instance.GetWhisperRuntime();
                WhisperRuntimeType runtime = ParseRuntime(runtimeSetting);
                
                Console.WriteLine($"[Whisper] Loading model: {fullPath}");
                Console.WriteLine($"[Whisper] Configured runtime: {runtimeSetting} -> {runtime}");

                // Create factory with specified runtime
                factory = CreateFactoryWithRuntime(fullPath, runtime);
                
                string current_source_language = MapLanguageToWhisper(ConfigManager.Instance.GetSourceLanguage());

                var processorBuilder = factory.CreateBuilder()
                    .WithLanguage(current_source_language)
                    .WithBeamSearchSamplingStrategy()
                    .ParentBuilder;

                processor = processorBuilder.Build();

                deviceEnumerator = new MMDeviceEnumerator();
                var devices = deviceEnumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);

                Console.WriteLine("=== Available Audio Devices ===");
                foreach (var device in devices)
                {
                    Console.WriteLine($"Device: {device.FriendlyName}");
                }
                var defaultDevice = deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                Console.WriteLine($"Using default device: {defaultDevice.FriendlyName}");

                loopbackCapture = new WasapiLoopbackCapture(defaultDevice);
                // debugWriter = new WaveFileWriter("debug_audio_raw.wav", loopbackCapture.WaveFormat);
                bufferedProvider = new BufferedWaveProvider(loopbackCapture.WaveFormat);
                bufferedProvider.DiscardOnBufferOverflow = true;

                // Build pipeline: Buffered -> Sample -> Resample (16k) -> Mono
                var sampleProvider = bufferedProvider.ToSampleProvider();
                var resampler = new WdlResamplingSampleProvider(sampleProvider, 16000);
                bufferedProvider.BufferDuration = TimeSpan.FromSeconds(60);
                processedProvider = resampler.ToMono();

                // Setup debug writer for 16k 16bit mono
                var targetFormat = new WaveFormat(16000, 16, 1);
                // debugWriterProcessed = new WaveFileWriter("debug_audio_16k.wav", targetFormat);

                loopbackCapture.DataAvailable += OnGameAudioReceived;
                loopbackCapture.StartRecording();

                _cancellationTokenSource = new CancellationTokenSource();
                processingTask = Task.Run(() => ProcessLoop(onResult, _cancellationTokenSource.Token));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"StartServiceAsync failed: {ex.Message}");
                Console.WriteLine($"[Whisper] Stack trace: {ex.StackTrace}");
                try { Stop(); } catch { }
            }
        }

        /// <summary>
        /// Parse runtime string to enum
        /// </summary>
        private WhisperRuntimeType ParseRuntime(string setting)
        {
            return setting?.ToLower() switch
            {
                "cuda" or "nvidia" => WhisperRuntimeType.Cuda,
                "vulkan" or "gpu" => WhisperRuntimeType.Vulkan,
                _ => WhisperRuntimeType.Cpu
            };
        }

        /// <summary>
        /// Create WhisperFactory with specified runtime
        /// Use RuntimeOptions.RuntimeLibraryOrder to select runtime
        /// </summary>
        private WhisperFactory CreateFactoryWithRuntime(string modelPath, WhisperRuntimeType runtime)
        {
            try
            {
                // Reset LoadedLibrary to force Whisper.net to reload with new order
                RuntimeOptions.LoadedLibrary = null;
                
                // Set runtime priority order based on user choice
                // NOTE: Only include runtimes you want to use in the list
                switch (runtime)
                {
                    case WhisperRuntimeType.Cuda:
                        Console.WriteLine("[Whisper] Setting runtime: CUDA only (fallback to CPU if unavailable)");
                        RuntimeOptions.RuntimeLibraryOrder = new List<RuntimeLibrary>
                        {
                            RuntimeLibrary.Cuda,
                            RuntimeLibrary.Cpu
                        };
                        break;

                    case WhisperRuntimeType.Vulkan:
                        Console.WriteLine("[Whisper] Setting runtime: Vulkan only (fallback to CPU if unavailable)");
                        RuntimeOptions.RuntimeLibraryOrder = new List<RuntimeLibrary>
                        {
                            RuntimeLibrary.Vulkan,
                            RuntimeLibrary.Cpu
                        };
                        break;

                    case WhisperRuntimeType.Cpu:
                    default:
                        // CPU only - no GPUs in the list
                        Console.WriteLine("[Whisper] Setting runtime: CPU ONLY (no GPU)");
                        RuntimeOptions.RuntimeLibraryOrder = new List<RuntimeLibrary>
                        {
                            RuntimeLibrary.Cpu,
                            RuntimeLibrary.CpuNoAvx  // Fallback if CPU does not support AVX
                        };
                        break;
                }

                Console.WriteLine($"[Whisper] RuntimeLibraryOrder set to: [{string.Join(", ", RuntimeOptions.RuntimeLibraryOrder)}]");
                Console.WriteLine($"[Whisper] Creating factory with model: {modelPath}");
                
                var factory = WhisperFactory.FromPath(modelPath);
                
                if (RuntimeOptions.LoadedLibrary.HasValue)
                {
                    Console.WriteLine($"[Whisper] ✓ Actually loaded runtime: {RuntimeOptions.LoadedLibrary.Value}");
                }
                else
                {
                    Console.WriteLine("[Whisper] ⚠ LoadedLibrary is null - runtime unknown");
                }
                
                return factory;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Whisper] Error creating factory with {runtime}: {ex.Message}");
                Console.WriteLine($"[Whisper] Stack trace: {ex.StackTrace}");
                
                // Fallback: reset to default and try again
                Console.WriteLine("[Whisper] Falling back to CPU only");
                RuntimeOptions.LoadedLibrary = null;
                RuntimeOptions.RuntimeLibraryOrder = new List<RuntimeLibrary> 
                { 
                    RuntimeLibrary.Cpu, 
                    RuntimeLibrary.CpuNoAvx 
                };
                return WhisperFactory.FromPath(modelPath);
            }
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

                // Cancel processing loop
                try { _cancellationTokenSource?.Cancel(); } catch { }

                // Dispose token source
                try { _cancellationTokenSource?.Dispose(); } catch { }
                _cancellationTokenSource = null;

                // Wait for processingTask to finish (longer timeout)
                try
                {
                    if (processingTask != null && !processingTask.IsCompleted)
                    {
                        processingTask.Wait(3000);
                    }
                }
                catch (AggregateException) { }
                catch (Exception) { }

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

                // Dispose whisper objects (after waiting for processingTask)
                try { processor?.Dispose(); } catch { }
                processor = null;
                try { factory?.Dispose(); } catch { }
                factory = null;

                audioBuffer.Clear();
                _lastTranslatedText = "";
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

        private async Task ProcessLoop(Action<string, string> onResult, CancellationToken cancellationToken)
        {
            float[] readBuffer = new float[8000]; // Max read ~0.5s @ 16kHz
            while (loopbackCapture != null && !cancellationToken.IsCancellationRequested && !_isStopping)
            {
                // 1. Consumer: Read from Resampler & VAD Check
                if (processedProvider != null && bufferedProvider != null && bufferedProvider.BufferedBytes > minBytesToProcess)
                {
                    try
                    {
                        int samplesRead = processedProvider.Read(readBuffer, 0, readBuffer.Length);
                        if (samplesRead > 0)
                        {
                            var newSamples = new float[samplesRead];
                            Array.Copy(readBuffer, newSamples, samplesRead);

                            // Debug Writer (Float -> Short)
                            if (debugWriterProcessed != null)
                            {
                                var byteBuffer = new byte[samplesRead * 2];
                                for (int i = 0; i < samplesRead; i++)
                                {
                                    short s = (short)Math.Max(short.MinValue, Math.Min(short.MaxValue, (newSamples[i] * 32768f)));
                                    BitConverter.GetBytes(s).CopyTo(byteBuffer, i * 2);
                                }
                                debugWriterProcessed.Write(byteBuffer, 0, byteBuffer.Length);
                            }

                            // VAD Logic
                            float maxVol = newSamples.Max(x => Math.Abs(x));
                            if (maxVol > SilenceThreshold)
                            {
                                lastVoiceDetected = DateTime.Now;
                                isSpeaking = true;
                                voiceFrameCount++;
                            }
                            else
                            {
                                if ((DateTime.Now - lastVoiceDetected).TotalMilliseconds > SilenceDurationMs)
                                {
                                    voiceFrameCount = 0;
                                }
                            }

                            // Buffer Accumulation
                            lock (bufferLock)
                            {
                                audioBuffer.AddRange(newSamples);
                                if (audioBuffer.Count > MaxBufferSamples)
                                {
                                    Console.WriteLine($"Warning: Audio buffer exceeded {MaxBufferSamples} samples, forcing cut");
                                    forceProcessing = true;
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error reading audio pipe: {ex.Message}");
                    }
                }

                // 2. Processing Logic
                bool shouldProcess = forceProcessing ||
                                    (isSpeaking && (DateTime.Now - lastVoiceDetected).TotalMilliseconds > SilenceDurationMs);

                // if (isSpeaking) Console.WriteLine($"[DEBUG] Speaking... Buf: {audioBuffer.Count}");

                if (shouldProcess)
                {
                    float[] samplesToProcess;
                    lock (bufferLock)
                    {
                        samplesToProcess = audioBuffer.ToArray();
                        audioBuffer.Clear();
                        forceProcessing = false;
                    }

                    isSpeaking = false; // Reset VAD state

                    if (samplesToProcess.Length > 0)
                    {
                        Console.WriteLine($"[DEBUG] Processing {samplesToProcess.Length} samples ({samplesToProcess.Length / 16000.0:F1}s audio)");
                        if (processor == null || _isStopping)
                        {
                            Console.WriteLine("[DEBUG] Skipping processing because processor is null or stopping");
                        }
                        else
                        {
                            await ProcessAudioAsync(samplesToProcess, onResult, cancellationToken);
                        }
                    }
                    voiceFrameCount = 0;
                }

                try
                {
                    await Task.Delay(20, cancellationToken);
                }
                catch (TaskCanceledException)
                {
                    break;
                }
            }
        }


        private bool IsRepetitiveText(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;

            string[] words = text.Split(new[] { ' ', ',', '.', '!', '?' },
                StringSplitOptions.RemoveEmptyEntries);

            if (words.Length < 3) return false;

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
                if (ratio > 0.4)
                {
                    Console.WriteLine($"[REPETITION] Word repeats {ratio:P0} of text");
                    return true;
                }
            }

            return false;
        }

        private async Task ProcessAudioAsync(float[] samples, Action<string, string> onResult, CancellationToken token)
        {
            try
            {
                if (processor == null || _isStopping)
                {
                    Console.WriteLine("ProcessAudioAsync: processor null or stopping, returning");
                    return;
                }

                await foreach (var result in processor.ProcessAsync(samples).WithCancellation(token))
                {
                    string originalText = result.Text.Trim();

                    if (string.IsNullOrEmpty(originalText) || originalText.Length < 3)
                    {
                        continue;
                    }

                    if (NoisePattern.IsMatch(originalText))
                    {
                        continue;
                    }
                    if (originalText.Length < 5 || originalText.All(c => c == '.'))
                    {
                        continue;
                    }

                    string currentNormal = originalText.ToLower().Replace(".", "").Replace("!", "").Replace("?", "").Trim();
                    string lastNormal = _lastTranslatedText.ToLower().Replace(".", "").Replace("!", "").Replace("?", "").Trim();
                    if (currentNormal == lastNormal || (lastNormal.Contains(currentNormal) && currentNormal.Length > 5))
                    {
                        continue;
                    }
                    _lastTranslatedText = originalText;
                    if (IsRepetitiveText(originalText))
                    {

                        continue;
                    }


                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        Logic.Instance.AddAudioTextObject(originalText);
                        // _ = Logic.Instance.TranslateTextObjectsAsync();
                    });

                    onResult(originalText, "");
                }
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
                Console.WriteLine("Whisper Error: " + ex.Message);
            }
        }
    }
}