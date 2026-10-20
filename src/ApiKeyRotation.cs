using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;

namespace RSTGameTranslation
{
    /// <summary>
    /// Switching to the next API key of a service when the current one hits a rate limit, runs
    /// out of quota or is rejected. Each request tracks the keys it already tried, so it tries
    /// every key at most once and then gives up, independently of other requests running at the
    /// same time.
    /// </summary>
    internal static class ApiKeyRotation
    {
        private static readonly object _lock = new object();

        /// <summary>
        /// Whether the error is about the key itself (rate limit, quota, invalid key), so another
        /// key may succeed
        /// </summary>
        public static bool ShouldSwitchApiKey(HttpStatusCode statusCode, string errorMessage)
        {
            if (statusCode == HttpStatusCode.Unauthorized ||  // 401 - Invalid API key
                (int)statusCode == 429 ||                     // Too Many Requests - Rate limit
                statusCode == HttpStatusCode.Forbidden)       // 403 - Quota exceeded
            {
                return true;
            }

            string lowerMessage = errorMessage.ToLowerInvariant();
            return lowerMessage.Contains("quota") ||
                   lowerMessage.Contains("rate limit") ||
                   lowerMessage.Contains("rate-limit") ||
                   lowerMessage.Contains("invalid api key") ||
                   lowerMessage.Contains("api key not found") ||
                   lowerMessage.Contains("api key invalid") ||
                   lowerMessage.Contains("api key not valid") ||  // Gemini, sent with HTTP 400
                   lowerMessage.Contains("api_key_invalid");      // Gemini error reason
        }

        /// <summary>
        /// Pick the key to retry with after <paramref name="failedKey"/> hit a key error, and make
        /// it the service's current key. Returns null once this request has tried every key.
        /// </summary>
        public static string? GetKeyToRetry(string serviceType, string failedKey, ISet<string> triedKeys)
        {
            lock (_lock)
            {
                triedKeys.Add(failedKey.Trim());

                // Another request already moved on from the failed key: use its choice rather
                // than switching again (which could go back to a key that is out of quota)
                string current = ConfigManager.Instance.GetCurrentApiKey(serviceType).Trim();
                if (current.Length > 0 && !triedKeys.Contains(current))
                {
                    return current;
                }

                // Otherwise the next key in the list this request hasn't tried
                List<string> keys = ConfigManager.Instance.GetApiKeysList(serviceType)
                    .Select(k => k.Trim())
                    .Where(k => k.Length > 0)
                    .ToList();
                int start = keys.IndexOf(failedKey.Trim());  // -1 starts from the first key
                for (int i = 1; i <= keys.Count; i++)
                {
                    string candidate = keys[(start + i) % keys.Count];
                    if (!triedKeys.Contains(candidate))
                    {
                        ConfigManager.Instance.SetCurrentApiKey(serviceType, candidate);
                        Console.WriteLine($"{serviceType}: switched API key from {MaskApiKey(failedKey)} to {MaskApiKey(candidate)}");
                        return candidate;
                    }
                }

                Console.WriteLine($"{serviceType}: all {keys.Count} API key(s) failed for this request");
                return null;
            }
        }

        /// <summary>
        /// Masked API key for logging (only the first and last 4 characters)
        /// </summary>
        public static string MaskApiKey(string apiKey)
        {
            if (string.IsNullOrEmpty(apiKey) || apiKey.Length < 8)
                return "***";
            return $"{apiKey.Substring(0, 4)}...{apiKey.Substring(apiKey.Length - 4)}";
        }
    }
}
