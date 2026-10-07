using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Whisper.net;
using Whisper.net.LibraryLoader;

namespace RSTGameTranslation
{
    /// <summary>
    /// Enum to specify Whisper runtime types
    /// </summary>
    public enum WhisperRuntimeType
    {
        Cpu,
        Cuda,   // NVIDIA GPU
        Vulkan  // AMD/NVIDIA/Intel GPU
    }

    /// <summary>
    /// Whisper.net based speech recognition engine (extracted from localWhisperService).
    /// </summary>
    public class WhisperEngine : ISpeechRecognitionEngine
    {
        private WhisperProcessor? processor;
        private WhisperFactory? factory;

        public void Initialize()
        {
            string modelPath = ConfigManager.Instance.GetAudioProcessingModel() + ".bin";
            string fullPath = Path.Combine(ConfigManager.Instance._audioProcessingModelFolderPath, modelPath);

            // Get runtime from config
            string runtimeSetting = ConfigManager.Instance.GetWhisperRuntime();
            WhisperRuntimeType runtime = ParseRuntime(runtimeSetting);

            Console.WriteLine($"[Whisper] Loading model: {fullPath}");
            Console.WriteLine($"[Whisper] Configured runtime: {runtimeSetting} -> {runtime}");

            // Create factory with specified runtime
            factory = CreateFactoryWithRuntime(fullPath, runtime);

            string current_source_language = MapLanguageToWhisper(ConfigManager.Instance.GetSourceLanguage());

            // Get thread count from config (0 = auto, use all available cores)
            int configThreadCount = ConfigManager.Instance.GetWhisperThreadCount();
            int threadCount = configThreadCount > 0 ? configThreadCount : Math.Max(1, Environment.ProcessorCount);
            Console.WriteLine($"[Whisper] Using {threadCount} threads (config: {configThreadCount}, 0=auto)");

            var processorBuilder = factory.CreateBuilder()
                .WithLanguage(current_source_language)
                // Use Greedy sampling - much faster than Beam Search
                .WithGreedySamplingStrategy()
                .ParentBuilder
                // Optimize for speed
                .WithThreads(threadCount)
                // Disable context between segments: stops a hallucinated phrase in one segment
                // from being carried forward and repeated in every following segment.
                .WithNoContext()
                .WithMaxLastTextTokens(0)
                // Anti-repetition / anti-hallucination decode thresholds. Without these,
                // greedy decoding has no escape hatch when it enters a repetition loop —
                // it just keeps emitting the same phrase until the segment ends.
                // Temperature fallback: on detecting a degenerate result (entropy or average
                // log-probability past the thresholds below), re-decode with a higher
                // temperature. This is whisper's standard fix for stuck loops.
                .WithTemperature(0.0f)
                .WithTemperatureInc(0.2f)
                .WithEntropyThreshold(2.4f)
                .WithLogProbThreshold(-1.0f)
                // Treat a segment as silence when the no-speech probability is high, instead of
                // inventing subtitle-style filler for it.
                .WithNoSpeechThreshold(0.6f);

            processor = processorBuilder.Build();
        }

        public async Task<IReadOnlyList<string>> RecognizeAsync(float[] samples, CancellationToken token)
        {
            var results = new List<string>();
            if (processor == null)
            {
                Console.WriteLine("[Whisper] RecognizeAsync: processor null, returning");
                return results;
            }

            // whisper.cpp needs at least ~1s of audio; a shorter buffer produces garbage or
            // fails outright. Pad with silence rather than dropping the speech.
            const int minSamples = 16000;
            if (samples.Length < minSamples)
            {
                var padded = new float[minSamples];
                Array.Copy(samples, padded, samples.Length);
                samples = padded;
            }

            await foreach (var result in processor.ProcessAsync(samples).WithCancellation(token))
            {
                string text = result.Text.Trim();
                if (!string.IsNullOrEmpty(text))
                {
                    results.Add(text);
                }
            }

            return results;
        }

        public void Dispose()
        {
            try { processor?.Dispose(); } catch { }
            processor = null;
            try { factory?.Dispose(); } catch { }
            factory = null;
        }

        private string MapLanguageToWhisper(string language)
        {
            return language.ToLower() switch
            {
                "japanese" or "japan" or "ja" => "ja",
                "english" or "en" => "en",
                "chinese" or "zh" or "ch_sim" => "zh",
                "korean" or "ko" => "ko",
                "vietnamese" or "vi" => "vi",
                "french" or "fr" => "fr",
                "german" or "de" => "de",
                "spanish" or "es" => "es",
                "italian" or "it" => "it",
                "portuguese" or "pt" => "pt",
                "russian" or "ru" => "ru",
                "hindi" or "hi" => "hi",
                "indonesian" or "id" => "id",
                "polish" or "pl" => "pl",
                "arabic" or "ar" => "ar",
                "dutch" or "nl" => "nl",
                "romanian" or "ro" => "ro",
                "persian" or "farsi" or "fa" => "fa",
                "czech" or "cs" => "cs",
                "bulgarian" or "bg" => "bg",
                "thai" or "th" or "thailand" => "th",
                "traditional chinese" or "ch_tra" => "zh",
                "croatian" or "hr" => "hr",
                "hungarian" or "hu" => "hu",
                "turkish" or "tr" => "tr",
                "sinhala" or "si" => "si",
                "danish" or "da" => "da",
                "ukrainian" or "uk" => "uk",
                "finnish" or "fi" => "fi",
                "central kurdish" or "ckb" => "ckb",
                "bengali" or "bn" => "bn",
                "greek" or "el" => "el",
                "hebrew" or "he" or "iw" => "he",
                _ => language
            };
        }

        /// <summary>
        /// Parse runtime string to enum
        /// </summary>
        private WhisperRuntimeType ParseRuntime(string setting)
        {
            return setting?.ToLower() switch
            {
                "cuda" or "nvidia" => WhisperRuntimeType.Cuda,
                "vulkan" or "gpu" => WhisperRuntimeType.Vulkan,
                _ => WhisperRuntimeType.Cpu
            };
        }

        /// <summary>
        /// Create WhisperFactory with specified runtime
        /// Use RuntimeOptions.RuntimeLibraryOrder to select runtime
        /// </summary>
        private WhisperFactory CreateFactoryWithRuntime(string modelPath, WhisperRuntimeType runtime)
        {
            try
            {
                // Reset LoadedLibrary to force Whisper.net to reload with new order
                RuntimeOptions.LoadedLibrary = null;

                // Set runtime priority order based on user choice
                // NOTE: Only include runtimes you want to use in the list
                switch (runtime)
                {
                    case WhisperRuntimeType.Cuda:
                        Console.WriteLine("[Whisper] Setting runtime: CUDA only (fallback to CPU if unavailable)");
                        RuntimeOptions.RuntimeLibraryOrder = new List<RuntimeLibrary>
                        {
                            RuntimeLibrary.Cuda,
                            RuntimeLibrary.Cpu
                        };
                        break;

                    case WhisperRuntimeType.Vulkan:
                        Console.WriteLine("[Whisper] Setting runtime: Vulkan only (fallback to CPU if unavailable)");
                        RuntimeOptions.RuntimeLibraryOrder = new List<RuntimeLibrary>
                        {
                            RuntimeLibrary.Vulkan,
                            RuntimeLibrary.Cpu
                        };
                        break;

                    case WhisperRuntimeType.Cpu:
                    default:
                        // CPU only - no GPUs in the list
                        Console.WriteLine("[Whisper] Setting runtime: CPU ONLY (no GPU)");
                        RuntimeOptions.RuntimeLibraryOrder = new List<RuntimeLibrary>
                        {
                            RuntimeLibrary.Cpu,
                            RuntimeLibrary.CpuNoAvx  // Fallback if CPU does not support AVX
                        };
                        break;
                }

                Console.WriteLine($"[Whisper] RuntimeLibraryOrder set to: [{string.Join(", ", RuntimeOptions.RuntimeLibraryOrder)}]");
                Console.WriteLine($"[Whisper] Creating factory with model: {modelPath}");

                var newFactory = WhisperFactory.FromPath(modelPath);

                if (RuntimeOptions.LoadedLibrary.HasValue)
                {
                    Console.WriteLine($"[Whisper] ✓ Actually loaded runtime: {RuntimeOptions.LoadedLibrary.Value}");
                }
                else
                {
                    Console.WriteLine("[Whisper] ⚠ LoadedLibrary is null - runtime unknown");
                }

                return newFactory;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Whisper] Error creating factory with {runtime}: {ex.Message}");
                Console.WriteLine($"[Whisper] Stack trace: {ex.StackTrace}");

                // Fallback: reset to default and try again
                Console.WriteLine("[Whisper] Falling back to CPU only");
                RuntimeOptions.LoadedLibrary = null;
                RuntimeOptions.RuntimeLibraryOrder = new List<RuntimeLibrary>
                {
                    RuntimeLibrary.Cpu,
                    RuntimeLibrary.CpuNoAvx
                };
                return WhisperFactory.FromPath(modelPath);
            }
        }
    }
}
