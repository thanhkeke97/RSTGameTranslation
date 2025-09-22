using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using NAudio.Wave;
using Windows.Media.SpeechSynthesis;
using Windows.Storage.Streams;
using MessageBox = System.Windows.MessageBox;
using SystemSpeech = System.Speech.Synthesis;

namespace RSTGameTranslation
{
    public class WindowsTTSService
    {
        private static WindowsTTSService? _instance;
        private readonly SpeechSynthesizer _synthesizer;
        private readonly SystemSpeech.SpeechSynthesizer _systemSynthesizer;
        
        // Dictionary of available voices with their names
        // This will be populated dynamically based on installed voices
        public static readonly Dictionary<string, string> AvailableVoices = new Dictionary<string, string>();
        
        // Dictionary to track which API each voice belongs to (UWP or System.Speech)
        // true = UWP (Windows.Media.SpeechSynthesis), false = System.Speech
        private static readonly Dictionary<string, bool> _voiceApiSource = new Dictionary<string, bool>();
        
        // Semaphore to ensure only one speech request is processed at a time
        private static readonly SemaphoreSlim _speechSemaphore = new SemaphoreSlim(1, 1);
        
        // Flag to track if we're currently playing audio
        private static bool _isPlayingAudio = false;
        
        // Current audio player
        private static IWavePlayer? _currentPlayer = null;
        
        // Current audio file reader
        private static AudioFileReader? _currentAudioFile = null;
        
        // Speech rate (from -10 to 10, where 0 is normal speed)
        private static int _speechRate = 3;
        
        // Default speech rate values
        public const int MinSpeechRate = -10;
        public const int MaxSpeechRate = 10;
        public const int DefaultSpeechRate = 3;
        
        // Path to temp directory
        private static readonly string _tempDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "temp");
        
        // List to track temporary files that need to be deleted
        private static readonly List<string> _tempFilesToDelete = new List<string>();
        
        // Timer to periodically clean up temp files
        private static System.Timers.Timer? _cleanupTimer;
        
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
            _systemSynthesizer = new SystemSpeech.SpeechSynthesizer();
            
            // Ensure temp directory exists
            Directory.CreateDirectory(_tempDir);
            
            // Clean up any old temp files
            CleanupTempFiles();
            
            // Initialize available voices
            InitializeVoices();
            
            // Set up a timer to periodically clean up temp files
            _cleanupTimer = new System.Timers.Timer(30000); // 30 seconds
            _cleanupTimer.Elapsed += (sender, e) => CleanupTempFiles();
            _cleanupTimer.Start();
            
            // Load speech rate from config
            // _speechRate = ConfigManager.Instance.GetWindowsTtsSpeechRate();
            
            // Register for application exit event to clean up
            AppDomain.CurrentDomain.ProcessExit += (sender, e) => CleanupTempFiles();
        }
        
        // Clean up any old temporary audio files
        private void CleanupTempFiles()
        {
            try
            {
                // First, try to delete files in our tracking list
                lock (_tempFilesToDelete)
                {
                    if (_tempFilesToDelete.Count > 0)
                    {
                        Console.WriteLine($"Attempting to delete {_tempFilesToDelete.Count} tracked temp files");
                        
                        List<string> successfullyDeleted = new List<string>();
                        
                        foreach (string file in _tempFilesToDelete)
                        {
                            try
                            {
                                if (File.Exists(file))
                                {
                                    // Ensure the file isn't locked
                                    GC.Collect();
                                    GC.WaitForPendingFinalizers();
                                    
                                    File.Delete(file);
                                    Console.WriteLine($"Deleted tracked temp file: {file}");
                                    successfullyDeleted.Add(file);
                                }
                                else
                                {
                                    // File doesn't exist anymore, remove from tracking
                                    successfullyDeleted.Add(file);
                                }
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"Failed to delete tracked temp file {file}: {ex.Message}");
                            }
                        }
                        
                        // Remove successfully deleted files from tracking list
                        foreach (string file in successfullyDeleted)
                        {
                            _tempFilesToDelete.Remove(file);
                        }
                    }
                }
                
                // Then, search for any other .wav files in the temp directory
                if (Directory.Exists(_tempDir))
                {
                    string[] tempFiles = Directory.GetFiles(_tempDir, "tts_*.wav");
                    
                    int count = 0;
                    foreach (string file in tempFiles)
                    {
                        // Check if the file is older than 10 minutes
                        FileInfo fileInfo = new FileInfo(file);
                        if (DateTime.Now - fileInfo.CreationTime > TimeSpan.FromMinutes(10))
                        {
                            try
                            {
                                // Ensure the file isn't locked
                                GC.Collect();
                                GC.WaitForPendingFinalizers();
                                
                                File.Delete(file);
                                count++;
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"Failed to delete old temp file {file}: {ex.Message}");
                                
                                // Add to tracking list for future deletion
                                lock (_tempFilesToDelete)
                                {
                                    if (!_tempFilesToDelete.Contains(file))
                                    {
                                        _tempFilesToDelete.Add(file);
                                    }
                                }
                            }
                        }
                    }
                    
                    if (count > 0)
                    {
                        Console.WriteLine($"Cleaned up {count} old temporary audio files");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error during temp file cleanup: {ex.Message}");
            }
        }
        
        private void InitializeVoices()
        {
            try
            {
                // Clear existing voices
                AvailableVoices.Clear();
                _voiceApiSource.Clear();
                
                // Get all installed voices from Windows.Media.SpeechSynthesis (UWP API)
                var uwpVoices = SpeechSynthesizer.AllVoices;
                
                foreach (var voice in uwpVoices)
                {
                    string language = voice.Language;
                    string displayName = voice.DisplayName;
                    string gender = voice.Gender.ToString();
                    
                    // Create a descriptive name for the voice
                    string voiceKey = $"{displayName} ({language}, {gender}, UWP)";
                    
                    // Add to dictionary - use the ID as the value
                    if (!AvailableVoices.ContainsKey(voiceKey))
                    {
                        AvailableVoices.Add(voiceKey, voice.Id);
                        _voiceApiSource.Add(voice.Id, true); // true = UWP API
                        Console.WriteLine($"Found Windows UWP TTS voice: {voiceKey} - {voice.Id}");
                    }
                }
                
                // Get all installed voices from System.Speech.Synthesis (SAPI 5, which Narrator typically uses)
                try
                {
                    var systemVoices = _systemSynthesizer.GetInstalledVoices();
                    
                    foreach (var voice in systemVoices)
                    {
                        if (voice.Enabled)
                        {
                            var info = voice.VoiceInfo;
                            string displayName = info.Name;
                            string gender = info.Gender.ToString();
                            string age = info.Age.ToString();
                            string culture = info.Culture?.DisplayName ?? "Unknown";
                            
                            // Create a descriptive name for the voice
                            string voiceKey = $"{displayName} ({culture}, {gender}, SAPI)";
                            
                            // Add to dictionary - use the name as the ID for System.Speech voices
                            if (!AvailableVoices.ContainsKey(voiceKey))
                            {
                                AvailableVoices.Add(voiceKey, info.Name);
                                _voiceApiSource.Add(info.Name, false); // false = System.Speech API
                                Console.WriteLine($"Found SAPI TTS voice: {voiceKey} - {info.Name}");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error getting System.Speech voices: {ex.Message}");
                }
                
                Console.WriteLine($"Initialized {AvailableVoices.Count} Windows TTS voices in total");
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
                
                // Process text to reduce pauses between lines
                string processedText = ProcessTextForSpeech(text);
                
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
                
                Console.WriteLine($"Using TTS voice ID: {voiceId} with speech rate: {_speechRate}");
                
                // Check which API this voice belongs to
                bool isUwpVoice = _voiceApiSource.TryGetValue(voiceId, out bool isUwp) && isUwp;
                
                string audioFile = string.Empty;
                
                if (isUwpVoice)
                {
                    // Use Windows.Media.SpeechSynthesis (UWP API)
                    // Find the voice object by ID
                    var selectedVoice = SpeechSynthesizer.AllVoices
                        .FirstOrDefault(v => v.Id == voiceId);
                    
                    if (selectedVoice == null)
                    {
                        Console.WriteLine($"Could not find UWP voice with ID: {voiceId}");
                        MessageBox.Show($"Could not find voice with ID: {voiceId}",
                            "Voice Not Found", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return false;
                    }
                    
                    // Set the voice
                    _synthesizer.Voice = selectedVoice;
                    
                    // Set speech rate for UWP API (convert our -10 to 10 scale to UWP's scale)
                    // UWP uses a double from 0.5 (half speed) to 2.0 (double speed)
                    double uwpRate = 1.0; // Default normal speed
                    
                    if (_speechRate > 0)
                    {
                        // Map 1-10 to 1.0-2.0 (faster)
                        uwpRate = 1.0 + (_speechRate / 10.0);
                    }
                    else if (_speechRate < 0)
                    {
                        // Map -1 to -10 to 1.0-0.5 (slower)
                        uwpRate = 1.0 + (_speechRate / 20.0); // Divide by 20 to map -10 to -0.5
                    }
                    
                    // Apply the speech rate
                    _synthesizer.Options.SpeakingRate = uwpRate;
                    
                    // Create SSML with minimal pauses
                    string ssml = CreateSsmlWithMinimalPauses(processedText);
                    
                    Console.WriteLine($"UWP speech rate set to: {uwpRate}");
                    
                    // Create a temp file path for the audio
                    audioFile = Path.Combine(_tempDir, $"tts_windows_{DateTime.Now.Ticks}.wav");
                    
                    Console.WriteLine($"Speaking text with UWP API: {processedText.Substring(0, Math.Min(50, processedText.Length))}...");
                    
                    // Generate speech stream using SSML
                    SpeechSynthesisStream stream = await _synthesizer.SynthesizeSsmlToStreamAsync(ssml);
                    
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
                }
                else
                {
                    // Use System.Speech.Synthesis API (SAPI 5, which Narrator typically uses)
                    audioFile = Path.Combine(_tempDir, $"tts_system_{DateTime.Now.Ticks}.wav");
                    
                    Console.WriteLine($"Speaking text with SAPI: {processedText.Substring(0, Math.Min(50, processedText.Length))}...");
                    
                    // Set the voice
                    _systemSynthesizer.SelectVoice(voiceId);
                    
                    // Set speech rate for SAPI (convert our -10 to 10 scale to SAPI's -10 to 10 scale)
                    _systemSynthesizer.Rate = _speechRate;
                    
                    // Create a prompt with SSML for minimal pauses
                    string ssml = CreateSsmlWithMinimalPauses(processedText);
                    var prompt = new SystemSpeech.Prompt(ssml, SystemSpeech.SynthesisTextFormat.Ssml);
                    
                    Console.WriteLine($"SAPI speech rate set to: {_speechRate}");
                    
                    // Set output to audio file
                    _systemSynthesizer.SetOutputToWaveFile(audioFile);
                    
                    // Speak the text using SSML
                    _systemSynthesizer.Speak(prompt);
                    
                    // Reset output to null to close the file
                    _systemSynthesizer.SetOutputToNull();
                }
                
                // Track this file for deletion
                lock (_tempFilesToDelete)
                {
                    if (!_tempFilesToDelete.Contains(audioFile))
                    {
                        _tempFilesToDelete.Add(audioFile);
                    }
                }
                
                Console.WriteLine($"Audio saved to {audioFile}, playing...");
                
                // Play the audio file and wait for it to complete
                bool playbackResult = await PlayAudioFileAsync(audioFile);
                
                return playbackResult;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error during TTS: {ex.Message}");
                
                // Show a message to the user on the UI thread
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    MessageBox.Show($"Error with Text-to-Speech: {ex.Message}",
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
        
        // Process text to optimize for speech with minimal pauses
        private string ProcessTextForSpeech(string text)
        {
            // Replace multiple newlines with a single space to reduce pauses
            text = System.Text.RegularExpressions.Regex.Replace(text, @"\n+", " ");
            
            // Replace multiple spaces with a single space
            text = System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ");
            
            return text.Trim();
        }
        
        // Create SSML with minimal pauses between sentences
        private string CreateSsmlWithMinimalPauses(string text)
        {
            StringBuilder ssml = new StringBuilder();
            ssml.Append("<speak version=\"1.0\" xmlns=\"http://www.w3.org/2001/10/synthesis\" xml:lang=\"en-US\">");
            
            // Set overall prosody for the speech
            ssml.Append("<prosody rate=\"medium\" pitch=\"medium\">");
            
            // Escape the text for XML
            string escapedText = System.Security.SecurityElement.Escape(text);
            
            // Replace sentence endings with minimal pauses (10ms)
            escapedText = System.Text.RegularExpressions.Regex.Replace(
                escapedText, 
                @"([.!?]) ", 
                "$1<break time=\"10ms\"/> "
            );
            
            // Replace commas with even shorter pauses (5ms)
            escapedText = System.Text.RegularExpressions.Regex.Replace(
                escapedText, 
                @"(,) ", 
                "$1<break time=\"5ms\"/> "
            );
            
            ssml.Append(escapedText);
            ssml.Append("</prosody>");
            ssml.Append("</speak>");
            
            return ssml.ToString();
        }
        
        // Stop any current playback
        private void StopCurrentPlayback()
        {
            if (_isPlayingAudio)
            {
                try
                {
                    Console.WriteLine("Stopping current audio playback");
                    
                    if (_currentPlayer != null)
                    {
                        _currentPlayer.Stop();
                        _currentPlayer.Dispose();
                        _currentPlayer = null;
                    }
                    
                    if (_currentAudioFile != null)
                    {
                        _currentAudioFile.Dispose();
                        _currentAudioFile = null;
                    }
                    
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
                    
                    if (_currentAudioFile != null)
                    {
                        _currentAudioFile.Dispose();
                        _currentAudioFile = null;
                    }
                    
                    // Delete the temp file with retry mechanism
                    DeleteFileWithRetry(filePath);
                    
                    // Signal completion
                    tcs.TrySetResult(true);
                };
                
                // Open the audio file
                _currentAudioFile = new AudioFileReader(filePath);
                
                // Hook up the audio file to the WaveOut device
                _currentPlayer.Init(_currentAudioFile);
                
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
                
                if (_currentAudioFile != null)
                {
                    _currentAudioFile.Dispose();
                    _currentAudioFile = null;
                }
                
                if (_currentPlayer != null)
                {
                    _currentPlayer.Dispose();
                    _currentPlayer = null;
                }
                
                // Delete the temp file with retry mechanism
                DeleteFileWithRetry(filePath);
                
                // Signal failure
                tcs.TrySetResult(false);
                return false;
            }
        }
        
        // Delete a file with retry mechanism
        private void DeleteFileWithRetry(string filePath, int maxRetries = 3)
        {
            Task.Run(async () =>
            {
                if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
                {
                    return;
                }
                
                for (int i = 0; i < maxRetries; i++)
                {
                    try
                    {
                        // Force garbage collection to release any file handles
                        GC.Collect();
                        GC.WaitForPendingFinalizers();
                        
                        // Try to delete the file
                        File.Delete(filePath);
                        Console.WriteLine($"Temp audio file deleted: {filePath}");
                        
                        // Remove from tracking list if it was there
                        lock (_tempFilesToDelete)
                        {
                            _tempFilesToDelete.Remove(filePath);
                        }
                        
                        return; // Success
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Failed to delete temp file (attempt {i+1}/{maxRetries}): {ex.Message}");
                        
                        if (i < maxRetries - 1)
                        {
                            // Wait before retrying
                            await Task.Delay(500 * (i + 1)); // Exponential backoff
                        }
                        else
                        {
                            // Add to tracking list for future cleanup
                            lock (_tempFilesToDelete)
                            {
                                if (!_tempFilesToDelete.Contains(filePath))
                                {
                                    _tempFilesToDelete.Add(filePath);
                                }
                            }
                        }
                    }
                }
            });
        }
        
        // Method to get the list of installed voices for settings UI
        public static List<string> GetInstalledVoiceNames()
        {
            // Make sure the instance is created to initialize voices
            if (_instance == null)
            {
                _instance = new WindowsTTSService();
            }
            
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
        
        // Method to get default system voice (the one Narrator is using)
        public static string? GetDefaultSystemVoice()
        {
            try
            {
                // Make sure the instance is created to initialize voices
                if (_instance == null)
                {
                    _instance = new WindowsTTSService();
                }
                
                // Create a temporary System.Speech synthesizer to get the default voice
                using (var synth = new SystemSpeech.SpeechSynthesizer())
                {
                    string defaultVoiceName = synth.Voice.Name;
                    
                    // Find the display name for this voice
                    foreach (var pair in AvailableVoices)
                    {
                        if (pair.Value == defaultVoiceName && 
                            _voiceApiSource.TryGetValue(defaultVoiceName, out bool isUwp) && !isUwp)
                        {
                            return pair.Key;
                        }
                    }
                    
                    // If we couldn't find the exact default voice, try to find any SAPI voice
                    foreach (var pair in _voiceApiSource)
                    {
                        if (!pair.Value) // Not UWP, so it's a SAPI voice
                        {
                            string? displayName = GetDisplayNameFromVoiceId(pair.Key);
                            if (displayName != null)
                            {
                                return displayName;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error getting default system voice: {ex.Message}");
            }
            
            // If no SAPI voice found, return the first available voice
            return AvailableVoices.Keys.FirstOrDefault();
        }
        
        // Force cleanup of all temp files
        public static void ForceCleanupTempFiles()
        {
            if (_instance != null)
            {
                _instance.CleanupTempFiles();
            }
        }
    }
}