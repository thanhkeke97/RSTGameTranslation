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

namespace UGTLive
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
            { "Sam", "yoZ06aMxZJJ28mfd3POQ" }
        };
        
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
            try
            {
                if (string.IsNullOrWhiteSpace(text))
                {
                    Console.WriteLine("Cannot speak empty text");
                    return false;
                }
                
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
                
                // Ensure we have a valid voice ID
                if (string.IsNullOrWhiteSpace(voice) || !DefaultVoices.ContainsValue(voice))
                {
                    // Use Rachel as default
                    voice = DefaultVoices["Rachel"];
                }
                
                // Create request payload
                var requestData = new
                {
                    text = text,
                    model_id = "eleven_multilingual_v2",
                    voice_settings = new
                    {
                        stability = 0.5,
                        similarity_boost = 0.75
                    }
                };
                
                // Serialize to JSON
                string jsonRequest = JsonSerializer.Serialize(requestData);
                var content = new StringContent(jsonRequest, Encoding.UTF8, "application/json");
                
                // Form the URL for text-to-speech endpoint
                string url = $"{_baseUrl}/text-to-speech/{voice}";
                
                Console.WriteLine($"Sending TTS request to ElevenLabs for text: {text.Substring(0, Math.Min(50, text.Length))}...");
                
                // Send the request as a Task to not block the UI
                return await Task.Run(async () =>
                {
                    try
                    {
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
                            
                            string audioFile = Path.Combine(tempDir, $"tts_{DateTime.Now.Ticks}{extension}");
                            
                            // Save audio to file
                            using (FileStream fileStream = File.Create(audioFile))
                            {
                                await audioStream.CopyToAsync(fileStream);
                            }
                            
                            Console.WriteLine($"Audio saved to {audioFile}, playing...");
                            
                            // Play the audio file
                            PlayAudioFile(audioFile);
                            
                            return true;
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
                        Console.WriteLine($"Error during TTS request: {ex.Message}");
                        return false;
                    }
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error initiating TTS: {ex.Message}");
                return false;
            }
        }
        
        private void PlayAudioFile(string filePath)
        {
            try
            {
                // Use a separate thread for audio playback to not block the UI
                Task.Run(() =>
                {
                    IWavePlayer? wavePlayer = null;
                    AudioFileReader? audioFile = null;
                    ManualResetEvent playbackFinished = new ManualResetEvent(false);

                    try
                    {
                        // Create a WaveOut device
                        wavePlayer = new WaveOutEvent();
                        wavePlayer.PlaybackStopped += (sender, args) =>
                        {
                            playbackFinished.Set(); // Signal when playback ends
                        };

                        // Open the audio file
                        audioFile = new AudioFileReader(filePath);
                        
                        // Hook up the audio file to the WaveOut device
                        wavePlayer.Init(audioFile);
                        
                        // Start playback
                        Console.WriteLine($"Starting audio playback of file: {filePath}");
                        wavePlayer.Play();
                        
                        // Wait for playback to complete
                        playbackFinished.WaitOne();
                        
                        // Close resources properly
                        wavePlayer.Stop();
                        
                        Console.WriteLine("Audio playback completed successfully");
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
                    }
                    finally
                    {
                        // Clean up resources
                        if (wavePlayer != null)
                        {
                            wavePlayer.Dispose();
                        }
                        
                        if (audioFile != null)
                        {
                            audioFile.Dispose();
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
                    }
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error starting audio playback thread: {ex.Message}");
            }
        }
    }
}