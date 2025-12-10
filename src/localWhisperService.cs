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
using System.Text.Json;
using NAudio.CoreAudioApi;
using System.Windows;

namespace RSTGameTranslation
{
    public class localWhisperService
    {
        private WasapiLoopbackCapture? loopbackCapture;
        private BufferedWaveProvider? bufferedProvider;
        private ISampleProvider? processedProvider;
        private WaveFileWriter? debugWriter;
        private WaveFileWriter? debugWriterProcessed;
        public bool IsRunning => loopbackCapture != null && loopbackCapture.CaptureState == CaptureState.Capturing;
        private byte[]? resampleBuffer;
        private bool forceProcessing = false;
        private WhisperProcessor? processor;
        private WhisperFactory? factory;
        private readonly List<float> audioBuffer = new List<float>();
        private readonly object bufferLock = new object();
        private CancellationTokenSource? _cancellationTokenSource;
        private const float SilenceThreshold = 0.005f;
        private const int SilenceDurationMs = 500;
        private DateTime lastVoiceDetected = DateTime.Now;
        private bool isSpeaking = false;
        private const int MaxBufferSamples = 16000 * 10;
        private int voiceFrameCount = 0;
        private const int MinVoiceFrames = 1;
        private static readonly System.Text.RegularExpressions.Regex NoisePattern =
            new System.Text.RegularExpressions.Regex(
                @"^\[.*\]$|^\(.*\)$|^\.{3,}$|^thank|^please|inaudible|blank",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase
            );

        // Singleton
        private static localWhisperService instance;
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

        public async Task StartServiceAsync(Action<string, string> onResult)
        {
            Stop();

            string modelPath = "ggml-small-q5_1.bin";
            // if (!File.Exists(modelPath))
            // {
            //     using var httpClient = new System.Net.Http.HttpClient();
            //     using var modelStream = await new WhisperGgmlDownloader(httpClient).GetGgmlModelAsync(GgmlType.LargeV3Turbo);
            //     using var fileWriter = File.OpenWrite(modelPath);
            //     await modelStream.CopyToAsync(fileWriter);
            // }

            factory = WhisperFactory.FromPath(modelPath);

            processor = factory.CreateBuilder()
                .WithLanguage("en")
                .WithThreads(4)
                .WithBeamSearchSamplingStrategy()
                .ParentBuilder
                .Build();

            var enumerator = new MMDeviceEnumerator();
            var devices = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);

            Console.WriteLine("=== Available Audio Devices ===");
            foreach (var device in devices)
            {
                Console.WriteLine($"Device: {device.FriendlyName}");
            }
            var defaultDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            Console.WriteLine($"Using default device: {defaultDevice.FriendlyName}");

            loopbackCapture = new WasapiLoopbackCapture(defaultDevice);
            debugWriter = new WaveFileWriter("debug_audio_raw.wav", loopbackCapture.WaveFormat);
            bufferedProvider = new BufferedWaveProvider(loopbackCapture.WaveFormat);
            bufferedProvider.DiscardOnBufferOverflow = true;

            // Build pipeline: Buffered -> Sample -> Resample (16k) -> Mono
            var sampleProvider = bufferedProvider.ToSampleProvider();
            var resampler = new WdlResamplingSampleProvider(sampleProvider, 16000);
            processedProvider = resampler.ToMono();

            // Setup debug writer for 16k 16bit mono
            var targetFormat = new WaveFormat(16000, 16, 1);
            debugWriterProcessed = new WaveFileWriter("debug_audio_16k.wav", targetFormat);

            loopbackCapture.DataAvailable += OnGameAudioReceived;
            loopbackCapture.StartRecording();

            _cancellationTokenSource = new CancellationTokenSource();
            _ = Task.Run(() => ProcessLoop(onResult, _cancellationTokenSource.Token));
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
            // Cancel the processing loop first
            _cancellationTokenSource?.Cancel();
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;

            loopbackCapture?.StopRecording();
            loopbackCapture?.Dispose();
            loopbackCapture = null;
            debugWriter?.Dispose();
            debugWriter = null;
            debugWriterProcessed?.Dispose();
            debugWriterProcessed = null;
            debugWriterProcessed = null;
            bufferedProvider?.ClearBuffer();
            // resampler.Dispose() handled by GC or if we assign it to a field. 
            // ISampleProvider doesn't need Dispose usually unless it wraps something that does.
            processedProvider = null;
            processor?.Dispose();
            factory?.Dispose();
            audioBuffer.Clear();
        }

        private async Task ProcessLoop(Action<string, string> onResult, CancellationToken cancellationToken)
        {
            float[] readBuffer = new float[8000]; // Max read ~0.5s @ 16kHz
            while (loopbackCapture != null && !cancellationToken.IsCancellationRequested)
            {
                // 1. Consumer: Read from Resampler & VAD Check
                if (processedProvider != null && bufferedProvider != null)
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
                        await ProcessAudioAsync(samplesToProcess, onResult);
                    }
                    voiceFrameCount = 0;
                }

                try
                {
                    await Task.Delay(100, cancellationToken);
                }
                catch (TaskCanceledException)
                {
                    break;
                }
            }
        }

        /// <summary>
        /// Kiểm tra text có bị lặp lại không (ví dụ: "hello hello hello")
        /// </summary>
        private bool IsRepetitiveText(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;

            // Split thành các từ
            string[] words = text.Split(new[] { ' ', ',', '.', '!', '?' },
                StringSplitOptions.RemoveEmptyEntries);

            if (words.Length < 3) return false; // Quá ngắn, không check

            // Đếm từ lặp lại
            var wordCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var word in words)
            {
                string normalized = word.ToLower().Trim();
                if (normalized.Length < 2) continue; // Bỏ qua từ quá ngắn

                if (!wordCounts.ContainsKey(normalized))
                    wordCounts[normalized] = 0;
                wordCounts[normalized]++;
            }

            // Kiểm tra nếu có từ nào lặp > 40% tổng số từ
            int totalWords = words.Length;
            foreach (var count in wordCounts.Values)
            {
                double ratio = (double)count / totalWords;
                if (ratio > 0.4) // Lặp quá 40%
                {
                    Console.WriteLine($"[REPETITION] Word repeats {ratio:P0} of text");
                    return true;
                }
            }

            return false;
        }

        private async Task ProcessAudioAsync(float[] samples, Action<string, string> onResult)
        {
            try
            {
                await foreach (var result in processor.ProcessAsync(samples))
                {
                    string originalText = result.Text.Trim();

                    if (string.IsNullOrEmpty(originalText) || originalText.Length < 3)
                    {
                        Console.WriteLine($"[DEBUG] Skipped short/empty result: '{originalText}'");
                        continue;
                    }

                    if (NoisePattern.IsMatch(originalText))
                    {
                        Console.WriteLine($"[DEBUG] Skipped noise/hallucination: '{originalText}'");
                        continue;
                    }
                    if (originalText.Length < 5 || originalText.All(c => c == '.'))
                    {
                        Console.WriteLine($"[DEBUG] Skipped suspicious text: '{originalText}'");
                        continue;
                    }

                    if (IsRepetitiveText(originalText))
                    {
                        Console.WriteLine($"[DEBUG] Skipped repetitive text: '{originalText}'");
                        continue;
                    }

                    Console.WriteLine($"[DEBUG] Whisper result: {originalText}");

                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        Logic.Instance.AddAudioTextObject(originalText);
                        _ = Logic.Instance.TranslateTextObjectsAsync();
                    });

                    onResult(originalText, "");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Whisper Error: " + ex.Message);
            }
        }
    }
}
