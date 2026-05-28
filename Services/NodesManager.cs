using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text;
using System.Text.Json;
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
            "AnywhereProxy",
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

        public async Task<bool> UpdateSubscriptionAsync(string subId)
        {
            var sub = Subscriptions.Find(s => s.Id == subId);
            if (sub == null) return false;

            try
            {
                using var client = new HttpClient();
                client.DefaultRequestHeaders.UserAgent.ParseAdd("Anywhere-Client");
                
                string content = await client.GetStringAsync(sub.Url);
                content = content.Trim();

                var newNodes = ParseSubscriptionContent(content, subId);
                if (newNodes.Count > 0)
                {
                    // Map old nodes by endpoint to preserve ID and IsFavorite
                    var oldNodes = Nodes.FindAll(n => n.SubscriptionId == subId);
                    var oldByEndpoint = new Dictionary<string, PersistedNode>(oldNodes.Count, StringComparer.OrdinalIgnoreCase);
                    foreach (var n in oldNodes)
                    {
                        var key = $"{n.Protocol}://{n.Host}";
                        oldByEndpoint[key] = n;
                    }

                    foreach (var n in newNodes)
                    {
                        var key = $"{n.Protocol}://{n.Host}";
                        if (oldByEndpoint.TryGetValue(key, out var match))
                        {
                            n.Id = match.Id;
                            n.IsFavorite = match.IsFavorite;
                        }
                    }

                    // Clear old nodes for this sub
                    Nodes.RemoveAll(n => n.SubscriptionId == subId);
                    Nodes.AddRange(newNodes);
                    sub.LastUpdated = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                    Save();
                    return true;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to update subscription: {ex.Message}");
            }
            return false;
        }

        private List<PersistedNode> ParseSubscriptionContent(string content, string subId)
        {
            var list = new List<PersistedNode>();

            // 1. Try Base64 decoding
            string decoded = string.Empty;
            try
            {
                byte[] bytes = Convert.FromBase64String(content);
                decoded = Encoding.UTF8.GetString(bytes);
            }
            catch
            {
                // Not Base64, might be plain text sharing URLs
                decoded = content;
            }

            // 2. Try parsing as URLs line-by-line
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

