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
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

        public async Task<IpInfoResponse?> GetIpInfoAsync(int? httpProxyPort, CancellationToken ct = default)
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
                    var body = await client.GetStringAsync("https://my.ippure.com/v1/info", ct);
                    return JsonSerializer.Deserialize<IpInfoResponse>(body);
                }
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
    }
}
