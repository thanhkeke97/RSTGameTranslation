using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NAudio.Wave;
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
        private MediaFoundationResampler? resampler;
        public bool IsRunning => loopbackCapture != null && loopbackCapture.CaptureState == CaptureState.Capturing;
        private byte[]? resampleBuffer;
        private bool forceProcessing = false;
        private WhisperProcessor? processor;
        private WhisperFactory? factory;
        private readonly List<float> audioBuffer = new List<float>();
        private readonly object bufferLock = new object();
        private CancellationTokenSource? _cancellationTokenSource;
        private const float SilenceThreshold = 0.01f;
        private const int SilenceDurationMs = 200;
        private DateTime lastVoiceDetected = DateTime.Now;
        private bool isSpeaking = false;
        private const int MaxBufferSamples = 16000 * 5;
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
            bufferedProvider = new BufferedWaveProvider(loopbackCapture.WaveFormat);
            bufferedProvider.DiscardOnBufferOverflow = true;

            var targetFormat = new WaveFormat(16000, 16, 1);
            resampler = new MediaFoundationResampler(bufferedProvider, targetFormat);
            resampler.ResamplerQuality = 60;

            loopbackCapture.DataAvailable += OnGameAudioReceived;
            loopbackCapture.StartRecording();

            _cancellationTokenSource = new CancellationTokenSource();
            _ = Task.Run(() => ProcessLoop(onResult, _cancellationTokenSource.Token));
        }


        private void OnGameAudioReceived(object? sender, WaveInEventArgs e)
        {
            Console.WriteLine($"[DEBUG] Audio received: {e.BytesRecorded} bytes");
            if (e.BytesRecorded == 0)
            {
                Console.WriteLine("[WARNING] e.BytesRecorded is 0, skipping");
                return;
            }
            try
            {
                bufferedProvider?.AddSamples(e.Buffer, 0, e.BytesRecorded);
                float testMaxVol = 0;
                for (int i = 0; i < e.BytesRecorded / 2; i++)
                {
                    short sample = BitConverter.ToInt16(e.Buffer, i * 2);
                    float sampleFloat = Math.Abs(sample / 32768f);
                    if (sampleFloat > testMaxVol)
                        testMaxVol = sampleFloat;
                }
                Console.WriteLine($"[DEBUG] Input buffer max volume: {testMaxVol:F4}");

                if (resampler != null)
                {
                    int estimatedOutputBytes = (e.BytesRecorded / loopbackCapture.WaveFormat.BlockAlign) * 2;

                    if (resampleBuffer == null || resampleBuffer.Length < estimatedOutputBytes)
                        resampleBuffer = new byte[estimatedOutputBytes * 2];

                    int bytesRead = resampler.Read(resampleBuffer, 0, resampleBuffer.Length);
                    Console.WriteLine($"[DEBUG] Resampler read {bytesRead} bytes from {resampleBuffer.Length} buffer");

                    if (bytesRead > 0)
                    {

                        float resampleMaxVol = 0;
                        for (int i = 0; i < bytesRead / 2; i++)
                        {
                            short sample = BitConverter.ToInt16(resampleBuffer, i * 2);
                            float sampleFloat = Math.Abs(sample / 32768f);
                            if (sampleFloat > resampleMaxVol)
                                resampleMaxVol = sampleFloat;
                        }
                        Console.WriteLine($"[DEBUG] Resampler output max volume: {resampleMaxVol:F4}");
                        // Convert byte[] (16kHz) -> float[] cho Whisper
                        var floatBuffer = new float[bytesRead / 2];
                        for (int i = 0; i < floatBuffer.Length; i++)
                        {
                            short sample = BitConverter.ToInt16(resampleBuffer, i * 2);
                            floatBuffer[i] = sample / 32768f;
                        }
                        float maxVol = floatBuffer.Max(x => Math.Abs(x));
                        Console.WriteLine($"[DEBUG] Max volume: {maxVol:F4}, Threshold: {SilenceThreshold}");
                        if (maxVol > SilenceThreshold)
                        {
                            lastVoiceDetected = DateTime.Now;
                            isSpeaking = true;
                            voiceFrameCount++; // Tăng đếm
                            Console.WriteLine($"[DEBUG] Voice frame count: {voiceFrameCount}");
                        }
                        else
                        {
                            if ((DateTime.Now - lastVoiceDetected).TotalMilliseconds > SilenceDurationMs)
                            {
                                voiceFrameCount = 0;
                            }
                        }

                        lock (bufferLock)
                        {
                            audioBuffer.AddRange(floatBuffer);
                            if (audioBuffer.Count > MaxBufferSamples)
                            {
                                Console.WriteLine($"Warning: Audio buffer exceeded {MaxBufferSamples} samples, forcing cut");
                                forceProcessing = true; // SET CỜ
                                isSpeaking = false;
                                lastVoiceDetected = DateTime.Now.AddSeconds(-10);
                            }
                        }
                    }
                }
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
            bufferedProvider?.ClearBuffer();
            resampler?.Dispose();
            processor?.Dispose();
            factory?.Dispose();
            audioBuffer.Clear();
        }

        private async Task ProcessLoop(Action<string, string> onResult, CancellationToken cancellationToken)
        {
            while (loopbackCapture != null && !cancellationToken.IsCancellationRequested)
            {
                bool shouldProcess = forceProcessing ||
                                    (isSpeaking && (DateTime.Now - lastVoiceDetected).TotalMilliseconds > SilenceDurationMs);
                Console.WriteLine($"[DEBUG] shouldProcess={shouldProcess}, forceProcessing={forceProcessing}, isSpeaking={isSpeaking}, voiceFrameCount={voiceFrameCount}");
                if (isSpeaking)
                {
                    double silenceDuration = (DateTime.Now - lastVoiceDetected).TotalMilliseconds;
                    Console.WriteLine($"[DEBUG] isSpeaking=true, silence={silenceDuration:F0}ms, voiceFrames={voiceFrameCount}, bufferSize={audioBuffer.Count}");
                }

                if (shouldProcess)
                {
                    float[] samplesToProcess;
                    lock (bufferLock)
                    {
                        samplesToProcess = audioBuffer.ToArray();
                        audioBuffer.Clear();
                        forceProcessing = false;
                    }

                    isSpeaking = false;

                    if (samplesToProcess.Length > 0)
                    {
                        Console.WriteLine($"[DEBUG] Processing {samplesToProcess.Length} samples ({samplesToProcess.Length / 16000.0:F1}s audio)");

                        float avgVol = samplesToProcess.Average(x => Math.Abs(x));
                        Console.WriteLine($"[DEBUG] Average volume: {avgVol:F4}");

                        await ProcessAudioAsync(samplesToProcess, onResult);
                    }
                    voiceFrameCount = 0; // Reset
                }

                try
                {
                    await Task.Delay(100, cancellationToken);
                }
                catch (TaskCanceledException)
                {
                    // Expected when stopping
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
