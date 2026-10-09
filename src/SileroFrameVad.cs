using System;
using System.IO;
using SherpaOnnx;

namespace RSTGameTranslation
{
    /// <summary>
    /// Per-frame speech classifier backed by Silero VAD (via sherpa-onnx).
    /// Game audio almost always has music or effects under the dialogue, so an RMS threshold
    /// sees "voice" nearly all the time and utterances only end at the hard limit. Silero is a
    /// small neural model trained to tell human speech from everything else.
    /// Only the speech/non-speech decision is used — segmentation (pre-roll, pause length,
    /// hard cut) stays in localWhisperService, so behavior is the same as the RMS path apart
    /// from what counts as voice.
    /// </summary>
    public sealed class SileroFrameVad : IDisposable
    {
        public const string ModelFileName = "silero_vad.onnx";

        private readonly VoiceActivityDetector detector;
        private float[] chunk = Array.Empty<float>();

        private SileroFrameVad(VoiceActivityDetector detector)
        {
            this.detector = detector;
        }

        /// <summary>
        /// Create the VAD from AudioModel/silero_vad.onnx. Returns null (caller falls back to
        /// RMS) when the model is missing or fails to load.
        /// </summary>
        public static SileroFrameVad? TryCreate(float threshold)
        {
            string modelPath = Path.Combine(ConfigManager.Instance._audioProcessingModelFolderPath, ModelFileName);
            if (!File.Exists(modelPath))
            {
                Console.WriteLine($"[VAD] Silero model not found at {modelPath}, falling back to RMS");
                return null;
            }

            try
            {
                var config = new VadModelConfig();
                config.SileroVad.Model = modelPath;
                config.SileroVad.Threshold = threshold;
                // Keep sherpa's own smoothing short: pause length is decided by the caller
                // (SilenceDurationMs), so a long internal min-silence would just add delay.
                config.SileroVad.MinSilenceDuration = 0.1f;
                config.SileroVad.MinSpeechDuration = 0.1f;
                // Above the caller's 28s hard limit so sherpa never splits on its own
                config.SileroVad.MaxSpeechDuration = 60f;
                config.SileroVad.WindowSize = 512;
                config.SampleRate = 16000;
                config.NumThreads = 1;
                config.Provider = "cpu";
                config.Debug = 0;

                var detector = new VoiceActivityDetector(config, 60);
                Console.WriteLine($"[VAD] Silero VAD loaded: {modelPath} (threshold {threshold:F2})");
                return new SileroFrameVad(detector);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[VAD] Failed to load Silero VAD, falling back to RMS: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Feed one frame of 16 kHz mono audio and return whether speech is currently detected.
        /// </summary>
        public bool IsVoice(float[] frame, int count)
        {
            if (chunk.Length != count) chunk = new float[count];
            Array.Copy(frame, chunk, count);
            detector.AcceptWaveform(chunk);

            // Finished segments are queued inside the detector; they are not used here, so drop
            // them to keep memory flat.
            if (!detector.IsEmpty()) detector.Clear();

            return detector.IsSpeechDetected();
        }

        public void Dispose()
        {
            try { detector.Dispose(); } catch { }
        }
    }
}
