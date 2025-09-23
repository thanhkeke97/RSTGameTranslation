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
        
        // Semaphore to ensure only one playback runs at a time (but allows multiple synthesis)
        private static readonly SemaphoreSlim _playbackSemaphore = new SemaphoreSlim(1, 1);
        
        // Queue for pending audio files to play
        private static readonly Queue<string> _audioFileQueue = new Queue<string>();
        
        // Flag to track if we're currently playing audio
        private static bool _isPlayingAudio = false;
        
        // Current audio player
        private static IWavePlayer? _currentPlayer = null;
        private string audioFile;
        
        // Current audio file reader
        private static AudioFileReader? _currentAudioFile = null;
        
        // Speech rate (from -10 to 10, where 0 is normal speed)
        private static int _speechRate = 2;
        
        // Default speech rate values
        public const int MinSpeechRate = -10;
        public const int MaxSpeechRate = 10;
        public const int DefaultSpeechRate = 2;
        
        // Path to temp directory
        private static readonly string _tempDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "temp");
        
        // List to track temporary files that need to be deleted
        private static readonly List<string> _tempFilesToDelete = new List<string>();
        
        // Timer to periodically clean up temp files
        private static System.Timers.Timer? _cleanupTimer;
        
        // Flag to track if we're currently processing the audio queue
        private static bool _isProcessingQueue = false;
        
        // Cancellation token source for stopping current playback
        private static CancellationTokenSource? _playbackCancellationTokenSource = null;
        private static readonly HashSet<string> _activeAudioFiles = new HashSet<string>();
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
            AppDomain.CurrentDomain.ProcessExit += (sender, e) => CleanupAllTempFiles();
        }

        // Force cleanup of all temp files, including active ones when application exits
        private void CleanupAllTempFiles()
        {
            try
            {
                Console.WriteLine("Application closing - cleaning up all temporary audio files");
                
                // Stop current playback
                StopCurrentPlayback();
                
                // Clear the audio queue
                lock (_audioFileQueue)
                {
                    _audioFileQueue.Clear();
                }
                
                // Clear active files list
                lock (_activeAudioFiles)
                {
                    _activeAudioFiles.Clear();
                }
                
                // Delete all files in the temp directory
                if (Directory.Exists(_tempDir))
                {
                    string[] tempFiles = Directory.GetFiles(_tempDir, "tts_*.wav");
                    
                    foreach (string file in tempFiles)
                    {
                        try
                        {
                            // Ensure the file isn't locked
                            GC.Collect();
                            GC.WaitForPendingFinalizers();
                            
                            File.Delete(file);
                            Console.WriteLine($"Deleted temp file on exit: {file}");
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Failed to delete temp file on exit {file}: {ex.Message}");
                        }
                    }
                    
                    Console.WriteLine($"Cleaned up {tempFiles.Length} temporary audio files on application exit");
                }
                
                // Clear the tracking list
                lock (_tempFilesToDelete)
                {
                    _tempFilesToDelete.Clear();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error during final temp file cleanup: {ex.Message}");
            }
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
                            // Check if the file is currently active
                            bool isActive = false;
                            lock (_activeAudioFiles)
                            {
                                isActive = _activeAudioFiles.Contains(file);
                            }
                            
                            // If the file is active, skip it
                            if (isActive)
                            {
                                Console.WriteLine($"Skipping active audio file: {file}");
                                continue;
                            }
                            
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
                        // Check if the file is currently active
                        bool isActive = false;
                        lock (_activeAudioFiles)
                        {
                            isActive = _activeAudioFiles.Contains(file);
                        }
                        
                        // If the file is active, skip it
                        if (isActive)
                        {
                            continue;
                        }
                        
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
                        _voiceApiSource.Add(voice.Id, true); // Store by voice.Id
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
                                _voiceApiSource.Add(info.Name, false); // Store by info.Name
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
            try
            {
                if (string.IsNullOrWhiteSpace(text))
                {
                    Console.WriteLine("Cannot speak empty text");
                    return false;
                }
                
                // Process text to reduce pauses between lines
                string processedText = ProcessTextForSpeech(text);
                
                // Get voice name from config
                string voiceName = ConfigManager.Instance.GetWindowsTtsVoice();
                
                // Check if this voice exists in our dictionary
                if (!AvailableVoices.ContainsKey(voiceName))
                {
                    Console.WriteLine($"Voice '{voiceName}' not found in available voices");
                    
                    // Use the first available voice as default
                    if (AvailableVoices.Count > 0)
                    {
                        voiceName = AvailableVoices.Keys.First();
                        Console.WriteLine($"Using first available voice: {voiceName}");
                    }
                    else
                    {
                        // No voices available
                        MessageBox.Show("No Windows TTS voices are available on this system.",
                            "No Voices Available", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return false;
                    }
                }
                
                // Get the voice ID for the selected voice
                string voiceId = AvailableVoices[voiceName];
                
                // Check which API this voice belongs to
                bool isUwpVoice = _voiceApiSource.TryGetValue(voiceId, out bool isUwp) && isUwp;
                
                // Add log for debugging
                if (!_voiceApiSource.ContainsKey(voiceId))
                {
                    Console.WriteLine($"WARNING: Voice ID '{voiceId}' not found in _voiceApiSource dictionary");
                    Console.WriteLine($"Available keys in _voiceApiSource: {string.Join(", ", _voiceApiSource.Keys.Take(5))}...");
                }
                
                Console.WriteLine($"Using TTS voice: {voiceName} (ID: {voiceId}, UWP: {isUwpVoice}) with speech rate: {_speechRate}");
                
                // Generate audio file asynchronously (can happen in parallel)
                string audioFilePath = await GenerateAudioFileAsync(processedText, voiceId, isUwpVoice);
                
                if (string.IsNullOrEmpty(audioFilePath))
                {
                    Console.WriteLine("Failed to generate audio file");
                    return false;
                }
                
                // Add to audio playback queue
                EnqueueAudioFile(audioFilePath);
                
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error preparing TTS: {ex.Message}");
                
                // Show a message to the user on the UI thread
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    MessageBox.Show($"Error with Text-to-Speech: {ex.Message}",
                        "TTS Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                });
                
                return false;
            }
        }
        
        // Generate audio file asynchronously
        private async Task<string> GenerateAudioFileAsync(string text, string voiceId, bool isUwpVoice)
        {
            try
            {
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
                        return string.Empty;
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
                    
                    Console.WriteLine($"UWP speech rate set to: {uwpRate}");
                    
                    // Create a temp file path for the audio
                    string tempDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "temp");
                    Directory.CreateDirectory(tempDir); // Create directory if it doesn't exist
                    audioFile = Path.Combine(tempDir, $"tts_windows_{DateTime.Now.Ticks}.wav");
                    
                    Console.WriteLine($"Generating audio for text: {text.Substring(0, Math.Min(50, text.Length))}...");
                    
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
                }
                else
                {
                    // Use Task.Run to run SAPI file generation in a separate thread
                    // because SAPI doesn't support async/await
                    await Task.Run(() =>
                    {
                        // Use System.Speech.Synthesis API (SAPI 5, which Narrator typically uses)
                        audioFile = Path.Combine(_tempDir, $"tts_system_{DateTime.Now.Ticks}.wav");
                        
                        Console.WriteLine($"Generating audio with SAPI for text: {text.Substring(0, Math.Min(50, text.Length))}...");
                        
                        // Create a new instance to avoid conflicts when generating multiple files simultaneously
                        using (var synthesizer = new SystemSpeech.SpeechSynthesizer())
                        {
                            // Set the voice
                            synthesizer.SelectVoice(voiceId);
                            
                            // Set speech rate for SAPI
                            synthesizer.Rate = _speechRate;
                            
                            Console.WriteLine($"SAPI speech rate set to: {_speechRate}");
                            
                            // Set output to audio file
                            synthesizer.SetOutputToWaveFile(audioFile);
                            
                            // Speak the text directly without using SSML
                            synthesizer.Speak(text);
                            
                            // Reset output to null to close the file
                            synthesizer.SetOutputToNull();
                        }
                    });
                }
                
                // Track this file for deletion
                lock (_tempFilesToDelete)
                {
                    if (!_tempFilesToDelete.Contains(audioFile))
                    {
                        _tempFilesToDelete.Add(audioFile);
                    }
                }
                
                Console.WriteLine($"Audio file generated: {audioFile}");
                return audioFile;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error generating audio file: {ex.Message}");
                return string.Empty;
            }
        }
        
        // Add audio file to playback queue
        private void EnqueueAudioFile(string audioFilePath)
        {
            if (string.IsNullOrEmpty(audioFilePath) || !File.Exists(audioFilePath))
            {
                Console.WriteLine($"Cannot enqueue invalid audio file: {audioFilePath}");
                return;
            }
            
            lock (_audioFileQueue)
            {
                // Mark file as active
                lock (_activeAudioFiles)
                {
                    _activeAudioFiles.Add(audioFilePath);
                }
                
                // Add file to queue
                _audioFileQueue.Enqueue(audioFilePath);
                Console.WriteLine($"Audio file enqueued: {audioFilePath}. Queue size: {_audioFileQueue.Count}");
                
                // If no queue processing is running, start a new one
                if (!_isProcessingQueue)
                {
                    Task.Run(ProcessAudioQueueAsync);
                }
            }
        }
        
        // Process audio playback queue
        private async Task ProcessAudioQueueAsync()
        {
            lock (_audioFileQueue)
            {
                if (_isProcessingQueue)
                {
                    return; // A processing task is already running
                }
                _isProcessingQueue = true;
            }
            
            try
            {
                while (true)
                {
                    string? audioFilePath = null;
                    
                    lock (_audioFileQueue)
                    {
                        if (_audioFileQueue.Count == 0)
                        {
                            _isProcessingQueue = false;
                            return; // Queue is empty, end processing
                        }
                        
                        // Get next file from queue
                        audioFilePath = _audioFileQueue.Dequeue();
                    }
                    
                    if (!string.IsNullOrEmpty(audioFilePath) && File.Exists(audioFilePath))
                    {
                        // Stop current playback (if any)
                        StopCurrentPlayback();
                        
                        // Wait for semaphore to ensure only one playback process at a time
                        await _playbackSemaphore.WaitAsync();
                        
                        try
                        {
                            // Play the audio file
                            await PlayAudioFileAsync(audioFilePath);
                        }
                        finally
                        {
                            // Release semaphore
                            _playbackSemaphore.Release();
                        }
                    }
                    else
                    {
                        Console.WriteLine($"Skipping invalid audio file: {audioFilePath}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error processing audio queue: {ex.Message}");
                
                lock (_audioFileQueue)
                {
                    _isProcessingQueue = false;
                }
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
        
        // Stop any current playback
        private void StopCurrentPlayback()
        {
            if (_isPlayingAudio)
            {
                try
                {
                    Console.WriteLine("Stopping current audio playback");
                    
                    // Cancel token to signal playback stop
                    if (_playbackCancellationTokenSource != null)
                    {
                        _playbackCancellationTokenSource.Cancel();
                        _playbackCancellationTokenSource.Dispose();
                        _playbackCancellationTokenSource = null;
                    }
                    
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
                
                // Create a new cancellation token source
                _playbackCancellationTokenSource = new CancellationTokenSource();
                var cancellationToken = _playbackCancellationTokenSource.Token;
                
                // Create a WaveOut device with low latency settings
                _currentPlayer = new WaveOutEvent
                {
                    DesiredLatency = 100 // Reduce latency to 100ms (default is 300ms)
                };
                
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
                    
                    // Remove file from active files list
                    lock (_activeAudioFiles)
                    {
                        _activeAudioFiles.Remove(filePath);
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
                
                // Register cancellation
                cancellationToken.Register(() =>
                {
                    if (_currentPlayer != null && _isPlayingAudio)
                    {
                        Console.WriteLine("Playback cancelled");
                        _currentPlayer.Stop();
                    }
                });
                
                // Wait for the playback to complete
                return await tcs.Task;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error playing audio file: {ex.Message}");
                
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
                
                // Remove file from active files list
                lock (_activeAudioFiles)
                {
                    _activeAudioFiles.Remove(filePath);
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
                            // Find the voiceKey corresponding to this voice ID
                            foreach (var voicePair in AvailableVoices)
                            {
                                if (voicePair.Value == pair.Key)
                                {
                                    return voicePair.Key;
                                }
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
        
        // Public method to stop all TTS activities (for use from MainWindow)
        public static void StopAllTTS()
        {
            try
            {
                Console.WriteLine("Stopping all TTS activities");

                // Stop current playback
                if (_instance != null)
                {
                    _instance.StopCurrentPlayback();
                }

                // Clear the audio queue
                lock (_audioFileQueue)
                {
                    Console.WriteLine($"Clearing audio queue due to stop request. {_audioFileQueue.Count} items removed.");

                    // Get all files in the queue for deletion
                    List<string> filesToDelete = new List<string>(_audioFileQueue);

                    // Remove all files in the queue from active files list
                    lock (_activeAudioFiles)
                    {
                        foreach (string file in _audioFileQueue)
                        {
                            _activeAudioFiles.Remove(file);
                        }
                    }

                    _audioFileQueue.Clear();

                    // Delete all queued audio files
                    foreach (string file in filesToDelete)
                    {
                        if (File.Exists(file))
                        {
                            try
                            {
                                // Force garbage collection to release any file handles
                                GC.Collect();
                                GC.WaitForPendingFinalizers();
                                
                                File.Delete(file);
                                Console.WriteLine($"Deleted queued audio file: {file}");
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"Failed to delete queued audio file {file}: {ex.Message}");
                                
                                // Add to tracking list for future cleanup
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
                }

                // Reset processing flag to allow new playback
                _isProcessingQueue = false;

                // Force immediate cleanup of all temp files
                Task.Run(() => {
                    try
                    {
                        // Delete all files in the temp directory that match our pattern
                        if (Directory.Exists(_tempDir))
                        {
                            string[] tempFiles = Directory.GetFiles(_tempDir, "tts_*.wav");
                            
                            foreach (string file in tempFiles)
                            {
                                // Skip files that are still active
                                bool isActive = false;
                                lock (_activeAudioFiles)
                                {
                                    isActive = _activeAudioFiles.Contains(file);
                                }
                                
                                if (!isActive)
                                {
                                    try
                                    {
                                        // Ensure the file isn't locked
                                        GC.Collect();
                                        GC.WaitForPendingFinalizers();
                                        
                                        File.Delete(file);
                                        Console.WriteLine($"Deleted temp audio file during stop: {file}");
                                    }
                                    catch (Exception ex)
                                    {
                                        Console.WriteLine($"Failed to delete temp file during stop {file}: {ex.Message}");
                                        
                                        // Add to tracking list for future cleanup
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
                            
                            Console.WriteLine($"Cleaned up temp files during stop: {tempFiles.Length - _activeAudioFiles.Count} files processed");
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error during temp file cleanup on stop: {ex.Message}");
                    }
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error stopping TTS activities: {ex.Message}");
            }
        }
        
        // Force cleanup of all temp files
        public static void ForceCleanupTempFiles()
        {
            if (_instance != null)
            {
                _instance.CleanupTempFiles();
            }
        }
        
        // Method to get and set speech rate
        public static int GetSpeechRate()
        {
            return _speechRate;
        }
        
        public static void SetSpeechRate(int rate)
        {
            _speechRate = Math.Clamp(rate, MinSpeechRate, MaxSpeechRate);
        }
        
        // Method to clear the audio queue
        public static void ClearAudioQueue()
        {
            lock (_audioFileQueue)
            {
                Console.WriteLine($"Clearing audio queue. {_audioFileQueue.Count} items removed.");
                
                // Remove all files in the queue from active files list
                lock (_activeAudioFiles)
                {
                    foreach (string file in _audioFileQueue)
                    {
                        _activeAudioFiles.Remove(file);
                    }
                }
                
                _audioFileQueue.Clear();
            }
        }
    }
}