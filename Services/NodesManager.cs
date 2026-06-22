using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace AnywhereWinUI.Services
{
    public class PersistedNode
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = string.Empty;
        public string Protocol { get; set; } = string.Empty; // VLESS, Shadowsocks, Trojan, Hysteria 2, VMess, TUIC, etc.
        public string Host { get; set; } = string.Empty; // host:port
        public string SubscriptionId { get; set; } = string.Empty; // empty for manual nodes

        // Auth fields
        public string? Uuid { get; set; }
        public string? Password { get; set; }
        public string? Username { get; set; }       // SOCKS5 / Naive username

        // Transport & protocol fields
        public string? Encryption { get; set; }
        public string? VlessEncryption { get; set; } // VLESS PQ encryption
        public string? Network { get; set; }
        public string? Path { get; set; }
        public string? WsHost { get; set; }
        public string? HeaderType { get; set; }     // tcp header type (http/none)
        public string? Alpn { get; set; }           // TLS ALPN list (comma-separated)
        public int     AlterId { get; set; }        // VMess AlterId

        // TLS / Reality fields
        public string? Security { get; set; }
        public string? Sni { get; set; }
        public string? Fingerprint { get; set; }
        public string? PublicKey { get; set; }      // Reality pbk
        public string? ShortId { get; set; }        // Reality sid
        public string? SpiderX { get; set; }        // Reality spx
        public string? Flow { get; set; }
        public string? Spec { get; set; }           // Nowhere spec
        public bool    AllowInsecure { get; set; }

        // Hysteria2 obfuscation
        public string? ObfsType { get; set; }
        public string? ObfsPassword { get; set; }

        // ShadowTLS
        public bool    IsShadowTls { get; set; }
        public int     ShadowTlsVersion { get; set; }
        public string? ShadowTlsPassword { get; set; }

        // WireGuard
        public string? WgPrivateKey { get; set; }      // WireGuard local private key (base64)
        public string? WgLocalAddress { get; set; }    // comma-separated CIDR, e.g. "10.0.0.2/32,fd00::2/128"
        public string? WgPreSharedKey { get; set; }    // optional pre-shared key (base64)
        public int     WgMtu { get; set; }             // 0 = use sing-box default (1408)

        // Snell
        public int     SnellVersion { get; set; }      // Snell protocol version: 1-5 (default 4)

        // Proxy Chain
        public string? ProxyChainId { get; set; }

        // Favorite status
        public bool    IsFavorite { get; set; }
    }

    public class PersistedSubscription
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
        public string LastUpdated { get; set; } = "从不";
    }

    public class NodesConfig
    {
        public List<PersistedNode> Nodes { get; set; } = new();
        public List<PersistedSubscription> Subscriptions { get; set; } = new();
        public string SelectedNodeId { get; set; } = string.Empty;
        public string ViewMode { get; set; } = "card"; // card or list

        // Personalized Settings
        public string ThemeSetting { get; set; } = "Default"; // Light, Dark, Default
        public string BackdropSetting { get; set; } = "Mica"; // Mica, Acrylic
        public bool ShowLatencyInDetails { get; set; } = true;
        public bool ShowAiUnlockInDetails { get; set; } = true;
        public bool ShowIpStatusInDetails { get; set; } = true;
        public string? ColorSs { get; set; }
        public string? ColorVless { get; set; }
        public string? ColorVmess { get; set; }
        public string? ColorHysteria2 { get; set; }
        public string? ColorTrojan { get; set; }
        public string? ColorFallback { get; set; }
    }

    public class NodesManager
    {
        private static readonly string ConfigPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SwellProxy",
            "nodes_config.json"
        );

        public static NodesManager Instance { get; } = new NodesManager();

        public List<PersistedNode> Nodes { get; private set; } = new();
        public List<PersistedSubscription> Subscriptions { get; private set; } = new();
        public string SelectedNodeId { get; set; } = string.Empty;
        public string ViewMode { get; set; } = "card";

        // Expose personalized settings as properties
        public string ThemeSetting { get; set; } = "Default";
        public string BackdropSetting { get; set; } = "Mica";
        public bool ShowLatencyInDetails { get; set; } = true;
        public bool ShowAiUnlockInDetails { get; set; } = true;
        public bool ShowIpStatusInDetails { get; set; } = true;
        public string ColorSs { get; set; } = "#60A5FA";
        public string ColorVless { get; set; } = "#34D399";
        public string ColorVmess { get; set; } = "#A78BFA";
        public string ColorHysteria2 { get; set; } = "#FB923C";
        public string ColorTrojan { get; set; } = "#F87171";
        public string ColorFallback { get; set; } = "#94A8A0";

        private NodesManager()
        {
            Load();
        }

        public void Load()
        {
            try
            {
                var dir = Path.GetDirectoryName(ConfigPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                if (File.Exists(ConfigPath))
                {
                    string json = File.ReadAllText(ConfigPath, Encoding.UTF8);
                    var config = JsonSerializer.Deserialize(json, AnywhereWinUI.Models.AppJsonContext.Default.NodesConfig);
                    if (config != null)
                    {
                        Nodes = config.Nodes ?? new();
                        Subscriptions = config.Subscriptions ?? new();
                        SelectedNodeId = config.SelectedNodeId ?? string.Empty;
                        if (string.IsNullOrEmpty(SelectedNodeId) && Nodes.Count > 0)
                        {
                            SelectedNodeId = Nodes[0].Id;
                            Save();
                        }
                        ViewMode = config.ViewMode ?? "card";

                        ThemeSetting = config.ThemeSetting ?? "Default";
                        BackdropSetting = config.BackdropSetting ?? "Mica";
                        ShowLatencyInDetails = config.ShowLatencyInDetails;
                        ShowAiUnlockInDetails = config.ShowAiUnlockInDetails;
                        ShowIpStatusInDetails = config.ShowIpStatusInDetails;
                        ColorSs = config.ColorSs ?? "#60A5FA";
                        ColorVless = config.ColorVless ?? "#34D399";
                        ColorVmess = config.ColorVmess ?? "#A78BFA";
                        ColorHysteria2 = config.ColorHysteria2 ?? "#FB923C";
                        ColorTrojan = config.ColorTrojan ?? "#F87171";
                        ColorFallback = config.ColorFallback ?? "#94A8A0";

                        // Load into ProtocolColorStore
                        Helpers.ProtocolColorStore.LoadFrom(config);
                        return;
                    }
                }
            }
            catch { }

            ThemeSetting = "Default";
            BackdropSetting = "Mica";
            ShowLatencyInDetails = true;
            ShowAiUnlockInDetails = true;
            ShowIpStatusInDetails = true;
            ColorSs = "#60A5FA";
            ColorVless = "#34D399";
            ColorVmess = "#A78BFA";
            ColorHysteria2 = "#FB923C";
            ColorTrojan = "#F87171";
            ColorFallback = "#94A8A0";

            // Default mock nodes
            Nodes = new List<PersistedNode>();
            Save();
        }

        public void Save()
        {
            try
            {
                var config = new NodesConfig
                {
                    Nodes = Nodes,
                    Subscriptions = Subscriptions,
                    SelectedNodeId = SelectedNodeId,
                    ViewMode = ViewMode,

                    ThemeSetting = ThemeSetting,
                    BackdropSetting = BackdropSetting,
                    ShowLatencyInDetails = ShowLatencyInDetails,
                    ShowAiUnlockInDetails = ShowAiUnlockInDetails,
                    ShowIpStatusInDetails = ShowIpStatusInDetails,
                    ColorSs = ColorSs,
                    ColorVless = ColorVless,
                    ColorVmess = ColorVmess,
                    ColorHysteria2 = ColorHysteria2,
                    ColorTrojan = ColorTrojan,
                    ColorFallback = ColorFallback
                };
                var options = new JsonSerializerOptions { WriteIndented = true };
                var context = new AnywhereWinUI.Models.AppJsonContext(options);
                string json = JsonSerializer.Serialize(config, context.NodesConfig);
                File.WriteAllText(ConfigPath, json, Encoding.UTF8);
            }
            catch { }
        }

        public void AddManualNode(string name, string protocol, string host)
        {
            Nodes.Add(new PersistedNode
            {
                Name = name,
                Protocol = protocol,
                Host = host,
                SubscriptionId = string.Empty
            });
            Save();
        }

        public void AddManualNode(PersistedNode node)
        {
            node.SubscriptionId = string.Empty;
            Nodes.Add(node);
            Save();
        }

        public void DeleteNode(string id)
        {
            Nodes.RemoveAll(n => n.Id == id);
            if (SelectedNodeId == id)
            {
                SelectedNodeId = Nodes.Count > 0 ? Nodes[0].Id : string.Empty;
            }
            Save();
        }

        public void AddSubscription(string name, string url)
        {
            Subscriptions.Add(new PersistedSubscription
            {
                Name = name,
                Url = url
            });
            Save();
        }

        public void DeleteSubscription(string subId)
        {
            Subscriptions.RemoveAll(s => s.Id == subId);
            Nodes.RemoveAll(n => n.SubscriptionId == subId);
            if (string.IsNullOrEmpty(SelectedNodeId) || !Nodes.Exists(n => n.Id == SelectedNodeId))
            {
                SelectedNodeId = Nodes.Count > 0 ? Nodes[0].Id : string.Empty;
            }
            Save();
        }

        /// <summary>
        /// 更新订阅。成功返回 null，失败返回错误描述字符串。
        /// </summary>
        public async Task<string?> UpdateSubscriptionAsync(string subId)
        {
            var sub = Subscriptions.Find(s => s.Id == subId);
            if (sub == null) return "订阅不存在";

            try
            {
                using var client = new HttpClient();
                client.Timeout = TimeSpan.FromSeconds(15);
                client.DefaultRequestHeaders.UserAgent.ParseAdd("Anywhere-Client");

                string content = await client.GetStringAsync(sub.Url);
                content = content.Trim();

                var newNodes = ParseSubscriptionContent(content, subId);
                if (newNodes.Count == 0)
                    return "订阅内容解析失败：未找到任何有效节点（支持 Base64 分享链接列表和 sing-box JSON 格式）";

                // Map old nodes by endpoint to preserve ID and IsFavorite
                var oldNodes = Nodes.FindAll(n => n.SubscriptionId == subId);
                var oldByEndpoint = new Dictionary<string, PersistedNode>(oldNodes.Count, StringComparer.OrdinalIgnoreCase);
                foreach (var n in oldNodes)
                {
                    var key = $"{n.Protocol}|{n.Host}";
                    oldByEndpoint[key] = n;
                }

                foreach (var n in newNodes)
                {
                    var key = $"{n.Protocol}|{n.Host}";
                    if (oldByEndpoint.TryGetValue(key, out var match))
                    {
                        n.Id = match.Id;
                        n.IsFavorite = match.IsFavorite;
                    }
                }

                // ── OnSubscribe plugin hook ──────────────────────────────────
                // Serialize nodes to JSON, let plugins filter/transform, then deserialize back.
                try
                {
                    var opts = new System.Text.Json.JsonSerializerOptions();
                    var nodesJson = System.Text.Json.JsonSerializer.Serialize(newNodes, opts);
                    var transformedJson = await Plugins.PluginManager.Instance
                        .FireSubscribeAsync(nodesJson, sub.Name);
                    if (!string.Equals(transformedJson, nodesJson, System.StringComparison.Ordinal))
                    {
                        var transformed = System.Text.Json.JsonSerializer
                            .Deserialize<List<PersistedNode>>(transformedJson, opts);
                        if (transformed != null)
                            newNodes = transformed;
                    }
                }
                catch { /* Plugin errors must not break subscription update */ }

                // Replace old sub nodes in-place so manual ordering is preserved.
                // Strategy:
                //   1. Find the index of the first node belonging to this subscription.
                //   2. Remove all old subscription nodes (preserving their slots).
                //   3. Insert new nodes at that same index.
                //   4. Any nodes that no longer exist in the subscription are dropped;
                //      brand-new nodes from the subscription are inserted at the end of
                //      where the old subscription block was.
                int insertIndex = Nodes.FindIndex(n => n.SubscriptionId == subId);
                Nodes.RemoveAll(n => n.SubscriptionId == subId);
                if (insertIndex < 0 || insertIndex > Nodes.Count)
                    insertIndex = Nodes.Count; // fallback: append at end
                Nodes.InsertRange(insertIndex, newNodes);
                sub.LastUpdated = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                Save();
                return null; // success
            }
            catch (HttpRequestException ex)
            {
                return $"网络请求失败：{ex.Message}";
            }
            catch (TaskCanceledException)
            {
                return "请求超时（15 秒），请检查网络或订阅地址是否可访问";
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to update subscription: {ex.Message}");
                return $"更新失败：{ex.Message}";
            }
        }

        private List<PersistedNode> ParseSubscriptionContent(string content, string subId)
        {
            var list = new List<PersistedNode>();

            // 1. 优先检测是否为 sing-box JSON 格式
            var trimmed0 = content.TrimStart();
            if (trimmed0.StartsWith("{") || trimmed0.StartsWith("["))
            {
                var singboxNodes = ParseSingboxConfig(content, subId);
                if (singboxNodes.Count > 0)
                    return singboxNodes;
                // 解析到 0 个节点时继续尝试其他格式（防止误判）
            }

            // 2. 尝试整体 Base64 解码（老式订阅格式）
            string decoded;
            try
            {
                // 先粗略判断：如果内容全是合法 Base64 字符才尝试解码
                var testContent = content.Replace("\r", "").Replace("\n", "").Trim();
                byte[] bytes = Convert.FromBase64String(
                    testContent.PadRight((testContent.Length + 3) / 4 * 4, '='));
                var candidate = Encoding.UTF8.GetString(bytes);
                // 解码结果必须含有协议前缀才算有效
                decoded = candidate.Contains("://") ? candidate : content;
            }
            catch
            {
                decoded = content;
            }

            // 3. 逐行解析分享链接
            var lines = decoded.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed)) continue;

                var parsed = ParseShareUrl(trimmed);
                if (parsed != null)
                {
                    parsed.SubscriptionId = subId;
                    list.Add(parsed);
                }
            }

            return list;
        }

        /// <summary>
        /// 解析 sing-box JSON 配置文件，提取 outbounds 中的代理节点。
        /// 跳过 selector / urltest / direct / block / dns 等非节点类型。
        /// </summary>
        private List<PersistedNode> ParseSingboxConfig(string json, string subId)
        {
            var list = new List<PersistedNode>();
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                // 兼容顶层直接是数组的情况（极少见）
                JsonElement outboundsEl;
                if (root.ValueKind == JsonValueKind.Array)
                    outboundsEl = root;
                else if (!root.TryGetProperty("outbounds", out outboundsEl))
                    return list;

                if (outboundsEl.ValueKind != JsonValueKind.Array) return list;

                // 不需要转换的内部出站类型
                var skipTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "selector", "urltest", "direct", "block", "dns",
                    "loadbalance", "fallback", "chain"
                };

                foreach (var ob in outboundsEl.EnumerateArray())
                {
                    if (!ob.TryGetProperty("type", out var typeProp)) continue;
                    string type = typeProp.GetString() ?? "";
                    if (skipTypes.Contains(type)) continue;

                    string tag = ob.TryGetProperty("tag", out var tagProp) ? tagProp.GetString() ?? "" : "";
                    string server = ob.TryGetProperty("server", out var serverProp) ? serverProp.GetString() ?? "" : "";
                    int port = ob.TryGetProperty("server_port", out var portProp) ? portProp.GetInt32() : 0;

                    var node = new PersistedNode
                    {
                        Name = tag,
                        SubscriptionId = subId
                    };

                    // 提取 TLS 信息（多种协议共用）
                    string sni = "", fingerprint = "", pbk = "", sid = "";
                    bool allowInsecure = false;
                    string security = "none";
                    string alpn = "";

                    if (ob.TryGetProperty("tls", out var tlsEl) && tlsEl.ValueKind == JsonValueKind.Object)
                    {
                        bool tlsEnabled = tlsEl.TryGetProperty("enabled", out var tlsEnabledProp) && tlsEnabledProp.GetBoolean();
                        if (tlsEnabled)
                        {
                            security = "tls";
                            sni = tlsEl.TryGetProperty("server_name", out var sniProp) ? sniProp.GetString() ?? "" : "";
                            allowInsecure = tlsEl.TryGetProperty("insecure", out var insecureProp) && insecureProp.GetBoolean();

                            if (tlsEl.TryGetProperty("utls", out var utlsEl))
                                fingerprint = utlsEl.TryGetProperty("fingerprint", out var fpProp) ? fpProp.GetString() ?? "" : "";

                            if (tlsEl.TryGetProperty("reality", out var realityEl)
                                && realityEl.TryGetProperty("enabled", out var rEnabledProp)
                                && rEnabledProp.GetBoolean())
                            {
                                security = "reality";
                                pbk = realityEl.TryGetProperty("public_key", out var pkProp) ? pkProp.GetString() ?? "" : "";
                                sid = realityEl.TryGetProperty("short_id", out var sidProp) ? sidProp.GetString() ?? "" : "";
                            }

                            if (tlsEl.TryGetProperty("alpn", out var alpnEl) && alpnEl.ValueKind == JsonValueKind.Array)
                            {
                                var parts = new List<string>();
                                foreach (var a in alpnEl.EnumerateArray())
                                    if (a.GetString() is string s) parts.Add(s);
                                alpn = string.Join(",", parts);
                            }
                        }
                    }

                    // 提取传输层信息
                    string network = "tcp", path = "", wsHost = "";
                    if (ob.TryGetProperty("transport", out var transportEl) && transportEl.ValueKind == JsonValueKind.Object)
                    {
                        network = transportEl.TryGetProperty("type", out var netProp) ? netProp.GetString() ?? "tcp" : "tcp";
                        path = transportEl.TryGetProperty("path", out var pathProp) ? pathProp.GetString() ?? "" : "";
                        // grpc serviceName
                        if (string.IsNullOrEmpty(path))
                            path = transportEl.TryGetProperty("service_name", out var snProp) ? snProp.GetString() ?? "" : "";
                        // ws host header (headers object)
                        if (transportEl.TryGetProperty("headers", out var headersEl))
                            wsHost = headersEl.TryGetProperty("Host", out var hostProp) ? hostProp.GetString() ?? "" : "";
                        // httpupgrade host (direct string field)
                        if (string.IsNullOrEmpty(wsHost) && network == "httpupgrade")
                            wsHost = transportEl.TryGetProperty("host", out var huHostProp) ? huHostProp.GetString() ?? "" : "";
                    }

                    switch (type.ToLowerInvariant())
                    {
                        case "shadowsocks":
                        {
                            string method = ob.TryGetProperty("method", out var mProp) ? mProp.GetString() ?? "" : "";
                            string password = ob.TryGetProperty("password", out var pwProp) ? pwProp.GetString() ?? "" : "";
                            // 跳过带 plugin 的 SIP003 节点（目前不支持）
                            if (ob.TryGetProperty("plugin", out _)) continue;
                            node.Protocol = "Shadowsocks";
                            node.Host = NodeLinkParser.FormatHostPortPublic(server, port);
                            node.Encryption = method;
                            node.Password = password;
                            node.Network = "tcp";
                            break;
                        }
                        case "vless":
                        {
                            string uuid = ob.TryGetProperty("uuid", out var uProp) ? uProp.GetString() ?? "" : "";
                            string flow = ob.TryGetProperty("flow", out var fProp) ? fProp.GetString() ?? "" : "";
                            node.Protocol = "VLESS";
                            node.Host = NodeLinkParser.FormatHostPortPublic(server, port);
                            node.Uuid = uuid;
                            node.Flow = flow;
                            node.Security = security;
                            node.Sni = sni;
                            node.Fingerprint = fingerprint;
                            node.PublicKey = pbk;
                            node.ShortId = sid;
                            node.AllowInsecure = allowInsecure;
                            node.Network = network;
                            node.Path = path;
                            node.WsHost = wsHost;
                            node.Alpn = alpn;
                            node.Encryption = "none";
                            break;
                        }
                        case "vmess":
                        {
                            string uuid = ob.TryGetProperty("uuid", out var uProp) ? uProp.GetString() ?? "" : "";
                            int alterId = ob.TryGetProperty("alter_id", out var aProp) ? aProp.GetInt32() : 0;
                            string enc = ob.TryGetProperty("security", out var eProp) ? eProp.GetString() ?? "auto" : "auto";
                            node.Protocol = "VMess";
                            node.Host = NodeLinkParser.FormatHostPortPublic(server, port);
                            node.Uuid = uuid;
                            node.AlterId = alterId;
                            node.Encryption = enc;
                            node.Security = security;
                            node.Sni = sni;
                            node.Fingerprint = fingerprint;
                            node.AllowInsecure = allowInsecure;
                            node.Network = network;
                            node.Path = path;
                            node.WsHost = wsHost;
                            node.Alpn = alpn;
                            break;
                        }
                        case "hysteria2":
                        {
                            string password = ob.TryGetProperty("password", out var pwProp) ? pwProp.GetString() ?? "" : "";
                            string obfsType = "", obfsPwd = "";
                            if (ob.TryGetProperty("obfs", out var obfsEl) && obfsEl.ValueKind == JsonValueKind.Object)
                            {
                                obfsType = obfsEl.TryGetProperty("type", out var otProp) ? otProp.GetString() ?? "" : "";
                                obfsPwd = obfsEl.TryGetProperty("password", out var opProp) ? opProp.GetString() ?? "" : "";
                            }
                            node.Protocol = "Hysteria 2";
                            node.Host = NodeLinkParser.FormatHostPortPublic(server, port);
                            node.Password = password;
                            node.Security = "tls";
                            node.Sni = sni;
                            node.AllowInsecure = allowInsecure;
                            node.ObfsType = string.IsNullOrEmpty(obfsType) ? "none" : obfsType;
                            node.ObfsPassword = obfsPwd;
                            node.Network = "udp";
                            break;
                        }
                        case "trojan":
                        {
                            string password = ob.TryGetProperty("password", out var pwProp) ? pwProp.GetString() ?? "" : "";
                            node.Protocol = "Trojan";
                            node.Host = NodeLinkParser.FormatHostPortPublic(server, port);
                            node.Password = password;
                            node.Security = security;
                            node.Sni = sni;
                            node.Fingerprint = fingerprint;
                            node.AllowInsecure = allowInsecure;
                            node.Network = network;
                            node.Path = path;
                            node.WsHost = wsHost;
                            node.Alpn = alpn;
                            break;
                        }
                        case "tuic":
                        {
                            string uuid = ob.TryGetProperty("uuid", out var uProp) ? uProp.GetString() ?? "" : "";
                            string password = ob.TryGetProperty("password", out var pwProp) ? pwProp.GetString() ?? "" : "";
                            node.Protocol = "TUIC";
                            node.Host = NodeLinkParser.FormatHostPortPublic(server, port);
                            node.Uuid = uuid;
                            node.Password = password;
                            node.Security = "tls";
                            node.Sni = sni;
                            node.AllowInsecure = allowInsecure;
                            node.Alpn = alpn;
                            node.Network = "udp";
                            break;
                        }
                        case "wireguard":
                        {
                            string privateKey = ob.TryGetProperty("private_key", out var pkProp) ? pkProp.GetString() ?? "" : "";
                            string localAddr = "";
                            if (ob.TryGetProperty("local_address", out var laEl) && laEl.ValueKind == JsonValueKind.Array)
                            {
                                var parts = new List<string>();
                                foreach (var a in laEl.EnumerateArray()) if (a.GetString() is string s) parts.Add(s);
                                localAddr = string.Join(",", parts);
                            }
                            string peerServer = server, peerPubKey = "";
                            int peerPort = port;
                            string preShared = "";
                            if (ob.TryGetProperty("peers", out var peersEl) && peersEl.ValueKind == JsonValueKind.Array)
                            {
                                foreach (var peer in peersEl.EnumerateArray())
                                {
                                    peerServer = peer.TryGetProperty("server", out var psProp) ? psProp.GetString() ?? server : server;
                                    peerPort = peer.TryGetProperty("server_port", out var ppProp) ? ppProp.GetInt32() : port;
                                    peerPubKey = peer.TryGetProperty("public_key", out var ppkProp) ? ppkProp.GetString() ?? "" : "";
                                    preShared = peer.TryGetProperty("pre_shared_key", out var pskProp) ? pskProp.GetString() ?? "" : "";
                                    break; // 只取第一个 peer
                                }
                            }
                            node.Protocol = "WireGuard";
                            node.Host = NodeLinkParser.FormatHostPortPublic(peerServer, peerPort);
                            node.WgPrivateKey = privateKey;
                            node.WgLocalAddress = localAddr;
                            node.WgPreSharedKey = preShared;
                            node.PublicKey = peerPubKey;
                            node.Network = "udp";
                            break;
                        }
                        case "socks":
                        case "socks5":
                        {
                            string username = ob.TryGetProperty("username", out var uProp) ? uProp.GetString() ?? "" : "";
                            string password = ob.TryGetProperty("password", out var pwProp) ? pwProp.GetString() ?? "" : "";
                            node.Protocol = "SOCKS5";
                            node.Host = NodeLinkParser.FormatHostPortPublic(server, port);
                            node.Username = username;
                            node.Password = password;
                            node.Network = "tcp";
                            break;
                        }
                        case "http":
                        {
                            string username = ob.TryGetProperty("username", out var uProp) ? uProp.GetString() ?? "" : "";
                            string password = ob.TryGetProperty("password", out var pwProp) ? pwProp.GetString() ?? "" : "";
                            node.Protocol = "HTTP";
                            node.Host = NodeLinkParser.FormatHostPortPublic(server, port);
                            node.Username = username;
                            node.Password = password;
                            node.Security = security;
                            node.Sni = sni;
                            node.Network = "tcp";
                            break;
                        }
                        case "naive":
                        {
                            string username = ob.TryGetProperty("username", out var uProp) ? uProp.GetString() ?? "" : "";
                            string password = ob.TryGetProperty("password", out var pwProp) ? pwProp.GetString() ?? "" : "";
                            node.Protocol = "Naive";
                            node.Host = NodeLinkParser.FormatHostPortPublic(server, port);
                            node.Username = username;
                            node.Password = password;
                            node.Security = "tls";
                            node.Sni = sni;
                            node.Network = "tcp";
                            break;
                        }
                        case "anytls":
                        {
                            string password = ob.TryGetProperty("password", out var pwProp) ? pwProp.GetString() ?? "" : "";
                            node.Protocol = "AnyTLS";
                            node.Host = NodeLinkParser.FormatHostPortPublic(server, port);
                            node.Password = password;
                            node.Security = "tls";
                            node.Sni = sni;
                            node.Fingerprint = fingerprint;
                            node.AllowInsecure = allowInsecure;
                            node.Network = "tcp";
                            break;
                        }
                        default:
                            // 未知协议，跳过
                            continue;
                    }

                    list.Add(node);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ParseSingboxConfig] failed: {ex.Message}");
            }
            return list;
        }

                public static string ToShareUrl(PersistedNode node)
        {
            try
            {
                var name = Uri.EscapeDataString(node.Name ?? "");
                if (node.Protocol == "VLESS")
                {
                    var qs = new List<string>();
                    if (!string.IsNullOrEmpty(node.Encryption)) qs.Add($"encryption={node.Encryption}");
                    if (!string.IsNullOrEmpty(node.Security)) qs.Add($"security={node.Security}");
                    if (!string.IsNullOrEmpty(node.Sni)) qs.Add($"sni={node.Sni}");
                    if (!string.IsNullOrEmpty(node.Fingerprint)) qs.Add($"fp={node.Fingerprint}");
                    if (!string.IsNullOrEmpty(node.Network)) qs.Add($"type={node.Network}");
                    if (!string.IsNullOrEmpty(node.Path)) qs.Add($"path={Uri.EscapeDataString(node.Path)}");
                    if (!string.IsNullOrEmpty(node.PublicKey)) qs.Add($"pbk={node.PublicKey}");
                    if (!string.IsNullOrEmpty(node.ShortId)) qs.Add($"sid={node.ShortId}");
                    if (!string.IsNullOrEmpty(node.Flow)) qs.Add($"flow={node.Flow}");
                    
                    var qString = qs.Count > 0 ? "?" + string.Join("&", qs) : "";
                    return $"vless://{node.Uuid}@{node.Host}{qString}#{name}";
                }
                else if (node.Protocol == "Trojan")
                {
                    var qs = new List<string>();
                    qs.Add("security=tls");
                    if (!string.IsNullOrEmpty(node.Sni)) qs.Add($"sni={node.Sni}");
                    if (!string.IsNullOrEmpty(node.Network)) qs.Add($"type={node.Network}");
                    if (!string.IsNullOrEmpty(node.Path)) qs.Add($"path={Uri.EscapeDataString(node.Path)}");
                    
                    var qString = qs.Count > 0 ? "?" + string.Join("&", qs) : "";
                    return $"trojan://{node.Password}@{node.Host}{qString}#{name}";
                }
                else if (node.Protocol == "Shadowsocks")
                {
                    string method = string.IsNullOrEmpty(node.Encryption) ? "chacha20-ietf-poly1305" : node.Encryption;
                    string creds = $"{method}:{node.Password}";
                    string base64Creds = Convert.ToBase64String(Encoding.UTF8.GetBytes(creds)).TrimEnd('=');
                    return $"ss://{base64Creds}@{node.Host}#{name}";
                }
                else if (node.Protocol == "Hysteria 2" || node.Protocol == "Hysteria2")
                {
                    var qs = new List<string>();
                    if (!string.IsNullOrEmpty(node.Sni)) qs.Add($"sni={node.Sni}");
                    if (node.AllowInsecure) qs.Add("insecure=1");
                    
                    var qString = qs.Count > 0 ? "?" + string.Join("&", qs) : "";
                    return $"hysteria2://{node.Password}@{node.Host}{qString}#{name}";
                }
                else if (node.Protocol == "Nowhere")
                {
                    var qs = new List<string>();
                    if (!string.IsNullOrEmpty(node.Spec)) qs.Add($"spec={Uri.EscapeDataString(node.Spec)}");
                    if (!string.IsNullOrEmpty(node.Alpn)) qs.Add($"alpn={Uri.EscapeDataString(node.Alpn)}");
                    if (node.AllowInsecure) qs.Add("insecure=1");

                    var qString = qs.Count > 0 ? "?" + string.Join("&", qs) : "";
                    return $"nowhere://{node.Password}@{node.Host}{qString}#{name}";
                }
            }
            catch { }
            return string.Empty;
        }

        /// <summary>
        /// Delegates link parsing to the full-featured NodeLinkParser.
        /// Kept as a public static for backward compatibility with call sites that still use NodesManager.ParseShareUrl.
        /// </summary>
        public static PersistedNode? ParseShareUrl(string url)
        {
            return NodeLinkParser.Parse(url);
        }



        public async Task ExportBackupAsync(string zipPath)
        {
            try
            {
                if (File.Exists(zipPath))
                    File.Delete(zipPath);

                using var zip = System.IO.Compression.ZipFile.Open(zipPath, System.IO.Compression.ZipArchiveMode.Create);
                if (File.Exists(ConfigPath))
                {
                    zip.CreateEntryFromFile(ConfigPath, "nodes_config.json");
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"备份保存失败: {ex.Message}", ex);
            }
            await Task.CompletedTask;
        }

        public async Task ImportBackupAsync(string zipPath)
        {
            try
            {
                using var zip = System.IO.Compression.ZipFile.OpenRead(zipPath);
                foreach (var entry in zip.Entries)
                {
                    if (entry.FullName.Equals("nodes_config.json", StringComparison.OrdinalIgnoreCase))
                    {
                        entry.ExtractToFile(ConfigPath, overwrite: true);
                    }
                }
                // Reload nodes config into memory
                Load();
            }
            catch (Exception ex)
            {
                throw new Exception($"备份导入恢复失败: {ex.Message}", ex);
            }
            await Task.CompletedTask;
        }
    }
}

