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
        private WhisperFactory? factory;
        private string language = "auto";
        private int threadCount = 1;

        // Whisper always encodes a fixed 30s window (1500 audio frames), even for a 2s line.
        // audio_ctx shrinks that window to fit the clip: measured ~4x faster on a 6s line with
        // identical text. It is fixed per processor, so short clips use a processor built with a
        // reduced context and longer ones fall back to the full window. Each is created lazily on
        // first use because it holds its own whisper state (sizeable on large models).
        // Buckets in seconds: most game lines are 1-5s, so that bucket matters most.
        private static readonly int[] ReducedBucketSeconds = { 5, 10 };
        private readonly WhisperProcessor?[] reducedProcessors = new WhisperProcessor?[ReducedBucketSeconds.Length];
        private WhisperProcessor? fullProcessor;
        private bool useReducedContext = true;

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

            language = SpeechLanguageCodes.ToIsoCode(ConfigManager.Instance.GetSourceLanguage());

            // Get thread count from config (0 = auto, use all available cores)
            int configThreadCount = ConfigManager.Instance.GetWhisperThreadCount();
            threadCount = configThreadCount > 0 ? configThreadCount : Math.Max(1, Environment.ProcessorCount);
            Console.WriteLine($"[Whisper] Using {threadCount} threads (config: {configThreadCount}, 0=auto)");

            useReducedContext = ConfigManager.Instance.GetWhisperReducedAudioContext();
            Console.WriteLine("[Whisper] Reduced audio context: " +
                              (useReducedContext ? $"on (buckets {string.Join("/", ReducedBucketSeconds)}s)" : "off"));

            // Build one processor up front so a broken model/runtime fails here, at start,
            // rather than on the first line of dialogue.
            if (useReducedContext) GetProcessor(16000);
            else GetProcessor(int.MaxValue);
        }

        private WhisperProcessor GetProcessor(int sampleCount)
        {
            if (factory == null) throw new InvalidOperationException("Whisper engine not initialized");

            if (useReducedContext)
            {
                for (int i = 0; i < ReducedBucketSeconds.Length; i++)
                {
                    if (sampleCount <= ReducedBucketSeconds[i] * 16000)
                    {
                        return reducedProcessors[i] ??= BuildProcessor(AudioContextFor(ReducedBucketSeconds[i]));
                    }
                }
            }
            return fullProcessor ??= BuildProcessor(0);
        }

        /// <summary>
        /// 50 audio frames per second of audio, plus ~1.3s of margin: a context that ends exactly
        /// at the audio end makes Whisper more prone to hallucinating a tail.
        /// </summary>
        private static int AudioContextFor(int seconds) => seconds * 50 + 64;

        /// <summary>
        /// A fresh builder per processor: builder options are mutable and may be shared with the
        /// processors built from them, so a shared builder could leak one bucket's audio context
        /// into another.
        /// </summary>
        /// <param name="audioContext">Encoder window in audio frames (50/s); 0 = full 30s.</param>
        private WhisperProcessor BuildProcessor(int audioContext)
        {
            if (factory == null) throw new InvalidOperationException("Whisper engine not initialized");

            var builder = factory.CreateBuilder()
                .WithLanguage(language)
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
            if (audioContext > 0) builder = builder.WithAudioContextSize(audioContext);
            return builder.Build();
        }

        public async Task<IReadOnlyList<string>> RecognizeAsync(float[] samples, CancellationToken token)
        {
            var results = new List<string>();
            if (factory == null)
            {
                Console.WriteLine("[Whisper] RecognizeAsync: engine not initialized, returning");
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

            var processor = GetProcessor(samples.Length);
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
            for (int i = 0; i < reducedProcessors.Length; i++)
            {
                try { reducedProcessors[i]?.Dispose(); } catch { }
                reducedProcessors[i] = null;
            }
            try { fullProcessor?.Dispose(); } catch { }
            fullProcessor = null;
            try { factory?.Dispose(); } catch { }
            factory = null;
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
