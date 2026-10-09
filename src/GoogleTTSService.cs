using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Threading;
using System.Collections.Generic;
using NAudio.Wave;

namespace RSTGameTranslation
{
    public class GoogleTTSService
    {
        private static GoogleTTSService? _instance;
        private readonly HttpClient _httpClient;
        private readonly string _baseUrl = "https://texttospeech.googleapis.com/v1/text:synthesize";

        // Dictionary of available voices with their IDs
        // Based on official Google Cloud TTS documentation for most advanced voices by language
        public static readonly Dictionary<string, string> AvailableVoices = new Dictionary<string, string>
        {
            // Japanese - Neural2 voices
            { "Japanese (Female) - Neural2", "ja-JP-Neural2-B" },
            { "Japanese (Male) - Neural2", "ja-JP-Neural2-C" },
            { "Japanese (Male Deep) - Neural2", "ja-JP-Neural2-D" },
            
            // English - Studio voices (highest quality)
            { "English US (Female) - Studio", "en-US-Studio-O" },
            { "English US (Male) - Studio", "en-US-Studio-M" },
            { "English UK (Female) - Neural2", "en-GB-Neural2-F" },
            
            // French - Neural2 voices
            { "French (Female) - Neural2", "fr-FR-Neural2-A" },
            { "French (Male) - Neural2", "fr-FR-Neural2-D" },
            
            // German - Neural2 voices
            { "German (Female) - Neural2", "de-DE-Neural2-F" },
            { "German (Male) - Neural2", "de-DE-Neural2-B" },
            
            // Spanish - Neural2 voices
            { "Spanish (Female) - Neural2", "es-ES-Neural2-F" },
            { "Spanish (Male) - Neural2", "es-ES-Neural2-C" },
            
            // Korean - Neural2 voices
            { "Korean (Female) - Neural2", "ko-KR-Neural2-A" },
            { "Korean (Male) - Neural2", "ko-KR-Neural2-C" },
            
            // Chinese - Neural2 voices
            { "Chinese (Female) - Neural2", "cmn-CN-Neural2-A" },
            { "Chinese (Male) - Neural2", "cmn-CN-Neural2-C" },

            // Vietnamese - Neural2 voices
            { "Vietnamese (Female) - Neural2", "vi-VN-Neural2-A" },
            { "Vietnamese (Male) - Neural2", "vi-VN-Neural2-D" }
        };
        
        // Semaphore to ensure only one speech request is processed at a time
        private static readonly SemaphoreSlim _speechSemaphore = new SemaphoreSlim(1, 1);

        // Clips play from memory, in order; SpeakText returns once its clip is queued
        private static readonly TtsPlaybackQueue _playback = new TtsPlaybackQueue("Google TTS");

        public static GoogleTTSService Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new GoogleTTSService();
                }
                return _instance;
            }
        }

        private GoogleTTSService()
        {
            _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        }

        public async Task<bool> SpeakText(string text)
        {
            // Wait for an in-flight request instead of dropping this one: with WaitAsync(0), a
            // manual "Speak" during auto speech was silently skipped and reported as an API error.
            await _speechSemaphore.WaitAsync();
            
            try
            {
                if (string.IsNullOrWhiteSpace(text))
                {
                    Console.WriteLine("Cannot speak empty text");
                    return false;
                }
                
                // Process text to reduce pauses between lines
                string processedText = ProcessTextForSpeech(text);
                
                // Get API key and other settings from config
                string apiKey = ConfigManager.Instance.GetGoogleTtsApiKey();
                if (string.IsNullOrWhiteSpace(apiKey))
                {
                    TtsErrorNotifier.ShowError("Google", "Google Cloud API key is not set. Please configure it in Settings.", "API Key Missing");
                    return false;
                }
                
                // Get voice ID
                string voice = ConfigManager.Instance.GetGoogleTtsVoice();
                
                // Ensure we have a valid voice ID
                if (string.IsNullOrWhiteSpace(voice) || !AvailableVoices.ContainsValue(voice))
                {
                    // Use a default voice if not found
                    voice = AvailableVoices["Japanese (Female) - Neural2"];
                }
                
                // Extract the language code from the voice ID (everything before the first dash and what follows)
                // Example: "en-US-Studio-O" → "en-US"
                string languageCode = "ja-JP"; // Default
                
                if (!string.IsNullOrEmpty(voice))
                {
                    // Find the first occurrence of "-Neural2" or "-Studio" or "-Standard"
                    int dashIndex = voice.IndexOf("-Neural2");
                    if (dashIndex == -1) dashIndex = voice.IndexOf("-Studio");
                    if (dashIndex == -1) dashIndex = voice.IndexOf("-Standard");
                    
                    // If found, extract the language code
                    if (dashIndex > 0)
                    {
                        languageCode = voice.Substring(0, dashIndex);
                    }
                }
                
                byte[]? audio = await SynthesizeAsync(processedText, voice, languageCode, apiKey, TtsPlaybackQueue.GetAutoSpeedFactor());
                if (audio == null || audio.Length == 0)
                {
                    Console.WriteLine("Failed to synthesize speech");
                    return false;
                }

                _playback.Enqueue(() => new Mp3FileReader(new MemoryStream(audio, writable: false)));
                return true;
            }
            catch (Exception ex)
            {
                TtsErrorNotifier.ShowError("Google", $"Error with Google Text-to-Speech: {ex.Message}");
                
                return false;
            }
            finally
            {
                // Always release the semaphore when done
                _speechSemaphore.Release();
            }
        }
        
        // Process text to optimize for speech with minimal pauses
        private string ProcessTextForSpeech(string text)
        {
            // Replace multiple newlines with a single space to reduce pauses
            text = System.Text.RegularExpressions.Regex.Replace(text, @"\n+", " ");
            
            // Replace multiple spaces with a single space
            text = System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ");
            
            // Remove extra punctuation that might cause delays
            text = System.Text.RegularExpressions.Regex.Replace(text, @"\.{2,}", ".");
            text = System.Text.RegularExpressions.Regex.Replace(text, @"\s*([.,;:!?])\s*", "$1 ");
            
            return text.Trim();
        }
        
        // Request MP3 audio for the text from Google Cloud TTS
        private async Task<byte[]?> SynthesizeAsync(string text, string voiceId, string languageCode, string apiKey, double speedFactor)
        {
            try
            {
                var requestData = new
                {
                    input = new { text = text },
                    voice = new
                    {
                        languageCode = languageCode,
                        name = voiceId
                    },
                    audioConfig = new
                    {
                        audioEncoding = "MP3",
                        // 1.0 is normal; raised by the auto speed-up when lines pile up (API range 0.25-4.0)
                        speakingRate = Math.Clamp(speedFactor, 0.25, 4.0)
                    }
                };

                Console.WriteLine($"Using voice '{voiceId}' with language code '{languageCode}'");
                var content = new StringContent(JsonSerializer.Serialize(requestData), Encoding.UTF8, "application/json");

                Console.WriteLine($"Sending TTS request to Google Cloud TTS API for text: {text.Substring(0, Math.Min(50, text.Length))}...");
                using HttpResponseMessage response = await _httpClient.PostAsync($"{_baseUrl}?key={apiKey}", content);

                if (!response.IsSuccessStatusCode)
                {
                    string errorContent = await response.Content.ReadAsStringAsync();
                    TtsErrorNotifier.ShowError("Google", $"Google TTS request failed: {(int)response.StatusCode} {response.StatusCode}\n\n{errorContent}");
                    return null;
                }

                // The response carries the audio as base64 in "audioContent"
                string jsonResponse = await response.Content.ReadAsStringAsync();
                using JsonDocument doc = JsonDocument.Parse(jsonResponse);
                if (!doc.RootElement.TryGetProperty("audioContent", out JsonElement audioElement))
                {
                    Console.WriteLine("No audioContent field found in Google TTS response");
                    return null;
                }

                string base64Audio = audioElement.GetString() ?? "";
                if (string.IsNullOrEmpty(base64Audio))
                {
                    Console.WriteLine("Empty audio content received from Google TTS");
                    return null;
                }
                return Convert.FromBase64String(base64Audio);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error synthesizing Google TTS audio: {ex.Message}");
                return null;
            }
        }

        public static void StopAllTTS()
        {
            try
            {
                Console.WriteLine("Stopping all Google TTS activities");
                _playback.Stop();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error stopping Google TTS: {ex.Message}");
            }
        }

        public static void ClearAudioQueue()
        {
            _playback.Stop();
        }
    }
}
