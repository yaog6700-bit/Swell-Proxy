using System;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AnywhereWinUI.Services
{
    public class IpInfoResponse
    {
        public string? ip { get; set; }
        public int? asn { get; set; }
        public string? asOrganization { get; set; }
        public string? country { get; set; }
        public string? countryCode { get; set; }
        public string? region { get; set; }
        public string? city { get; set; }
    }

    public sealed class IpInfoService
    {
        private static readonly TimeSpan AttemptTimeout = TimeSpan.FromSeconds(7);
        private static readonly int[] RetryDelaysMs = { 500, 1000 };
        private const string BrowserUserAgent =
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36";

        public async Task<IpInfoResponse?> GetIpInfoAsync(int? httpProxyPort, CancellationToken ct = default)
        {
            int maxAttempts = httpProxyPort.HasValue ? RetryDelaysMs.Length + 1 : 1;

            for (int attempt = 0; attempt < maxAttempts; attempt++)
            {
                ct.ThrowIfCancellationRequested();

                var primary = await TryFetchIpPureAsync(httpProxyPort, ct).ConfigureAwait(false);
                if (primary != null && !string.IsNullOrWhiteSpace(primary.ip))
                    return primary;

                if (attempt < maxAttempts - 1)
                {
                    await Task.Delay(RetryDelaysMs[attempt], ct).ConfigureAwait(false);
                }
            }

            // Fallback provider in case my.ippure.com is rate-limited or unreachable from exit IP
            return await TryFetchIpWhoIsFallbackAsync(httpProxyPort, ct).ConfigureAwait(false);
        }

        private static async Task<IpInfoResponse?> TryFetchIpPureAsync(int? httpProxyPort, CancellationToken ct)
        {
            try
            {
                using var handler = CreateHandler(httpProxyPort);
                using var client = new HttpClient(handler) { Timeout = AttemptTimeout };
                using var attemptCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                attemptCts.CancelAfter(AttemptTimeout);

                using var request = new HttpRequestMessage(HttpMethod.Get, "https://my.ippure.com/v1/info");
                request.Headers.TryAddWithoutValidation("User-Agent", BrowserUserAgent);
                request.Headers.TryAddWithoutValidation("Accept", "application/json");

                using var response = await client.SendAsync(request, attemptCts.Token).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                    return null;

                var body = await response.Content.ReadAsStringAsync(attemptCts.Token).ConfigureAwait(false);
                return JsonSerializer.Deserialize(body, AnywhereWinUI.Models.AppJsonContext.Default.IpInfoResponse);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                return null;
            }
        }

        private static async Task<IpInfoResponse?> TryFetchIpWhoIsFallbackAsync(int? httpProxyPort, CancellationToken ct)
        {
            try
            {
                using var handler = CreateHandler(httpProxyPort);
                using var client = new HttpClient(handler) { Timeout = AttemptTimeout };
                using var attemptCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                attemptCts.CancelAfter(AttemptTimeout);

                using var request = new HttpRequestMessage(HttpMethod.Get, "https://ipwho.is/");
                request.Headers.TryAddWithoutValidation("User-Agent", BrowserUserAgent);
                request.Headers.TryAddWithoutValidation("Accept", "application/json");

                using var response = await client.SendAsync(request, attemptCts.Token).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                    return null;

                var body = await response.Content.ReadAsStringAsync(attemptCts.Token).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;

                if (root.TryGetProperty("success", out var okProp) && okProp.ValueKind == JsonValueKind.False)
                    return null;

                string? ip = root.TryGetProperty("ip", out var ipProp) ? ipProp.GetString() : null;
                if (string.IsNullOrWhiteSpace(ip))
                    return null;

                int? asn = null;
                string? org = null;
                if (root.TryGetProperty("connection", out var conn) && conn.ValueKind == JsonValueKind.Object)
                {
                    if (conn.TryGetProperty("asn", out var asnProp) && asnProp.TryGetInt32(out int asnVal))
                        asn = asnVal;
                    if (conn.TryGetProperty("org", out var orgProp) && orgProp.ValueKind == JsonValueKind.String)
                        org = orgProp.GetString();
                    if (string.IsNullOrWhiteSpace(org) && conn.TryGetProperty("isp", out var ispProp) && ispProp.ValueKind == JsonValueKind.String)
                        org = ispProp.GetString();
                }

                return new IpInfoResponse
                {
                    ip = ip,
                    asn = asn,
                    asOrganization = org,
                    country = root.TryGetProperty("country", out var cProp) ? cProp.GetString() : null,
                    countryCode = root.TryGetProperty("country_code", out var ccProp) ? ccProp.GetString() : null,
                    region = root.TryGetProperty("region", out var rProp) ? rProp.GetString() : null,
                    city = root.TryGetProperty("city", out var cityProp) ? cityProp.GetString() : null
                };
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                return null;
            }
        }

        private static SocketsHttpHandler CreateHandler(int? httpProxyPort)
        {
            var handler = new SocketsHttpHandler
            {
                ConnectTimeout = TimeSpan.FromSeconds(5),
                AutomaticDecompression = DecompressionMethods.All
            };

            if (httpProxyPort.HasValue)
            {
                handler.Proxy = new WebProxy($"http://127.0.0.1:{httpProxyPort.Value}");
                handler.UseProxy = true;
            }
            else
            {
                handler.UseProxy = false;
            }

            return handler;
        }
    }
}
