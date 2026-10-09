using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SherpaOnnx;


namespace RSTGameTranslation
{
    /// <summary>
    /// FunASR speech recognition engine based on sherpa-onnx (SenseVoice CTC model, ONNX).
    /// Runs fully in-process on CPU — no Python required.
    /// Model files (not bundled): a folder under AudioModel/FunASR containing
    /// model.int8.onnx (or model.onnx) + tokens.txt, e.g. the
    /// sherpa-onnx-sense-voice-zh-en-ja-ko-yue-2024-07-17 release.
    /// </summary>
    public class FunAsrSenseVoiceEngine : ISpeechRecognitionEngine
    {
        private OfflineRecognizer? _recognizer;

        // SenseVoice may prefix output with meta tags like <|zh|><|HAPPY|><|Speech|> — strip them
        private static readonly System.Text.RegularExpressions.Regex MetaTagPattern =
            new System.Text.RegularExpressions.Regex(
                @"<\|[^>]*\|>",
                System.Text.RegularExpressions.RegexOptions.Compiled
            );
        public void Initialize()
        {
            string modelDir = ResolveModelFolder();
            string modelPath = ResolveModelFile(modelDir);
            string tokensPath = Path.Combine(modelDir, "tokens.txt");

            Console.WriteLine($"[FunASR] Loading SenseVoice model: {modelPath}");
            Console.WriteLine($"[FunASR] Tokens: {tokensPath}");

            int configThreadCount = ConfigManager.Instance.GetWhisperThreadCount();
            int threadCount = configThreadCount > 0 ? configThreadCount : Math.Max(1, Environment.ProcessorCount);
            string language = ResolveLanguage();
            string precision = ConfigManager.Instance.GetFunAsrModelPrecision();
            Console.WriteLine($"[FunASR] Using {threadCount} threads, language: {language}, precision: {precision} (source: {ConfigManager.Instance.GetSourceLanguage()})");

            var config = new OfflineRecognizerConfig();
            config.FeatConfig.SampleRate = 16000;
            config.FeatConfig.FeatureDim = 80;
            config.ModelConfig.SenseVoice.Model = modelPath;
            config.ModelConfig.SenseVoice.Language = language;
            config.ModelConfig.SenseVoice.UseInverseTextNormalization = 1;
            config.ModelConfig.Tokens = tokensPath;
            config.ModelConfig.Provider = "cpu";
            config.ModelConfig.NumThreads = threadCount;
            config.ModelConfig.Debug = 0;
            config.DecodingMethod = "greedy_search";

            _recognizer = new OfflineRecognizer(config);
            Console.WriteLine("[FunASR] ✓ Recognizer created");
        }

        public Task<IReadOnlyList<string>> RecognizeAsync(float[] samples, CancellationToken token)
        {
            var results = new List<string>();
            if (_recognizer == null)
            {
                Console.WriteLine("[FunASR] RecognizeAsync: recognizer null, returning");
                return Task.FromResult<IReadOnlyList<string>>(results);
            }

            return Task.Run<IReadOnlyList<string>>(() =>
            {
                token.ThrowIfCancellationRequested();

                OfflineStream? stream = null;
                try
                {
                    stream = _recognizer.CreateStream();
                    stream.AcceptWaveform(16000, samples);
                    _recognizer.Decode(stream);

                    string raw = stream.Result.Text ?? "";
                    string text = MetaTagPattern.Replace(raw, "").Trim();
                    if (!string.IsNullOrEmpty(text))
                    {
                        results.Add(text);
                    }
                }
                finally
                {
                    try { stream?.Dispose(); } catch { }
                }

                return (IReadOnlyList<string>)results;
            }, token);
        }

        public void Dispose()
        {
            try { _recognizer?.Dispose(); } catch { }
            _recognizer = null;
            Console.WriteLine("[FunASR] Recognizer disposed");
        }

        /// <summary>
        /// Resolve the model folder: configured name under AudioModel/FunASR,
        /// otherwise the first subfolder that looks like a SenseVoice model.
        /// </summary>
        private string ResolveModelFolder()
        {
            string root = ConfigManager.Instance._funAsrModelFolderPath;
            string configured = ConfigManager.Instance.GetFunAsrModel();

            if (!string.IsNullOrWhiteSpace(configured))
            {
                // Sentinel from the settings UI meaning "model files are in the FunASR root"
                if (configured == "(FunASR root)")
                {
                    if (IsValidModelFolder(root)) return root;
                }
                else
                {
                    string dir = Path.Combine(root, configured);
                    if (Directory.Exists(dir) && IsValidModelFolder(dir))
                    {
                        return dir;
                    }
                }
                // Config may store a file name (legacy) or the model may sit in the FunASR root —
                // fall through to auto-selection below
                Console.WriteLine($"[FunASR] Configured model '{configured}' is not a valid model folder, auto-selecting");
            }

            if (Directory.Exists(root))
            {
                // Case 1: model files placed directly in AudioModel/FunASR root
                if (IsValidModelFolder(root))
                {
                    Console.WriteLine($"[FunASR] Auto-selected model folder: {root}");
                    return root;
                }

                // Case 2: model in a subfolder of AudioModel/FunASR
                foreach (string dir in Directory.GetDirectories(root).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
                {
                    if (IsValidModelFolder(dir))
                    {
                        Console.WriteLine($"[FunASR] Auto-selected model folder: {dir}");
                        return dir;
                    }
                }
            }

            throw new DirectoryNotFoundException(
                $"[FunASR] No SenseVoice model found in '{root}'. " +
                "Download sherpa-onnx-sense-voice-zh-en-ja-ko-yue-2024-07-17.tar.bz2 from " +
                "https://github.com/k2-fsa/sherpa-onnx/releases/tag/asr-models and extract it there.");
        }

        private bool IsValidModelFolder(string dir)
        {
            return File.Exists(Path.Combine(dir, "tokens.txt")) &&
                   (File.Exists(Path.Combine(dir, "model.int8.onnx")) ||
                    File.Exists(Path.Combine(dir, "model.onnx")));
        }


        private string ResolveModelFile(string modelDir)
        {
            // Honor the precision setting: fp32 -> model.onnx, int8 -> model.int8.onnx.
            // Fall back to whichever file is present if the preferred one is missing.
            string precision = ConfigManager.Instance.GetFunAsrModelPrecision();
            if (string.Equals(precision, "int8", StringComparison.OrdinalIgnoreCase))
            {
                string int8 = Path.Combine(modelDir, "model.int8.onnx");
                if (File.Exists(int8)) return int8;
                // fall back to fp32 if int8 unavailable
                Console.WriteLine("[FunASR] int8 preferred but model.int8.onnx not found, falling back to fp32");
            }
            else
            {
                string fp32 = Path.Combine(modelDir, "model.onnx");
                if (File.Exists(fp32)) return fp32;
                // fall back to int8 if fp32 unavailable
                Console.WriteLine("[FunASR] fp32 preferred but model.onnx not found, falling back to int8");
            }

            // Last resort: prefer int8 (smaller) then fp32
            string int8File = Path.Combine(modelDir, "model.int8.onnx");
            if (File.Exists(int8File)) return int8File;
            return Path.Combine(modelDir, "model.onnx");
        }

        /// <summary>
        /// Resolve the SenseVoice language: explicit config ("auto" or a code) wins;
        /// otherwise map from the app's source_language setting.
        /// </summary>
        private string ResolveLanguage()
        {
            string configured = ConfigManager.Instance.GetFunAsrLanguage();
            if (!string.IsNullOrWhiteSpace(configured) && !string.Equals(configured, "auto", StringComparison.OrdinalIgnoreCase))
            {
                return configured.ToLowerInvariant();
            }
            // SenseVoice supports zh/en/ja/ko/yue; anything else falls back to "auto"
            return SpeechLanguageCodes.ToSenseVoiceCode(ConfigManager.Instance.GetSourceLanguage());
        }
    }
}
