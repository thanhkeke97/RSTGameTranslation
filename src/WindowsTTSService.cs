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
using SystemSpeech = System.Speech.Synthesis;

namespace RSTGameTranslation
{
    public class WindowsTTSService
    {
        private static WindowsTTSService? _instance;
        private SpeechSynthesizer? _synthesizer;
        private SystemSpeech.SpeechSynthesizer? _systemSynthesizer;

        // Dictionary of available voices with their names
        // This will be populated dynamically based on installed voices
        public static readonly Dictionary<string, string> AvailableVoices = new Dictionary<string, string>();

        // Dictionary to track which API each voice belongs to (UWP or System.Speech)
        // true = UWP (Windows.Media.SpeechSynthesis), false = System.Speech
        private static readonly Dictionary<string, bool> _voiceApiSource = new Dictionary<string, bool>();

        // Serializes synthesis: the UWP synthesizer is shared and its Voice/Options are mutated per call
        private static readonly SemaphoreSlim _synthesisSemaphore = new SemaphoreSlim(1, 1);

        // Clips play from memory, in order; SpeakText returns once its clip is queued
        private static readonly TtsPlaybackQueue _playback = new TtsPlaybackQueue("Windows TTS");

        // Speech rate range (-10 to 10, where 0 is normal speed); the value lives in config
        public const int MinSpeechRate = -10;
        public const int MaxSpeechRate = 10;

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
            try
            {
                _synthesizer = new SpeechSynthesizer();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to initialize WinRT SpeechSynthesizer (Windows N/KN edition or speech features disabled?): {ex.Message}");
                _synthesizer = null;
            }

            try
            {
                _systemSynthesizer = new SystemSpeech.SpeechSynthesizer();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to initialize System.Speech SpeechSynthesizer: {ex.Message}");
                _systemSynthesizer = null;
            }

            // Initialize available voices
            InitializeVoices();
        }

        private void InitializeVoices()
        {
            try
            {
                // Clear existing voices
                AvailableVoices.Clear();
                _voiceApiSource.Clear();
                
                // Get all installed voices from Windows.Media.SpeechSynthesis (UWP API)
                if (_synthesizer != null)
                {
                    try
                    {
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
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error enumerating UWP voices: {ex.Message}");
                    }
                }
                else
                {
                    Console.WriteLine("Skipping UWP voice enumeration (SpeechSynthesizer not available)");
                }
                
                // Get all installed voices from System.Speech.Synthesis (SAPI 5, which Narrator typically uses)
                if (_systemSynthesizer != null)
                {
                try
                {
                    var systemVoices = _systemSynthesizer.GetInstalledVoices();
                    
                    foreach (var voice in systemVoices)
                    {
                        if (voice.Enabled)
                        {
                            try
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
                            catch (Exception ex)
                            {
                                // Log error but continue processing other voices
                                Console.WriteLine($"Error processing individual System.Speech voice: {ex.Message}");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error getting System.Speech voices: {ex.Message}");
                }
                }
                else
                {
                    Console.WriteLine("Skipping SAPI voice enumeration (System.Speech SpeechSynthesizer not available)");
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
                        TtsErrorNotifier.ShowError("Windows", "No Windows TTS voices are available on this system.", "No Voices Available");
                        return false;
                    }
                }

                // Get the voice ID for the selected voice
                string voiceId = AvailableVoices[voiceName];

                // Check which API this voice belongs to
                bool isUwpVoice = _voiceApiSource.TryGetValue(voiceId, out bool isUwp) && isUwp;

                int rate = GetSpeechRate();
                double speedFactor = TtsPlaybackQueue.GetAutoSpeedFactor();
                Console.WriteLine($"Using TTS voice: {voiceName} (ID: {voiceId}, UWP: {isUwpVoice}) with speech rate: {rate}, auto speed x{speedFactor:F2}");

                byte[]? wav;
                await _synthesisSemaphore.WaitAsync();
                try
                {
                    wav = isUwpVoice
                        ? await SynthesizeUwpAsync(processedText, voiceId, rate, speedFactor)
                        : await Task.Run(() => SynthesizeSapi(processedText, voiceId, rate, speedFactor));
                }
                finally
                {
                    _synthesisSemaphore.Release();
                }

                if (wav == null || wav.Length == 0)
                {
                    Console.WriteLine("Failed to synthesize speech");
                    return false;
                }

                _playback.Enqueue(() => new WaveFileReader(new MemoryStream(wav, writable: false)));
                return true;
            }
            catch (Exception ex)
            {
                TtsErrorNotifier.ShowError("Windows", $"Error with Text-to-Speech: {ex.Message}");
                return false;
            }
        }

        // Windows.Media.SpeechSynthesis (UWP API) -> WAV bytes
        private async Task<byte[]?> SynthesizeUwpAsync(string text, string voiceId, int rate, double speedFactor)
        {
            if (_synthesizer == null)
            {
                Console.WriteLine("UWP SpeechSynthesizer is not available on this system");
                return null;
            }

            var selectedVoice = SpeechSynthesizer.AllVoices.FirstOrDefault(v => v.Id == voiceId);
            if (selectedVoice == null)
            {
                Console.WriteLine($"Could not find UWP voice with ID: {voiceId}");
                return null;
            }
            _synthesizer.Voice = selectedVoice;

            // UWP takes a multiplier (0.5-6.0): map the -10..10 rate onto 0.5x..2x as before, then
            // apply the auto speed-up on top.
            double uwpRate = rate >= 0 ? 1.0 + rate / 10.0 : 1.0 + rate / 20.0;
            _synthesizer.Options.SpeakingRate = Math.Clamp(uwpRate * speedFactor, 0.5, 6.0);

            // WinRT objects: dispose them, or every line leaks the synthesized audio buffer
            using SpeechSynthesisStream stream = await _synthesizer.SynthesizeTextToStreamAsync(text);
            using var inputStream = stream.GetInputStreamAt(0);
            using var dataReader = new DataReader(inputStream);
            await dataReader.LoadAsync((uint)stream.Size);
            byte[] buffer = new byte[stream.Size];
            dataReader.ReadBytes(buffer);
            return buffer;
        }

        // System.Speech (SAPI 5, which Narrator typically uses) -> WAV bytes
        private static byte[]? SynthesizeSapi(string text, string voiceId, int rate, double speedFactor)
        {
            // SAPI rate is roughly logarithmic: +10 is about 3x, so one step is about 3^(1/10)
            int speedSteps = (int)Math.Round(10 * Math.Log(speedFactor) / Math.Log(3));
            using var output = new MemoryStream();
            // A new instance per call: SAPI synthesizers are not safe to share across threads
            using (var synthesizer = new SystemSpeech.SpeechSynthesizer())
            {
                synthesizer.SelectVoice(voiceId);
                synthesizer.Rate = Math.Clamp(rate + speedSteps, MinSpeechRate, MaxSpeechRate);
                synthesizer.SetOutputToWaveStream(output);
                synthesizer.Speak(text);
                synthesizer.SetOutputToNull();
            }
            return output.ToArray();
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
                _playback.Stop();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error stopping TTS activities: {ex.Message}");
            }
        }

        // Speech rate (-10..10), stored in config
        public static int GetSpeechRate()
        {
            return Math.Clamp(ConfigManager.Instance.GetWindowsTtsSpeechRate(), MinSpeechRate, MaxSpeechRate);
        }

        public static void SetSpeechRate(int rate)
        {
            ConfigManager.Instance.SetWindowsTtsSpeechRate(Math.Clamp(rate, MinSpeechRate, MaxSpeechRate));
        }

        // Method to clear the audio queue
        public static void ClearAudioQueue()
        {
            _playback.Stop();
        }
    }
}
