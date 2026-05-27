using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AnywhereWinUI.Services
{
    public enum AiUnlockStatus
    {
        Unknown,
        Unlocked,
        Blocked
    }

    public sealed class AiUnlockCheckService
    {
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

        private static readonly HashSet<string> GeminiBlockedCountries = new(StringComparer.OrdinalIgnoreCase)
        {
            "CN", // China
            "RU", // Russia
            "IR", // Iran
        };

        public async Task<AiUnlockStatus> CheckOpenAiAsync(int? httpProxyPort, CancellationToken ct = default)
        {
            try
            {
                var handler = new HttpClientHandler();
                if (httpProxyPort.HasValue)
                {
                    handler.Proxy = new WebProxy($"http://127.0.0.1:{httpProxyPort.Value}");
                    handler.UseProxy = true;
                }
                else
                {
                    handler.UseProxy = false;
                }

                using (handler)
                using (var client = new HttpClient(handler) { Timeout = Timeout })
                {
                    var response = await client.GetAsync("https://api.openai.com/", ct);
                    var body = await response.Content.ReadAsStringAsync(ct);

                    if (body.Contains("unsupported_country_region_territory", StringComparison.OrdinalIgnoreCase))
                        return AiUnlockStatus.Blocked;

                    if ((int)response.StatusCode == 403)
                        return AiUnlockStatus.Blocked;

                    return AiUnlockStatus.Unlocked;
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                return AiUnlockStatus.Blocked;
            }
        }

        public async Task<AiUnlockStatus> CheckClaudeAsync(int? httpProxyPort, CancellationToken ct = default)
        {
            try
            {
                var handler = new HttpClientHandler();
                if (httpProxyPort.HasValue)
                {
                    handler.Proxy = new WebProxy($"http://127.0.0.1:{httpProxyPort.Value}");
                    handler.UseProxy = true;
                }
                else
                {
                    handler.UseProxy = false;
                }

                using (handler)
                using (var client = new HttpClient(handler) { Timeout = Timeout })
                {
                    var request = new HttpRequestMessage(HttpMethod.Get, "https://api.anthropic.com/v1/messages");
                    var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);

                    var code = (int)response.StatusCode;

                    if (code == 401 || code == 400 || code == 405)
                        return AiUnlockStatus.Unlocked;

                    if (code == 403)
                        return AiUnlockStatus.Blocked;

                    if (code >= 200 && code < 400)
                        return AiUnlockStatus.Unlocked;

                    return AiUnlockStatus.Blocked;
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                return AiUnlockStatus.Blocked;
            }
        }

        public async Task<AiUnlockStatus> CheckGeminiAsync(int? httpProxyPort, CancellationToken ct = default)
        {
            try
            {
                var handler = new HttpClientHandler();
                if (httpProxyPort.HasValue)
                {
                    handler.Proxy = new WebProxy($"http://127.0.0.1:{httpProxyPort.Value}");
                    handler.UseProxy = true;
                }
                else
                {
                    handler.UseProxy = false;
                }

                using (handler)
                using (var client = new HttpClient(handler) { Timeout = Timeout })
                {
                    var country = await GetCountryFromCloudflareAsync(client, ct);

                    if (string.IsNullOrEmpty(country))
                        country = await GetCountryFromIpInfoAsync(client, ct);

                    if (string.IsNullOrEmpty(country))
                        return AiUnlockStatus.Blocked;

                    return GeminiBlockedCountries.Contains(country)
                        ? AiUnlockStatus.Blocked
                        : AiUnlockStatus.Unlocked;
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                return AiUnlockStatus.Blocked;
            }
        }

        private static async Task<string?> GetCountryFromCloudflareAsync(HttpClient client, CancellationToken ct)
        {
            try
            {
                var body = await client.GetStringAsync("https://www.cloudflare.com/cdn-cgi/trace", ct);
                foreach (var line in body.Split('\n'))
                {
                    if (line.StartsWith("loc=", StringComparison.OrdinalIgnoreCase))
                        return line.Substring(4).Trim();
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                // return null
            }
            return null;
        }

        private static async Task<string?> GetCountryFromIpInfoAsync(HttpClient client, CancellationToken ct)
        {
            try
            {
                var body = await client.GetStringAsync("https://ipinfo.io/json", ct);
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("country", out var prop))
                    return prop.GetString();
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                // return null
            }
            return null;
        }
    }
}
