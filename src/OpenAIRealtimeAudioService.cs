using System;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NAudio.Wave;
using System.IO;
using System.Text.Json;
using System.Collections.Generic;

namespace UGTLive
{
    public class OpenAIRealtimeAudioServiceWhisper
    {
        private ClientWebSocket? ws;
        private CancellationTokenSource? cts;
        private WaveInEvent? waveIn;
        private readonly List<byte> audioBuffer = new List<byte>();
        private readonly object bufferLock = new object();
        private int chunkSize;

        public void StartRealtimeAudioService(Action<string, string> onResult)
        {
            Stop();
            cts = new CancellationTokenSource();
            Task.Run(() => RunAsync(onResult, cts.Token), cts.Token);
        }

        public void Stop()
        {
            try
            {
                cts?.Cancel();
                if (waveIn != null)
                {
                    waveIn.DataAvailable -= OnDataAvailable;
                    waveIn.StopRecording();
                }
                ws?.Dispose();
            }
            catch { }
            waveIn = null;
            ws = null;
        }

        private async Task RunAsync(Action<string, string> onResult, CancellationToken token)
        {
            string apiKey = ConfigManager.Instance.GetOpenAiRealtimeApiKey();
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                System.Windows.MessageBox.Show(
                    "OpenAI Realtime API key is not set. Please configure it in Settings.",
                    "API Key Missing"
                );
                return;
            }

            ws = new ClientWebSocket();
            ws.Options.SetRequestHeader("Authorization", $"Bearer {apiKey}");
            ws.Options.SetRequestHeader("OpenAI-Beta", "realtime=v1");

            // Establish the websocket connection to the realtime endpoint.  The model query string is required.
            await ws.ConnectAsync(new Uri("wss://api.openai.com/v1/realtime?model=gpt-4o-realtime-preview"), token);

            // ------------------------------------------------------------------
            // 1)   Configure the session for TRANSCRIPTION-ONLY usage.
            //      • Enable Whisper transcription of incoming audio
            //      • Enable server-side VAD so we do not have to manually commit
            //      • Disable automatic response generation (create_response = false)
            // ------------------------------------------------------------------
            var sessionUpdate = new
            {
                type = "session.update",
                session = new
                {
                    // The default input format we are going to stream – 16-bit PCM @ 24 kHz mono
                    input_audio_format = "pcm16",
                    input_audio_transcription = new
                    {
                        model = "whisper-1",   // Whisper is used to get text back
                        //language = "ja" //leaving this blank means "auto detect source language"
                    },

                    turn_detection = new
                    {
                        type = "server_vad",
                        // We only need the VAD to know when to commit, not to auto-respond
                        silence_duration_ms = 400,
                        create_response = false
                    }
                }
            };
            await SendJson(ws, sessionUpdate, token);

            // ------------------------------------------------------------------
            // 2)   Initialise microphone capture
            // ------------------------------------------------------------------
            waveIn = new WaveInEvent();
            waveIn.WaveFormat = new WaveFormat(24000, 16, 1); // 24 kHz, 16-bit mono PCM

            chunkSize = waveIn.WaveFormat.AverageBytesPerSecond / 3;

            audioBuffer.Clear();
            waveIn.DataAvailable += OnDataAvailable;
            waveIn.StartRecording();

            var buffer = new byte[8192];
            var messageBuffer = new MemoryStream();
            var transcriptBuilder = new StringBuilder();

            // ------------------------------------------------------------------
            // 3)   Receive loop – watch for transcription delta/completed events
            // ------------------------------------------------------------------
            while (!token.IsCancellationRequested && ws.State == WebSocketState.Open)
            {
                try
                {
                    messageBuffer.SetLength(0);
                    WebSocketReceiveResult result;
                    do
                    {
                        result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), token);
                        if (result.MessageType == WebSocketMessageType.Close)
                        {
                            await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, string.Empty, token);
                            break;
                        }
                        messageBuffer.Write(buffer, 0, result.Count);
                    } while (!result.EndOfMessage);

                    if (messageBuffer.Length == 0) continue;

                    var json = Encoding.UTF8.GetString(messageBuffer.GetBuffer(), 0, (int)messageBuffer.Length);
                    Log($"Received message: {json}");

                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;

                    // Handle error events first
                    if (root.TryGetProperty("type", out var typeProp) && typeProp.GetString() == "error")
                    {
                        var error = root.GetProperty("error");
                        Log($"API Error: {error.GetProperty("message").GetString()}");
                        continue;
                    }

                    // ------------------------------------------------------------------
                    // conversation.item.input_audio_transcription.delta -> partial transcription
                    // conversation.item.input_audio_transcription.completed -> final transcription
                    // ------------------------------------------------------------------
                    if (root.TryGetProperty("type", out var eventType))
                    {
                        switch (eventType.GetString())
                        {
                            case "conversation.item.input_audio_transcription.delta":
                                {
                                    if (root.TryGetProperty("delta", out var deltaProp))
                                    {
                                        var delta = deltaProp.GetString();
                                        if (!string.IsNullOrEmpty(delta))
                                        {
                                            transcriptBuilder.Append(delta);
                                          //  onResult(transcriptBuilder.ToString(), string.Empty);
                                        }
                                    }
                                    break;
                                }
                            case "conversation.item.input_audio_transcription.completed":
                                {
                                    if (root.TryGetProperty("transcript", out var transcriptProp))
                                    {
                                        var transcript = transcriptProp.GetString() ?? string.Empty;
                                        transcriptBuilder.Clear();

                                        // Perform translation and parse JSON response to extract original and translated text
                                        string translationJson = await TranslateLineAsync(transcript);
                                        string origText = transcript;
                                        string translatedText = string.Empty;
                                        if (!string.IsNullOrEmpty(translationJson))
                                        {
                                            try
                                            {
                                                using var transDoc = JsonDocument.Parse(translationJson);
                                                var root2 = transDoc.RootElement;
                                                if (root2.TryGetProperty("translations", out var translations) &&
                                                    translations.ValueKind == JsonValueKind.Array &&
                                                    translations.GetArrayLength() > 0)
                                                {
                                                    var first = translations[0];
                                                    origText = first.GetProperty("original_text").GetString() ?? origText;
                                                    translatedText = first.GetProperty("translated_text").GetString() ?? string.Empty;
                                                }
                                            }
                                            catch (Exception parseEx)
                                            {
                                                Log($"Translation JSON parse error: {parseEx}");
                                            }
                                        }
                                        onResult(origText, translatedText);
                                    }
                                    break;
                                }
                            default:
                                // Ignore other event types in this transcription-only context
                                break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log($"Error in receive loop: {ex}");
                }
            }

            // ------------------------------------------------------------------
            // 4)   Tidy-up
            // ------------------------------------------------------------------
            waveIn?.StopRecording();
            await FlushRemainingBuffer(token);
            ws?.Dispose();
        }

        private async Task FlushRemainingBuffer(CancellationToken token)
        {
            byte[] leftover;
            lock (bufferLock)
            {
                leftover = audioBuffer.ToArray();
                audioBuffer.Clear();
            }

            if (leftover.Length > 0)
            {
                try
                {
                    string base64 = Convert.ToBase64String(leftover);
                    var audioEvent = new { type = "input_audio_buffer.append", audio = base64 };
                    await SendJson(ws!, audioEvent, token);
                }
                catch (Exception ex)
                {
                    Log($"Error flushing buffer: {ex}");
                }
            }

            var commitEvent = new { type = "input_audio_buffer.commit" };
            await SendJson(ws!, commitEvent, token);
        }

        private void OnDataAvailable(object? sender, WaveInEventArgs e)
        {
            if (e.BytesRecorded <= 0) return;

            lock (bufferLock)
            {
                audioBuffer.AddRange(new ArraySegment<byte>(e.Buffer, 0, e.BytesRecorded));
            }

            Task.Run(async () =>
            {
                while (true)
                {
                    byte[]? chunkData = null;
                    lock (bufferLock)
                    {
                        if (audioBuffer.Count >= chunkSize)
                        {
                            chunkData = audioBuffer.GetRange(0, chunkSize).ToArray();
                            audioBuffer.RemoveRange(0, chunkSize);
                        }
                    }

                    if (chunkData == null) break;

                    try
                    {
                        string base64Audio = Convert.ToBase64String(chunkData);
                        var audioEvent = new { type = "input_audio_buffer.append", audio = base64Audio };
                        await SendJson(ws!, audioEvent, cts!.Token);
                        // No manual commit – server VAD handles committing & turn detection
                    }
                    catch (Exception ex)
                    {
                        Log($"Error sending chunk: {ex}");
                    }
                }
            });
        }

        private async Task SendJson(ClientWebSocket ws, object obj, CancellationToken token)
        {
            try
            {
                if (ws == null || ws.State != WebSocketState.Open || token.IsCancellationRequested)
                    return;

                var json = JsonSerializer.Serialize(obj);
                var bytes = Encoding.UTF8.GetBytes(json);
                await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, token);
            }
            catch (ObjectDisposedException)
            {
                // WebSocket was disposed while attempting to send; ignore as we're shutting down.
            }
            catch (WebSocketException)
            {
                // Socket closed or aborted. Safe to ignore during shutdown.
            }
            catch (Exception ex)
            {
                Log($"SendJson error: {ex.Message}");
            }
        }

        private void Log(string message)
        {
            try
            {
                string logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "openai_audio_log.txt");
                File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
            }
            catch { }
            Console.WriteLine("[OpenAIRealtimeAudioService] " + message);
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
                Log($"Translation error: {ex}");
            }
            return string.Empty;
        }
    }
}
