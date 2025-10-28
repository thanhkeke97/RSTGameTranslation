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
using System.Collections.Generic;
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
            { "Chinese (Male) - Neural2", "cmn-CN-Neural2-C" },

            // Vietnamese - Neural2 voices
            { "Vietnamese (Female) - Neural2", "vi-VN-Neural2-A" },
            { "Vietnamese (Male) - Neural2", "vi-VN-Neural2-D" }
        };
        
        // Semaphore to ensure only one speech request is processed at a time
        private static readonly SemaphoreSlim _speechSemaphore = new SemaphoreSlim(1, 1);
        
        // Semaphore for playback to ensure only one playback process at a time
        private static readonly SemaphoreSlim _playbackSemaphore = new SemaphoreSlim(1, 1);
        
        // Queue for pending audio files to play
        private static readonly Queue<string> _audioFileQueue = new Queue<string>();
        
        // Flag to track if we're currently playing audio
        private static bool _isPlayingAudio = false;
        
        // Flag to track if we're currently processing the audio queue
        private static bool _isProcessingQueue = false;
        
        // Current audio player
        private static IWavePlayer? _currentPlayer = null;
        
        // Current audio file reader
        private static AudioFileReader? _currentAudioFile = null;
        
        // Path to temp directory
        private static readonly string _tempDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "temp");
        
        // List to track temporary files that need to be deleted
        private static readonly List<string> _tempFilesToDelete = new List<string>();
        
        // Timer to periodically clean up temp files
        private static System.Timers.Timer? _cleanupTimer;
        
        // Cancellation token source for stopping current playback
        private static CancellationTokenSource? _playbackCancellationTokenSource = null;
        
        // Set of active audio files (currently being played or in queue)
        private static readonly HashSet<string> _activeAudioFiles = new HashSet<string>();
        
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
            
            // Ensure temp directory exists
            Directory.CreateDirectory(_tempDir);
            
            // Clean up any old temp files
            CleanupTempFiles();
            
            // Set up a timer to periodically clean up temp files
            _cleanupTimer = new System.Timers.Timer(30000); // 30 seconds
            _cleanupTimer.Elapsed += (sender, e) => CleanupTempFiles();
            _cleanupTimer.Start();
            
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
                    string[] tempFiles = Directory.GetFiles(_tempDir, "tts_google_*.mp3");
                    
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
                
                // Then, search for any other .mp3 files in the temp directory
                if (Directory.Exists(_tempDir))
                {
                    string[] tempFiles = Directory.GetFiles(_tempDir, "tts_google_*.mp3");
                    
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
                
                // Process text to reduce pauses between lines
                string processedText = ProcessTextForSpeech(text);
                
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
                
                // Generate audio file asynchronously
                string audioFilePath = await GenerateAudioFileAsync(processedText, voice, languageCode, apiKey);
                
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
        
        // Generate audio file asynchronously
        private async Task<string> GenerateAudioFileAsync(string text, string voiceId, string languageCode, string apiKey)
        {
            try
            {
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
                        name = voiceId
                    },
                    audioConfig = new
                    {
                        audioEncoding = "MP3"
                    }
                };
                
                Console.WriteLine($"Using voice '{voiceId}' with language code '{languageCode}'");
                
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
                            Directory.CreateDirectory(_tempDir); // Create directory if it doesn't exist
                            string audioFile = Path.Combine(_tempDir, $"tts_google_{DateTime.Now.Ticks}.mp3");
                            
                            // Save audio to file
                            await File.WriteAllBytesAsync(audioFile, audioBytes);
                            
                            Console.WriteLine($"Audio saved to {audioFile}");
                            
                            // Track this file for deletion
                            lock (_tempFilesToDelete)
                            {
                                if (!_tempFilesToDelete.Contains(audioFile))
                                {
                                    _tempFilesToDelete.Add(audioFile);
                                }
                            }
                            
                            return audioFile;
                        }
                        else
                        {
                            Console.WriteLine("Empty audio content received from Google TTS");
                            return string.Empty;
                        }
                    }
                    else
                    {
                        Console.WriteLine("No audioContent field found in Google TTS response");
                        return string.Empty;
                    }
                }
                else
                {
                    string errorContent = await response.Content.ReadAsStringAsync();
                    Console.WriteLine($"Google TTS request failed: {response.StatusCode}. Details: {errorContent}");
                    return string.Empty;
                }
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
        
        // Play audio file asynchronously
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
        
        // Public method to stop all TTS activities (for use from MainWindow)
        public static void StopAllTTS()
        {
            try
            {
                Console.WriteLine("Stopping all Google TTS activities");

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
                            string[] tempFiles = Directory.GetFiles(_tempDir, "tts_google_*.mp3");
                            
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
                Console.WriteLine($"Error stopping Google TTS activities: {ex.Message}");
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
        
        // Method to clear the audio queue
        public static void ClearAudioQueue()
        {
            lock (_audioFileQueue)
            {
                Console.WriteLine($"Clearing Google TTS audio queue. {_audioFileQueue.Count} items removed.");
                
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