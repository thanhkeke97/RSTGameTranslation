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
        private byte[]? resampleBuffer;
        private bool forceProcessing = false;
        private WhisperProcessor? processor;
        private WhisperFactory? factory;
        private readonly List<float> audioBuffer = new List<float>(); // Whisper.net dùng float[]
        private readonly object bufferLock = new object();

        // Cấu hình VAD (Phát hiện giọng nói)
        private const float SilenceThreshold = 0.01f; // Ngưỡng âm thanh (Cần tinh chỉnh tùy Mic)
        private const int SilenceDurationMs = 500;    // Thời gian im lặng để chốt câu (0.5s)
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

            // 1. Tải Model (Chỉ làm 1 lần, hoặc check file tồn tại)
            // Model "Tiny En" (75MB) là vua về tốc độ cho Game.
            string modelPath = "ggml-base.en.bin";
            if (!File.Exists(modelPath))
            {
                using var httpClient = new System.Net.Http.HttpClient();
                using var modelStream = await new WhisperGgmlDownloader(httpClient).GetGgmlModelAsync(GgmlType.TinyEn);
                using var fileWriter = File.OpenWrite(modelPath);
                await modelStream.CopyToAsync(fileWriter);
            }

            // 2. Khởi tạo Whisper Factory
            factory = WhisperFactory.FromPath(modelPath);

            // 3. Tạo Processor (Quan trọng: Tối ưu cho Game)
            processor = factory.CreateBuilder()
                .WithLanguage("en")
                .WithThreads(5)
                .WithBeamSearchSamplingStrategy()
                .ParentBuilder
                .Build();

            // 1. Khởi tạo Loopback Capture
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
            // 2. Tạo bộ đệm để chứa âm thanh gốc từ game
            bufferedProvider = new BufferedWaveProvider(loopbackCapture.WaveFormat);
            bufferedProvider.DiscardOnBufferOverflow = true; // Tránh tràn RAM nếu xử lý chậm

            // 3. Tạo Resampler: Đọc từ bộ đệm -> Ra chuẩn 16kHz Mono
            var targetFormat = new WaveFormat(16000, 16, 1);
            resampler = new MediaFoundationResampler(bufferedProvider, targetFormat);
            resampler.ResamplerQuality = 60; // 60 là đủ tốt cho Voice, max là 60

            // 4. Bắt sự kiện
            loopbackCapture.DataAvailable += OnGameAudioReceived;
            loopbackCapture.StartRecording();


            // 5. Chạy vòng lặp xử lý nền
            _ = Task.Run(() => ProcessLoop(onResult));
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
                // Đẩy dữ liệu thô từ Game vào bộ đệm
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

                // Bây giờ đọc từ Resampler ra để lấy dữ liệu chuẩn 16kHz
                if (resampler != null)
                {
                    // Tính toán lượng dữ liệu cần đọc
                    // Tỷ lệ sample rate: Ví dụ Game 48k -> Whisper 16k (Giảm 3 lần)
                    int estimatedOutputBytes = (e.BytesRecorded / loopbackCapture.WaveFormat.BlockAlign) * 2; // x2 vì 16bit
                    
                    if (resampleBuffer == null || resampleBuffer.Length < estimatedOutputBytes)
                        resampleBuffer = new byte[estimatedOutputBytes * 2]; // Cấp phát dư ra chút

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
                        // for (int i = 1; i < floatBuffer.Length; i++)
                        // {
                        //     floatBuffer[i] = floatBuffer[i] - 0.95f * floatBuffer[i - 1];
                        // }

                        // Check VAD và thêm vào audioBuffer...
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
            loopbackCapture?.StopRecording();
            loopbackCapture?.Dispose();
            loopbackCapture = null;
            bufferedProvider?.ClearBuffer();
            resampler?.Dispose();
            processor?.Dispose();
            factory?.Dispose();
            audioBuffer.Clear();
        }

        // Vòng lặp xử lý (Thay thế cho WebSocket Loop)
        private async Task ProcessLoop(Action<string, string> onResult)
        {
            while (loopbackCapture != null)
            {
                // Kiểm tra cờ force processing TRƯỚC
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
                        forceProcessing = false; // RESET CỜ
                    }
                    
                    isSpeaking = false;

                    if (samplesToProcess.Length > 0)
                    {
                        Console.WriteLine($"[DEBUG] Processing {samplesToProcess.Length} samples ({samplesToProcess.Length / 16000.0:F1}s audio)");
                        
                        // Tính âm lượng trung bình để debug
                        float avgVol = samplesToProcess.Average(x => Math.Abs(x));
                        Console.WriteLine($"[DEBUG] Average volume: {avgVol:F4}");
                        
                        await ProcessAudioAsync(samplesToProcess, onResult);
                    }
                    voiceFrameCount = 0; // Reset
                }

                await Task.Delay(100);
            }
        }

        private async Task ProcessAudioAsync(float[] samples, Action<string, string> onResult)
        {
            try
            {
                await foreach (var result in processor.ProcessAsync(samples))
                {
                    string originalText = result.Text.Trim();
                    
                    // BỎ QUA nếu text quá ngắn hoặc rỗng
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

                    Console.WriteLine($"[DEBUG] Whisper result: {originalText}");

                    // Đẩy text vào Logic để xử lý
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

        // Translate a single line if auto-translate is enabled
        private async Task<string> TranslateLineAsync(string text)
        {
            try
            {
                if (!ConfigManager.Instance.IsAudioServiceAutoTranslateEnabled() || string.IsNullOrEmpty(text))
                    return string.Empty;

                var service = TranslationServiceFactory.CreateService();
                // Prepare minimal JSON with one text block
                var payload = new
                {
                    text_blocks = new[] { new { id = "text_0", text = text } }
                };
                string json = JsonSerializer.Serialize(payload);
                string? response = await service.TranslateAsync(json, string.Empty);
                if (!string.IsNullOrEmpty(response))
                    return response;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Translation error: {ex}");
            }
            return string.Empty;
        }
    }
}
