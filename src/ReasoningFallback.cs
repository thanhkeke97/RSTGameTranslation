using System;
using System.Collections.Concurrent;

namespace RSTGameTranslation
{
    /// <summary>
    /// "Prefer speed" support for LLM services: ask the model to think as little as possible before
    /// answering. Which thinking/reasoning values a model accepts differs per model (and changes as
    /// providers release models), so a service tries the fastest option first and, when the API
    /// rejects it, steps down to the next one. The step that works is remembered per model for the
    /// rest of the session, so only the first request to a model can pay for a rejected attempt.
    /// </summary>
    public static class ReasoningFallback
    {
        private static readonly ConcurrentDictionary<string, int> _steps = new();
        private static readonly ConcurrentDictionary<string, byte> _droppedParameters = new();

        // Index of the option to try for this model (0 = fastest)
        public static int GetStep(string modelKey) => _steps.TryGetValue(modelKey, out int step) ? step : 0;

        public static void StepDown(string modelKey, int rejectedStep, string reason)
        {
            _steps[modelKey] = rejectedStep + 1;
            Console.WriteLine($"[{modelKey}] option {rejectedStep} rejected ({reason}); using the next one from now on");
        }

        // Request parameters a model rejected, so they are left out of later requests
        public static bool IsDropped(string modelKey, string parameter) => _droppedParameters.ContainsKey(modelKey + "|" + parameter);

        public static void Drop(string modelKey, string parameter)
        {
            _droppedParameters[modelKey + "|" + parameter] = 0;
            Console.WriteLine($"[{modelKey}] parameter '{parameter}' not supported; leaving it out from now on");
        }
    }
}
