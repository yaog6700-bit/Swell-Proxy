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
        [JsonConverter(typeof(FlexibleStringJsonConverter))]
        public string SourcePort { get; set; } = string.Empty;

        [JsonPropertyName("destinationPort")]
        [JsonConverter(typeof(FlexibleStringJsonConverter))]
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
        [JsonConverter(typeof(FlexibleDateTimeJsonConverter))]
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
        private bool _isRunning = true;

        public event EventHandler<ClashConnectionsMessage>? ConnectionsUpdated;
        public event EventHandler<Exception>? OnError;

        private SingboxApiClient() { }

        public async Task StartAsync()
        {
            _isRunning = true;

            // 仅在核心运行时才可拉取初始快照
            if (!CoreManager.Instance.IsRunning)
                return;

            // CoreManager.StartStatsPolling() already polls /connections every 1s and calls
            // PublishConnectionsSnapshot(). Trigger one immediate snapshot here so ConnectionsPage
            // renders immediately upon navigation without starting a duplicate 1s poll loop.
            try
            {
                var snapshot = await FetchConnectionsAsync().ConfigureAwait(false);
                if (snapshot != null && _isRunning)
                {
                    ConnectionsUpdated?.Invoke(this, snapshot);
                }
            }
            catch
            {
                // Ignore transient startup error; CoreManager's 1s loop will deliver subsequent ticks
            }
        }

        public Task StopAsync()
        {
            _isRunning = false;
            _cts?.Cancel();
            _cts = null;
            return Task.CompletedTask;
        }

        internal void PublishConnectionsSnapshot(ClashConnectionsMessage message)
        {
            if (!_isRunning || !MainWindow.IsVisibleToUser || ConnectionsUpdated == null) return;
            ConnectionsUpdated.Invoke(this, message);
        }

        /// <summary>
        /// One-shot fetch of /connections (includes downloadTotal/uploadTotal).
        /// Streams UTF-8 JSON directly without allocating intermediate full-payload strings (0 LOH).
        /// </summary>
        public async Task<ClashConnectionsMessage?> FetchConnectionsAsync(CancellationToken token = default)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, ConnectionsUrl);
            ApplyAuth(request);

            using var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return null;

            await using var stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
            var message = await JsonSerializer.DeserializeAsync(
                stream,
                AnywhereWinUI.Models.AppJsonContext.Default.ClashConnectionsMessage,
                token).ConfigureAwait(false);
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

        public void Dispose()
        {
            _ = StopAsync();
        }
    }

    /// <summary>
    /// Allows Clash API fields such as sourcePort / destinationPort to deserialize cleanly
    /// whether the active core serializes them as JSON strings ("443") or JSON numbers (443).
    /// </summary>
    internal sealed class FlexibleStringJsonConverter : JsonConverter<string>
    {
        public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            return reader.TokenType switch
            {
                JsonTokenType.String => reader.GetString() ?? string.Empty,
                JsonTokenType.Number => reader.TryGetInt64(out var n) ? n.ToString() : string.Empty,
                JsonTokenType.True => "true",
                JsonTokenType.False => "false",
                _ => string.Empty
            };
        }

        public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(value);
        }
    }

    internal sealed class FlexibleDateTimeJsonConverter : JsonConverter<DateTime>
    {
        public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.String)
            {
                if (reader.TryGetDateTime(out var dt)) return dt;
                var raw = reader.GetString();
                if (!string.IsNullOrEmpty(raw) && DateTimeOffset.TryParse(raw, out var dto))
                    return dto.LocalDateTime;
            }
            else if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt64(out var unix))
            {
                return unix > 10_000_000_000L
                    ? DateTimeOffset.FromUnixTimeMilliseconds(unix).LocalDateTime
                    : DateTimeOffset.FromUnixTimeSeconds(unix).LocalDateTime;
            }
            return DateTime.Now;
        }

        public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(value);
        }
    }
}
