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
        
        // Flag to track if we're currently playing audio
        private static bool _isPlayingAudio = false;
        
        // Current audio player
        private static IWavePlayer? _currentPlayer = null;
        
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
                string apiKey = ConfigManager.Instance.GetElevenLabsApiKey();
                if (string.IsNullOrWhiteSpace(apiKey))
                {
                    MessageBox.Show("ElevenLabs API key is not set. Please configure it in Settings.", 
                        "API Key Missing", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return false;
                }
                
                // Get voice ID
                string voice = ConfigManager.Instance.GetElevenLabsVoice();
                
                // Set API key in headers
                _httpClient.DefaultRequestHeaders.Remove("xi-api-key");
                _httpClient.DefaultRequestHeaders.Add("xi-api-key", apiKey);
                
                // Ensure we have a valid voice ID (allow custom voice IDs)
                if (string.IsNullOrWhiteSpace(voice))
                {
                    // Use Rachel as default only if no voice is configured
                    voice = DefaultVoices["Rachel"];
                }
                
                // Get model from config (defaults to eleven_flash_v2_5)
                string model = ConfigManager.Instance.GetElevenLabsModel();

                // Create request payload
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
                
                // Serialize to JSON
                string jsonRequest = JsonSerializer.Serialize(requestData);
                var content = new StringContent(jsonRequest, Encoding.UTF8, "application/json");
                
                // Form the URL for text-to-speech endpoint
                string url = $"{_baseUrl}/text-to-speech/{voice}";
                
                Console.WriteLine($"Sending TTS request to ElevenLabs for text: {text.Substring(0, Math.Min(50, text.Length))}...");
                
                // Post request to ElevenLabs API
                HttpResponseMessage response = await _httpClient.PostAsync(url, content);
                
                // Check if request was successful
                if (response.IsSuccessStatusCode)
                {
                    // Log the content type
                    string contentType = response.Content.Headers.ContentType?.MediaType ?? "unknown";
                    Console.WriteLine($"TTS request successful, received audio data with content type: {contentType}");
                    
                    // Get audio data as stream
                    using Stream audioStream = await response.Content.ReadAsStreamAsync();
                    
                    // Create a temp file path for the audio with appropriate extension
                    string tempDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "temp");
                    Directory.CreateDirectory(tempDir); // Create directory if it doesn't exist
                    
                    // Determine file extension based on content type
                    string extension = ".mp3"; // Default
                    if (contentType.Contains("audio/mpeg") || contentType.Contains("audio/mp3"))
                    {
                        extension = ".mp3";
                    }
                    else if (contentType.Contains("audio/wav") || contentType.Contains("audio/x-wav"))
                    {
                        extension = ".wav";
                    }
                    else if (contentType.Contains("audio/mp4") || contentType.Contains("audio/x-m4a"))
                    {
                        extension = ".m4a";
                    }
                    else if (contentType.Contains("audio/ogg"))
                    {
                        extension = ".ogg";
                    }
                    
                    string audioFile = Path.Combine(tempDir, $"tts_elevenlabs_{DateTime.Now.Ticks}{extension}");
                    
                    // Save audio to file
                    using (FileStream fileStream = File.Create(audioFile))
                    {
                        await audioStream.CopyToAsync(fileStream);
                    }
                    
                    Console.WriteLine($"Audio saved to {audioFile}, playing...");
                    
                    // Play the audio file and wait for it to complete
                    bool playbackResult = await PlayAudioFileAsync(audioFile);
                    
                    return playbackResult;
                }
                else
                {
                    string errorContent = await response.Content.ReadAsStringAsync();
                    Console.WriteLine($"TTS request failed: {response.StatusCode}. Details: {errorContent}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error during TTS: {ex.Message}");
                
                // Show a message to the user on the UI thread
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    MessageBox.Show($"Error with ElevenLabs Text-to-Speech: {ex.Message}",
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