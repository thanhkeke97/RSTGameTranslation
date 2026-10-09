using System;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace RSTGameTranslation
{
    /// <summary>
    /// Prepares the configured translation service when OCR or audio translation starts, so the
    /// first line does not pay connection setup on top of the translation itself. Measured on a
    /// cold start: Microsoft 0.3-1.4 s, Google Translate up to 3.6 s (DNS 3 s + TCP 1 s) — close
    /// to the free client's 4 s per-request timeout, which then fell through to slower fallback
    /// endpoints. Warm requests take ~0.1-0.4 s.
    /// Online services: a HEAD request to each host the service talks to; it opens and pools
    /// the HTTPS connection without calling a translation API (no quota used). Ollama: loads the
    /// model into memory, the dominant first-request cost for local LLMs.
    /// </summary>
    public static class TranslationWarmUp
    {
        /// <summary>Fire-and-forget; never throws.</summary>
        public static void Start()
        {
            string service = ConfigManager.Instance.GetCurrentTranslationService();
            _ = Task.Run(async () =>
            {
                try
                {
                    Task warmUp = service switch
                    {
                        "Microsoft" => MicrosoftLegacyTranslationService.WarmUpConnectionAsync(),
                        "Google Translate" => GoogleTranslateService.WarmUpConnectionAsync(),
                        "Gemini" => GeminiTranslationService.WarmUpConnectionAsync(),
                        "ChatGPT" => ChatGptTranslationService.WarmUpConnectionAsync(),
                        "Groq" => GroqTranslationService.WarmUpConnectionAsync(),
                        "Mistral" => MistralTranslationService.WarmUpConnectionAsync(),
                        "Yandex" => YandexTranslationService.WarmUpConnectionAsync(),
                        "Ollama" => OllamaTranslationService.PreloadModelAsync(),
                        _ => Task.CompletedTask
                    };
                    await warmUp;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[WarmUp] {service}: {ex.Message}");
                }
            });
        }

        /// <summary>
        /// Open (and pool) an HTTPS connection to each URL's host using the service's own client.
        /// Any HTTP status is fine — only the connection matters.
        /// </summary>
        public static Task OpenConnectionsAsync(HttpClient client, string service, params string[] urls)
        {
            return Task.WhenAll(urls.Select(async url =>
            {
                var timer = Stopwatch.StartNew();
                try
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                    using var request = new HttpRequestMessage(HttpMethod.Head, url);
                    using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token);
                    Console.WriteLine($"[WarmUp] {service}: connected to {new Uri(url).Host} in {timer.ElapsedMilliseconds} ms ({(int)response.StatusCode})");
                }
                catch (Exception ex)
                {
                    // Even when this request times out, the connection attempt usually keeps
                    // going and is pooled once established.
                    Console.WriteLine($"[WarmUp] {service}: {new Uri(url).Host} not ready after {timer.ElapsedMilliseconds} ms ({ex.Message})");
                }
            }));
        }
    }
}
