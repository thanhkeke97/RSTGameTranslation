// Supertonic TTS service for RSTGameTranslation
// Original Supertonic C# example: https://github.com/supertone-inc/supertonic
// Supertonic SDK (csharp/Helper.cs, csharp/ExampleONNX.cs) is MIT licensed.
// The model itself is OpenRAIL-M licensed.
//
// This file embeds a slimmed-down port of Supertonic's C# helper classes
// (adapted into namespace RSTGameTranslation.Supertonic) and wraps them
// in a singleton service that follows the same pattern as
// WindowsTTSService / GoogleTTSService / ElevenLabsService already in the
// project: a NAudio-based playback queue, semaphore for serial speech,
// temp-file cleanup, and StopAllTTS() static hook.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using NAudio.Wave;
using MessageBox = System.Windows.MessageBox;

namespace RSTGameTranslation.Supertonic
{
    // ============================================================================
    // Available languages for multilingual TTS
    // 31 languages + "na" (language-agnostic) for auto-detection
    // ============================================================================
    internal static class Languages
    {
        public static readonly string[] Available =
        {
            "en", "ko", "ja", "ar", "bg", "cs", "da", "de", "el", "es",
            "et", "fi", "fr", "hi", "hr", "hu", "id", "it", "lt", "lv",
            "nl", "pl", "pt", "ro", "ru", "sk", "sl", "sv", "tr", "uk",
            "vi", "na"
        };
    }

    // ============================================================================
    // Configuration classes
    // ============================================================================
    internal class StConfig
    {
        public AEConfig AE { get; set; } = null!;
        public TTLConfig TTL { get; set; } = null!;

        public class AEConfig
        {
            public int SampleRate { get; set; }
            public int BaseChunkSize { get; set; }
        }

        public class TTLConfig
        {
            public int ChunkCompressFactor { get; set; }
            public int LatentDim { get; set; }
        }
    }

    // ============================================================================
    // Style class - voice style embedding
    // ============================================================================
    internal class Style
    {
        public float[] Ttl { get; set; }
        public long[] TtlShape { get; set; }
        public float[] Dp { get; set; }
        public long[] DpShape { get; set; }

        public Style(float[] ttl, long[] ttlShape, float[] dp, long[] dpShape)
        {
            Ttl = ttl;
            TtlShape = ttlShape;
            Dp = dp;
            DpShape = dpShape;
        }
    }

    // ============================================================================
    // Unicode text processor
    // ============================================================================
    internal class UnicodeProcessor
    {
        private readonly Dictionary<int, long> _indexer;

        public UnicodeProcessor(string unicodeIndexerPath)
        {
            var json = File.ReadAllText(unicodeIndexerPath);
            var indexerArray = JsonSerializer.Deserialize<long[]>(json)
                ?? throw new Exception("Failed to load unicode indexer");
            _indexer = new Dictionary<int, long>();
            for (int i = 0; i < indexerArray.Length; i++)
            {
                _indexer[i] = indexerArray[i];
            }
        }

        private static string RemoveEmojis(string text)
        {
            var result = new StringBuilder();
            for (int i = 0; i < text.Length; i++)
            {
                int codePoint;
                if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
                {
                    codePoint = char.ConvertToUtf32(text[i], text[i + 1]);
                    i++;
                }
                else
                {
                    codePoint = text[i];
                }

                bool isEmoji = (codePoint >= 0x1F600 && codePoint <= 0x1F64F) ||
                               (codePoint >= 0x1F300 && codePoint <= 0x1F5FF) ||
                               (codePoint >= 0x1F680 && codePoint <= 0x1F6FF) ||
                               (codePoint >= 0x1F700 && codePoint <= 0x1F77F) ||
                               (codePoint >= 0x1F780 && codePoint <= 0x1F7FF) ||
                               (codePoint >= 0x1F800 && codePoint <= 0x1F8FF) ||
                               (codePoint >= 0x1F900 && codePoint <= 0x1F9FF) ||
                               (codePoint >= 0x1FA00 && codePoint <= 0x1FA6F) ||
                               (codePoint >= 0x1FA70 && codePoint <= 0x1FAFF) ||
                               (codePoint >= 0x2600 && codePoint <= 0x26FF) ||
                               (codePoint >= 0x2700 && codePoint <= 0x27BF) ||
                               (codePoint >= 0x1F1E6 && codePoint <= 0x1F1FF);

                if (!isEmoji)
                {
                    if (codePoint > 0xFFFF)
                        result.Append(char.ConvertFromUtf32(codePoint));
                    else
                        result.Append((char)codePoint);
                }
            }
            return result.ToString();
        }

        private string PreprocessText(string text, string lang)
        {
            text = text.Normalize(NormalizationForm.FormKD);
            text = RemoveEmojis(text);

            var replacements = new Dictionary<string, string>
            {
                {"–", "-"}, {"‑", "-"}, {"—", "-"},
                {"_", " "},
                {"\u201C", "\""}, {"\u201D", "\""},
                {"\u2018", "'"},  {"\u2019", "'"},
                {"´", "'"},  {"`", "'"},
                {"[", " "}, {"]", " "}, {"|", " "}, {"/", " "}, {"#", " "},
                {"→", " "}, {"←", " "},
            };
            foreach (var kvp in replacements)
                text = text.Replace(kvp.Key, kvp.Value);

            text = Regex.Replace(text, @"[♥☆♡©\\]", "");
            text = Regex.Replace(text, @"\s+", " ").Trim();

            if (!Regex.IsMatch(text, @"[.!?;:,'\u0022\u201C\u201D\u2018\u2019)\]}…。」』】〉》›»]$"))
                text += ".";

            if (!Languages.Available.Contains(lang))
                throw new ArgumentException($"Invalid language: {lang}");

            text = $"<{lang}>" + text + $"</{lang}>";
            return text;
        }

        private int[] TextToUnicodeValues(string text) =>
            text.Select(c => (int)c).ToArray();

        private static float[][][] GetTextMask(long[] textIdsLengths) =>
            StHelper.LengthToMask(textIdsLengths);

        public (long[][] textIds, float[][][] textMask) Call(List<string> textList, List<string> langList)
        {
            var processedTexts = textList.Select((t, i) => PreprocessText(t, langList[i])).ToList();
            var textIdsLengths = processedTexts.Select(t => (long)t.Length).ToArray();
            long maxLen = textIdsLengths.Max();

            var textIds = new long[textList.Count][];
            for (int i = 0; i < processedTexts.Count; i++)
            {
                textIds[i] = new long[maxLen];
                var unicodeVals = TextToUnicodeValues(processedTexts[i]);
                for (int j = 0; j < unicodeVals.Length; j++)
                {
                    if (_indexer.TryGetValue(unicodeVals[j], out long val))
                        textIds[i][j] = val;
                }
            }

            var textMask = GetTextMask(textIdsLengths);
            return (textIds, textMask);
        }
    }

    // ============================================================================
    // TextToSpeech - main inference engine
    // ============================================================================
    internal class TextToSpeech
    {
        private readonly StConfig _cfgs;
        private readonly UnicodeProcessor _textProcessor;
        private readonly InferenceSession _dpOrt;
        private readonly InferenceSession _textEncOrt;
        private readonly InferenceSession _vectorEstOrt;
        private readonly InferenceSession _vocoderOrt;
        public readonly int SampleRate;
        private readonly int _baseChunkSize;
        private readonly int _chunkCompressFactor;
        private readonly int _ldim;

        public TextToSpeech(
            StConfig cfgs,
            UnicodeProcessor textProcessor,
            InferenceSession dpOrt,
            InferenceSession textEncOrt,
            InferenceSession vectorEstOrt,
            InferenceSession vocoderOrt)
        {
            _cfgs = cfgs;
            _textProcessor = textProcessor;
            _dpOrt = dpOrt;
            _textEncOrt = textEncOrt;
            _vectorEstOrt = vectorEstOrt;
            _vocoderOrt = vocoderOrt;
            SampleRate = cfgs.AE.SampleRate;
            _baseChunkSize = cfgs.AE.BaseChunkSize;
            _chunkCompressFactor = cfgs.TTL.ChunkCompressFactor;
            _ldim = cfgs.TTL.LatentDim;
        }

        private (float[][][] noisyLatent, float[][][] latentMask) SampleNoisyLatent(float[] duration)
        {
            int bsz = duration.Length;
            float wavLenMax = duration.Max() * SampleRate;
            var wavLengths = duration.Select(d => (long)(d * SampleRate)).ToArray();
            int chunkSize = _baseChunkSize * _chunkCompressFactor;
            int latentLen = (int)((wavLenMax + chunkSize - 1) / chunkSize);
            int latentDim = _ldim * _chunkCompressFactor;

            var random = new Random();
            var noisyLatent = new float[bsz][][];
            for (int b = 0; b < bsz; b++)
            {
                noisyLatent[b] = new float[latentDim][];
                for (int d = 0; d < latentDim; d++)
                {
                    noisyLatent[b][d] = new float[latentLen];
                    for (int t = 0; t < latentLen; t++)
                    {
                        // Box-Muller transform for normal distribution
                        double u1 = 1.0 - random.NextDouble();
                        double u2 = 1.0 - random.NextDouble();
                        noisyLatent[b][d][t] = (float)(Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2));
                    }
                }
            }

            var latentMask = StHelper.GetLatentMask(wavLengths, _baseChunkSize, _chunkCompressFactor);
            for (int b = 0; b < bsz; b++)
                for (int d = 0; d < latentDim; d++)
                    for (int t = 0; t < latentLen; t++)
                        noisyLatent[b][d][t] *= latentMask[b][0][t];

            return (noisyLatent, latentMask);
        }

        private (float[] wav, float[] duration) _Infer(
            List<string> textList, List<string> langList, Style style, int totalStep, float speed)
        {
            int bsz = textList.Count;
            if (bsz != style.TtlShape[0])
                throw new ArgumentException("Number of texts must match number of style vectors");

            var (textIds, textMask) = _textProcessor.Call(textList, langList);
            var textIdsShape = new long[] { bsz, textIds[0].Length };
            var textMaskShape = new long[] { bsz, 1, textMask[0][0].Length };

            var textIdsTensor = StHelper.IntArrayToTensor(textIds, textIdsShape);
            var textMaskTensor = StHelper.ArrayToTensor(textMask, textMaskShape);
            var styleTtlTensor = new DenseTensor<float>(style.Ttl, style.TtlShape.Select(x => (int)x).ToArray());
            var styleDpTensor = new DenseTensor<float>(style.Dp, style.DpShape.Select(x => (int)x).ToArray());

            // Run duration predictor
            var dpInputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor("text_ids", textIdsTensor),
                NamedOnnxValue.CreateFromTensor("style_dp", styleDpTensor),
                NamedOnnxValue.CreateFromTensor("text_mask", textMaskTensor)
            };
            using var dpOutputs = _dpOrt.Run(dpInputs);
            var durOnnx = dpOutputs.First(o => o.Name == "duration").AsTensor<float>().ToArray();
            for (int i = 0; i < durOnnx.Length; i++)
                durOnnx[i] /= speed;

            // Run text encoder
            var textEncInputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor("text_ids", textIdsTensor),
                NamedOnnxValue.CreateFromTensor("style_ttl", styleTtlTensor),
                NamedOnnxValue.CreateFromTensor("text_mask", textMaskTensor)
            };
            using var textEncOutputs = _textEncOrt.Run(textEncInputs);
            var textEmbTensor = textEncOutputs.First(o => o.Name == "text_emb").AsTensor<float>();

            // Sample noisy latent
            var (xt, latentMask) = SampleNoisyLatent(durOnnx);
            var latentShape = new long[] { bsz, xt[0].Length, xt[0][0].Length };
            var latentMaskShape = new long[] { bsz, 1, latentMask[0][0].Length };

            var totalStepArray = Enumerable.Repeat((float)totalStep, bsz).ToArray();

            // Iterative denoising
            for (int step = 0; step < totalStep; step++)
            {
                var currentStepArray = Enumerable.Repeat((float)step, bsz).ToArray();

                var vectorEstInputs = new List<NamedOnnxValue>
                {
                    NamedOnnxValue.CreateFromTensor("noisy_latent", StHelper.ArrayToTensor(xt, latentShape)),
                    NamedOnnxValue.CreateFromTensor("text_emb", textEmbTensor),
                    NamedOnnxValue.CreateFromTensor("style_ttl", styleTtlTensor),
                    NamedOnnxValue.CreateFromTensor("text_mask", textMaskTensor),
                    NamedOnnxValue.CreateFromTensor("latent_mask", StHelper.ArrayToTensor(latentMask, latentMaskShape)),
                    NamedOnnxValue.CreateFromTensor("total_step", new DenseTensor<float>(totalStepArray, new int[] { bsz })),
                    NamedOnnxValue.CreateFromTensor("current_step", new DenseTensor<float>(currentStepArray, new int[] { bsz }))
                };
                using var vectorEstOutputs = _vectorEstOrt.Run(vectorEstInputs);
                var denoisedLatent = vectorEstOutputs.First(o => o.Name == "denoised_latent").AsTensor<float>();

                int idx = 0;
                for (int b = 0; b < bsz; b++)
                    for (int d = 0; d < xt[b].Length; d++)
                        for (int t = 0; t < xt[b][d].Length; t++)
                            xt[b][d][t] = denoisedLatent.GetValue(idx++);
            }

            // Run vocoder
            var vocoderInputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor("latent", StHelper.ArrayToTensor(xt, latentShape))
            };
            using var vocoderOutputs = _vocoderOrt.Run(vocoderInputs);
            var wavTensor = vocoderOutputs.First(o => o.Name == "wav_tts").AsTensor<float>();

            return (wavTensor.ToArray(), durOnnx);
        }

        public (float[] wav, float[] duration) Call(
            string text, string lang, Style style, int totalStep, float speed = 1.05f, float silenceDuration = 0.3f)
        {
            if (style.TtlShape[0] != 1)
                throw new ArgumentException("Single speaker text to speech only supports single style");

            int maxLen = (lang == "ko" || lang == "ja") ? 120 : 300;
            var textList = StHelper.ChunkText(text, maxLen);
            var wavCat = new List<float>();
            float durCat = 0.0f;

            foreach (var chunk in textList)
            {
                var (wav, duration) = _Infer(new List<string> { chunk }, new List<string> { lang }, style, totalStep, speed);

                if (wavCat.Count == 0)
                {
                    wavCat.AddRange(wav);
                    durCat = duration[0];
                }
                else
                {
                    int silenceLen = (int)(silenceDuration * SampleRate);
                    var silence = new float[silenceLen];
                    wavCat.AddRange(silence);
                    wavCat.AddRange(wav);
                    durCat += duration[0] + silenceDuration;
                }
            }

            return (wavCat.ToArray(), new float[] { durCat });
        }

        public (float[] wav, float[] duration) Batch(
            List<string> textList, List<string> langList, Style style, int totalStep, float speed = 1.05f)
        {
            return _Infer(textList, langList, style, totalStep, speed);
        }
    }

    // ============================================================================
    // Helper - utility / loader functions
    // ============================================================================
    internal static class StHelper
    {
        public static float[][][] LengthToMask(long[] lengths, long maxLen = -1)
        {
            if (maxLen == -1)
                maxLen = lengths.Max();
            var mask = new float[lengths.Length][][];
            for (int i = 0; i < lengths.Length; i++)
            {
                mask[i] = new float[1][];
                mask[i][0] = new float[maxLen];
                for (int j = 0; j < maxLen; j++)
                    mask[i][0][j] = j < lengths[i] ? 1.0f : 0.0f;
            }
            return mask;
        }

        public static float[][][] GetLatentMask(long[] wavLengths, int baseChunkSize, int chunkCompressFactor)
        {
            int latentSize = baseChunkSize * chunkCompressFactor;
            var latentLengths = wavLengths.Select(len => (len + latentSize - 1) / latentSize).ToArray();
            return LengthToMask(latentLengths);
        }

        public static InferenceSession LoadOnnx(string onnxPath, SessionOptions opts) =>
            new InferenceSession(onnxPath, opts);

        public static (InferenceSession dp, InferenceSession textEnc, InferenceSession vectorEst, InferenceSession vocoder)
            LoadOnnxAll(string onnxDir, SessionOptions opts)
        {
            var dpPath = Path.Combine(onnxDir, "duration_predictor.onnx");
            var textEncPath = Path.Combine(onnxDir, "text_encoder.onnx");
            var vectorEstPath = Path.Combine(onnxDir, "vector_estimator.onnx");
            var vocoderPath = Path.Combine(onnxDir, "vocoder.onnx");

            return (
                LoadOnnx(dpPath, opts),
                LoadOnnx(textEncPath, opts),
                LoadOnnx(vectorEstPath, opts),
                LoadOnnx(vocoderPath, opts)
            );
        }

        public static StConfig LoadCfgs(string onnxDir)
        {
            var cfgPath = Path.Combine(onnxDir, "tts.json");
            var json = File.ReadAllText(cfgPath);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            return new StConfig
            {
                AE = new StConfig.AEConfig
                {
                    SampleRate = root.GetProperty("ae").GetProperty("sample_rate").GetInt32(),
                    BaseChunkSize = root.GetProperty("ae").GetProperty("base_chunk_size").GetInt32()
                },
                TTL = new StConfig.TTLConfig
                {
                    ChunkCompressFactor = root.GetProperty("ttl").GetProperty("chunk_compress_factor").GetInt32(),
                    LatentDim = root.GetProperty("ttl").GetProperty("latent_dim").GetInt32()
                }
            };
        }

        public static UnicodeProcessor LoadTextProcessor(string onnxDir)
        {
            var unicodeIndexerPath = Path.Combine(onnxDir, "unicode_indexer.json");
            return new UnicodeProcessor(unicodeIndexerPath);
        }

        public static Style LoadVoiceStyle(List<string> voiceStylePaths, bool verbose = false)
        {
            int bsz = voiceStylePaths.Count;
            var firstJson = File.ReadAllText(voiceStylePaths[0]);
            using var firstDoc = JsonDocument.Parse(firstJson);
            var firstRoot = firstDoc.RootElement;

            var ttlDims = ParseInt64Array(firstRoot.GetProperty("style_ttl").GetProperty("dims"));
            var dpDims = ParseInt64Array(firstRoot.GetProperty("style_dp").GetProperty("dims"));
            long ttlDim1 = ttlDims[1];
            long ttlDim2 = ttlDims[2];
            long dpDim1 = dpDims[1];
            long dpDim2 = dpDims[2];

            int ttlSize = (int)(bsz * ttlDim1 * ttlDim2);
            int dpSize = (int)(bsz * dpDim1 * dpDim2);
            var ttlFlat = new float[ttlSize];
            var dpFlat = new float[dpSize];

            for (int i = 0; i < bsz; i++)
            {
                var json = File.ReadAllText(voiceStylePaths[i]);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                var ttlData3D = ParseFloat3DArray(root.GetProperty("style_ttl").GetProperty("data"));
                var ttlDataFlat = new List<float>();
                foreach (var batch in ttlData3D)
                    foreach (var row in batch)
                        ttlDataFlat.AddRange(row);

                var dpData3D = ParseFloat3DArray(root.GetProperty("style_dp").GetProperty("data"));
                var dpDataFlat = new List<float>();
                foreach (var batch in dpData3D)
                    foreach (var row in batch)
                        dpDataFlat.AddRange(row);

                int ttlOffset = (int)(i * ttlDim1 * ttlDim2);
                ttlDataFlat.CopyTo(ttlFlat, ttlOffset);
                int dpOffset = (int)(i * dpDim1 * dpDim2);
                dpDataFlat.CopyTo(dpFlat, dpOffset);
            }

            var ttlShape = new long[] { bsz, ttlDim1, ttlDim2 };
            var dpShape = new long[] { bsz, dpDim1, dpDim2 };
            if (verbose)
                Console.WriteLine($"Loaded {bsz} voice styles");
            return new Style(ttlFlat, ttlShape, dpFlat, dpShape);
        }

        private static float[][][] ParseFloat3DArray(JsonElement element)
        {
            var result = new List<float[][]>();
            foreach (var batch in element.EnumerateArray())
            {
                var batch2D = new List<float[]>();
                foreach (var row in batch.EnumerateArray())
                {
                    var rowData = new List<float>();
                    foreach (var val in row.EnumerateArray())
                        rowData.Add(val.GetSingle());
                    batch2D.Add(rowData.ToArray());
                }
                result.Add(batch2D.ToArray());
            }
            return result.ToArray();
        }

        private static long[] ParseInt64Array(JsonElement element)
        {
            var result = new List<long>();
            foreach (var val in element.EnumerateArray())
                result.Add(val.GetInt64());
            return result.ToArray();
        }

        public static void WriteWavFile(string filename, float[] audioData, int sampleRate)
        {
            using var writer = new BinaryWriter(File.Open(filename, FileMode.Create));
            int numChannels = 1;
            int bitsPerSample = 16;
            int byteRate = sampleRate * numChannels * bitsPerSample / 8;
            short blockAlign = (short)(numChannels * bitsPerSample / 8);
            int dataSize = audioData.Length * bitsPerSample / 8;

            writer.Write(Encoding.ASCII.GetBytes("RIFF"));
            writer.Write(36 + dataSize);
            writer.Write(Encoding.ASCII.GetBytes("WAVE"));

            writer.Write(Encoding.ASCII.GetBytes("fmt "));
            writer.Write(16);
            writer.Write((short)1);
            writer.Write((short)numChannels);
            writer.Write(sampleRate);
            writer.Write(byteRate);
            writer.Write(blockAlign);
            writer.Write((short)bitsPerSample);

            writer.Write(Encoding.ASCII.GetBytes("data"));
            writer.Write(dataSize);

            foreach (var sample in audioData)
            {
                float clamped = Math.Max(-1.0f, Math.Min(1.0f, sample));
                short intSample = (short)(clamped * 32767);
                writer.Write(intSample);
            }
        }

        public static DenseTensor<float> ArrayToTensor(float[][][] array, long[] dims)
        {
            var flat = new List<float>();
            foreach (var batch in array)
                foreach (var row in batch)
                    flat.AddRange(row);
            return new DenseTensor<float>(flat.ToArray(), dims.Select(x => (int)x).ToArray());
        }

        public static DenseTensor<long> IntArrayToTensor(long[][] array, long[] dims)
        {
            var flat = new List<long>();
            foreach (var row in array)
                flat.AddRange(row);
            return new DenseTensor<long>(flat.ToArray(), dims.Select(x => (int)x).ToArray());
        }

        public static List<string> ChunkText(string text, int maxLen = 300)
        {
            var chunks = new List<string>();
            var paragraphRegex = new Regex(@"\n\s*\n+");
            var paragraphs = paragraphRegex.Split(text.Trim())
                .Select(p => p.Trim())
                .Where(p => !string.IsNullOrEmpty(p))
                .ToList();

            var sentenceRegex = new Regex(
                @"(?<!Mr\.|Mrs\.|Ms\.|Dr\.|Prof\.|Sr\.|Jr\.|Ph\.D\.|etc\.|e\.g\.|i\.e\.|vs\.|Inc\.|Ltd\.|Co\.|Corp\.|St\.|Ave\.|Blvd\.)(?<!\b[A-Z]\.)(?<=[.!?])\s+");

            foreach (var paragraph in paragraphs)
            {
                var sentences = sentenceRegex.Split(paragraph);
                string currentChunk = "";
                foreach (var sentence in sentences)
                {
                    if (string.IsNullOrEmpty(sentence)) continue;
                    if (currentChunk.Length + sentence.Length + 1 <= maxLen)
                    {
                        if (!string.IsNullOrEmpty(currentChunk))
                            currentChunk += " ";
                        currentChunk += sentence;
                    }
                    else
                    {
                        if (!string.IsNullOrEmpty(currentChunk))
                            chunks.Add(currentChunk.Trim());
                        currentChunk = sentence;
                    }
                }
                if (!string.IsNullOrEmpty(currentChunk))
                    chunks.Add(currentChunk.Trim());
            }

            if (chunks.Count == 0)
                chunks.Add(text.Trim());
            return chunks;
        }
    }
}

namespace RSTGameTranslation
{
    // ============================================================================
    // SupertonicTTSService - public singleton service consumed by the rest of RST
    // ============================================================================
    public class SupertonicTTSService
    {
        private static SupertonicTTSService? _instance;
        private static readonly object _instanceLock = new object();
        public static SupertonicTTSService Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_instanceLock)
                    {
                        if (_instance == null) _instance = new SupertonicTTSService();
                    }
                }
                return _instance;
            }
        }

        // Suppress the "Supertonic model is not installed" MessageBox after the
        // first popup in this session so we don't spam the user on every chat
        // line when they've selected Supertonic but haven't downloaded yet.
        private static bool _suppressMissingModelNotice = false;
        private static readonly object _noticeLock = new object();

        // Lazy-loaded ONNX engine + style (re-loaded when voice style changes)
        private Supertonic.TextToSpeech? _tts;
        private Supertonic.Style? _style;
        private string? _styleLoadedFor;
        private readonly object _initLock = new object();

        // ---- Static playback state (mirrors WindowsTTSService) ----
        private static readonly SemaphoreSlim _speechSemaphore = new SemaphoreSlim(1, 1);
        private static readonly SemaphoreSlim _playbackSemaphore = new SemaphoreSlim(1, 1);
        private static readonly Queue<string> _audioFileQueue = new Queue<string>();
        private static readonly HashSet<string> _activeAudioFiles = new HashSet<string>();
        private static readonly List<string> _tempFilesToDelete = new List<string>();
        private static IWavePlayer? _currentPlayer = null;
        private static AudioFileReader? _currentAudioFile = null;
        private static CancellationTokenSource? _playbackCancellationTokenSource = null;
        private static bool _isPlayingAudio = false;
        private static bool _isProcessingQueue = false;
        private static readonly string _tempDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "temp");
        private static System.Timers.Timer? _cleanupTimer;

        private static readonly string[] RequiredOnnxFiles =
        {
            "duration_predictor.onnx", "text_encoder.onnx",
            "vector_estimator.onnx",  "vocoder.onnx",
            "tts.json",                "unicode_indexer.json"
        };

        private SupertonicTTSService()
        {
            try
            {
                Directory.CreateDirectory(_tempDir);
                StartCleanupTimer();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"SupertonicTTSService init: {ex.Message}");
            }
        }

        // ===================== Public API =====================

        public static string GetOnnxDir()
        {
            return Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                ConfigManager.Instance._supertonicModelFolderPath,
                "onnx");
        }

        public static string GetVoiceStylesDir()
        {
            return Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                ConfigManager.Instance._supertonicModelFolderPath,
                "voice_styles");
        }

        public static string GetModelRoot()
        {
            return Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                ConfigManager.Instance._supertonicModelFolderPath);
        }

        public static bool IsModelInstalled()
        {
            try
            {
                // Pre-create the model root dir so downstream file-exists checks
                // never crash on a fresh machine. Cheap no-op if it already exists.
                try
                {
                    string root = GetModelRoot();
                    if (!string.IsNullOrEmpty(root)) Directory.CreateDirectory(root);
                }
                catch { /* permission errors are surfaced by file-existence checks below */ }

                string onnxDir = GetOnnxDir();
                if (!Directory.Exists(onnxDir)) return false;
                foreach (var f in RequiredOnnxFiles)
                {
                    if (!File.Exists(Path.Combine(onnxDir, f))) return false;
                }
                return File.Exists(Path.Combine(GetVoiceStylesDir(), "M1.json"));
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Resets the once-per-session "model missing" notice so the next
        /// SpeakText call after the user clicks "Download model" will surface
        /// any new error.
        /// </summary>
        public static void ResetMissingModelNotice()
        {
            lock (_noticeLock) { _suppressMissingModelNotice = false; }
        }

        public static List<string> GetInstalledVoiceStyles()
        {
            var list = new List<string>();
            try
            {
                string dir = GetVoiceStylesDir();
                if (!Directory.Exists(dir)) return list;
                foreach (var f in Directory.GetFiles(dir, "*.json"))
                {
                    var name = Path.GetFileNameWithoutExtension(f);
                    if (!string.IsNullOrEmpty(name)) list.Add(name);
                }
                list.Sort();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"GetInstalledVoiceStyles: {ex.Message}");
            }
            return list;
        }

        /// <summary>
        /// Background pre-load of ONNX sessions + voice style. Call once at startup
        /// if the user has already enabled TTS and selected Supertonic, so the
        /// first SpeakText call returns instantly. Always safe to call —
        /// catches all exceptions and never propagates an unobserved task
        /// exception to the host process.
        /// </summary>
        public async Task WarmUpAsync()
        {
            try
            {
                if (!IsModelInstalled())
                {
                    Console.WriteLine("Supertonic warm-up skipped: model not installed");
                    return;
                }
                await Task.Run(() =>
                {
                    try
                    {
                        EnsureLoaded();
                        Console.WriteLine("Supertonic warm-up complete");
                    }
                    catch (Exception inner)
                    {
                        // Don't let a bad model file stop the app from starting;
                        // the next SpeakText will surface the error to the user.
                        Console.WriteLine($"Supertonic warm-up inner error: {inner.Message}");
                    }
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Supertonic warm-up error: {ex.Message}");
            }
        }

        public async Task<bool> SpeakText(string text)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(text))
                {
                    Console.WriteLine("Supertonic: cannot speak empty text");
                    return false;
                }

                if (!IsModelInstalled())
                {
                    // Only show the popup the first time per session so we
                    // don't spam the user for every chat line.
                    bool shouldShow = false;
                    lock (_noticeLock)
                    {
                        if (!_suppressMissingModelNotice)
                        {
                            _suppressMissingModelNotice = true;
                            shouldShow = true;
                        }
                    }
                    if (shouldShow)
                    {
                        try
                        {
                            System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                                MessageBox.Show(
                                    "Supertonic model is not installed.\n\nPlease open Settings → TTS and click \"Download model\".",
                                    "Supertonic model missing",
                                    MessageBoxButton.OK, MessageBoxImage.Warning));
                        }
                        catch (Exception mbx)
                        {
                            Console.WriteLine($"Supertonic: failed to show missing-model notice: {mbx.Message}");
                        }
                    }
                    return false;
                }

                if (!await _speechSemaphore.WaitAsync(0))
                {
                    Console.WriteLine("Supertonic: another speech request in progress, skipping");
                    return false;
                }

                try
                {
                    string processedText = ProcessTextForSpeech(text);
                    string lang = ResolveSupertonicLang();
                    string voiceStyle = ConfigManager.Instance.GetSupertonicVoiceStyle();
                    int totalSteps = ConfigManager.Instance.GetSupertonicTotalSteps();
                    float speed = ConfigManager.Instance.GetSupertonicSpeed();

                    if (string.IsNullOrWhiteSpace(voiceStyle)) voiceStyle = "M1";
                    if (totalSteps < 1) totalSteps = 1;
                    if (totalSteps > 32) totalSteps = 32;
                    if (speed < 0.5f) speed = 0.5f;
                    if (speed > 2.0f) speed = 2.0f;

                    // Ensure model + style are loaded (lazy). Catches errors
                    // here so the user gets a clear MessageBox instead of
                    // a silent failure that gets swallowed by callers.
                    float[] wav;
                    try
                    {
                        wav = await Task.Run(() =>
                        {
                            var tts = EnsureLoaded();
                            if (tts == null) throw new InvalidOperationException("Failed to load Supertonic TTS");
                            EnsureStyle(voiceStyle);
                            if (_style == null) throw new InvalidOperationException("Failed to load voice style");
                            var (samples, _) = tts.Call(processedText, lang, _style, totalSteps, speed, 0.3f);
                            return samples;
                        });
                    }
                    catch (Exception synthEx)
                    {
                        // Reset the missing-model notice so the user can
                        // re-trigger a popup after fixing the install.
                        ResetMissingModelNotice();
                        Console.WriteLine($"Supertonic synthesis error: {synthEx.Message}");
                        try
                        {
                            System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                                MessageBox.Show(
                                    $"Supertonic failed to synthesize speech.\n\n{synthEx.Message}\n\nThe model may be corrupted - try re-downloading in Settings → TTS.",
                                    "Supertonic synthesis error",
                                    MessageBoxButton.OK, MessageBoxImage.Warning));
                        }
                        catch { }
                        return false;
                    }

                    if (wav == null || wav.Length == 0)
                    {
                        Console.WriteLine("Supertonic: synthesis returned no audio");
                        return false;
                    }

                    string audioFilePath = Path.Combine(_tempDir, $"tts_supertonic_{DateTime.Now.Ticks}.wav");
                    await Task.Run(() => Supertonic.StHelper.WriteWavFile(audioFilePath, wav, _tts!.SampleRate));

                    lock (_tempFilesToDelete)
                    {
                        if (!_tempFilesToDelete.Contains(audioFilePath))
                            _tempFilesToDelete.Add(audioFilePath);
                    }

                    EnqueueAudioFile(audioFilePath);
                    return true;
                }
                finally
                {
                    _speechSemaphore.Release();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Supertonic SpeakText error: {ex.Message}");
                try
                {
                    System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                        MessageBox.Show(
                            $"Error with Supertonic Text-to-Speech: {ex.Message}",
                            "TTS Error", MessageBoxButton.OK, MessageBoxImage.Warning));
                }
                catch (Exception mbx)
                {
                    Console.WriteLine($"Supertonic: failed to show TTS error MessageBox: {mbx.Message}");
                }
                return false;
            }
        }

        public static void StopAllTTS()
        {
            try
            {
                Console.WriteLine("Stopping all Supertonic TTS activities");

                if (_instance != null) _instance.StopCurrentPlayback();

                lock (_audioFileQueue)
                {
                    var filesToDelete = new List<string>(_audioFileQueue);
                    lock (_activeAudioFiles)
                    {
                        foreach (var f in _audioFileQueue) _activeAudioFiles.Remove(f);
                    }
                    _audioFileQueue.Clear();
                    foreach (var f in filesToDelete)
                    {
                        try
                        {
                            if (File.Exists(f)) File.Delete(f);
                        }
                        catch
                        {
                            lock (_tempFilesToDelete)
                            {
                                if (!_tempFilesToDelete.Contains(f)) _tempFilesToDelete.Add(f);
                            }
                        }
                    }
                }
                _isProcessingQueue = false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"StopAllTTS error: {ex.Message}");
            }
        }

        // ===================== Internal =====================

        private Supertonic.TextToSpeech? EnsureLoaded()
        {
            lock (_initLock)
            {
                if (_tts != null) return _tts;
                string onnxDir = GetOnnxDir();
                if (!Directory.Exists(onnxDir))
                    throw new DirectoryNotFoundException($"ONNX dir not found: {onnxDir}");

                var opts = new SessionOptions();
                // CPU only (Supertonic does not yet support GPU via its public ONNX assets)
                var cfgs = Supertonic.StHelper.LoadCfgs(onnxDir);
                var (dp, te, ve, vo) = Supertonic.StHelper.LoadOnnxAll(onnxDir, opts);
                var tp = Supertonic.StHelper.LoadTextProcessor(onnxDir);
                _tts = new Supertonic.TextToSpeech(cfgs, tp, dp, te, ve, vo);
                Console.WriteLine($"Supertonic TTS loaded (sample rate {_tts.SampleRate} Hz)");
                return _tts;
            }
        }

        private void EnsureStyle(string styleName)
        {
            if (_style != null && _styleLoadedFor == styleName) return;
            lock (_initLock)
            {
                if (_style != null && _styleLoadedFor == styleName) return;
                string path = Path.Combine(GetVoiceStylesDir(), styleName + ".json");
                if (!File.Exists(path))
                {
                    Console.WriteLine($"Supertonic voice style not found: {path}, falling back to M1");
                    path = Path.Combine(GetVoiceStylesDir(), "M1.json");
                    if (!File.Exists(path))
                        throw new FileNotFoundException($"Default voice style not found: {path}");
                }
                _style = Supertonic.StHelper.LoadVoiceStyle(new List<string> { path }, verbose: true);
                _styleLoadedFor = styleName;
            }
        }

        private static string ResolveSupertonicLang()
        {
            try
            {
                // Prefer target_language if it matches a Supertonic code
                string target = ConfigManager.Instance.GetTargetLanguage().ToLowerInvariant();
                if (Supertonic.Languages.Available.Contains(target)) return target;

                // Otherwise use source_language (e.g. when translating from a supported lang)
                string source = ConfigManager.Instance.GetSourceLanguage().ToLowerInvariant();
                if (Supertonic.Languages.Available.Contains(source)) return source;

                return "na";
            }
            catch
            {
                return "na";
            }
        }

        private static string ProcessTextForSpeech(string text)
        {
            // Light normalization - Supertonic's UnicodeProcessor does the heavy lifting
            // (emoji removal, dash/quote normalization, language tagging, sentence
            // punctuation). We just trim whitespace.
            return text?.Trim() ?? string.Empty;
        }

        // ===================== Playback queue (mirrors WindowsTTSService) =====================

        private void EnqueueAudioFile(string audioFilePath)
        {
            if (string.IsNullOrEmpty(audioFilePath) || !File.Exists(audioFilePath))
            {
                Console.WriteLine($"Supertonic: cannot enqueue invalid audio file: {audioFilePath}");
                return;
            }
            lock (_audioFileQueue)
            {
                lock (_activeAudioFiles) _activeAudioFiles.Add(audioFilePath);
                _audioFileQueue.Enqueue(audioFilePath);
                Console.WriteLine($"Supertonic: audio enqueued ({_audioFileQueue.Count} in queue)");
                if (!_isProcessingQueue) Task.Run(ProcessAudioQueueAsync);
            }
        }

        private async Task ProcessAudioQueueAsync()
        {
            lock (_audioFileQueue)
            {
                if (_isProcessingQueue) return;
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
                            return;
                        }
                        audioFilePath = _audioFileQueue.Dequeue();
                    }
                    if (!string.IsNullOrEmpty(audioFilePath) && File.Exists(audioFilePath))
                    {
                        StopCurrentPlayback();
                        await _playbackSemaphore.WaitAsync();
                        try { await PlayAudioFileAsync(audioFilePath); }
                        finally { _playbackSemaphore.Release(); }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Supertonic queue error: {ex.Message}");
                lock (_audioFileQueue) _isProcessingQueue = false;
            }
        }

        private async Task<bool> PlayAudioFileAsync(string filePath)
        {
            var tcs = new TaskCompletionSource<bool>();
            try
            {
                _isPlayingAudio = true;
                _playbackCancellationTokenSource = new CancellationTokenSource();
                var cancellationToken = _playbackCancellationTokenSource.Token;

                _currentPlayer = new WaveOutEvent { DesiredLatency = 100 };
                _currentPlayer.PlaybackStopped += (sender, args) =>
                {
                    Console.WriteLine("Supertonic: audio playback completed");
                    _isPlayingAudio = false;
                    _currentPlayer?.Dispose();
                    _currentPlayer = null;
                    _currentAudioFile?.Dispose();
                    _currentAudioFile = null;
                    lock (_activeAudioFiles) _activeAudioFiles.Remove(filePath);
                    DeleteFileWithRetry(filePath);
                    tcs.TrySetResult(true);
                };

                _currentAudioFile = new AudioFileReader(filePath);
                _currentPlayer.Init(_currentAudioFile);
                Console.WriteLine($"Supertonic: playing {filePath}");
                _currentPlayer.Play();

                cancellationToken.Register(() =>
                {
                    if (_currentPlayer != null && _isPlayingAudio)
                    {
                        Console.WriteLine("Supertonic: playback cancelled");
                        try { _currentPlayer.Stop(); } catch { }
                    }
                });
                return await tcs.Task;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Supertonic playback error: {ex.Message}");
                _isPlayingAudio = false;
                _currentAudioFile?.Dispose(); _currentAudioFile = null;
                _currentPlayer?.Dispose();   _currentPlayer = null;
                lock (_activeAudioFiles) _activeAudioFiles.Remove(filePath);
                DeleteFileWithRetry(filePath);
                tcs.TrySetResult(false);
                return false;
            }
        }

        private void StopCurrentPlayback()
        {
            try
            {
                if (_currentPlayer != null && _isPlayingAudio)
                {
                    _playbackCancellationTokenSource?.Cancel();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"StopCurrentPlayback: {ex.Message}");
            }
        }

        private static void DeleteFileWithRetry(string filePath)
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath)) return;
            try
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                File.Delete(filePath);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Supertonic temp delete failed: {ex.Message}");
                lock (_tempFilesToDelete)
                {
                    if (!_tempFilesToDelete.Contains(filePath))
                        _tempFilesToDelete.Add(filePath);
                }
            }
        }

        private void StartCleanupTimer()
        {
            try
            {
                _cleanupTimer = new System.Timers.Timer(30000);
                _cleanupTimer.Elapsed += (s, e) => CleanupTempFiles();
                _cleanupTimer.Start();
                AppDomain.CurrentDomain.ProcessExit += (s, e) => CleanupAllTempFiles();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Cleanup timer start failed: {ex.Message}");
            }
        }

        private static void CleanupTempFiles()
        {
            try
            {
                lock (_tempFilesToDelete)
                {
                    var toRemove = new List<string>();
                    foreach (var file in _tempFilesToDelete)
                    {
                        bool active;
                        lock (_activeAudioFiles) active = _activeAudioFiles.Contains(file);
                        if (active) continue;
                        try
                        {
                            if (File.Exists(file))
                            {
                                GC.Collect(); GC.WaitForPendingFinalizers();
                                File.Delete(file);
                                toRemove.Add(file);
                            }
                        }
                        catch
                        {
                            // keep on list, retry next tick
                        }
                    }
                    foreach (var f in toRemove) _tempFilesToDelete.Remove(f);
                }

                if (!Directory.Exists(_tempDir)) return;
                foreach (var file in Directory.GetFiles(_tempDir, "tts_supertonic_*.wav"))
                {
                    bool active;
                    lock (_activeAudioFiles) active = _activeAudioFiles.Contains(file);
                    if (active) continue;
                    var info = new FileInfo(file);
                    if (DateTime.Now - info.CreationTime > TimeSpan.FromMinutes(10))
                    {
                        try
                        {
                            GC.Collect(); GC.WaitForPendingFinalizers();
                            File.Delete(file);
                        }
                        catch
                        {
                            lock (_tempFilesToDelete)
                            {
                                if (!_tempFilesToDelete.Contains(file)) _tempFilesToDelete.Add(file);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"CleanupTempFiles error: {ex.Message}");
            }
        }

        private static void CleanupAllTempFiles()
        {
            try
            {
                if (!Directory.Exists(_tempDir)) return;
                foreach (var file in Directory.GetFiles(_tempDir, "tts_supertonic_*.wav"))
                {
                    try { File.Delete(file); } catch { }
                }
            }
            catch { }
        }
    }
}
