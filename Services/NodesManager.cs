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
    public class NodesManager
    {
        private static readonly string ConfigPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SwellProxy",
            "nodes_config.json"
        );
        public static readonly TimeSpan SubscriptionAutoRefreshInterval = TimeSpan.FromHours(24);

        public static NodesManager Instance { get; } = new NodesManager();

        private readonly object _sync = new();
        public List<PersistedNode> Nodes { get; private set; } = new();
        public List<PersistedSubscription> Subscriptions { get; private set; } = new();
        public string SelectedNodeId { get; set; } = string.Empty;
        public string ViewMode { get; set; } = "card";

        public List<PersistedNode> GetNodesSnapshot()
        {
            lock (_sync)
            {
                return new List<PersistedNode>(Nodes);
            }
        }

        public List<PersistedSubscription> GetSubscriptionsSnapshot()
        {
            lock (_sync)
            {
                return new List<PersistedSubscription>(Subscriptions);
            }
        }

        public PersistedNode? FindNode(string? id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            lock (_sync)
            {
                return Nodes.Find(n => string.Equals(n.Id, id, StringComparison.Ordinal));
            }
        }

        public void AddNodes(IEnumerable<PersistedNode> nodesToAdd)
        {
            if (nodesToAdd == null) return;
            lock (_sync)
            {
                Nodes.AddRange(nodesToAdd);
                if (string.IsNullOrEmpty(SelectedNodeId) && Nodes.Count > 0)
                {
                    SelectedNodeId = Nodes[0].Id;
                }
                Save();
            }
        }

        public void ReplaceAllNodes(IEnumerable<PersistedNode> orderedNodes)
        {
            if (orderedNodes == null) return;
            lock (_sync)
            {
                Nodes.Clear();
                Nodes.AddRange(orderedNodes);
                if (!string.IsNullOrEmpty(SelectedNodeId) && !Nodes.Exists(n => n.Id == SelectedNodeId))
                {
                    SelectedNodeId = Nodes.Count > 0 ? Nodes[0].Id : string.Empty;
                }
                Save();
            }
        }

        public void SetNodeFavorite(string id, bool isFavorite)
        {
            lock (_sync)
            {
                var node = Nodes.Find(n => n.Id == id);
                if (node != null)
                {
                    node.IsFavorite = isFavorite;
                    Save();
                }
            }
        }

        public string? GetLastSubscriptionId()
        {
            lock (_sync)
            {
                return Subscriptions.Count > 0 ? Subscriptions[^1].Id : null;
            }
        }

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
            lock (_sync)
            {
                bool fileExisted = false;
                try
                {
                    var dir = Path.GetDirectoryName(ConfigPath);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    {
                        Directory.CreateDirectory(dir);
                    }

                    fileExisted = File.Exists(ConfigPath);
                    if (fileExisted)
                    {
                        string json = File.ReadAllText(ConfigPath, Encoding.UTF8);
                        if (!string.IsNullOrWhiteSpace(json))
                        {
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

                        // Existing file failed to parse or was truncated — back it up and DO NOT overwrite it!
                        Helpers.LocalSettingsHelper.BackupCorruptFileBestEffort(ConfigPath);
                    }
                }
                catch
                {
                    if (fileExisted)
                    {
                        Helpers.LocalSettingsHelper.BackupCorruptFileBestEffort(ConfigPath);
                    }
                }

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

                Nodes = new List<PersistedNode>();
                if (!fileExisted)
                {
                    Save();
                }
            }
        }

        public void Save()
        {
            lock (_sync)
            {
                try
                {
                    var config = new NodesConfig
                    {
                        Nodes = new List<PersistedNode>(Nodes),
                        Subscriptions = new List<PersistedSubscription>(Subscriptions),
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
                    Helpers.LocalSettingsHelper.WriteAllTextAtomic(ConfigPath, json);
                }
                catch { }
            }
        }

        public void AddManualNode(string name, string protocol, string host)
        {
            lock (_sync)
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
        }

        public void AddManualNode(PersistedNode node)
        {
            lock (_sync)
            {
                node.SubscriptionId = string.Empty;
                Nodes.Add(node);
                Save();
            }
        }

        public void DeleteNode(string id)
        {
            lock (_sync)
            {
                var existing = Nodes.Find(n => n.Id == id);
                if (existing != null)
                {
                    CustomConfigService.DeleteConfigFile(existing);

                    // Remember single-node deletion for subscription nodes so refreshing
                    // the subscription won't resurrect unwanted info/dead nodes.
                    if (!string.IsNullOrEmpty(existing.SubscriptionId) && !string.IsNullOrWhiteSpace(existing.Name))
                    {
                        var sub = Subscriptions.Find(s => s.Id == existing.SubscriptionId);
                        if (sub != null)
                        {
                            sub.ExcludedNodeNames ??= new List<string>();
                            string trimmedName = existing.Name.Trim();
                            if (!sub.ExcludedNodeNames.Exists(x => string.Equals(x, trimmedName, StringComparison.OrdinalIgnoreCase)))
                            {
                                sub.ExcludedNodeNames.Add(trimmedName);
                            }
                        }
                    }
                }
                Nodes.RemoveAll(n => n.Id == id);
                if (SelectedNodeId == id)
                {
                    SelectedNodeId = Nodes.Count > 0 ? Nodes[0].Id : string.Empty;
                }
                Save();
            }
        }

        public async Task<string?> RestoreExcludedNodesAsync(string subId)
        {
            lock (_sync)
            {
                var sub = Subscriptions.Find(s => s.Id == subId);
                if (sub == null) return "订阅不存在";

                sub.ExcludedNodeNames?.Clear();
                Save();
            }
            return await UpdateSubscriptionAsync(subId);
        }

        internal static List<PersistedNode> FilterExcludedSubscriptionNodes(
            List<PersistedNode> nodes,
            IReadOnlyCollection<string>? excludedNodeNames)
        {
            if (excludedNodeNames == null || excludedNodeNames.Count == 0 || nodes.Count == 0)
                return nodes;

            var excludedSet = new HashSet<string>(
                excludedNodeNames.Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n.Trim()),
                StringComparer.OrdinalIgnoreCase);

            if (excludedSet.Count == 0)
                return nodes;

            return nodes.FindAll(n => !excludedSet.Contains((n.Name ?? string.Empty).Trim()));
        }

        public void AddSubscription(string name, string url)
        {
            lock (_sync)
            {
                Subscriptions.Add(new PersistedSubscription
                {
                    Name = name,
                    Url = url
                });
                Save();
            }
        }

        public void DeleteSubscription(string subId)
        {
            lock (_sync)
            {
                Subscriptions.RemoveAll(s => s.Id == subId);
                Nodes.RemoveAll(n => n.SubscriptionId == subId);
                if (string.IsNullOrEmpty(SelectedNodeId) || !Nodes.Exists(n => n.Id == SelectedNodeId))
                {
                    SelectedNodeId = Nodes.Count > 0 ? Nodes[0].Id : string.Empty;
                }
                Save();
            }
        }

        private static readonly HttpClient _subHttpClient = new(new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),
            AutomaticDecompression = System.Net.DecompressionMethods.All
        })
        {
            Timeout = TimeSpan.FromSeconds(15)
        };

        /// <summary>
        /// 更新订阅。成功返回 null，失败返回错误描述字符串。
        /// </summary>
        public async Task<string?> UpdateSubscriptionAsync(string subId)
        {
            PersistedSubscription? sub;
            string subUrl;
            string subName;
            lock (_sync)
            {
                sub = Subscriptions.Find(s => s.Id == subId);
                if (sub == null) return "订阅不存在";
                subUrl = sub.Url;
                subName = sub.Name;
            }

            try
            {
                var newNodes = await FetchAndParseSubscriptionAsync(_subHttpClient, subUrl, subId, "Anywhere-Client clash.meta/1.19");
                if (newNodes.Count == 0)
                {
                    try
                    {
                        newNodes = await FetchAndParseSubscriptionAsync(_subHttpClient, subUrl, subId, "Anywhere-Client");
                    }
                    catch { /* 回退重试失败时保留首次结果，走统一空节点提示 */ }
                }

                if (newNodes.Count == 0)
                    return "订阅内容解析失败：未找到任何有效节点（支持 Base64 分享链接列表、sing-box JSON 和 Clash YAML 格式）";

                List<PersistedNode> oldNodes;
                List<string>? excludedSnapshot;
                lock (_sync)
                {
                    oldNodes = Nodes.FindAll(n => n.SubscriptionId == subId);
                    excludedSnapshot = sub.ExcludedNodeNames != null ? new List<string>(sub.ExcludedNodeNames) : null;
                }
                PreserveNodeIdentityAndFavorites(oldNodes, newNodes);

                // ── OnSubscribe plugin hook (Trim-safe via AppJsonContext!) ──────
                try
                {
                    var nodesJson = JsonSerializer.Serialize(newNodes, AnywhereWinUI.Models.AppJsonContext.Default.ListPersistedNode);
                    var transformedJson = await Plugins.PluginManager.Instance
                        .FireSubscribeAsync(nodesJson, subName);
                    if (!string.Equals(transformedJson, nodesJson, System.StringComparison.Ordinal))
                    {
                        var transformed = JsonSerializer.Deserialize(
                            transformedJson, AnywhereWinUI.Models.AppJsonContext.Default.ListPersistedNode);
                        if (transformed != null)
                            newNodes = transformed;
                    }
                }
                catch { /* Plugin errors must not break subscription update */ }

                foreach (var n in newNodes)
                {
                    n.SubscriptionId = subId;
                }

                newNodes = FilterExcludedSubscriptionNodes(newNodes, excludedSnapshot);

                var newEndpointKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var n in newNodes)
                {
                    newEndpointKeys.Add($"{n.Protocol}|{n.Host}");
                }

                bool activeAffected = false;
                int remainingCount = 0;
                lock (_sync)
                {
                    // Re-verify subscription wasn't deleted while network request was in flight
                    var currentSub = Subscriptions.Find(s => s.Id == subId);
                    if (currentSub == null) return null;

                    var activeNodeBefore = Nodes.Find(n => n.Id == SelectedNodeId);
                    bool activeWasInSub = activeNodeBefore != null &&
                                          (string.Equals(activeNodeBefore.SubscriptionId, subId, StringComparison.Ordinal) ||
                                           (!string.IsNullOrEmpty(activeNodeBefore.ProxyChainId) &&
                                            Nodes.Exists(c => c.Id == activeNodeBefore.ProxyChainId &&
                                                              string.Equals(c.SubscriptionId, subId, StringComparison.Ordinal))));

                    int insertIndex = Nodes.FindIndex(n => n.SubscriptionId == subId);
                    if (insertIndex < 0)
                    {
                        insertIndex = Nodes.FindIndex(n =>
                            !string.IsNullOrEmpty(n.SubscriptionId) &&
                            !Subscriptions.Exists(s => s.Id == n.SubscriptionId) &&
                            newEndpointKeys.Contains($"{n.Protocol}|{n.Host}"));
                    }
                    Nodes.RemoveAll(n => n.SubscriptionId == subId);
                    Nodes.RemoveAll(n =>
                        !string.IsNullOrEmpty(n.SubscriptionId) &&
                        !Subscriptions.Exists(s => s.Id == n.SubscriptionId) &&
                        newEndpointKeys.Contains($"{n.Protocol}|{n.Host}"));
                    if (insertIndex < 0 || insertIndex > Nodes.Count)
                        insertIndex = Nodes.Count;
                    Nodes.InsertRange(insertIndex, newNodes);
                    if (string.IsNullOrEmpty(SelectedNodeId) || !Nodes.Exists(n => n.Id == SelectedNodeId))
                    {
                        SelectedNodeId = Nodes.Count > 0 ? Nodes[0].Id : string.Empty;
                    }
                    currentSub.LastUpdated = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                    remainingCount = Nodes.Count;
                    activeAffected = activeWasInSub || AppSession.Instance.IsAutoNodeSelect;
                    Save();
                }

                // Clear cached hot-switch tags so updated credentials/ports never reuse stale sing-box outbounds
                AppSession.Instance.ClearHotSwitchTags();
                if (CoreManager.Instance.IsRunning && activeAffected)
                {
                    if (remainingCount == 0)
                    {
                        await CoreManager.Instance.StopAsync().ConfigureAwait(false);
                    }
                    else
                    {
                        var activeNode = FindNode(SelectedNodeId);
                        string cfg = await ConfigBuilder.BuildAsync(activeNode).ConfigureAwait(false);
                        await CoreManager.Instance.StartAsync(cfg).ConfigureAwait(false);
                    }
                }
                return null;
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

        public async Task<int> RefreshDueSubscriptionsAsync()
        {
            if (!AppSession.Instance.EnableSubscriptionAutoRefresh)
                return 0;

            var dueSubscriptions = new List<PersistedSubscription>();
            lock (_sync)
            {
                foreach (var sub in Subscriptions)
                {
                    if (IsSubscriptionAutoRefreshDue(sub))
                        dueSubscriptions.Add(sub);
                }
            }

            int refreshedCount = 0;
            foreach (var sub in dueSubscriptions)
            {
                string? err = await UpdateSubscriptionAsync(sub.Id);
                if (err == null)
                    refreshedCount++;
            }
            return refreshedCount;
        }

        public static bool IsSubscriptionAutoRefreshDue(PersistedSubscription sub)
        {
            if (!TryParseLastUpdated(sub.LastUpdated, out var lastUpdated))
                return true;

            return DateTime.Now - lastUpdated >= SubscriptionAutoRefreshInterval;
        }

        public static string GetSubscriptionNextAutoRefreshText(PersistedSubscription sub)
        {
            if (!TryParseLastUpdated(sub.LastUpdated, out var lastUpdated))
                return Helpers.L.SubDialog_AutoRefreshPending;

            return Helpers.Loc.Format("SubDialog_AutoRefreshNextFmt", $"{lastUpdated.Add(SubscriptionAutoRefreshInterval):yyyy-MM-dd HH:mm:ss}");
        }

        private static bool TryParseLastUpdated(string? value, out DateTime lastUpdated)
        {
            return DateTime.TryParseExact(
                value,
                "yyyy-MM-dd HH:mm:ss",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None,
                out lastUpdated);
        }

        private async Task<List<PersistedNode>> FetchAndParseSubscriptionAsync(
            HttpClient client, string url, string subId, string userAgent)
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.UserAgent.ParseAdd(userAgent);
            using var resp = await client.SendAsync(req);
            resp.EnsureSuccessStatusCode();
            string content = (await resp.Content.ReadAsStringAsync()).Trim();
            return ParseSubscriptionContent(content, subId);
        }

        public static void PreserveNodeIdentityAndFavorites(
            IReadOnlyList<PersistedNode> oldNodes, IReadOnlyList<PersistedNode> newNodes)
        {
            if (oldNodes.Count == 0 || newNodes.Count == 0) return;

            var claimedOldIndices = new HashSet<int>();
            var matchedNewIndices = new HashSet<int>();

            // 第一轮：协议 + Host + 名称完全一致时优先绑定
            var exactQueues = new Dictionary<string, Queue<int>>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < oldNodes.Count; i++)
            {
                var exactKey = $"{oldNodes[i].Protocol}|{oldNodes[i].Host}|{oldNodes[i].Name}";
                if (!exactQueues.TryGetValue(exactKey, out var q))
                {
                    q = new Queue<int>();
                    exactQueues[exactKey] = q;
                }
                q.Enqueue(i);
            }

            for (int j = 0; j < newNodes.Count; j++)
            {
                var exactKey = $"{newNodes[j].Protocol}|{newNodes[j].Host}|{newNodes[j].Name}";
                if (exactQueues.TryGetValue(exactKey, out var q) && q.Count > 0)
                {
                    int oldIdx = q.Dequeue();
                    claimedOldIndices.Add(oldIdx);
                    matchedNewIndices.Add(j);
                    newNodes[j].Id = oldNodes[oldIdx].Id;
                    newNodes[j].IsFavorite = oldNodes[oldIdx].IsFavorite;
                }
            }

            // 第二轮：对尚未匹配的新节点，按协议 + Host 顺序认领剩余旧节点（跨格式/改名兼容）
            var endpointQueues = new Dictionary<string, Queue<int>>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < oldNodes.Count; i++)
            {
                if (claimedOldIndices.Contains(i)) continue;
                var epKey = $"{oldNodes[i].Protocol}|{oldNodes[i].Host}";
                if (!endpointQueues.TryGetValue(epKey, out var q))
                {
                    q = new Queue<int>();
                    endpointQueues[epKey] = q;
                }
                q.Enqueue(i);
            }

            for (int j = 0; j < newNodes.Count; j++)
            {
                if (matchedNewIndices.Contains(j)) continue;
                var epKey = $"{newNodes[j].Protocol}|{newNodes[j].Host}";
                if (endpointQueues.TryGetValue(epKey, out var q) && q.Count > 0)
                {
                    int oldIdx = q.Dequeue();
                    claimedOldIndices.Add(oldIdx);
                    newNodes[j].Id = oldNodes[oldIdx].Id;
                    newNodes[j].IsFavorite = oldNodes[oldIdx].IsFavorite;
                }
            }
        }

        public List<PersistedNode> ParseSubscriptionContent(string content, string subId)
        {
            var list = new List<PersistedNode>();
            if (string.IsNullOrWhiteSpace(content))
                return list;

            content = content.TrimStart('\uFEFF');

            // 1. 优先检测是否为 sing-box JSON 格式
            var trimmed0 = content.TrimStart();
            if (trimmed0.StartsWith("{") || trimmed0.StartsWith("["))
            {
                var singboxNodes = ParseSingboxConfig(content, subId);
                if (singboxNodes.Count > 0)
                    return singboxNodes;
                // 解析到 0 个节点时继续尝试其他格式（防止误判）
            }

            // 1.5 Clash / Clash Meta (mihomo) YAML —— 只导入 proxies 列表中的节点
            if (ClashSubscriptionParser.LooksLikeClashYaml(content))
            {
                var clashNodes = ClashSubscriptionParser.Parse(content, subId);
                if (clashNodes.Count > 0)
                    return clashNodes;
                // 解析到 0 个节点时继续尝试其他格式（防止误判）
            }

            // 2. 尝试整体 Base64 / URL-Safe Base64 解码（老式订阅格式）
            string decoded;
            try
            {
                var sb = new StringBuilder(content.Length);
                foreach (char ch in content)
                {
                    if (!char.IsWhiteSpace(ch))
                        sb.Append(ch == '-' ? '+' : ch == '_' ? '/' : ch);
                }
                var testContent = sb.ToString().TrimEnd('=');
                if (testContent.Length > 0 && testContent.Length % 4 != 1)
                {
                    int pad = (4 - (testContent.Length % 4)) % 4;
                    byte[] bytes = Convert.FromBase64String(testContent.PadRight(testContent.Length + pad, '='));
                    var candidate = Encoding.UTF8.GetString(bytes);
                    decoded = (candidate.Contains("://") || candidate.Contains("= snell,", StringComparison.OrdinalIgnoreCase))
                        ? candidate
                        : content;
                }
                else
                {
                    decoded = content;
                }
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
                    if (!NodeLinkParser.TrySplitHostPort(parsed.Host, out _, out _))
                    {
                        System.Diagnostics.Debug.WriteLine($"[NodesManager] Skipping parsed node with invalid host: {parsed.Name} ({parsed.Host})");
                        continue;
                    }

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
                    if (string.IsNullOrWhiteSpace(server))
                        continue;

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
                            string vlessEnc = ob.TryGetProperty("encryption", out var veProp) ? veProp.GetString() ?? "" : "";
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
                            node.VlessEncryption = vlessEnc;
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
                            node.PublicKey = pbk;
                            node.ShortId = sid;
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
                        case "snell":
                        {
                            string psk = ob.TryGetProperty("psk", out var pskProp) ? pskProp.GetString() ?? "" : "";
                            int version = ob.TryGetProperty("version", out var verProp) ? verProp.GetInt32() : 4;

                            node.Protocol = "Snell";
                            node.Host = NodeLinkParser.FormatHostPortPublic(server, port);
                            node.Password = psk;
                            node.SnellVersion = version;
                            node.Network = "tcp";

                            if (version == 6)
                            {
                                // v6: 流量整形 mode
                                node.SnellMode = ob.TryGetProperty("mode", out var modeProp) ? modeProp.GetString() ?? "default" : "default";
                            }
                            else
                            {
                                // v4/v5: 新格式扁平化 obfs_mode / obfs_host（官方 sing-box 1.14+）
                                if (ob.TryGetProperty("obfs_mode", out var obfsModeProp))
                                {
                                    node.ObfsType = obfsModeProp.GetString() ?? "none";
                                    node.WsHost = ob.TryGetProperty("obfs_host", out var obfsHostProp) ? obfsHostProp.GetString() ?? "" : "";
                                }
                                // 旧格式嵌套 obfs 对象（reF1nd fork 兼容）
                                else if (ob.TryGetProperty("obfs", out var obfsEl) && obfsEl.ValueKind == JsonValueKind.Object)
                                {
                                    node.ObfsType = obfsEl.TryGetProperty("mode", out var modeProp2) ? modeProp2.GetString() ?? "none" : "none";
                                    node.WsHost = obfsEl.TryGetProperty("host", out var hostProp) ? hostProp.GetString() ?? "" : "";
                                }
                            }
                            break;
                        }
                        default:
                            // 未知协议，跳过
                            continue;
                    }

                    if (!NodeLinkParser.TrySplitHostPort(node.Host, out _, out _))
                    {
                        System.Diagnostics.Debug.WriteLine($"[ParseSingboxConfig] skipping node with invalid host: {node.Name} ({node.Host})");
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
                var proto = (node.Protocol ?? "").Trim();

                if (proto.Equals("VLESS", StringComparison.OrdinalIgnoreCase))
                {
                    var qs = new List<string>();
                    string vEnc = !string.IsNullOrEmpty(node.VlessEncryption) ? node.VlessEncryption : "none";
                    qs.Add($"encryption={Uri.EscapeDataString(vEnc)}");
                    if (!string.IsNullOrEmpty(node.Security)) qs.Add($"security={Uri.EscapeDataString(node.Security)}");
                    if (!string.IsNullOrEmpty(node.Sni)) qs.Add($"sni={Uri.EscapeDataString(node.Sni)}");
                    if (!string.IsNullOrEmpty(node.Fingerprint) && node.Fingerprint != "none") qs.Add($"fp={Uri.EscapeDataString(node.Fingerprint)}");
                    if (!string.IsNullOrEmpty(node.Network)) qs.Add($"type={Uri.EscapeDataString(node.Network)}");
                    if (!string.IsNullOrEmpty(node.WsHost)) qs.Add($"host={Uri.EscapeDataString(node.WsHost)}");
                    if (!string.IsNullOrEmpty(node.Path)) qs.Add($"path={Uri.EscapeDataString(node.Path)}");
                    if (!string.IsNullOrEmpty(node.PublicKey)) qs.Add($"pbk={Uri.EscapeDataString(node.PublicKey)}");
                    if (!string.IsNullOrEmpty(node.ShortId)) qs.Add($"sid={Uri.EscapeDataString(node.ShortId)}");
                    if (!string.IsNullOrEmpty(node.SpiderX)) qs.Add($"spx={Uri.EscapeDataString(node.SpiderX)}");
                    if (!string.IsNullOrEmpty(node.Flow)) qs.Add($"flow={Uri.EscapeDataString(node.Flow)}");
                    if (!string.IsNullOrEmpty(node.Alpn)) qs.Add($"alpn={Uri.EscapeDataString(node.Alpn)}");
                    if (!string.IsNullOrEmpty(node.HeaderType) && node.HeaderType != "none") qs.Add($"headerType={Uri.EscapeDataString(node.HeaderType)}");
                    if (node.AllowInsecure) qs.Add("allowInsecure=1");

                    var qString = qs.Count > 0 ? "?" + string.Join("&", qs) : "";
                    return $"vless://{Uri.EscapeDataString(node.Uuid ?? "")}@{node.Host}{qString}#{name}";
                }
                else if (proto.Equals("VMess", StringComparison.OrdinalIgnoreCase))
                {
                    var (vHost, vPort) = NodeLinkParser.SplitHostPort(node.Host);
                    var vmessJson = new JsonObject
                    {
                        ["v"] = "2",
                        ["ps"] = node.Name ?? "",
                        ["add"] = vHost.Trim('[', ']'),
                        ["port"] = (vPort > 0 ? vPort : 443).ToString(),
                        ["id"] = node.Uuid ?? "",
                        ["aid"] = node.AlterId.ToString(),
                        ["scy"] = string.IsNullOrEmpty(node.Encryption) ? "auto" : node.Encryption,
                        ["net"] = string.IsNullOrEmpty(node.Network) ? "tcp" : node.Network,
                        ["type"] = string.IsNullOrEmpty(node.HeaderType) ? "none" : node.HeaderType,
                        ["host"] = node.WsHost ?? "",
                        ["path"] = node.Path ?? "",
                        ["tls"] = string.Equals(node.Security, "tls", StringComparison.OrdinalIgnoreCase) ? "tls" : "",
                        ["sni"] = node.Sni ?? "",
                        ["alpn"] = node.Alpn ?? "",
                        ["fp"] = node.Fingerprint == "none" ? "" : (node.Fingerprint ?? "")
                    };
                    string b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(vmessJson.ToJsonString()));
                    return $"vmess://{b64}";
                }
                else if (proto.Equals("Trojan", StringComparison.OrdinalIgnoreCase))
                {
                    var qs = new List<string>();
                    string sec = string.IsNullOrEmpty(node.Security) || node.Security == "none" ? "tls" : node.Security;
                    qs.Add($"security={Uri.EscapeDataString(sec)}");
                    if (!string.IsNullOrEmpty(node.Sni)) qs.Add($"sni={Uri.EscapeDataString(node.Sni)}");
                    if (!string.IsNullOrEmpty(node.Fingerprint) && node.Fingerprint != "none") qs.Add($"fp={Uri.EscapeDataString(node.Fingerprint)}");
                    if (!string.IsNullOrEmpty(node.PublicKey)) qs.Add($"pbk={Uri.EscapeDataString(node.PublicKey)}");
                    if (!string.IsNullOrEmpty(node.ShortId)) qs.Add($"sid={Uri.EscapeDataString(node.ShortId)}");
                    if (!string.IsNullOrEmpty(node.Network)) qs.Add($"type={Uri.EscapeDataString(node.Network)}");
                    if (!string.IsNullOrEmpty(node.WsHost)) qs.Add($"host={Uri.EscapeDataString(node.WsHost)}");
                    if (!string.IsNullOrEmpty(node.Path)) qs.Add($"path={Uri.EscapeDataString(node.Path)}");
                    if (!string.IsNullOrEmpty(node.Alpn)) qs.Add($"alpn={Uri.EscapeDataString(node.Alpn)}");
                    if (node.AllowInsecure) qs.Add("allowInsecure=1");

                    var qString = qs.Count > 0 ? "?" + string.Join("&", qs) : "";
                    return $"trojan://{Uri.EscapeDataString(node.Password ?? "")}@{node.Host}{qString}#{name}";
                }
                else if (proto.Equals("Shadowsocks", StringComparison.OrdinalIgnoreCase) || proto.Equals("ss", StringComparison.OrdinalIgnoreCase))
                {
                    string method = string.IsNullOrEmpty(node.Encryption) ? "chacha20-ietf-poly1305" : node.Encryption;
                    string creds = $"{method}:{node.Password}";
                    string base64Creds = Convert.ToBase64String(Encoding.UTF8.GetBytes(creds)).TrimEnd('=');
                    var qs = new List<string>();
                    if (node.IsShadowTls)
                    {
                        qs.Add($"shadow-tls=1");
                        if (!string.IsNullOrEmpty(node.Sni)) qs.Add($"shadow-tls-sni={Uri.EscapeDataString(node.Sni)}");
                    }
                    var qString = qs.Count > 0 ? "/?" + string.Join("&", qs) : "";
                    return $"ss://{base64Creds}@{node.Host}{qString}#{name}";
                }
                else if (proto.Equals("Hysteria 2", StringComparison.OrdinalIgnoreCase) || proto.Equals("Hysteria2", StringComparison.OrdinalIgnoreCase))
                {
                    var qs = new List<string>();
                    if (!string.IsNullOrEmpty(node.Sni)) qs.Add($"sni={Uri.EscapeDataString(node.Sni)}");
                    if (!string.IsNullOrEmpty(node.Alpn)) qs.Add($"alpn={Uri.EscapeDataString(node.Alpn)}");
                    if (!string.IsNullOrEmpty(node.ObfsType) && node.ObfsType != "none")
                    {
                        qs.Add($"obfs={Uri.EscapeDataString(node.ObfsType)}");
                        if (!string.IsNullOrEmpty(node.ObfsPassword))
                            qs.Add($"obfs-password={Uri.EscapeDataString(node.ObfsPassword)}");
                    }
                    if (node.AllowInsecure) qs.Add("insecure=1");

                    var qString = qs.Count > 0 ? "?" + string.Join("&", qs) : "";
                    return $"hysteria2://{Uri.EscapeDataString(node.Password ?? "")}@{node.Host}{qString}#{name}";
                }
                else if (proto.Equals("TUIC", StringComparison.OrdinalIgnoreCase))
                {
                    var qs = new List<string>();
                    if (!string.IsNullOrEmpty(node.Sni)) qs.Add($"sni={Uri.EscapeDataString(node.Sni)}");
                    if (!string.IsNullOrEmpty(node.Alpn)) qs.Add($"alpn={Uri.EscapeDataString(node.Alpn)}");
                    if (node.AllowInsecure) qs.Add("allow_insecure=1");
                    qs.Add("congestion_control=bbr");
                    var qString = "?" + string.Join("&", qs);
                    return $"tuic://{Uri.EscapeDataString(node.Uuid ?? "")}:{Uri.EscapeDataString(node.Password ?? "")}@{node.Host}{qString}#{name}";
                }
                else if (proto.Equals("AnyTLS", StringComparison.OrdinalIgnoreCase))
                {
                    var qs = new List<string>();
                    if (!string.IsNullOrEmpty(node.Sni)) qs.Add($"sni={Uri.EscapeDataString(node.Sni)}");
                    if (!string.IsNullOrEmpty(node.Fingerprint) && node.Fingerprint != "none") qs.Add($"fp={Uri.EscapeDataString(node.Fingerprint)}");
                    if (!string.IsNullOrEmpty(node.Alpn)) qs.Add($"alpn={Uri.EscapeDataString(node.Alpn)}");
                    if (node.AllowInsecure) qs.Add("insecure=1");
                    var qString = qs.Count > 0 ? "?" + string.Join("&", qs) : "";
                    return $"anytls://{Uri.EscapeDataString(node.Password ?? "")}@{node.Host}{qString}#{name}";
                }
                else if (proto.Equals("Naive", StringComparison.OrdinalIgnoreCase) || proto.Equals("NaiveProxy", StringComparison.OrdinalIgnoreCase))
                {
                    string user = !string.IsNullOrEmpty(node.Username) ? node.Username : (node.Uuid ?? "");
                    var qs = new List<string>();
                    if (!string.IsNullOrEmpty(node.Sni)) qs.Add($"sni={Uri.EscapeDataString(node.Sni)}");
                    var qString = qs.Count > 0 ? "?" + string.Join("&", qs) : "";
                    return $"naive+https://{Uri.EscapeDataString(user)}:{Uri.EscapeDataString(node.Password ?? "")}@{node.Host}{qString}#{name}";
                }
                else if (proto.Equals("WireGuard", StringComparison.OrdinalIgnoreCase) || proto.Equals("wg", StringComparison.OrdinalIgnoreCase))
                {
                    var qs = new List<string>();
                    if (!string.IsNullOrEmpty(node.PublicKey)) qs.Add($"publickey={Uri.EscapeDataString(node.PublicKey)}");
                    if (!string.IsNullOrEmpty(node.WgLocalAddress)) qs.Add($"address={Uri.EscapeDataString(node.WgLocalAddress)}");
                    if (!string.IsNullOrEmpty(node.WgPreSharedKey)) qs.Add($"presharedkey={Uri.EscapeDataString(node.WgPreSharedKey)}");
                    if (node.WgMtu > 0) qs.Add($"mtu={node.WgMtu}");
                    var qString = qs.Count > 0 ? "?" + string.Join("&", qs) : "";
                    return $"wireguard://{Uri.EscapeDataString(node.WgPrivateKey ?? "")}@{node.Host}{qString}#{name}";
                }
                else if (proto.Equals("SOCKS5", StringComparison.OrdinalIgnoreCase) || proto.Equals("socks", StringComparison.OrdinalIgnoreCase))
                {
                    string auth = !string.IsNullOrEmpty(node.Username) || !string.IsNullOrEmpty(node.Password)
                        ? $"{Uri.EscapeDataString(node.Username ?? "")}:{Uri.EscapeDataString(node.Password ?? "")}@"
                        : "";
                    return $"socks5://{auth}{node.Host}#{name}";
                }
                else if (proto.Equals("HTTP", StringComparison.OrdinalIgnoreCase) || proto.Equals("HTTPS", StringComparison.OrdinalIgnoreCase))
                {
                    string scheme = proto.Equals("HTTPS", StringComparison.OrdinalIgnoreCase) || string.Equals(node.Security, "tls", StringComparison.OrdinalIgnoreCase)
                        ? "https"
                        : "http";
                    string auth = !string.IsNullOrEmpty(node.Username) || !string.IsNullOrEmpty(node.Password)
                        ? $"{Uri.EscapeDataString(node.Username ?? "")}:{Uri.EscapeDataString(node.Password ?? "")}@"
                        : "";
                    return $"{scheme}://{auth}{node.Host}#{name}";
                }
                else if (proto.Equals("Snell", StringComparison.OrdinalIgnoreCase))
                {
                    var (sHost, sPort) = NodeLinkParser.SplitHostPort(node.Host);
                    int ver = node.SnellVersion > 0 ? node.SnellVersion : 4;
                    var parts = new List<string>
                    {
                        $"{(string.IsNullOrWhiteSpace(node.Name) ? "Snell" : node.Name)} = snell",
                        sHost.Trim('[', ']'),
                        (sPort > 0 ? sPort : 443).ToString(),
                        $"psk={node.Password}",
                        $"version={ver}"
                    };
                    if (ver == 6 && !string.IsNullOrEmpty(node.SnellMode) && node.SnellMode != "default")
                    {
                        parts.Add($"mode={node.SnellMode}");
                    }
                    else if (!string.IsNullOrEmpty(node.ObfsType) && node.ObfsType != "none")
                    {
                        parts.Add($"obfs={node.ObfsType}");
                        if (!string.IsNullOrEmpty(node.WsHost))
                            parts.Add($"obfs-host={node.WsHost}");
                    }
                    return string.Join(", ", parts);
                }
                else if (proto.Equals("Nowhere", StringComparison.OrdinalIgnoreCase))
                {
                    var qs = new List<string>();
                    if (!string.IsNullOrEmpty(node.Spec)) qs.Add($"spec={Uri.EscapeDataString(node.Spec)}");
                    if (!string.IsNullOrEmpty(node.Alpn)) qs.Add($"alpn={Uri.EscapeDataString(node.Alpn)}");
                    if (!string.IsNullOrEmpty(node.Sni)) qs.Add($"sni={Uri.EscapeDataString(node.Sni)}");
                    if (node.AllowInsecure) qs.Add("insecure=1");

                    var qString = qs.Count > 0 ? "?" + string.Join("&", qs) : "";
                    return $"nowhere://{Uri.EscapeDataString(node.Password ?? "")}@{node.Host}{qString}#{name}";
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



        public Task ExportBackupAsync(string zipPath)
        {
            return Task.Run(() =>
            {
                try
                {
                    if (File.Exists(zipPath))
                        File.Delete(zipPath);

                    using var zip = System.IO.Compression.ZipFile.Open(zipPath, System.IO.Compression.ZipArchiveMode.Create);
                    lock (_sync)
                    {
                        if (File.Exists(ConfigPath))
                        {
                            zip.CreateEntryFromFile(ConfigPath, "nodes_config.json");
                        }
                    }
                    if (Directory.Exists(CustomConfigService.ConfigsDirectory))
                    {
                        foreach (var cfgFile in Directory.GetFiles(CustomConfigService.ConfigsDirectory, "*.json"))
                        {
                            zip.CreateEntryFromFile(cfgFile, $"configs/{Path.GetFileName(cfgFile)}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    throw new Exception($"备份保存失败: {ex.Message}", ex);
                }
            });
        }

        public Task ImportBackupAsync(string zipPath)
        {
            return Task.Run(() =>
            {
                try
                {
                    using var zip = System.IO.Compression.ZipFile.OpenRead(zipPath);
                    lock (_sync)
                    {
                        foreach (var entry in zip.Entries)
                        {
                            if (entry.FullName.Equals("nodes_config.json", StringComparison.OrdinalIgnoreCase))
                            {
                                string tmp = $"{ConfigPath}.{Guid.NewGuid():N}.tmp";
                                entry.ExtractToFile(tmp, overwrite: true);
                                File.Move(tmp, ConfigPath, overwrite: true);
                            }
                            else if (entry.FullName.StartsWith("configs/", StringComparison.OrdinalIgnoreCase) &&
                                     entry.FullName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                            {
                                var safeFileName = Path.GetFileName(entry.FullName);
                                if (!string.IsNullOrWhiteSpace(safeFileName))
                                {
                                    Directory.CreateDirectory(CustomConfigService.ConfigsDirectory);
                                    entry.ExtractToFile(Path.Combine(CustomConfigService.ConfigsDirectory, safeFileName), overwrite: true);
                                }
                            }
                        }
                    }
                    Load();
                }
                catch (Exception ex)
                {
                    throw new Exception($"备份导入恢复失败: {ex.Message}", ex);
                }
            });
        }
    }
}

