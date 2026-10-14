using System;
using System.Diagnostics;

namespace RSTGameTranslation
{
    /// <summary>
    /// Timestamped log lines for the screen → OCR → translation → overlay pipeline, so the console
    /// shows where the time goes for each new piece of text. OCR runs several times a second, so
    /// only frames with new text are logged; later stages show the time since that text was seen.
    /// Grep the console for "[OCR timing".
    /// </summary>
    public static class OcrTiming
    {
        private static readonly Stopwatch _sinceCapture = new Stopwatch();
        private static readonly Stopwatch _sinceNewText = new Stopwatch();

        // A captured frame was handed to the OCR engine (not logged: this happens every cycle)
        public static void MarkCaptureSent() => _sinceCapture.Restart();

        // OCR returned text that differs from what is on screen; starts the per-text timeline
        public static void NewTextDetected(string detail)
        {
            long ocrMs = _sinceCapture.IsRunning ? _sinceCapture.ElapsedMilliseconds : -1;
            _sinceNewText.Restart();
            Log("1. New text from OCR", $"OCR took {ocrMs} ms; {detail}");
        }

        public static void Log(string stage, string detail = "")
        {
            string elapsed = _sinceNewText.IsRunning ? $" +{_sinceNewText.ElapsedMilliseconds}ms" : "";
            Console.WriteLine($"[OCR timing {DateTime.Now:HH:mm:ss.fff}{elapsed}] {stage}{(detail.Length > 0 ? ": " + detail : "")}");
        }

        public static string Preview(string text, int max = 40) => AudioTiming.Preview(text, max);
    }
}
