using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.WebSockets;
using System.Text;
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

    public class SingboxApiClient : IDisposable
    {
        private static readonly Lazy<SingboxApiClient> _instance = new(() => new SingboxApiClient());
        public static SingboxApiClient Instance => _instance.Value;

        private ClientWebSocket? _webSocket;
        private CancellationTokenSource? _cts;
        private bool _isConnecting;

        public event EventHandler<ClashConnectionsMessage>? ConnectionsUpdated;
        public event EventHandler<Exception>? OnError;

        private SingboxApiClient() { }

        public async Task StartAsync()
        {
            if (_isConnecting || (_webSocket != null && _webSocket.State == WebSocketState.Open))
                return;

            _isConnecting = true;
            try
            {
                // Only connect if the core is actually running
                if (!CoreManager.Instance.IsRunning)
                    return;

                _cts?.Cancel();
                _cts = new CancellationTokenSource();
                
                _webSocket = new ClientWebSocket();
                Uri uri = new Uri("ws://127.0.0.1:9090/connections");

                await _webSocket.ConnectAsync(uri, _cts.Token);
                _ = Task.Run(() => ReceiveLoopAsync(_cts.Token), _cts.Token);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SingboxApiClient] Connect error: {ex.Message}");
                OnError?.Invoke(this, ex);
            }
            finally
            {
                _isConnecting = false;
            }
        }

        public async Task StopAsync()
        {
            try
            {
                _cts?.Cancel();
                if (_webSocket != null)
                {
                    if (_webSocket.State == WebSocketState.Open)
                    {
                        await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Client closing", CancellationToken.None);
                    }
                    _webSocket.Dispose();
                    _webSocket = null;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SingboxApiClient] Stop error: {ex.Message}");
            }
        }

        private async Task ReceiveLoopAsync(CancellationToken token)
        {
            var buffer = new byte[1024 * 64]; // 64KB buffer
            
            try
            {
                while (_webSocket != null && _webSocket.State == WebSocketState.Open && !token.IsCancellationRequested)
                {
                    using var ms = new MemoryStream();
                    WebSocketReceiveResult result;
                    do
                    {
                        result = await _webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), token);
                        if (result.MessageType == WebSocketMessageType.Close)
                        {
                            await StopAsync();
                            return;
                        }
                        ms.Write(buffer, 0, result.Count);
                    } while (!result.EndOfMessage);

                    ms.Seek(0, SeekOrigin.Begin);

                    if (result.MessageType == WebSocketMessageType.Text)
                    {
                        try
                        {
                            // Parse JSON using System.Text.Json (fast, low allocation)
                            var message = await JsonSerializer.DeserializeAsync<ClashConnectionsMessage>(ms, cancellationToken: token);
                            if (message != null)
                            {
                                ConnectionsUpdated?.Invoke(this, message);
                            }
                        }
                        catch (JsonException ex)
                        {
                            Debug.WriteLine($"[SingboxApiClient] JSON Parse error: {ex.Message}");
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Expected when stopping
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SingboxApiClient] ReceiveLoop error: {ex.Message}");
                OnError?.Invoke(this, ex);
            }
            finally
            {
                if (_webSocket != null && _webSocket.State != WebSocketState.Closed && _webSocket.State != WebSocketState.Aborted)
                {
                    await StopAsync();
                }
            }
        }

        public void Dispose()
        {
            _ = StopAsync();
        }
    }
}
