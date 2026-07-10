using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace AnywhereWinUI.Services
{
    // API 数据模型
    public class ClashMetadata
    {
        [JsonPropertyName("network")]
        public string Network { get; set; } = string.Empty;

        [JsonPropertyName("type")]
        public string Type { get; set; } = string.Empty;

        [JsonPropertyName("sourceIP")]
        public string SourceIP { get; set; } = string.Empty;

        [JsonPropertyName("destinationIP")]
        public string DestinationIP { get; set; } = string.Empty;

        [JsonPropertyName("sourcePort")]
        public string SourcePort { get; set; } = string.Empty;

        [JsonPropertyName("destinationPort")]
        public string DestinationPort { get; set; } = string.Empty;

        [JsonPropertyName("host")]
        public string Host { get; set; } = string.Empty;
    }

    public class ClashConnectionNode
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("metadata")]
        public ClashMetadata Metadata { get; set; } = new();

        [JsonPropertyName("upload")]
        public long Upload { get; set; }

        [JsonPropertyName("download")]
        public long Download { get; set; }

        [JsonPropertyName("start")]
        public DateTime Start { get; set; }

        [JsonPropertyName("chains")]
        public List<string> Chains { get; set; } = new();

        [JsonPropertyName("rule")]
        public string Rule { get; set; } = string.Empty;

        [JsonPropertyName("rulePayload")]
        public string RulePayload { get; set; } = string.Empty;
    }

    public class ClashConnectionsMessage
    {
        [JsonPropertyName("downloadTotal")]
        public long DownloadTotal { get; set; }

        [JsonPropertyName("uploadTotal")]
        public long UploadTotal { get; set; }

        [JsonPropertyName("connections")]
        public List<ClashConnectionNode> Connections { get; set; } = new();
    }

    /// <summary>
    /// 通过 HTTP 轮询 sing-box Clash 兼容 API 的 /connections 端点获取活动连接与累计流量。
    /// </summary>
    public class SingboxApiClient : IDisposable
    {
        private static readonly Lazy<SingboxApiClient> _instance = new(() => new SingboxApiClient());
        public static SingboxApiClient Instance => _instance.Value;

        public const string ConnectionsUrl = "http://127.0.0.1:9090/connections";

        private static readonly HttpClient _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(3)
        };

        private CancellationTokenSource? _cts;
        private bool _isRunning;

        public event EventHandler<ClashConnectionsMessage>? ConnectionsUpdated;
        public event EventHandler<Exception>? OnError;

        private SingboxApiClient() { }

        public Task StartAsync()
        {
            if (_isRunning) return Task.CompletedTask;

            // 仅在核心运行时才启动轮询
            if (!CoreManager.Instance.IsRunning)
                return Task.CompletedTask;

            _cts?.Cancel();
            _cts = new CancellationTokenSource();
            _isRunning = true;

            _ = Task.Run(() => PollLoopAsync(_cts.Token));
            return Task.CompletedTask;
        }

        public Task StopAsync()
        {
            _isRunning = false;
            _cts?.Cancel();
            _cts = null;
            return Task.CompletedTask;
        }

        /// <summary>
        /// One-shot fetch of /connections (includes downloadTotal/uploadTotal).
        /// Used by CoreManager traffic stats and the connections poll loop.
        /// </summary>
        public async Task<ClashConnectionsMessage?> FetchConnectionsAsync(CancellationToken token = default)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, ConnectionsUrl);
            ApplyAuth(request);

            using var response = await _httpClient.SendAsync(request, token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return null;

            var json = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
            var message = JsonSerializer.Deserialize(json, AnywhereWinUI.Models.AppJsonContext.Default.ClashConnectionsMessage);
            if (message != null)
                message.Connections ??= new List<ClashConnectionNode>();
            return message;
        }

        /// <summary>
        /// Returns proxy-session traffic totals from Clash API, or null if unavailable.
        /// </summary>
        public async Task<(long downloadTotal, long uploadTotal)?> TryGetTrafficTotalsAsync(CancellationToken token = default)
        {
            try
            {
                var message = await FetchConnectionsAsync(token).ConfigureAwait(false);
                if (message == null) return null;
                return (message.DownloadTotal, message.UploadTotal);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                return null;
            }
        }

        internal static void ApplyAuth(HttpRequestMessage request)
        {
            var secret = AppSession.Instance.ClashApiSecret;
            if (string.IsNullOrEmpty(secret)) return;
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", secret);
        }

        private async Task PollLoopAsync(CancellationToken token)
        {
            // 等待 sing-box 完全启动后再开始轮询
            await Task.Delay(500, token).ConfigureAwait(false);

            while (!token.IsCancellationRequested && _isRunning)
            {
                try
                {
                    var message = await FetchConnectionsAsync(token).ConfigureAwait(false);
                    if (message != null)
                        ConnectionsUpdated?.Invoke(this, message);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (HttpRequestException ex)
                {
                    // sing-box 尚未就绪或已停止，静默等待，不触发 OnError
                    Debug.WriteLine($"[SingboxApiClient] HTTP poll error (core may not be ready): {ex.Message}");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[SingboxApiClient] Poll error: {ex.Message}");
                    OnError?.Invoke(this, ex);
                }

                // 每秒轮询一次
                try
                {
                    await Task.Delay(1000, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }

            _isRunning = false;
        }

        public void Dispose()
        {
            _ = StopAsync();
        }
    }
}
