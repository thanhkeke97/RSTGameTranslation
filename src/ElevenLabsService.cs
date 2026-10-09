using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Threading;
using NAudio.Wave;

namespace RSTGameTranslation
{
    public class ElevenLabsService
    {
        private static ElevenLabsService? _instance;
        private readonly HttpClient _httpClient;
        private readonly string _baseUrl = "https://api.elevenlabs.io/v1";
        
        // Dictionary of default voices with their IDs
        public static readonly Dictionary<string, string> DefaultVoices = new Dictionary<string, string>
        {
            { "Rachel", "21m00Tcm4TlvDq8ikWAM" },
            { "Domi", "AZnzlk1XvdvUeBnXmlld" },
            { "Bella", "EXAVITQu4vr4xnSDxMaL" },
            { "Antoni", "ErXwobaYiN019PkySvjV" },
            { "Elli", "MF3mGyEYCl7XYWbV9V6O" },
            { "Josh", "TxGEqnHWrfWFTfGW9XjX" },
            { "Arnold", "VR6AewLTigWG4xSOukaG" },
            { "Adam", "pNInz6obpgDQGcFmaJgB" },
            { "Sam", "yoZ06aMxZJJ28mfd3POQ" },
            { "MC Anh Đức", "XBDAUT8ybuJTTCoOLSUj" },
            { "Mc Hà My - Vietnames - calm and kind", "RmcV9cAq1TByxNSgbii7" }
        };
        
        // Semaphore to ensure only one speech request is processed at a time
        private static readonly SemaphoreSlim _speechSemaphore = new SemaphoreSlim(1, 1);

        // Clips play from memory, in order; SpeakText returns once its clip is queued
        private static readonly TtsPlaybackQueue _playback = new TtsPlaybackQueue("ElevenLabs");

        public static ElevenLabsService Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new ElevenLabsService();
                }
                return _instance;
            }
        }
        
        private ElevenLabsService()
        {
            _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        }
        
        public async Task<bool> SpeakText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                Console.WriteLine("Cannot speak empty text");
                return false;
            }

            // Wait for an in-flight request instead of dropping this one: with WaitAsync(0), a
            // manual "Speak" during auto speech was silently skipped and reported as an API error.
            await _speechSemaphore.WaitAsync();
            try
            {
                string apiKey = ConfigManager.Instance.GetElevenLabsApiKey();
                if (string.IsNullOrWhiteSpace(apiKey))
                {
                    TtsErrorNotifier.ShowError("ElevenLabs", "ElevenLabs API key is not set. Please configure it in Settings.", "API Key Missing");
                    return false;
                }

                // Ensure we have a valid voice ID (allow custom voice IDs)
                string voice = ConfigManager.Instance.GetElevenLabsVoice();
                if (string.IsNullOrWhiteSpace(voice))
                {
                    // Use Rachel as default only if no voice is configured
                    voice = DefaultVoices["Rachel"];
                }

                // Get model from config (defaults to eleven_flash_v2_5)
                string model = ConfigManager.Instance.GetElevenLabsModel();

                var requestData = new
                {
                    text = text,
                    model_id = model,
                    voice_settings = new
                    {
                        stability = 0.5,
                        similarity_boost = 0.75,
                        speed = 1.2
                    }
                };

                using var request = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/text-to-speech/{voice}")
                {
                    Content = new StringContent(JsonSerializer.Serialize(requestData), Encoding.UTF8, "application/json")
                };
                // Per request rather than on DefaultRequestHeaders, which is not safe to mutate
                // while another request is using the client
                request.Headers.Add("xi-api-key", apiKey);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("audio/mpeg"));

                Console.WriteLine($"Sending TTS request to ElevenLabs for text: {text.Substring(0, Math.Min(50, text.Length))}...");
                using HttpResponseMessage response = await _httpClient.SendAsync(request);

                if (!response.IsSuccessStatusCode)
                {
                    string errorContent = await response.Content.ReadAsStringAsync();
                    TtsErrorNotifier.ShowError("ElevenLabs", $"ElevenLabs request failed: {(int)response.StatusCode} {response.StatusCode}\n\n{errorContent}");
                    return false;
                }

                string contentType = response.Content.Headers.ContentType?.MediaType ?? "audio/mpeg";
                byte[] audio = await response.Content.ReadAsByteArrayAsync();
                Console.WriteLine($"ElevenLabs: received {audio.Length} bytes ({contentType}), queued for playback");

                _playback.Enqueue(() => OpenClip(audio, contentType));
                return true;
            }
            catch (Exception ex)
            {
                TtsErrorNotifier.ShowError("ElevenLabs", $"Error with ElevenLabs Text-to-Speech: {ex.Message}");
                return false;
            }
            finally
            {
                _speechSemaphore.Release();
            }
        }

        private static WaveStream OpenClip(byte[] audio, string contentType)
        {
            var stream = new MemoryStream(audio, writable: false);
            if (contentType.Contains("wav")) return new WaveFileReader(stream);
            if (contentType.Contains("mpeg") || contentType.Contains("mp3")) return new Mp3FileReader(stream);
            return new StreamMediaFoundationReader(stream);
        }

        public static void StopAllTTS()
        {
            try
            {
                Console.WriteLine("Stopping all ElevenLabs TTS activities");
                _playback.Stop();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error stopping ElevenLabs TTS: {ex.Message}");
            }
        }
    }
}
