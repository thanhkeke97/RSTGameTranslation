using System;

namespace RSTGameTranslation
{
    /// <summary>
    /// Timestamped log lines for the audio → translation → TTS pipeline, so the console shows
    /// where the time goes for each line: end of speech, recognition, translation, synthesis,
    /// playback. Grep the console for "[Timing".
    /// </summary>
    public static class AudioTiming
    {
        public static void Log(string stage, string detail = "")
        {
            Console.WriteLine($"[Timing {DateTime.Now:HH:mm:ss.fff}] {stage}{(detail.Length > 0 ? ": " + detail : "")}");
        }

        public static string Preview(string text, int max = 40)
        {
            if (string.IsNullOrEmpty(text)) return "";
            text = text.Replace('\n', ' ');
            return text.Length <= max ? text : text.Substring(0, max) + "…";
        }
    }
}
