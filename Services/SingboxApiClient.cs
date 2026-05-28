using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
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
    /// 通过 HTTP 轮询 sing-box Clash 兼容 API 的 /connections 端点获取活动连接。
    /// sing-box 的 Clash API 不支持 WebSocket 推送，需使用 REST 轮询方式。
    /// </summary>
    public class SingboxApiClient : IDisposable
    {
        private static readonly Lazy<SingboxApiClient> _instance = new(() => new SingboxApiClient());
        public static SingboxApiClient Instance => _instance.Value;

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

        private async Task PollLoopAsync(CancellationToken token)
        {
            const string url = "http://127.0.0.1:9090/connections";

            // 等待 sing-box 完全启动后再开始轮询
            await Task.Delay(500, token).ConfigureAwait(false);

            while (!token.IsCancellationRequested && _isRunning)
            {
                try
                {
                    var response = await _httpClient.GetAsync(url, token).ConfigureAwait(false);
                    if (response.IsSuccessStatusCode)
                    {
                        var json = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
                        var message = JsonSerializer.Deserialize(json, AnywhereWinUI.Models.AppJsonContext.Default.ClashConnectionsMessage);
                        if (message != null)
                        {
                            // 若 connections 字段为 null，补充空列表防止后续 NullReferenceException
                            message.Connections ??= new List<ClashConnectionNode>();
                            ConnectionsUpdated?.Invoke(this, message);
                        }
                    }
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
