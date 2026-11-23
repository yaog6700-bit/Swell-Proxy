using System;
using System.Buffers;
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

        [JsonPropertyName("process")]
        public string Process { get; set; } = string.Empty;

        [JsonPropertyName("processPath")]
        public string ProcessPath { get; set; } = string.Empty;
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

        public const string DefaultControllerBaseUrl = "http://127.0.0.1:9090";
        public const string DefaultConnectionsUrl = DefaultControllerBaseUrl + "/connections";
        private volatile string _controllerBaseUrl = DefaultControllerBaseUrl;
        private volatile string _connectionsUrl = DefaultConnectionsUrl;
        public string ControllerBaseUrl => _controllerBaseUrl;
        public string ConnectionsUrl => _connectionsUrl;

        private static readonly HttpClient _httpClient = new HttpClient(new SocketsHttpHandler
        {
            UseProxy = false,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5)
        })
        {
            Timeout = TimeSpan.FromSeconds(3)
        };

        private static readonly HttpClient _delayProbeClient = new HttpClient(new SocketsHttpHandler
        {
            UseProxy = false,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5)
        })
        {
            Timeout = TimeSpan.FromSeconds(6)
        };

        private CancellationTokenSource? _cts;
        private bool _isRunning = true;

        public event EventHandler<ClashConnectionsMessage>? ConnectionsUpdated;

        /// <summary>
        /// True only when the main window is visible AND at least one live connection view
        /// (ConnectionsPage or TopologyGraphControl) is actively subscribed.
        /// When false, CoreManager uses zero-allocation streaming Utf8JsonReader instead of
        /// deserializing full ClashConnectionsMessage graphs.
        /// </summary>
        public bool HasActiveVisualConsumers => MainWindow.IsVisibleToUser && ConnectionsUpdated != null;

        private SingboxApiClient() { }

        public void UpdateControllerEndpoint(string? externalController)
        {
            if (string.IsNullOrWhiteSpace(externalController))
            {
                _controllerBaseUrl = DefaultControllerBaseUrl;
                _connectionsUrl = DefaultConnectionsUrl;
                return;
            }

            string trimmed = externalController.Trim();
            if (trimmed.StartsWith(':'))
                trimmed = "127.0.0.1" + trimmed;
            else if (trimmed.StartsWith("0.0.0.0:", StringComparison.Ordinal))
                trimmed = "127.0.0.1:" + trimmed.Substring("0.0.0.0:".Length);
            else if (trimmed.StartsWith("[::]:", StringComparison.Ordinal))
                trimmed = "[::1]:" + trimmed.Substring("[::]:".Length);

            if (!trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                trimmed = "http://" + trimmed;
            }

            string baseUrl = trimmed.TrimEnd('/');
            _controllerBaseUrl = baseUrl;
            _connectionsUrl = baseUrl + "/connections";
        }

        /// <summary>
        /// Zero-drop hot-switches a Clash/sing-box selector group (e.g. "proxy") to <paramref name="outboundTag"/>
        /// via PUT /proxies/{groupTag}. Trim-safe (uses Utf8JsonWriter with 0 reflection).
        /// </summary>
        public async Task<bool> SelectOutboundAsync(string groupTag, string outboundTag, CancellationToken token = default)
        {
            if (string.IsNullOrWhiteSpace(groupTag) || string.IsNullOrWhiteSpace(outboundTag))
                return false;

            try
            {
                var writerBuffer = new ArrayBufferWriter<byte>(128);
                using (var writer = new Utf8JsonWriter(writerBuffer))
                {
                    writer.WriteStartObject();
                    writer.WriteString("name"u8, outboundTag);
                    writer.WriteEndObject();
                }

                var payload = writerBuffer.WrittenMemory.ToArray();
                using var content = new ByteArrayContent(payload);
                content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

                using var request = new HttpRequestMessage(
                    HttpMethod.Put,
                    $"{_controllerBaseUrl}/proxies/{Uri.EscapeDataString(groupTag)}")
                {
                    Content = content
                };
                ApplyAuth(request);

                using var response = await _httpClient.SendAsync(request, token).ConfigureAwait(false);
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Queries GET /proxies/{groupTag} (e.g. "urltest" or "proxy") and returns its active "now" outbound tag.
        /// Trim-safe via Utf8JsonReader.
        /// </summary>
        public async Task<string?> GetGroupSelectedAsync(string groupTag, CancellationToken token = default)
        {
            if (string.IsNullOrWhiteSpace(groupTag))
                return null;

            try
            {
                using var request = new HttpRequestMessage(
                    HttpMethod.Get,
                    $"{_controllerBaseUrl}/proxies/{Uri.EscapeDataString(groupTag)}");
                ApplyAuth(request);

                using var response = await _httpClient.SendAsync(request, token).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                    return null;

                var bytes = await response.Content.ReadAsByteArrayAsync(token).ConfigureAwait(false);
                var reader = new Utf8JsonReader(bytes);
                if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
                    return null;

                while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
                {
                    if (reader.TokenType != JsonTokenType.PropertyName)
                        continue;

                    if (reader.ValueTextEquals("now"u8))
                    {
                        if (!reader.Read()) return null;
                        return reader.TokenType == JsonTokenType.String ? reader.GetString() : null;
                    }

                    if (!reader.Read()) break;
                    reader.Skip();
                }
                return null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Performs a true end-to-end protocol URL-Test via sing-box Clash API:
        /// GET /proxies/{outboundTag}/delay?url=https://www.gstatic.com/generate_204&amp;timeout={timeoutMs}.
        /// Returns &gt;0 ms on HTTP 204 round-trip success, -1 on proxy/handshake/timeout failure (503/504),
        /// or null if the local Clash API controller itself was unreachable (allowing fallback to socket probe).
        /// </summary>
        public async Task<int?> ProbeOutboundDelayAsync(string outboundTag, int timeoutMs = 3500, CancellationToken token = default)
        {
            if (string.IsNullOrWhiteSpace(outboundTag))
                return null;

            try
            {
                string url = $"{_controllerBaseUrl}/proxies/{Uri.EscapeDataString(outboundTag)}/delay?url=https%3A%2F%2Fwww.gstatic.com%2Fgenerate_204&timeout={timeoutMs}";
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                ApplyAuth(request);

                using var response = await _delayProbeClient.SendAsync(request, token).ConfigureAwait(false);
                if (response.StatusCode == System.Net.HttpStatusCode.NotFound ||
                    response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                {
                    return null;
                }

                if (!response.IsSuccessStatusCode)
                {
                    // 503 / 504 from Clash API means the outbound failed end-to-end handshake or timed out
                    return -1;
                }

                var bytes = await response.Content.ReadAsByteArrayAsync(token).ConfigureAwait(false);
                var reader = new Utf8JsonReader(bytes);
                if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
                    return -1;

                while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
                {
                    if (reader.TokenType != JsonTokenType.PropertyName)
                        continue;

                    if (reader.ValueTextEquals("delay"u8))
                    {
                        if (!reader.Read()) return -1;
                        if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out int ms) && ms > 0)
                            return ms;
                        return -1;
                    }

                    if (!reader.Read()) break;
                    reader.Skip();
                }

                return -1;
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
            // Deliver snapshot whenever window is visible and there are active subscribers (e.g. TopologyGraphControl),
            // even if ConnectionsPage paused its own table updates!
            if (!MainWindow.IsVisibleToUser || ConnectionsUpdated == null) return;
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
        /// Fetches /connections into a pooled ArrayPool&lt;byte&gt; buffer and applies proxy-traffic
        /// deltas via Utf8JsonReader without allocating ClashConnectionNode / ClashMetadata objects.
        /// </summary>
        internal async Task<(long down, long up, long downSpeed, long upSpeed)?> FetchAndApplyTrafficZeroAllocAsync(
            ProxyTrafficTracker tracker,
            bool seedOnly,
            double elapsedSeconds,
            CancellationToken token = default)
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
            byte[] buffer = ArrayPool<byte>.Shared.Rent(65536);
            int totalRead = 0;
            try
            {
                while (true)
                {
                    if (totalRead == buffer.Length)
                    {
                        if (buffer.Length >= 8 * 1024 * 1024)
                            return null; // Safety cap against pathological payloads

                        byte[] larger = ArrayPool<byte>.Shared.Rent(buffer.Length * 2);
                        Buffer.BlockCopy(buffer, 0, larger, 0, totalRead);
                        ArrayPool<byte>.Shared.Return(buffer);
                        buffer = larger;
                    }

                    int read = await stream.ReadAsync(buffer.AsMemory(totalRead, buffer.Length - totalRead), token).ConfigureAwait(false);
                    if (read == 0) break;
                    totalRead += read;
                }

                if (totalRead == 0)
                    return null;

                return tracker.ApplyUtf8Json(buffer.AsSpan(0, totalRead), seedOnly, elapsedSeconds);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
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

        public async Task<bool> CloseConnectionAsync(string id, CancellationToken token = default)
        {
            if (string.IsNullOrWhiteSpace(id)) return false;
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Delete, $"{ConnectionsUrl}/{Uri.EscapeDataString(id)}");
                ApplyAuth(request);
                using var response = await _httpClient.SendAsync(request, token).ConfigureAwait(false);
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        public async Task<bool> CloseAllConnectionsAsync(CancellationToken token = default)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Delete, ConnectionsUrl);
                ApplyAuth(request);
                using var response = await _httpClient.SendAsync(request, token).ConfigureAwait(false);
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
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
