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
        
        // TaskCompletionSource to track current playback
        private TaskCompletionSource<bool>? _currentPlaybackTcs;
        
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
            try
            {
                if (string.IsNullOrWhiteSpace(text))
                {
                    Console.WriteLine("Cannot speak empty text");
                    return false;
                }
                
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
        }
        
        // New async version that returns a Task<bool> for completion status
        private async Task<bool> PlayAudioFileAsync(string filePath)
        {
            // Create a TaskCompletionSource to track playback completion
            _currentPlaybackTcs = new TaskCompletionSource<bool>();
            
            try
            {
                // Start a task to play the audio
                _= Task.Run(() =>
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
                            
                            // Signal completion to the TaskCompletionSource
                            _currentPlaybackTcs?.TrySetResult(true);
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
                        
                        // Signal failure to the TaskCompletionSource
                        _currentPlaybackTcs?.TrySetException(ex);
                        
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
                        
                        // Make sure we always signal completion in case of errors
                        _currentPlaybackTcs?.TrySetResult(false);
                    }
                });
                
                // Wait for the playback to complete
                return await _currentPlaybackTcs.Task;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error starting audio playback: {ex.Message}");
                return false;
            }
        }
        
        // Keep the old method for backward compatibility but make it private
        private void PlayAudioFile(string filePath)
        {
            // Just call the async version and ignore the result
            _ = PlayAudioFileAsync(filePath);
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