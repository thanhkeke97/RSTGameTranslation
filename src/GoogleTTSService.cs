using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Media;
using System.Diagnostics;
using System.Threading;
using NAudio.Wave;
using MessageBox = System.Windows.MessageBox;

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
            { "Chinese (Male) - Neural2", "cmn-CN-Neural2-C" }
        };
        
        // Semaphore to ensure only one speech request is processed at a time
        private static readonly SemaphoreSlim _speechSemaphore = new SemaphoreSlim(1, 1);
        
        // Flag to track if we're currently playing audio
        private static bool _isPlayingAudio = false;
        
        // Current audio player
        private static IWavePlayer? _currentPlayer = null;
        
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
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        }
        
        public async Task<bool> SpeakText(string text)
        {
            // Try to acquire the semaphore to ensure only one speech request runs at a time
            if (!await _speechSemaphore.WaitAsync(0))
            {
                Console.WriteLine("Another speech request is already in progress. Skipping this one.");
                return false;
            }
            
            try
            {
                if (string.IsNullOrWhiteSpace(text))
                {
                    Console.WriteLine("Cannot speak empty text");
                    return false;
                }
                
                // Stop any current playback
                StopCurrentPlayback();
                
                // Get API key and other settings from config
                string apiKey = ConfigManager.Instance.GetGoogleTtsApiKey();
                if (string.IsNullOrWhiteSpace(apiKey))
                {
                    MessageBox.Show("Google Cloud API key is not set. Please configure it in Settings.", 
                        "API Key Missing", MessageBoxButton.OK, MessageBoxImage.Warning);
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
                
                // Create request payload with matching language code
                var requestData = new
                {
                    input = new
                    {
                        text = text
                    },
                    voice = new
                    {
                        languageCode = languageCode,
                        name = voice
                    },
                    audioConfig = new
                    {
                        audioEncoding = "MP3"
                    }
                };
                
                Console.WriteLine($"Using voice '{voice}' with language code '{languageCode}'");
                
                // The Chirp model is specified separately - newer API may have different requirements
                // For now, use the standard Neural2 model
                
                // Serialize to JSON
                string jsonRequest = JsonSerializer.Serialize(requestData);
                var content = new StringContent(jsonRequest, Encoding.UTF8, "application/json");
                
                // Form the URL including API key
                string url = $"{_baseUrl}?key={apiKey}";
                
                Console.WriteLine($"Sending TTS request to Google Cloud TTS API for text: {text.Substring(0, Math.Min(50, text.Length))}...");
                
                // Post request to Google TTS API
                HttpResponseMessage response = await _httpClient.PostAsync(url, content);
                
                // Check if request was successful
                if (response.IsSuccessStatusCode)
                {
                    // Log the content type
                    string contentType = response.Content.Headers.ContentType?.MediaType ?? "application/json";
                    Console.WriteLine($"TTS request successful, received response with content type: {contentType}");
                    
                    // Parse the JSON response
                    string jsonResponse = await response.Content.ReadAsStringAsync();
                    using JsonDocument doc = JsonDocument.Parse(jsonResponse);
                    
                    // The response contains a base64-encoded audioContent field
                    if (doc.RootElement.TryGetProperty("audioContent", out JsonElement audioElement))
                    {
                        string base64Audio = audioElement.GetString() ?? "";
                        if (!string.IsNullOrEmpty(base64Audio))
                        {
                            // Convert base64 to byte array
                            byte[] audioBytes = Convert.FromBase64String(base64Audio);
                            
                            // Create a temp file path for the audio
                            string tempDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "temp");
                            Directory.CreateDirectory(tempDir); // Create directory if it doesn't exist
                            string audioFile = Path.Combine(tempDir, $"tts_google_{DateTime.Now.Ticks}.mp3");
                            
                            // Save audio to file
                            await File.WriteAllBytesAsync(audioFile, audioBytes);
                            
                            Console.WriteLine($"Audio saved to {audioFile}, playing...");
                            
                            // Play the audio file and wait for it to complete
                            bool playbackResult = await PlayAudioFileAsync(audioFile);
                            
                            return playbackResult;
                        }
                        else
                        {
                            Console.WriteLine("Empty audio content received from Google TTS");
                            return false;
                        }
                    }
                    else
                    {
                        Console.WriteLine("No audioContent field found in Google TTS response");
                        return false;
                    }
                }
                else
                {
                    string errorContent = await response.Content.ReadAsStringAsync();
                    Console.WriteLine($"Google TTS request failed: {response.StatusCode}. Details: {errorContent}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error during Google TTS: {ex.Message}");
                
                // Show a message to the user on the UI thread
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    MessageBox.Show($"Error with Google Text-to-Speech: {ex.Message}",
                        "TTS Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                });
                
                return false;
            }
            finally
            {
                // Always release the semaphore when done
                _speechSemaphore.Release();
            }
        }
        
        // Stop any current playback
        private void StopCurrentPlayback()
        {
            if (_isPlayingAudio && _currentPlayer != null)
            {
                try
                {
                    Console.WriteLine("Stopping current audio playback");
                    _currentPlayer.Stop();
                    _currentPlayer.Dispose();
                    _currentPlayer = null;
                    _isPlayingAudio = false;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error stopping current playback: {ex.Message}");
                }
            }
        }
        
        // New async version that returns a Task<bool> for completion status
        private async Task<bool> PlayAudioFileAsync(string filePath)
        {
            var tcs = new TaskCompletionSource<bool>();
            
            try
            {
                // Mark as playing audio
                _isPlayingAudio = true;
                
                // Create a WaveOut device
                _currentPlayer = new WaveOutEvent();
                
                // Set up playback stopped event
                _currentPlayer.PlaybackStopped += (sender, args) =>
                {
                    Console.WriteLine("Audio playback completed");
                    _isPlayingAudio = false;
                    
                    // Clean up resources
                    if (_currentPlayer != null)
                    {
                        _currentPlayer.Dispose();
                        _currentPlayer = null;
                    }
                    
                    // Delete the temp file
                    try
                    {
                        if (File.Exists(filePath))
                        {
                            File.Delete(filePath);
                            Console.WriteLine($"Temp audio file deleted: {filePath}");
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Failed to delete temp audio file: {ex.Message}");
                    }
                    
                    // Signal completion
                    tcs.TrySetResult(true);
                };
                
                // Open the audio file
                var audioFile = new AudioFileReader(filePath);
                
                // Hook up the audio file to the WaveOut device
                _currentPlayer.Init(audioFile);
                
                // Start playback
                Console.WriteLine($"Starting audio playback of file: {filePath}");
                _currentPlayer.Play();
                
                // Wait for the playback to complete
                return await tcs.Task;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error playing audio file: {ex.Message}");
                
                // Show a message to the user
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    MessageBox.Show($"Error playing audio: {ex.Message}",
                        "Audio Playback Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                });
                
                // Clean up
                _isPlayingAudio = false;
                if (_currentPlayer != null)
                {
                    _currentPlayer.Dispose();
                    _currentPlayer = null;
                }
                
                // Delete the temp file
                try
                {
                    if (File.Exists(filePath))
                    {
                        File.Delete(filePath);
                        Console.WriteLine($"Temp audio file deleted: {filePath}");
                    }
                }
                catch (Exception fileEx)
                {
                    Console.WriteLine($"Failed to delete temp audio file: {fileEx.Message}");
                }
                
                // Signal failure
                tcs.TrySetResult(false);
                return false;
            }
        }
    }
}