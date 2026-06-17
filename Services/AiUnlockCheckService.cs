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
                    var request = new HttpRequestMessage(HttpMethod.Post, "https://gemini.google.com/_/BardChatUi/data/batchexecute?rpcids=K4WWud");
                    request.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
                    request.Content = new FormUrlEncodedContent(new[]
                    {
                        new KeyValuePair<string, string>("f.req", "[[[\"K4WWud\",\"[[1],[\\\"en-US\\\"]]\",null,\"generic\"]]]")
                    });

                    var response = await client.SendAsync(request, ct);
                    if (!response.IsSuccessStatusCode)
                        return AiUnlockStatus.Blocked;

                    var body = await response.Content.ReadAsStringAsync(ct);

                    var jsonStart = body.IndexOf("[[", StringComparison.Ordinal);
                    if (jsonStart == -1)
                        return AiUnlockStatus.Blocked;

                    using var doc = JsonDocument.Parse(body.AsMemory(jsonStart));

                    if (!TryGetArrayItem(doc.RootElement, 0, out var outer) ||
                        !TryGetArrayItem(outer, 2, out var innerJsonElem))
                        return AiUnlockStatus.Blocked;

                    var innerJsonStr = innerJsonElem.GetString();
                    if (string.IsNullOrEmpty(innerJsonStr))
                        return AiUnlockStatus.Blocked;

                    using var innerDoc = JsonDocument.Parse(innerJsonStr);

                    if (!TryGetArrayItem(innerDoc.RootElement, 0, out var inner) ||
                        !TryGetArrayItem(inner, 0, out var locationElem))
                        return AiUnlockStatus.Blocked;

                    var location = locationElem.ValueKind == JsonValueKind.String ? locationElem.GetString() : null;
                    if (string.IsNullOrEmpty(location))
                        return AiUnlockStatus.Blocked;

                    return IsBlockedRegion(location) ? AiUnlockStatus.Blocked : AiUnlockStatus.Unlocked;
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

        private static bool TryGetArrayItem(JsonElement parent, int index, out JsonElement item)
        {
            if (parent.ValueKind == JsonValueKind.Array && parent.GetArrayLength() > index)
            {
                item = parent[index];
                return true;
            }
            item = default;
            return false;
        }

        private static readonly string[] ChinaTerms = { "China", "中国" };
        private static readonly string[] ChinaExceptions = { "Hong Kong", "香港", "Macau", "Macao", "澳门", "Taiwan", "台湾" };
        private static readonly string[] OtherBlockedTerms =
        {
            "Russia", "俄罗斯",
            "Iran", "伊朗",
            "North Korea", "朝鲜",
            "Syria", "叙利亚",
            "Cuba", "古巴"
        };

        private static bool IsBlockedRegion(string location)
        {
            bool isChinaMainland = ContainsAny(location, ChinaTerms) && !ContainsAny(location, ChinaExceptions);
            return isChinaMainland || ContainsAny(location, OtherBlockedTerms);
        }

        private static bool ContainsAny(string text, string[] terms)
        {
            foreach (var term in terms)
            {
                if (text.Contains(term, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }
    }
}
