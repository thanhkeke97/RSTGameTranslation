using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace RSTGameTranslation
{
    // One shared instance keeps throttling and seed caching consistent across service instances.
    internal sealed class GoogleTranslateFreeClient
    {
        private const string WebOrigin = "https://translate.google.com.hk/";
        private const int RequestTimeoutMs = 4000;
        private const int SeedTimeoutMs = 1500;
        private readonly HttpClient _httpClient;
        private readonly SemaphoreSlim _seedGate = new SemaphoreSlim(1, 1);
        private readonly object _throttleLock = new object();
        private DateTime _nextRequestUtc = DateTime.MinValue;
        private DateTime _cooldownUntilUtc = DateTime.MinValue;
        private string? _seed;
        private DateTime _seedRefreshAfterUtc = DateTime.MinValue;

        internal GoogleTranslateFreeClient(HttpClient httpClient) => _httpClient = httpClient;

        internal async Task<string?> TranslateAsync(string text, string source, string target, CancellationToken cancellationToken)
        {
            try
            {
                // gtx first, signed webapp second, then the existing anonymous endpoint.
                // These endpoints do not imply independent quotas.
                for (int endpoint = 0; endpoint < 3; endpoint++)
                {
                    bool refreshSeed = false;
                    for (int attempt = 0; attempt < 2; attempt++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        string? seed = endpoint == 1
                            ? await GetSeedAsync(refreshSeed, cancellationToken) : null;
                        if (endpoint == 1 && seed == null)
                            break;

                        string origin = endpoint == 1 ? WebOrigin : "https://translate.googleapis.com/";
                        string client = endpoint == 0 ? "client=gtx&" : endpoint == 1 ? "client=webapp&" : "";
                        string url = origin + "translate_a/single?" + client
                            + "sl=" + Uri.EscapeDataString(source) + "&tl=" + Uri.EscapeDataString(target)
                            + "&dt=t&q=" + Uri.EscapeDataString(text);
                        if (seed != null)
                            url += "&tk=" + GoogleTranslateWebToken.Create(text, seed);

                        using var requestCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                        requestCts.CancelAfter(RequestTimeoutMs);
                        try
                        {
                            if (!await ThrottleAsync(requestCts.Token))
                                return null;
                            using var request = CreateRequest(url);
                            if (endpoint == 1)
                                request.Headers.Referrer = new Uri(WebOrigin);
                            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, requestCts.Token);

                            if (response.IsSuccessStatusCode)
                            {
                                // The timeout must cover the body, not just response headers.
                                string body = await response.Content.ReadAsStringAsync(requestCts.Token);
                                string? translated = ParseResponse(body);
                                if (!string.IsNullOrWhiteSpace(translated))
                                    return translated;
                                Console.WriteLine($"Google Translate endpoint {endpoint + 1}: invalid/empty response.");
                                break;
                            }

                            Console.WriteLine($"Google Translate endpoint {endpoint + 1}: HTTP {(int)response.StatusCode}.");
                            if (endpoint == 1 && response.StatusCode == HttpStatusCode.Forbidden)
                            {
                                await InvalidateSeedAsync(seed!, cancellationToken);
                                refreshSeed = true;
                                continue;
                            }

                            if ((int)response.StatusCode == 429 || (int)response.StatusCode >= 500)
                            {
                                if (ApplyServerCooldown(response))
                                    return null;
                                if (attempt == 0)
                                {
                                    await Task.Delay(250 + Random.Shared.Next(150), cancellationToken);
                                    continue;
                                }
                            }
                            break;
                        }
                        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                        {
                            Console.WriteLine($"Google Translate endpoint {endpoint + 1}: request timed out.");
                            break; // Leave time for the fallback within the shared budget.
                        }
                        catch (HttpRequestException ex)
                        {
                            Console.WriteLine($"Google Translate endpoint {endpoint + 1}: {ex.Message}");
                            break;
                        }
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                Console.WriteLine("Google Translate free translation budget exhausted.");
            }
            return null;
        }

        private async Task<string?> GetSeedAsync(bool forceRefresh, CancellationToken cancellationToken)
        {
            await _seedGate.WaitAsync(cancellationToken);
            try
            {
                if (!forceRefresh && DateTime.UtcNow < _seedRefreshAfterUtc)
                    return _seed;

                _seed = null;
                // Missing TKK is normal on newer Google pages. Do not fetch the page per OCR block.
                _seedRefreshAfterUtc = DateTime.UtcNow.AddMinutes(5);
                using var requestCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                requestCts.CancelAfter(SeedTimeoutMs);
                try
                {
                    if (!await ThrottleAsync(requestCts.Token))
                        return null;
                    using var request = CreateRequest(WebOrigin);
                    using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, requestCts.Token);
                    if (!response.IsSuccessStatusCode)
                    {
                        Console.WriteLine($"Google Web: seed request returned HTTP {(int)response.StatusCode}; skipping signed fallback.");
                        if ((int)response.StatusCode == 429 || (int)response.StatusCode >= 500)
                            ApplyServerCooldown(response);
                        return null;
                    }
                    string html = await response.Content.ReadAsStringAsync(requestCts.Token);
                    _seed = GoogleTranslateWebToken.ExtractSeed(html);
                    if (_seed != null)
                        _seedRefreshAfterUtc = DateTime.UtcNow.AddMinutes(30);
                    else
                        Console.WriteLine("Google Web: TKK unavailable; skipping signed fallback for 5 minutes.");
                    return _seed;
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    Console.WriteLine("Google Web: seed request timed out; skipping signed fallback.");
                    return null;
                }
                catch (HttpRequestException ex)
                {
                    Console.WriteLine($"Google Web: seed request failed: {ex.Message}");
                    return null;
                }
                catch (RegexMatchTimeoutException)
                {
                    Console.WriteLine("Google Web: seed parsing timed out; skipping signed fallback.");
                    return null;
                }
            }
            finally
            {
                _seedGate.Release();
            }
        }

        private async Task InvalidateSeedAsync(string seed, CancellationToken cancellationToken)
        {
            await _seedGate.WaitAsync(cancellationToken);
            try
            {
                if (_seed == seed)
                {
                    _seed = null;
                    _seedRefreshAfterUtc = DateTime.UtcNow.AddMinutes(5);
                }
            }
            finally
            {
                _seedGate.Release();
            }
        }

        private static HttpRequestMessage CreateRequest(string url) => new HttpRequestMessage(HttpMethod.Get, url)
        {
            // Google can reject .NET HTTP/1.1 requests with 429 while accepting HTTP/2.
            // Set this on the message: HttpClient defaults do not apply to SendAsync(request).
            Version = HttpVersion.Version20,
            VersionPolicy = HttpVersionPolicy.RequestVersionOrLower
        };

        private bool ApplyServerCooldown(HttpResponseMessage response)
        {
            TimeSpan? delay = response.Headers.RetryAfter?.Delta;
            if (delay == null && response.Headers.RetryAfter?.Date is DateTimeOffset date)
                delay = date - DateTimeOffset.UtcNow;
            if (delay == null || delay <= TimeSpan.Zero)
                return false;
            // Honor server cooldown across blocks/calls without keeping the UI waiting.
            lock (_throttleLock)
            {
                DateTime until = DateTime.UtcNow.Add(delay.Value);
                if (until > _cooldownUntilUtc)
                    _cooldownUntilUtc = until;
            }
            return true;
        }

        private async Task<bool> ThrottleAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            TimeSpan delay;
            lock (_throttleLock)
            {
                DateTime now = DateTime.UtcNow;
                if (now < _cooldownUntilUtc)
                    return false;
                DateTime scheduled = now > _nextRequestUtc ? now : _nextRequestUtc;
                delay = scheduled - now;
                _nextRequestUtc = scheduled.AddMilliseconds(200);
            }
            if (delay > TimeSpan.Zero)
                await Task.Delay(delay, cancellationToken);
            // Another request may have received Retry-After while this one was queued.
            lock (_throttleLock)
                return DateTime.UtcNow >= _cooldownUntilUtc;
        }

        internal static string? ParseResponse(string body)
        {
            try
            {
                using JsonDocument doc = JsonDocument.Parse(body);
                JsonElement root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() == 0
                    || root[0].ValueKind != JsonValueKind.Array)
                    return null;
                var result = new StringBuilder();
                foreach (JsonElement segment in root[0].EnumerateArray())
                {
                    if (segment.ValueKind == JsonValueKind.Array && segment.GetArrayLength() > 0
                        && segment[0].ValueKind == JsonValueKind.String)
                        result.Append(segment[0].GetString());
                }
                return result.Length == 0 ? null : result.ToString();
            }
            catch (JsonException)
            {
                return null;
            }
        }
    }
}
