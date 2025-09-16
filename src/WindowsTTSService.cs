using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using NAudio.Wave;
using Windows.Media.SpeechSynthesis;
using Windows.Storage.Streams;
using MessageBox = System.Windows.MessageBox;

namespace RSTGameTranslation
{
    public class WindowsTTSService
    {
        private static WindowsTTSService? _instance;
        private readonly SpeechSynthesizer _synthesizer;
        
        // Dictionary of available voices with their names
        // This will be populated dynamically based on installed voices
        public static readonly Dictionary<string, string> AvailableVoices = new Dictionary<string, string>();
        
        // Semaphore to ensure only one speech request is processed at a time
        private static readonly SemaphoreSlim _speechSemaphore = new SemaphoreSlim(1, 1);
        
        // Flag to track if we're currently playing audio
        private static bool _isPlayingAudio = false;
        
        // Current audio player
        private static IWavePlayer? _currentPlayer = null;
        
        public static WindowsTTSService Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new WindowsTTSService();
                }
                return _instance;
            }
        }
        
        private WindowsTTSService()
        {
            _synthesizer = new SpeechSynthesizer();
            
            // Initialize available voices
            InitializeVoices();
        }
        
        private void InitializeVoices()
        {
            try
            {
                // Clear existing voices
                AvailableVoices.Clear();
                
                // Get all installed voices
                var installedVoices = SpeechSynthesizer.AllVoices;
                
                foreach (var voice in installedVoices)
                {
                    string language = voice.Language;
                    string displayName = voice.DisplayName;
                    string gender = voice.Gender.ToString();
                    
                    // Create a descriptive name for the voice
                    string voiceKey = $"{displayName} ({language}, {gender})";
                    
                    // Add to dictionary - use the ID as the value
                    AvailableVoices.Add(voiceKey, voice.Id);
                    
                    Console.WriteLine($"Found Windows TTS voice: {voiceKey} - {voice.Id}");
                }
                
                Console.WriteLine($"Initialized {AvailableVoices.Count} Windows TTS voices");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error initializing Windows TTS voices: {ex.Message}");
            }
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
                
                // Get voice ID from config
                string voiceName = ConfigManager.Instance.GetWindowsTtsVoice();
                AvailableVoices.TryGetValue(voiceName, out string? voiceId);
                
                // If no voice is configured or the configured voice is not available, use the default voice
                if (string.IsNullOrWhiteSpace(voiceId))
                {
                    // Use the first available voice as default
                    if (AvailableVoices.Count > 0)
                    {
                        voiceId = AvailableVoices.Values.First();
                    }
                    else
                    {
                        // No voices available
                        MessageBox.Show("No Windows TTS voices are available on this system.",
                            "No Voices Available", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return false;
                    }
                }
                
                Console.WriteLine($"Using Windows TTS voice ID: {voiceId}");
                
                // Find the voice object by ID
                var selectedVoice = SpeechSynthesizer.AllVoices
                    .FirstOrDefault(v => v.Id == voiceId);
                
                if (selectedVoice == null)
                {
                    Console.WriteLine($"Could not find voice with ID: {voiceId}");
                    MessageBox.Show($"Could not find voice with ID: {voiceId}",
                        "Voice Not Found", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return false;
                }
                
                // Set the voice
                _synthesizer.Voice = selectedVoice;
                
                // Create a temp file path for the audio
                string tempDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "temp");
                Directory.CreateDirectory(tempDir); // Create directory if it doesn't exist
                string audioFile = Path.Combine(tempDir, $"tts_windows_{DateTime.Now.Ticks}.wav");
                
                Console.WriteLine($"Speaking text: {text.Substring(0, Math.Min(50, text.Length))}...");
                
                // Generate speech stream
                SpeechSynthesisStream stream = await _synthesizer.SynthesizeTextToStreamAsync(text);
                
                // Save to WAV file
                using (var fileStream = new FileStream(audioFile, FileMode.Create, FileAccess.Write))
                {
                    // Convert the stream to a byte array
                    var dataReader = new DataReader(stream.GetInputStreamAt(0));
                    await dataReader.LoadAsync((uint)stream.Size);
                    byte[] buffer = new byte[stream.Size];
                    dataReader.ReadBytes(buffer);
                    
                    // Write to file
                    fileStream.Write(buffer, 0, buffer.Length);
                }
                
                Console.WriteLine($"Audio saved to {audioFile}, playing...");
                
                // Play the audio file and wait for it to complete
                bool playbackResult = await PlayAudioFileAsync(audioFile);
                
                return playbackResult;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error during Windows TTS: {ex.Message}");
                
                // Show a message to the user on the UI thread
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    MessageBox.Show($"Error with Windows Text-to-Speech: {ex.Message}",
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
        
        // Method to get the list of installed voices for settings UI
        public static List<string> GetInstalledVoiceNames()
        {
            // Return the display names (keys) from the AvailableVoices dictionary
            return AvailableVoices.Keys.ToList();
        }
        
        // Method to get voice ID from display name
        public static string? GetVoiceIdFromDisplayName(string displayName)
        {
            if (AvailableVoices.TryGetValue(displayName, out string? voiceId))
            {
                return voiceId;
            }
            return null;
        }
        
        // Method to get display name from voice ID
        public static string? GetDisplayNameFromVoiceId(string voiceId)
        {
            foreach (var pair in AvailableVoices)
            {
                if (pair.Value == voiceId)
                {
                    return pair.Key;
                }
            }
            return null;
        }
    }
}