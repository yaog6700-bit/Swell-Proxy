using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using AnywhereWinUI.Models;

namespace AnywhereWinUI.Services
{
    public static class ConfigBuilder
    {
        private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

        public static async System.Threading.Tasks.Task<string> BuildAsync(
            PersistedNode? selectedNode = null,
            bool forceSingbox = false,
            bool singleNodeOnly = false)
        {
            var session = AppSession.Instance;
            var routingMode = session.RoutingMode;
            var bypassChina = session.BypassChina;
            var blockAds = session.BlockAds;
            var blockIPv6 = session.BlockIPv6;
            
            var ruleNetflixAction = session.RuleNetflixAction;
            var ruleChatGPTAction = session.RuleChatGPTAction;
            var ruleTelegramAction = session.RuleTelegramAction;
            var ruleGoogleAction = session.RuleGoogleAction;
            var ruleYouTubeAction = session.RuleYouTubeAction;
            var ruleTikTokAction = session.RuleTikTokAction;
            var ruleClaudeAction = session.RuleClaudeAction;

            var actions = new System.Collections.Generic.List<string>
            {
                ruleNetflixAction,
                ruleChatGPTAction,
                ruleTelegramAction,
                ruleGoogleAction,
                ruleYouTubeAction,
                ruleTikTokAction,
                ruleClaudeAction
            };
            if (session.EnableAdvancedRouting)
            {
                foreach (var routingRule in RoutingRulesService.LoadRules())
                {
                    if (routingRule.IsEnabled && !string.IsNullOrWhiteSpace(routingRule.OutboundTag))
                        actions.Add(routingRule.OutboundTag);
                }
            }

            var nodesSnapshot = NodesManager.Instance.GetNodesSnapshot();
            if (selectedNode == null)
            {
                selectedNode = nodesSnapshot.Find(n => n.Id == NodesManager.Instance.SelectedNodeId);
                // If still null, create a mock node from session
                if (selectedNode == null)
                {
                    selectedNode = new PersistedNode
                    {
                        Id = "default-selected",
                        Name = session.SelectedNodeName,
                        Protocol = session.SelectedNodeProtocol,
                        Host = session.SelectedNodeHost
                    };
                }
            }

            session.LastBuildNode = selectedNode;

            bool wantsShoes = !forceSingbox &&
                              string.Equals(session.CoreEngine, ShoesConfigBuilder.EngineShoes, StringComparison.OrdinalIgnoreCase);
            if (wantsShoes)
            {
                var shoesSecret = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
                if (ShoesConfigBuilder.TryBuild(selectedNode, session, shoesSecret, out var shoesYaml, out var fallbackReason))
                {
                    session.ResetActiveCustomState();
                    session.ClearHotSwitchTags();
                    session.ActiveCoreEngine = ShoesConfigBuilder.EngineShoes;
                    session.ActiveEngineFallbackReason = null;
                    return shoesYaml;
                }

                // Seamless fallback to sing-box without altering user's saved CoreEngine preference
                session.ActiveCoreEngine = ShoesConfigBuilder.EngineSingBox;
                session.ActiveEngineFallbackReason = fallbackReason;
            }
            else
            {
                session.ActiveCoreEngine = ShoesConfigBuilder.EngineSingBox;
                if (!forceSingbox)
                {
                    session.ActiveEngineFallbackReason = null;
                }
            }

            if (CustomConfigService.IsCustomNode(selectedNode))
            {
                session.ClearHotSwitchTags();
                return await CustomConfigService.BuildRuntimeConfigAsync(selectedNode);
            }

            session.ResetActiveCustomState();

            var enableTun = session.EnableTunMode;
            var intranet = Helpers.WindowsIntranetDetector.Detect();

            // TUN 模式下始终探测真实物理出站网卡（同时为 IPv4 IP_UNICAST_IF 与 IPv6 IPV6_UNICAST_IF 绑定真实网卡，彻底杜绝 TUN 回环）
            string? outboundInterface = null;
            if (enableTun && !session.EnableTailscale)
            {
                var tunSvc = new TunService();
                outboundInterface = tunSvc.DetectDefaultOutboundInterfaceName();
                System.Diagnostics.Debug.WriteLine($"[ConfigBuilder] TUN 出站网卡: {outboundInterface ?? "(未探测到)"}");
            }

            bool hasIntranetTunnelRoutes = intranet.DirectDomainSuffixes.Count > 0 || intranet.DirectIpCidrs.Count > 0;

            // 检测本地 sing-box 内核版本，用于按版本生成不同的配置节（兼容 1.13 / 1.14 / 1.15+）
            var coreVersionStr = CoreUpdateService.GetLocalSingboxVersionText();
            var coreVersion = CoreUpdateService.GetLocalSingboxNumericVersion();
            bool isSingbox114OrAbove = coreVersion == null || coreVersion >= new System.Version(1, 14, 0);
            bool isSingbox115OrAbove = coreVersion != null && coreVersion >= new System.Version(1, 15, 0);

            // Per-session bearer secret (synced into AppSession inside CoreManager.StartAsync after old core stops)
            var apiSecret = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();

            var outbounds = BuildOutbounds(
                selectedNode,
                nodesSnapshot,
                session.IsAutoNodeSelect,
                enableTun,
                outboundInterface,
                intranet,
                hasIntranetTunnelRoutes,
                isSingbox114OrAbove,
                singleNodeOnly,
                out var wireGuardEndpoints,
                out var validOutboundTags,
                out var hotSwitchTags,
                actions.ToArray());

            session.SetHotSwitchTags(hotSwitchTags);

            var experimentalObj = new JsonObject
            {
                ["clash_api"] = new JsonObject
                {
                    ["external_controller"] = "127.0.0.1:9090",
                    ["external_ui"] = "",
                    ["external_ui_download_url"] = "",
                    ["external_ui_download_detour"] = "direct",
                    ["secret"] = apiSecret,
                    ["default_mode"] = "rule"
                }
            };

            // Only attach cache_file when FakeDNS is explicitly enabled!
            // In sing-box, an enabled cache_file automatically runs LoadSelected("proxy") on startup
            // and overrides selector's "default" outbound with stale cached selections from previous runs.
            if (session.EnableFakeDns)
            {
                experimentalObj["cache_file"] = new JsonObject
                {
                    ["enabled"] = true,
                    ["path"] = System.IO.Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "SwellProxy",
                        "swell_fakeip.db"),
                    ["store_fakeip"] = true,
                    ["store_rdrc"] = false
                };
            }

            var config = new JsonObject
            {
                ["log"] = new JsonObject
                {
                    ["level"] = "warn",
                    ["timestamp"] = true
                },
                ["dns"] = BuildDns(bypassChina, blockIPv6, routingMode, intranet, enableTun, outboundInterface),
                ["inbounds"] = await BuildInboundsAsync(enableTun, selectedNode, nodesSnapshot, intranet, isSingbox115OrAbove, singleNodeOnly),
                ["outbounds"] = outbounds,
                ["route"] = BuildRoute(routingMode, bypassChina, blockAds, intranet, validOutboundTags, enableTun, outboundInterface, isSingbox114OrAbove),
                ["experimental"] = experimentalObj
            };

            if (isSingbox114OrAbove)
            {
                config["http_clients"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["tag"] = "default-http",
                        ["detour"] = "proxy"
                    },
                    new JsonObject
                    {
                        ["tag"] = "direct-http",
                        ["detour"] = "direct"
                    }
                };
                System.Diagnostics.Debug.WriteLine($"[ConfigBuilder] 检测到 sing-box {coreVersionStr}，已启用 http_clients，1.15 原生 TUN={isSingbox115OrAbove}");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"[ConfigBuilder] 检测到 sing-box {coreVersionStr}（< 1.14.0），跳过 http_clients 注入");
            }

            // 汇总所有 endpoints（WireGuard 节点端点 + Tailscale 端点）
            var allEndpoints = new JsonArray();
            foreach (var wgEp in wireGuardEndpoints)
            {
                if (wgEp != null) allEndpoints.Add(wgEp.DeepClone());
            }
            if (session.EnableTailscale)
            {
                foreach (var tsEp in BuildEndpoints(session))
                {
                    if (tsEp != null) allEndpoints.Add(tsEp.DeepClone());
                }
            }
            if (allEndpoints.Count > 0)
            {
                config["endpoints"] = allEndpoints;
            }

            var configJson = config.ToJsonString(JsonOpts);

            // Allow plugins to modify the config before it is written to disk
            configJson = await Plugins.PluginManager.Instance.FireBeforeCoreStartAsync(configJson);

            return configJson;
        }

        private static JsonArray BuildEndpoints(AppSession session)
        {
            var ep = new JsonObject
            {
                ["type"] = "tailscale",
                ["tag"]  = "tailscale-ep",
                ["ephemeral"] = session.TailscaleEphemeral,
                ["accept_routes"] = session.TailscaleAcceptRoutes,
                ["advertise_exit_node"] = session.TailscaleAdvertiseExitNode,
                ["domain_resolver"] = "local-dns"
            };

            if (!string.IsNullOrWhiteSpace(session.TailscaleAuthKey))
                ep["auth_key"] = session.TailscaleAuthKey;

            if (!string.IsNullOrWhiteSpace(session.TailscaleHostname))
                ep["hostname"] = session.TailscaleHostname;

            // 状态目录：优先使用用户配置值；留空时自动回退到 App 数据目录，确保有写权限
            var stateDir = string.IsNullOrWhiteSpace(session.TailscaleStateDirectory)
                ? System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "SwellProxy", "tailscale-state")
                : session.TailscaleStateDirectory;

            // 提前创建目录，防止 sing-box 因目录不存在而报 Access is denied
            try { System.IO.Directory.CreateDirectory(stateDir); } catch { }

            ep["state_directory"] = stateDir;

            if (!string.IsNullOrWhiteSpace(session.TailscaleControlUrl))
                ep["control_url"] = session.TailscaleControlUrl;

            if (!string.IsNullOrWhiteSpace(session.TailscaleExitNode))
                ep["exit_node"] = session.TailscaleExitNode;

            if (!string.IsNullOrWhiteSpace(session.TailscaleAdvertiseRoutes))
            {
                var routeArr = new JsonArray();
                foreach (var cidr in session.TailscaleAdvertiseRoutes.Split(',', System.StringSplitOptions.RemoveEmptyEntries | System.StringSplitOptions.TrimEntries))
                    routeArr.Add((JsonNode)cidr);
                if (routeArr.Count > 0)
                    ep["advertise_routes"] = routeArr;
            }

            return new JsonArray { ep };
        }

        private static JsonObject BuildDns(
            bool bypassChina,
            bool blockIPv6,
            string routingMode,
            Helpers.WindowsIntranetDetector.IntranetDetectionResult intranet,
            bool enableTun = false,
            string? outboundInterface = null)
        {
            var session = AppSession.Instance;
            string proxyDns = !string.IsNullOrEmpty(session.ProxyDns) ? session.ProxyDns : "https://1.1.1.1/dns-query";
            string directDns = !string.IsNullOrEmpty(session.DirectDns) ? session.DirectDns : (enableTun ? "223.5.5.5" : "114.114.114.114");
            string strategy = !string.IsNullOrEmpty(session.DnsStrategy) ? session.DnsStrategy : "prefer_ipv4";

            var proxyDnsObj = new JsonObject { ["tag"] = "remote-dns", ["detour"] = "proxy", ["domain_resolver"] = "bootstrap-dns" };
            ParseAndSetDnsServer(proxyDnsObj, proxyDns);

            var localDnsObj = new JsonObject { ["tag"] = "local-dns", ["domain_resolver"] = "bootstrap-dns" };
            ParseAndSetDnsServer(localDnsObj, directDns);
            if (enableTun && !string.IsNullOrEmpty(outboundInterface))
            {
                localDnsObj["bind_interface"] = outboundInterface;
            }

            JsonObject bootstrapDnsObj;
            if (enableTun)
            {
                // Use UDP 223.5.5.5 bound directly to the physical NIC (and excluded in route_exclude_address):
                // avoids ISP/router drops on outbound TCP port 53 while still preventing any TUN DNS loop.
                bootstrapDnsObj = new JsonObject { ["tag"] = "bootstrap-dns", ["type"] = "udp", ["server"] = "223.5.5.5" };
                if (!string.IsNullOrEmpty(outboundInterface))
                {
                    bootstrapDnsObj["bind_interface"] = outboundInterface;
                }
            }
            else
            {
                bootstrapDnsObj = new JsonObject { ["tag"] = "bootstrap-dns", ["type"] = "local" };
            }

            var servers = new JsonArray { (JsonNode)proxyDnsObj, (JsonNode)localDnsObj, (JsonNode)bootstrapDnsObj };
            var rules = new JsonArray();

            int intranetDnsIdx = 0;
            string? singleTunnelNic = enableTun && intranet.TunnelInterfaceNames.Count == 1
                ? intranet.TunnelInterfaceNames[0]
                : null;
            foreach (var kv in intranet.SplitDnsServerToDomains)
            {
                if (kv.Value.Count == 0) continue;
                string tag = $"intranet-dns-{intranetDnsIdx++}";
                var intranetDnsObj = new JsonObject
                {
                    ["tag"] = tag,
                    ["type"] = "udp",
                    ["server"] = kv.Key
                };
                if (!string.IsNullOrEmpty(singleTunnelNic))
                {
                    intranetDnsObj["bind_interface"] = singleTunnelNic;
                }
                servers.Add(intranetDnsObj);
                var domArr = new JsonArray();
                foreach (var d in kv.Value) domArr.Add((JsonNode)d);
                rules.Add(new JsonObject
                {
                    ["domain_suffix"] = domArr,
                    ["server"] = tag
                });
            }

            if (enableTun && strategy != "ipv6_only" && !session.AllowIPv6Dns)
            {
                rules.Add(new JsonObject
                {
                    ["query_type"] = new JsonArray { (JsonNode)"AAAA" },
                    ["action"] = "reject"
                });
            }
            else if (blockIPv6 && !session.AllowIPv6Dns)
            {
                rules.Add(new JsonObject
                {
                    ["query_type"] = new JsonArray { (JsonNode)"AAAA" },
                    ["action"] = "reject"
                });
            }

            if (bypassChina && routingMode == "smart")
            {
                rules.Add(new JsonObject
                {
                    ["domain_suffix"] = new JsonArray
                    {
                        (JsonNode)"googleapis.cn",
                        (JsonNode)"xn--ngstr-lra8j.com",
                        (JsonNode)"play.googleapis.com"
                    },
                    ["server"] = "remote-dns"
                });

                rules.Add(new JsonObject
                {
                    ["rule_set"] = new JsonArray { (JsonNode)"geosite-cn" },
                    ["server"] = "local-dns"
                });
            }

            if (session.EnableFakeDns)
            {
                servers.Add(new JsonObject
                {
                    ["tag"] = "fakeip",
                    ["type"] = "fakeip",
                    ["inet4_range"] = "198.18.0.0/15",
                    // RFC 5180 IPv6 benchmarking range (twin of 198.18.0.0/15) so fc00::/7 private rules never bypass FakeIP
                    ["inet6_range"] = "2001:2::/48"
                });

                rules.Add(new JsonObject
                {
                    ["query_type"] = new JsonArray { (JsonNode)"A", (JsonNode)"AAAA" },
                    ["server"] = "fakeip"
                });
            }

            var dnsConfig = new JsonObject
            {
                ["servers"] = servers,
                ["rules"] = rules,
                ["final"] = "remote-dns",
                ["strategy"] = strategy
            };

            // FakeDNS requires cache to map fake IPs back to domains
            if (!session.EnableDnsCache && !session.EnableFakeDns)
            {
                dnsConfig["disable_cache"] = true;
                dnsConfig["disable_expire"] = true;
            }

            return dnsConfig;
        }

        private static void ParseAndSetDnsServer(JsonObject obj, string address)
        {
            if (string.IsNullOrWhiteSpace(address)) return;
            address = address.Trim();

            if (address.Contains("://", StringComparison.Ordinal) &&
                Uri.TryCreate(address, UriKind.Absolute, out var uri) &&
                uri.Scheme != "file")
            {
                string scheme = uri.Scheme.ToLowerInvariant();
                obj["type"] = scheme;
                obj["server"] = CleanHost(uri.IdnHost);

                if (!uri.IsDefaultPort && uri.Port > 0)
                {
                    obj["server_port"] = uri.Port;
                }

                if ((scheme == "https" || scheme == "h3") && !string.IsNullOrEmpty(uri.PathAndQuery) && uri.PathAndQuery != "/")
                {
                    obj["path"] = uri.PathAndQuery;
                }
                return;
            }

            // Plain IPv4 / IPv6 / host[:port] without scheme -> UDP
            obj["type"] = "udp";
            if (System.Net.IPAddress.TryParse(CleanHost(address), out var directIp))
            {
                obj["server"] = directIp.ToString();
                return;
            }

            if (NodeLinkParser.TrySplitHostPort(address, out var dnsHost, out var dnsPort))
            {
                obj["server"] = CleanHost(dnsHost);
                if (dnsPort > 0)
                {
                    obj["server_port"] = dnsPort;
                }
            }
            else
            {
                obj["server"] = CleanHost(address);
            }
        }

        private static async System.Threading.Tasks.Task<JsonArray> BuildInboundsAsync(
            bool enableTun,
            PersistedNode? selectedNode,
            List<PersistedNode> nodesSnapshot,
            Helpers.WindowsIntranetDetector.IntranetDetectionResult intranet,
            bool isSingbox115OrAbove = false,
            bool singleNodeOnly = false)
        {
            var list = new JsonArray();

            if (enableTun)
            {
                var tunObj = new JsonObject
                {
                    ["type"]           = "tun",
                    ["tag"]            = "tun-in",
                    ["interface_name"] = TunService.DefaultTunInterfaceName,
                    ["address"]        = new JsonArray { (JsonNode)"172.18.0.1/30", (JsonNode)"fdfe:dcba:9876::1/126" },
                    ["auto_route"]     = true,
                    ["strict_route"]   = false,
                    // Standard 1500 Ethernet MTU prevents oversized TCP MSS (8960) & QUIC UDP frames from blackholing on PPPoE / IPLC VLESS links
                    ["mtu"]            = 1500
                };

                if (!isSingbox115OrAbove)
                {
                    tunObj["stack"] = NormalizeTunStack(AppSession.Instance.TunStack);
                }

                var excludeAddresses = new JsonArray
                {
                    (JsonNode)"10.0.0.0/8",
                    (JsonNode)"172.16.0.0/12",
                    (JsonNode)"192.168.0.0/16"
                };
                if (!AppSession.Instance.EnableTailscale)
                {
                    excludeAddresses.Add((JsonNode)"100.64.0.0/10");
                }
                excludeAddresses.Add((JsonNode)"169.254.0.0/16");
                excludeAddresses.Add((JsonNode)"127.0.0.0/8");
                excludeAddresses.Add((JsonNode)"224.0.0.0/4");
                excludeAddresses.Add((JsonNode)"240.0.0.0/4");
                excludeAddresses.Add((JsonNode)"fc00::/7");
                excludeAddresses.Add((JsonNode)"fe80::/10");
                excludeAddresses.Add((JsonNode)"223.5.5.5/32");
                excludeAddresses.Add((JsonNode)"114.114.114.114/32");
                excludeAddresses.Add((JsonNode)"8.8.8.8/32");
                excludeAddresses.Add((JsonNode)"1.1.1.1/32");
                excludeAddresses.Add((JsonNode)"2400:3200::1/128");

                // Exclude ALL valid IPv4/IPv6 proxy server addresses & corporate VPN split-tunnel CIDRs/DNS so TUN never captures them
                var resolvedCidrs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var priorityDomains = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var uniqueDomains = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var excludeLock = new object();

                // Also exclude custom DirectDns IP if configured
                string rawDirectDns = AppSession.Instance.DirectDns ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(rawDirectDns))
                {
                    string dnsHostCandidate = rawDirectDns.Contains("://", StringComparison.Ordinal) &&
                                              Uri.TryCreate(rawDirectDns, UriKind.Absolute, out var dUri)
                        ? CleanHost(dUri.IdnHost)
                        : (NodeLinkParser.TrySplitHostPort(rawDirectDns, out var dh, out _) ? CleanHost(dh) : CleanHost(rawDirectDns));
                    if (System.Net.IPAddress.TryParse(dnsHostCandidate, out var customDnsIp) && IsValidRoutableServerIp(customDnsIp))
                    {
                        resolvedCidrs.Add(customDnsIp.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6
                            ? $"{customDnsIp}/128"
                            : $"{customDnsIp}/32");
                    }
                }

                foreach (var intranetCidr in intranet.DirectIpCidrs)
                {
                    if (!string.IsNullOrWhiteSpace(intranetCidr) && !string.Equals(intranetCidr, "100.64.0.0/10", StringComparison.Ordinal))
                    {
                        resolvedCidrs.Add(intranetCidr);
                    }
                }
                foreach (var splitDnsIp in intranet.SplitDnsServerToDomains.Keys)
                {
                    if (System.Net.IPAddress.TryParse(splitDnsIp, out var dnsIp) && IsValidRoutableServerIp(dnsIp))
                    {
                        string cidr = dnsIp.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6
                            ? $"{dnsIp}/128"
                            : $"{dnsIp}/32";
                        resolvedCidrs.Add(cidr);
                    }
                }

                void CollectNodeHost(PersistedNode? n, bool highPriority)
                {
                    if (n == null || string.IsNullOrWhiteSpace(n.Host) || CustomConfigService.IsCustomNode(n) || IsInformationalPseudoNode(n))
                        return;
                    var (nHost, _) = NodeLinkParser.SplitHostPort(n.Host);
                    var cleanNHost = CleanHost(nHost);
                    if (string.IsNullOrWhiteSpace(cleanNHost)) return;

                    if (System.Net.IPAddress.TryParse(cleanNHost, out var ip))
                    {
                        if (IsValidRoutableServerIp(ip))
                        {
                            string cidr = ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6 ? $"{ip}/128" : $"{ip}/32";
                            resolvedCidrs.Add(cidr);
                        }
                    }
                    else if (highPriority)
                    {
                        priorityDomains.Add(cleanNHost);
                    }
                    else if (!priorityDomains.Contains(cleanNHost))
                    {
                        uniqueDomains.Add(cleanNHost);
                    }
                }

                // Always prioritize the user's active node (and its proxy chain hop) with a dedicated 1.5s lookup
                CollectNodeHost(selectedNode, highPriority: true);
                if (selectedNode != null && !string.IsNullOrEmpty(selectedNode.ProxyChainId))
                {
                    CollectNodeHost(nodesSnapshot.Find(n => n.Id == selectedNode.ProxyChainId), highPriority: true);
                }

                if (!singleNodeOnly)
                {
                    foreach (var node in nodesSnapshot)
                    {
                        CollectNodeHost(node, highPriority: false);
                    }
                }

                foreach (var pDomain in priorityDomains)
                {
                    try
                    {
                        using var pCts = new System.Threading.CancellationTokenSource(TimeSpan.FromMilliseconds(1500));
                        var ips = await System.Net.Dns.GetHostAddressesAsync(pDomain, pCts.Token).ConfigureAwait(false);
                        foreach (var resolvedIp in ips)
                        {
                            if (!IsValidRoutableServerIp(resolvedIp)) continue;
                            string cidr = resolvedIp.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6
                                ? $"{resolvedIp}/128"
                                : $"{resolvedIp}/32";
                            resolvedCidrs.Add(cidr);
                        }
                    }
                    catch { }
                }

                if (uniqueDomains.Count > 0)
                {
                    using var gate = new System.Threading.SemaphoreSlim(24, 24);
                    using var overallCts = new System.Threading.CancellationTokenSource(TimeSpan.FromMilliseconds(1500));
                    var dnsTasks = new List<System.Threading.Tasks.Task>(uniqueDomains.Count);

                    foreach (var domain in uniqueDomains)
                    {
                        var capturedHost = domain;
                        dnsTasks.Add(System.Threading.Tasks.Task.Run(async () =>
                        {
                            bool entered = false;
                            try
                            {
                                await gate.WaitAsync(overallCts.Token).ConfigureAwait(false);
                                entered = true;
                                using var perHostCts = System.Threading.CancellationTokenSource.CreateLinkedTokenSource(overallCts.Token);
                                perHostCts.CancelAfter(800);
                                var ips = await System.Net.Dns.GetHostAddressesAsync(capturedHost, perHostCts.Token).ConfigureAwait(false);
                                lock (excludeLock)
                                {
                                    foreach (var resolvedIp in ips)
                                    {
                                        if (!IsValidRoutableServerIp(resolvedIp)) continue;
                                        string cidr = resolvedIp.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6
                                            ? $"{resolvedIp}/128"
                                            : $"{resolvedIp}/32";
                                        resolvedCidrs.Add(cidr);
                                    }
                                }
                            }
                            catch { /* Timeout or DNS error — safely skipped */ }
                            finally
                            {
                                if (entered)
                                {
                                    try { gate.Release(); } catch { }
                                }
                            }
                        }));
                    }

                    try
                    {
                        await System.Threading.Tasks.Task.WhenAll(dnsTasks)
                            .WaitAsync(TimeSpan.FromMilliseconds(1500))
                            .ConfigureAwait(false);
                    }
                    catch (TimeoutException)
                    {
                        // Never crash TUN startup on slow DNS — snapshot whatever resolved within 1.5s!
                        try { overallCts.Cancel(); } catch { }
                    }
                }

                lock (excludeLock)
                {
                    foreach (var cidr in resolvedCidrs)
                    {
                        excludeAddresses.Add((JsonNode)cidr);
                    }
                }

                tunObj["route_exclude_address"] = excludeAddresses;
                list.Add(tunObj);
            }

            list.Add(new JsonObject
            {
                ["type"]        = "mixed",
                ["tag"]         = "mixed-in",
                ["listen"]      = AppSession.Instance.AllowLanAccess ? "0.0.0.0" : "127.0.0.1",
                ["listen_port"] = AppSession.Instance.MixedPort
            });

            return list;
        }

        private static bool IsValidRoutableServerIp(System.Net.IPAddress ip)
        {
            if (System.Net.IPAddress.IsLoopback(ip) ||
                ip.Equals(System.Net.IPAddress.Any) ||
                ip.Equals(System.Net.IPAddress.IPv6Any) ||
                ip.Equals(System.Net.IPAddress.Broadcast) ||
                ip.IsIPv6LinkLocal)
            {
                return false;
            }

            var bytes = ip.GetAddressBytes();
            if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && bytes.Length == 4)
            {
                // Never add FakeIP (198.18.0.0/15) or TUN subnet (172.18.0.0/30) to route_exclude_address
                if (bytes[0] == 198 && (bytes[1] == 18 || bytes[1] == 19))
                    return false;
                if (bytes[0] == 172 && bytes[1] == 18 && bytes[2] == 0 && bytes[3] <= 3)
                    return false;
            }
            else if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6 && bytes.Length == 16)
            {
                // Never add FakeIPv6 (2001:2::/48) or ULA (fc00::/7) to route_exclude_address
                if (bytes[0] == 0x20 && bytes[1] == 0x01 && bytes[2] == 0x00 && bytes[3] == 0x02 && bytes[4] == 0x00 && bytes[5] == 0x00)
                    return false;
                if ((bytes[0] & 0xFE) == 0xFC)
                    return false;
            }

            return true;
        }

        private static bool IsInformationalPseudoNode(PersistedNode node)
        {
            if (CustomConfigService.IsCustomNode(node)) return true;
            if (!NodeLinkParser.TrySplitHostPort(node.Host, out var h, out var p) || string.IsNullOrWhiteSpace(h) || p <= 0)
                return true;

            string cleanH = CleanHost(h);
            if (cleanH is "127.0.0.1" or "0.0.0.0" or "localhost" or "::1")
                return true;

            string name = node.Name ?? string.Empty;
            if (name.Contains("剩余流量", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("套餐到期", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("到期时间", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("距离下次重置", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("官网", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            return false;
        }

        internal static string? GetXrayExclusiveIncompatibilityReason(PersistedNode? node)
        {
            if (node == null || CustomConfigService.IsCustomNode(node))
                return null;

            string net = (node.Network ?? string.Empty).Trim().ToLowerInvariant();
            if (net is "xhttp" or "splithttp" or "kcp" or "mkcp")
            {
                return $"传输层协议 \"{node.Network}\"（Xray 专属扩展）";
            }

            string proto = (node.Protocol ?? string.Empty).Trim().ToLowerInvariant();
            if (proto.Contains("vless") &&
                !string.IsNullOrWhiteSpace(node.VlessEncryption) &&
                !string.Equals(node.VlessEncryption.Trim(), "none", StringComparison.OrdinalIgnoreCase))
            {
                return "VLESS 传输加密（Xray 专属扩展）";
            }

            return null;
        }

        private static bool IsCompatibleSingboxSelectorNode(PersistedNode node)
        {
            if (IsInformationalPseudoNode(node) || IsWireGuardNode(node))
                return false;

            // Never preload "nowhere" or Xray-exclusive nodes into the shared selector: official sing-box 1.14+ exits
            // if even a single unselected incompatible outbound exists in outbounds!
            string proto = (node.Protocol ?? string.Empty).Trim().ToLowerInvariant();
            if (proto == "nowhere")
                return false;

            if (GetXrayExclusiveIncompatibilityReason(node) != null)
                return false;

            string net = (node.Network ?? string.Empty).Trim().ToLowerInvariant();
            if (net == "quic")
                return false;

            string sec = (node.Security ?? string.Empty).Trim().ToLowerInvariant();
            if (sec == "reality" && string.IsNullOrWhiteSpace(node.PublicKey))
                return false;

            return true;
        }

        private static bool IsSafeHotSwitchCandidate(PersistedNode node, List<PersistedNode> nodesSnapshot)
        {
            if (!IsCompatibleSingboxSelectorNode(node))
                return false;

            if (!string.IsNullOrEmpty(node.ProxyChainId))
            {
                var chainNode = nodesSnapshot.Find(n => n.Id == node.ProxyChainId);
                if (chainNode == null || !IsCompatibleSingboxSelectorNode(chainNode))
                    return false;
            }

            return true;
        }

        private static JsonArray BuildOutbounds(
            PersistedNode selectedNode,
            List<PersistedNode> nodesSnapshot,
            bool isAutoSelect,
            bool enableTun,
            string? outboundInterface,
            Helpers.WindowsIntranetDetector.IntranetDetectionResult intranet,
            bool hasIntranetTunnelRoutes,
            bool isSingbox114OrAbove,
            bool singleNodeOnly,
            out JsonArray wireGuardEndpoints,
            out HashSet<string> validOutboundTags,
            out HashSet<string> hotSwitchTags,
            params string[] appActions)
        {
            var list = new JsonArray();
            var wgEndpoints = new JsonArray();
            wireGuardEndpoints = wgEndpoints;
            var addedTags = new HashSet<string>(StringComparer.Ordinal);
            var selectorTagSet = new HashSet<string>(StringComparer.Ordinal);
            var selectorNodeIds = new List<string>();
            var urlTestNodeIds = new List<string>();
            validOutboundTags = addedTags;
            hotSwitchTags = selectorTagSet;

            bool needsUrlTest = isAutoSelect;
            foreach (var act in appActions)
            {
                if (act == "urltest")
                {
                    needsUrlTest = true;
                    break;
                }
            }

            string primaryTag = !string.IsNullOrWhiteSpace(selectedNode.Id) ? selectedNode.Id : "selected-node";

            void EnsureNodeGenerated(PersistedNode node, string tag)
            {
                if (addedTags.Contains(tag)) return;
                var (nHost, nPort) = NodeLinkParser.SplitHostPort(node.Host);
                if (nPort == 0) nPort = 443;

                var generated = GenerateOutboundsForNode(node, nodesSnapshot, nHost, nPort, tag, isSingbox114OrAbove, wgEndpoints, addedTags);
                foreach (var ob in generated)
                {
                    // Bind non-detoured proxy outbounds directly to the physical NIC in TUN mode so even TTL=1s
                    // or multi-CNAME dynamic GTM proxy server IPs never loop back into singbox-tun!
                    if (enableTun && !string.IsNullOrEmpty(outboundInterface) && !ob.ContainsKey("detour"))
                    {
                        ob["bind_interface"] = outboundInterface;
                    }

                    var obTag = ob["tag"]?.ToString() ?? string.Empty;
                    if (addedTags.Add(obTag))
                    {
                        list.Add(ob);
                    }
                }
            }

            // 1. Always ensure the selected fallback/active node is generated first
            EnsureNodeGenerated(selectedNode, primaryTag);

            // 2. Populate selector & urltest outbounds for <5ms zero-drop hot node switching
            //    (skipped in singleNodeOnly self-healing mode if an unrelated subscription node caused sing-box init error)
            const int MaxSelectorNodes = 400;
            const int MaxUrlTestNodes = 120;

            if (!singleNodeOnly)
            {
                // Pass A: Favorites first so urltest & selector always cover user's pinned nodes
                foreach (var node in nodesSnapshot)
                {
                    if (selectorNodeIds.Count >= MaxSelectorNodes) break;
                    if (!node.IsFavorite || !IsSafeHotSwitchCandidate(node, nodesSnapshot))
                        continue;

                    EnsureNodeGenerated(node, node.Id);
                    if (selectorTagSet.Add(node.Id))
                    {
                        selectorNodeIds.Add(node.Id);
                        if (urlTestNodeIds.Count < MaxUrlTestNodes)
                            urlTestNodeIds.Add(node.Id);
                    }
                }

                // Pass B: Remaining safe nodes in user's list order
                foreach (var node in nodesSnapshot)
                {
                    if (selectorNodeIds.Count >= MaxSelectorNodes) break;
                    if (node.IsFavorite || !IsSafeHotSwitchCandidate(node, nodesSnapshot))
                        continue;

                    EnsureNodeGenerated(node, node.Id);
                    if (selectorTagSet.Add(node.Id))
                    {
                        selectorNodeIds.Add(node.Id);
                        if (urlTestNodeIds.Count < MaxUrlTestNodes)
                            urlTestNodeIds.Add(node.Id);
                    }
                }
            }

            // Ensure primaryTag is always selectable in the main "proxy" selector (e.g. if WireGuard or standalone node)
            if (selectorTagSet.Add(primaryTag))
            {
                selectorNodeIds.Insert(0, primaryTag);
                if (!IsWireGuardNode(selectedNode) && urlTestNodeIds.Count == 0)
                {
                    urlTestNodeIds.Add(primaryTag);
                }
            }

            // 3. Check for any extra nodes explicitly referenced in routing rules (e.g. WireGuard rule target)
            foreach (var action in appActions)
            {
                if (action == "urltest") continue;

                string resolvedId = action.StartsWith("node:") ? action.Substring(5) : action;
                if (resolvedId != "proxy" && resolvedId != "direct" && resolvedId != "block" && resolvedId != "reject" && !addedTags.Contains(resolvedId))
                {
                    var extraNode = nodesSnapshot.Find(n => n.Id == resolvedId);
                    if (extraNode != null && !CustomConfigService.IsCustomNode(extraNode))
                    {
                        EnsureNodeGenerated(extraNode, extraNode.Id);
                    }
                }
            }

            // 4. ONLY build "urltest" outbound when explicitly needed (Auto Select mode or routing rule uses urltest)!
            // NOTE: sing-box's URLTest.PostStart() unconditionally probes ALL child outbounds concurrently at t=0ms
            // even when not selected, which otherwise floods DNS/NAT/TUN on startup in normal manual node mode!
            if (needsUrlTest && urlTestNodeIds.Count > 0)
            {
                var urlTestOutbounds = new JsonArray();
                foreach (var id in urlTestNodeIds)
                    urlTestOutbounds.Add((JsonNode)id);

                addedTags.Add("urltest");
                selectorTagSet.Add("urltest");
                list.Add(new JsonObject
                {
                    ["type"] = "urltest",
                    ["tag"] = "urltest",
                    ["outbounds"] = urlTestOutbounds,
                    ["url"] = "http://www.gstatic.com/generate_204",
                    ["interval"] = "3m",
                    ["tolerance"] = 50,
                    ["idle_timeout"] = "30m",
                    ["interrupt_exist_connections"] = true
                });
            }

            // 5. Build top-level "proxy" selector outbound at index 0 (100% passive — zero background probes!)
            var selectorOutbounds = new JsonArray();
            if (addedTags.Contains("urltest"))
            {
                selectorOutbounds.Add((JsonNode)"urltest");
            }
            foreach (var id in selectorNodeIds)
            {
                selectorOutbounds.Add((JsonNode)id);
            }

            string defaultTarget = (isAutoSelect && addedTags.Contains("urltest"))
                ? "urltest"
                : primaryTag;

            addedTags.Add("proxy");
            list.Insert(0, new JsonObject
            {
                ["type"] = "selector",
                ["tag"] = "proxy",
                ["outbounds"] = selectorOutbounds,
                ["default"] = defaultTarget,
                ["interrupt_exist_connections"] = true
            });

            var directOutbound = new JsonObject { ["type"] = "direct", ["tag"] = "direct" };
            if (enableTun && !string.IsNullOrEmpty(outboundInterface))
            {
                directOutbound["bind_interface"] = outboundInterface;
            }
            addedTags.Add("direct");
            list.Add(directOutbound);

            if (hasIntranetTunnelRoutes)
            {
                // Dedicated direct outbound for corporate VPN / intranet split-tunnel CIDRs & domains
                var intranetDirect = new JsonObject { ["type"] = "direct", ["tag"] = "intranet-direct" };
                if (enableTun && intranet.TunnelInterfaceNames.Count == 1)
                {
                    intranetDirect["bind_interface"] = intranet.TunnelInterfaceNames[0];
                }
                addedTags.Add("intranet-direct");
                list.Add(intranetDirect);
            }

            if (enableTun && !string.IsNullOrEmpty(outboundInterface))
            {
                foreach (var wgNode in wgEndpoints)
                {
                    if (wgNode is JsonObject wgObj && !wgObj.ContainsKey("detour"))
                    {
                        wgObj["bind_interface"] = outboundInterface;
                    }
                }
            }

            return list;
        }

        private static string NormalizeTunStack(string? stack)
        {
            return stack == "system" || stack == "gvisor" || stack == "mixed" ? stack : "mixed";
        }

        private static bool IsWireGuardNode(PersistedNode node)
        {
            var proto = node.Protocol?.ToLowerInvariant() ?? string.Empty;
            return proto.Contains("wireguard") || proto == "wg";
        }

        private static JsonObject BuildWireGuardEndpoint(PersistedNode node, string host, int port, string tag, string? detourTag = null)
        {
            var addresses = new JsonArray();
            var localAddr = !string.IsNullOrEmpty(node.WgLocalAddress) ? node.WgLocalAddress : "10.0.0.2/32";
            foreach (var a in localAddr.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                addresses.Add((JsonNode)a);

            var peer = new JsonObject
            {
                ["address"]     = CleanHost(host),
                ["port"]        = port,
                ["public_key"]  = node.PublicKey ?? string.Empty,
                ["allowed_ips"] = new JsonArray { (JsonNode)"0.0.0.0/0", (JsonNode)"::/0" }
            };
            if (!string.IsNullOrEmpty(node.WgPreSharedKey))
                peer["pre_shared_key"] = node.WgPreSharedKey;

            var endpoint = new JsonObject
            {
                ["type"]            = "wireguard",
                ["tag"]             = tag,
                ["address"]         = addresses,
                ["private_key"]     = node.WgPrivateKey ?? string.Empty,
                ["domain_resolver"] = "bootstrap-dns",
                ["peers"]           = new JsonArray { peer }
            };

            if (node.WgMtu > 0)
                endpoint["mtu"] = node.WgMtu;

            if (!string.IsNullOrEmpty(detourTag))
                endpoint["detour"] = detourTag;

            return endpoint;
        }

        private static List<JsonObject> GenerateOutboundsForNode(
            PersistedNode node,
            List<PersistedNode> nodesSnapshot,
            string host,
            int port,
            string baseTag,
            bool isSingbox114OrAbove,
            JsonArray wireGuardEndpoints,
            HashSet<string> addedTags)
        {
            var list = new List<JsonObject>();

            PersistedNode? chainNode = null;
            if (!string.IsNullOrEmpty(node.ProxyChainId))
            {
                chainNode = nodesSnapshot.Find(n => n.Id == node.ProxyChainId);
            }

            // WireGuard 出站在 sing-box 1.13+ 已彻底移除，统一迁移至顶级 endpoints
            if (IsWireGuardNode(node))
            {
                string? detourTag = chainNode != null ? $"{baseTag}-chain-proxy" : null;
                if (addedTags.Add(baseTag))
                {
                    wireGuardEndpoints.Add(BuildWireGuardEndpoint(node, host, port, baseTag, detourTag));
                }

                if (chainNode != null)
                {
                    var (chainHost, chainPort) = NodeLinkParser.SplitHostPort(chainNode.Host);
                    if (chainPort == 0) chainPort = 443;
                    string chainTag = $"{baseTag}-chain-proxy";
                    if (IsWireGuardNode(chainNode))
                    {
                        if (addedTags.Add(chainTag))
                            wireGuardEndpoints.Add(BuildWireGuardEndpoint(chainNode, chainHost, chainPort, chainTag));
                    }
                    else
                    {
                        list.Add(BuildSingleOutbound(chainNode, chainHost, chainPort, chainTag, isSingbox114OrAbove));
                    }
                }

                return list;
            }

            // 1. Generate main proxy outbound
            var proxyOutbound = BuildSingleOutbound(node, host, port, baseTag, isSingbox114OrAbove);

            if (node.IsShadowTls)
            {
                // 主代理出站的 detour 指向 shadow-tls-out
                proxyOutbound["detour"] = $"{baseTag}-shadow-tls-out";

                // 在被 shadow-tls 包裹时，物理连接�?shadow-tls 负责连接，主 outbound 中的 server/port 必须剔除
                proxyOutbound.Remove("server");
                proxyOutbound.Remove("server_port");

                // 2. 生成 shadow-tls 出站
                var shadowTlsOutbound = new JsonObject
                {
                    ["type"] = "shadowtls",
                    ["tag"] = $"{baseTag}-shadow-tls-out",
                    ["server"] = FormatHost(host),
                    ["server_port"] = port,
                    ["version"] = node.ShadowTlsVersion > 0 ? node.ShadowTlsVersion : 3,
                    ["password"] = node.ShadowTlsPassword ?? "",
                    ["tls"] = new JsonObject
                    {
                        ["enabled"] = true,
                        ["server_name"] = !string.IsNullOrEmpty(node.Sni) ? node.Sni : host,
                        ["insecure"] = node.AllowInsecure
                    }
                };

                // 如果有前置代理链，shadow-tls �?detour 应该指向前置代理
                if (chainNode != null)
                {
                    shadowTlsOutbound["detour"] = $"{baseTag}-chain-proxy";
                }

                list.Add(proxyOutbound);
                list.Add(shadowTlsOutbound);
            }
            else
            {
                // 如果没有 shadow-tls，但是有前置代理链，那么主出站的 detour 指向 chain-proxy
                if (chainNode != null)
                {
                    proxyOutbound["detour"] = $"{baseTag}-chain-proxy";
                }
                list.Add(proxyOutbound);
            }

            // 3. 如果有前置代理链，生成前置代理的出站或端点配置 (chain-proxy)
            if (chainNode != null)
            {
                var (chainHost, chainPort) = NodeLinkParser.SplitHostPort(chainNode.Host);
                if (chainPort == 0) chainPort = 443;
                string chainTag = $"{baseTag}-chain-proxy";

                if (IsWireGuardNode(chainNode))
                {
                    if (addedTags.Add(chainTag))
                    {
                        wireGuardEndpoints.Add(BuildWireGuardEndpoint(chainNode, chainHost, chainPort, chainTag));
                    }
                }
                else
                {
                    var chainOutbound = BuildSingleOutbound(chainNode, chainHost, chainPort, chainTag, isSingbox114OrAbove);
                    if (chainNode.IsShadowTls)
                    {
                        string chainStlsTag = $"{chainTag}-shadow-tls-out";
                        chainOutbound["detour"] = chainStlsTag;
                        chainOutbound.Remove("server");
                        chainOutbound.Remove("server_port");
                        list.Add(chainOutbound);
                        list.Add(new JsonObject
                        {
                            ["type"] = "shadowtls",
                            ["tag"] = chainStlsTag,
                            ["server"] = FormatHost(chainHost),
                            ["server_port"] = chainPort,
                            ["version"] = chainNode.ShadowTlsVersion > 0 ? chainNode.ShadowTlsVersion : 3,
                            ["password"] = chainNode.ShadowTlsPassword ?? "",
                            ["tls"] = new JsonObject
                            {
                                ["enabled"] = true,
                                ["server_name"] = !string.IsNullOrEmpty(chainNode.Sni) ? chainNode.Sni : chainHost,
                                ["insecure"] = chainNode.AllowInsecure
                            }
                        });
                    }
                    else
                    {
                        list.Add(chainOutbound);
                    }
                }
            }


            return list;
        }

        private static JsonObject BuildSingleOutbound(PersistedNode node, string host, int port, string tag, bool isSingbox114OrAbove)
        {
            var proxyOutbound = new JsonObject
            {
                ["tag"] = tag,
                ["server"] = FormatHost(host),
                ["server_port"] = port
            };

            string protoLower = (node.Protocol ?? string.Empty).Trim().ToLowerInvariant();
            
            // Common values
            string uuidVal = !string.IsNullOrWhiteSpace(node.Uuid) ? node.Uuid.Trim() : "de000000-0000-4000-8000-000000000000";
            string passwordVal = !string.IsNullOrEmpty(node.Password) ? node.Password : "anywhere-password";
            string securityVal = !string.IsNullOrWhiteSpace(node.Security) ? node.Security.Trim().ToLowerInvariant() : "none";
            string networkVal = !string.IsNullOrWhiteSpace(node.Network) ? node.Network.Trim().ToLowerInvariant() : "tcp";
            string pathVal = !string.IsNullOrEmpty(node.Path) ? node.Path.Trim() : "";
            string wsHostVal = !string.IsNullOrWhiteSpace(node.WsHost) ? node.WsHost.Trim() : "";
            string sniVal = !string.IsNullOrWhiteSpace(node.Sni) ? node.Sni.Trim() : CleanHost(host);
            string fingerprintVal = !string.IsNullOrWhiteSpace(node.Fingerprint) ? node.Fingerprint.Trim().ToLowerInvariant() : "none";
            bool allowInsecureVal = node.AllowInsecure;

            if (protoLower.Contains("vless"))
            {
                proxyOutbound["type"] = "vless";
                proxyOutbound["uuid"] = uuidVal;
                proxyOutbound["packet_encoding"] = "xudp";

                // Normalize Xray flow: sing-box only accepts "xtls-rprx-vision", and only over TCP + TLS/Reality.
                // NOTE: Do NOT emit "encryption" on sing-box vless outbounds — official sing-box 1.14+ rejects it as an unknown field!
                string flowVal = !string.IsNullOrWhiteSpace(node.Flow) ? node.Flow.Trim() : string.Empty;
                if (string.Equals(flowVal, "xtls-rprx-vision-udp443", StringComparison.OrdinalIgnoreCase))
                {
                    flowVal = "xtls-rprx-vision";
                }
                if (!string.IsNullOrEmpty(flowVal) &&
                    networkVal == "tcp" &&
                    (securityVal == "tls" || securityVal == "reality"))
                {
                    proxyOutbound["flow"] = flowVal;
                }

                if (securityVal == "tls" || securityVal == "reality")
                {
                    var tlsObj = new JsonObject { ["enabled"] = true };
                    
                    if (securityVal == "reality")
                    {
                        tlsObj["server_name"] = sniVal;
                        tlsObj["reality"] = new JsonObject
                        {
                            ["enabled"] = true,
                            ["public_key"] = !string.IsNullOrWhiteSpace(node.PublicKey) ? node.PublicKey.Trim() : "",
                            ["short_id"] = !string.IsNullOrWhiteSpace(node.ShortId) ? node.ShortId.Trim() : ""
                        };

                        // sing-box requires uTLS on every Reality client ("uTLS is required by reality client");
                        // default to "chrome" when a subscription omits fp=.
                        if (string.IsNullOrEmpty(fingerprintVal) || fingerprintVal == "none")
                        {
                            fingerprintVal = "chrome";
                        }
                    }
                    else
                    {
                        tlsObj["server_name"] = sniVal;
                        tlsObj["insecure"] = allowInsecureVal;
                    }

                    if (!string.IsNullOrEmpty(fingerprintVal) && fingerprintVal != "none")
                    {
                        tlsObj["utls"] = new JsonObject
                        {
                            ["enabled"] = true,
                            ["fingerprint"] = fingerprintVal
                        };
                    }

                    ApplyAlpnToTls(tlsObj, node.Alpn);
                    proxyOutbound["tls"] = tlsObj;
                }

                // Transport layer settings
                ApplyTransportToOutbound(proxyOutbound, networkVal, pathVal, wsHostVal);
            }
            else if (protoLower.Contains("vmess"))
            {
                proxyOutbound["type"] = "vmess";
                proxyOutbound["uuid"] = uuidVal;
                proxyOutbound["security"] = !string.IsNullOrEmpty(node.Encryption) ? node.Encryption : "auto";
                proxyOutbound["alter_id"] = node.AlterId;

                if (securityVal == "tls" || protoLower == "https")
                {
                    var tlsObj = new JsonObject
                    {
                        ["enabled"] = true,
                        ["server_name"] = sniVal,
                        ["insecure"] = allowInsecureVal
                    };
                    if (fingerprintVal != "none")
                    {
                        tlsObj["utls"] = new JsonObject
                        {
                            ["enabled"] = true,
                            ["fingerprint"] = fingerprintVal
                        };
                    }
                    ApplyAlpnToTls(tlsObj, node.Alpn);
                    proxyOutbound["tls"] = tlsObj;
                }

                ApplyTransportToOutbound(proxyOutbound, networkVal, pathVal, wsHostVal);
            }
            else if (protoLower.Contains("hysteria"))
            {
                proxyOutbound["type"] = "hysteria2";
                proxyOutbound["password"] = passwordVal;
                
                var tlsObj = new JsonObject
                {
                    ["enabled"] = true,
                    ["server_name"] = sniVal,
                    ["insecure"] = allowInsecureVal
                };
                ApplyAlpnToTls(tlsObj, node.Alpn);
                proxyOutbound["tls"] = tlsObj;

                if (!string.IsNullOrEmpty(node.ObfsType) && node.ObfsType != "none")
                {
                    proxyOutbound["obfs"] = new JsonObject
                    {
                        ["type"] = node.ObfsType,
                        ["password"] = node.ObfsPassword ?? ""
                    };
                }
            }
            else if (protoLower.Contains("trojan"))
            {
                proxyOutbound["type"] = "trojan";
                proxyOutbound["password"] = passwordVal;

                if (securityVal == "tls" || securityVal == "reality")
                {
                    var tlsObj = new JsonObject
                    {
                        ["enabled"] = true,
                        ["server_name"] = sniVal,
                        ["insecure"] = allowInsecureVal
                    };
                    if (securityVal == "reality")
                    {
                        tlsObj.Remove("insecure");
                        tlsObj["reality"] = new JsonObject
                        {
                            ["enabled"] = true,
                            ["public_key"] = !string.IsNullOrWhiteSpace(node.PublicKey) ? node.PublicKey.Trim() : "",
                            ["short_id"] = !string.IsNullOrWhiteSpace(node.ShortId) ? node.ShortId.Trim() : ""
                        };
                        if (string.IsNullOrEmpty(fingerprintVal) || fingerprintVal == "none")
                        {
                            fingerprintVal = "chrome";
                        }
                    }
                    if (!string.IsNullOrEmpty(fingerprintVal) && fingerprintVal != "none")
                    {
                        tlsObj["utls"] = new JsonObject
                        {
                            ["enabled"] = true,
                            ["fingerprint"] = fingerprintVal
                        };
                    }
                    ApplyAlpnToTls(tlsObj, node.Alpn);
                    proxyOutbound["tls"] = tlsObj;
                }

                ApplyTransportToOutbound(proxyOutbound, networkVal, pathVal, wsHostVal);
            }
            else if (protoLower.Contains("tuic"))
            {
                string finalUuid = uuidVal;
                string finalPassword = passwordVal;
                var colonInUuid = finalUuid.IndexOf(':');
                if (colonInUuid > 0 && finalPassword == "anywhere-password")
                {
                    finalPassword = finalUuid.Substring(colonInUuid + 1);
                    finalUuid = finalUuid.Substring(0, colonInUuid);
                }

                proxyOutbound["type"] = "tuic";
                proxyOutbound["uuid"] = finalUuid;
                proxyOutbound["password"] = finalPassword;
                proxyOutbound["congestion_control"] = "bbr";

                var alpnArr = new JsonArray();
                if (!string.IsNullOrEmpty(node.Alpn))
                {
                    foreach (var a in node.Alpn.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                        alpnArr.Add((JsonNode)a);
                }
                else
                {
                    alpnArr.Add((JsonNode)"h3");
                }

                proxyOutbound["tls"] = new JsonObject
                {
                    ["enabled"] = true,
                    ["server_name"] = sniVal,
                    ["insecure"] = allowInsecureVal,
                    ["alpn"] = alpnArr
                };
            }
            else if (protoLower.Contains("naive"))
            {
                proxyOutbound["type"] = "naive";
                proxyOutbound["username"] = !string.IsNullOrEmpty(node.Username) ? node.Username : (node.Uuid ?? "");
                proxyOutbound["password"] = passwordVal;
                proxyOutbound["tls"] = new JsonObject
                {
                    ["enabled"] = true,
                    ["server_name"] = sniVal
                };
            }
            else if (protoLower.Contains("anytls"))
            {
                proxyOutbound["type"] = "anytls";
                proxyOutbound["password"] = passwordVal;

                var tlsObj = new JsonObject
                {
                    ["enabled"] = true,
                    ["server_name"] = sniVal,
                    ["insecure"] = allowInsecureVal
                };
                if (fingerprintVal != "none")
                {
                    tlsObj["utls"] = new JsonObject
                    {
                        ["enabled"] = true,
                        ["fingerprint"] = fingerprintVal
                    };
                }
                ApplyAlpnToTls(tlsObj, node.Alpn);
                proxyOutbound["tls"] = tlsObj;
            }
            else if (protoLower == "http" || protoLower == "https")
            {
                proxyOutbound["type"] = "http";
                if (!string.IsNullOrEmpty(node.Username))
                    proxyOutbound["username"] = node.Username;
                if (!string.IsNullOrEmpty(node.Password))
                    proxyOutbound["password"] = node.Password;

                if (securityVal == "tls" || protoLower == "https")
                {
                    var tlsObj = new JsonObject
                    {
                        ["enabled"] = true,
                        ["server_name"] = sniVal,
                        ["insecure"] = allowInsecureVal
                    };
                    if (fingerprintVal != "none")
                    {
                        tlsObj["utls"] = new JsonObject
                        {
                            ["enabled"] = true,
                            ["fingerprint"] = fingerprintVal
                        };
                    }
                    ApplyAlpnToTls(tlsObj, node.Alpn);
                    proxyOutbound["tls"] = tlsObj;
                }
            }
            else if (protoLower.Contains("wireguard") || protoLower == "wg")
            {
                // WireGuard: server info lives inside peers[], not at top level
                proxyOutbound.Remove("server");
                proxyOutbound.Remove("server_port");
                proxyOutbound["type"] = "wireguard";

                // Local interface addresses (comma-separated CIDR)
                var addresses = new JsonArray();
                var localAddr = !string.IsNullOrEmpty(node.WgLocalAddress) ? node.WgLocalAddress : "10.0.0.2/32";
                foreach (var a in localAddr.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    addresses.Add((JsonNode)a);
                proxyOutbound["local_address"] = addresses;

                proxyOutbound["private_key"] = node.WgPrivateKey ?? string.Empty;

                if (node.WgMtu > 0)
                    proxyOutbound["mtu"] = node.WgMtu;

                // Peer definition
                var peer = new JsonObject
                {
                    ["server"]      = FormatHost(host),
                    ["server_port"] = port,
                    ["public_key"]  = node.PublicKey ?? string.Empty,
                    ["allowed_ips"] = new JsonArray { (JsonNode)"0.0.0.0/0", (JsonNode)"::/0" }
                };
                if (!string.IsNullOrEmpty(node.WgPreSharedKey))
                    peer["pre_shared_key"] = node.WgPreSharedKey;

                proxyOutbound["peers"] = new JsonArray { peer };
            }
            else if (protoLower == "snell")
            {
                proxyOutbound["type"] = "snell";
                proxyOutbound["psk"] = passwordVal;
                proxyOutbound["version"] = node.SnellVersion > 0 ? node.SnellVersion : 4;

                string snellObfs = !string.IsNullOrEmpty(node.ObfsType) ? node.ObfsType.ToLower() : "none";
                string snellHost = !string.IsNullOrEmpty(node.WsHost) ? node.WsHost : "bing.com";
                bool isSnellV6 = (proxyOutbound["version"]?.GetValue<int>() ?? 4) == 6;

                if (isSingbox114OrAbove)
                {
                    // Official sing-box 1.14.0+ format
                    if (isSnellV6)
                    {
                        // Snell v6 uses `mode` for traffic shaping, no HTTP obfs
                        string sMode = !string.IsNullOrEmpty(node.SnellMode) ? node.SnellMode.ToLower() : "default";
                        if (sMode != "default")
                        {
                            proxyOutbound["mode"] = sMode;
                        }
                    }
                    else if (snellObfs == "http")
                    {
                        // Snell v4/v5 flattened obfs
                        proxyOutbound["obfs_mode"] = "http";
                        proxyOutbound["obfs_host"] = snellHost;
                    }
                }
                else
                {
                    // Legacy reF1nd fork format
                    if (!isSnellV6 && snellObfs == "http")
                    {
                        proxyOutbound["obfs"] = new JsonObject 
                        { 
                            ["mode"] = "http", 
                            ["host"] = snellHost 
                        };
                    }
                }
            }
            else if (protoLower == "nowhere")
            {
                proxyOutbound["type"] = "nowhere";
                proxyOutbound["key"] = passwordVal;
                
                if (!string.IsNullOrEmpty(node.Spec))
                {
                    proxyOutbound["spec"] = node.Spec;
                }
                
                if (!string.IsNullOrEmpty(node.Alpn))
                {
                    proxyOutbound["alpn"] = node.Alpn;
                }

                proxyOutbound["tls"] = new JsonObject
                {
                    ["enabled"] = true,
                    ["server_name"] = sniVal,
                    ["insecure"] = allowInsecureVal
                };
            }
            else if (protoLower == "socks" || protoLower == "socks5")
            {
                proxyOutbound["type"] = "socks";
                proxyOutbound["version"] = "5";
                if (!string.IsNullOrEmpty(node.Username) || !string.IsNullOrEmpty(node.Password))
                {
                    proxyOutbound["username"] = node.Username;
                    proxyOutbound["password"] = node.Password;
                }
            }
            else // Shadowsocks
            {
                proxyOutbound["type"] = "shadowsocks";
                proxyOutbound["method"] = !string.IsNullOrEmpty(node.Encryption) ? node.Encryption : "aes-256-gcm";
                proxyOutbound["password"] = passwordVal;
            }

            return proxyOutbound;
        }

        private static void ApplyAlpnToTls(JsonObject tlsObj, string? alpn)
        {
            if (string.IsNullOrWhiteSpace(alpn)) return;
            var alpnArr = new JsonArray();
            foreach (var a in alpn.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                alpnArr.Add((JsonNode)a);
            }
            if (alpnArr.Count > 0)
            {
                tlsObj["alpn"] = alpnArr;
            }
        }

        private static string ExtractWsEarlyData(string rawPath, out int maxEarlyData)
        {
            maxEarlyData = 0;
            if (string.IsNullOrEmpty(rawPath)) return string.Empty;

            int qIdx = rawPath.IndexOf('?');
            if (qIdx < 0) return rawPath;

            string basePath = qIdx == 0 ? "/" : rawPath.Substring(0, qIdx);
            string query = rawPath.Substring(qIdx + 1);
            var keptParams = new List<string>();

            foreach (var part in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                int eqIdx = part.IndexOf('=');
                if (eqIdx > 0)
                {
                    string k = part.Substring(0, eqIdx);
                    string v = part.Substring(eqIdx + 1);
                    if (string.Equals(k, "ed", StringComparison.OrdinalIgnoreCase) &&
                        int.TryParse(v, out int parsedEd) &&
                        parsedEd > 0)
                    {
                        maxEarlyData = parsedEd;
                        continue;
                    }
                }
                keptParams.Add(part);
            }

            if (maxEarlyData <= 0)
                return rawPath;

            return keptParams.Count > 0
                ? $"{basePath}?{string.Join("&", keptParams)}"
                : basePath;
        }

        private static void ApplyTransportToOutbound(JsonObject proxyOutbound, string networkVal, string pathVal, string wsHostVal)
        {
            if (networkVal == "ws")
            {
                var transportObj = new JsonObject
                {
                    ["type"] = "ws"
                };
                string cleanWsPath = ExtractWsEarlyData(pathVal, out int maxEarlyData);
                if (!string.IsNullOrEmpty(cleanWsPath))
                    transportObj["path"] = cleanWsPath;
                if (!string.IsNullOrEmpty(wsHostVal))
                    transportObj["headers"] = new JsonObject { ["Host"] = wsHostVal };
                if (maxEarlyData > 0)
                {
                    transportObj["max_early_data"] = maxEarlyData;
                    transportObj["early_data_header_name"] = "Sec-WebSocket-Protocol";
                }
                proxyOutbound["transport"] = transportObj;
            }
            else if (networkVal == "httpupgrade")
            {
                var transportObj = new JsonObject
                {
                    ["type"] = "httpupgrade"
                };
                if (!string.IsNullOrEmpty(pathVal))
                    transportObj["path"] = pathVal;
                if (!string.IsNullOrEmpty(wsHostVal))
                    transportObj["host"] = wsHostVal;
                proxyOutbound["transport"] = transportObj;
            }
            else if (networkVal == "grpc")
            {
                var transportObj = new JsonObject
                {
                    ["type"] = "grpc"
                };
                if (!string.IsNullOrEmpty(pathVal))
                    transportObj["service_name"] = pathVal.TrimStart('/');
                proxyOutbound["transport"] = transportObj;
            }
            else if (networkVal == "h2" || networkVal == "http")
            {
                var transportObj = new JsonObject
                {
                    ["type"] = "http"
                };
                if (!string.IsNullOrEmpty(pathVal))
                {
                    transportObj["path"] = pathVal;
                }
                if (!string.IsNullOrEmpty(wsHostVal))
                {
                    transportObj["host"] = new JsonArray { (JsonNode)wsHostVal };
                }
                proxyOutbound["transport"] = transportObj;
            }
        }

        private static string CleanHost(string? host)
        {
            if (string.IsNullOrEmpty(host)) return string.Empty;
            if (host.StartsWith('[') && host.EndsWith(']'))
                return host.Substring(1, host.Length - 2);
            return host;
        }

        private static string FormatHost(string? host)
        {
            if (string.IsNullOrEmpty(host)) return string.Empty;
            var clean = CleanHost(host);
            return clean.Contains(':') ? $"[{clean}]" : clean;
        }

        private static JsonObject BuildRoute(
            string routingMode,
            bool bypassChina,
            bool blockAds,
            Helpers.WindowsIntranetDetector.IntranetDetectionResult intranet,
            HashSet<string> validOutboundTags,
            bool enableTun = false,
            string? outboundInterface = null,
            bool isSingbox114OrAbove = false)
        {
            var rules = new JsonArray
            {
                new JsonObject
                {
                    ["port"] = new JsonArray { (JsonNode)53 },
                    ["action"] = "hijack-dns"
                },
                new JsonObject
                {
                    ["protocol"] = "dns",
                    ["action"] = "hijack-dns"
                },
                new JsonObject
                {
                    ["action"] = "sniff"
                },
                new JsonObject
                {
                    ["ip_is_private"] = true,
                    ["outbound"] = "direct"
                }
            };

            if (AppSession.Instance.EnableTailscale)
            {
                rules.Add(new JsonObject
                {
                    ["domain_suffix"] = new JsonArray { (JsonNode)"ts.net" },
                    ["outbound"] = "tailscale-ep"
                });
                rules.Add(new JsonObject
                {
                    ["ip_cidr"] = new JsonArray { (JsonNode)"100.64.0.0/10" },
                    ["outbound"] = "tailscale-ep"
                });
            }

            string intranetTarget = validOutboundTags.Contains("intranet-direct") ? "intranet-direct" : "direct";
            if (intranet.DirectDomainSuffixes.Count > 0)
            {
                var domArr = new JsonArray();
                foreach (var d in intranet.DirectDomainSuffixes) domArr.Add((JsonNode)d);
                rules.Add(new JsonObject
                {
                    ["domain_suffix"] = domArr,
                    ["outbound"] = intranetTarget
                });
            }
            if (intranet.DirectIpCidrs.Count > 0)
            {
                var cidrArr = new JsonArray();
                foreach (var c in intranet.DirectIpCidrs) cidrArr.Add((JsonNode)c);
                rules.Add(new JsonObject
                {
                    ["ip_cidr"] = cidrArr,
                    ["outbound"] = intranetTarget
                });
            }

            string resolverStrategy = !string.IsNullOrEmpty(AppSession.Instance.DnsStrategy)
                ? AppSession.Instance.DnsStrategy
                : "prefer_ipv4";

            JsonNode MakeDefaultDomainResolver() => isSingbox114OrAbove
                ? new JsonObject { ["server"] = "local-dns", ["strategy"] = resolverStrategy }
                : (JsonNode)"local-dns";

            var ruleSets = new JsonArray();

            if (routingMode == "global")
            {
                var globalRoute = new JsonObject
                {
                    ["rules"] = rules,
                    ["rule_set"] = ruleSets,
                    ["final"] = "proxy",
                    ["auto_detect_interface"] = true,
                    ["find_process"] = true,
                    ["default_domain_resolver"] = MakeDefaultDomainResolver()
                };
                if (isSingbox114OrAbove)
                    globalRoute["default_http_client"] = "default-http";
                if (enableTun && !string.IsNullOrEmpty(outboundInterface))
                    globalRoute["default_interface"] = outboundInterface;
                return globalRoute;
            }

            if (routingMode == "direct")
            {
                var directRoute = new JsonObject
                {
                    ["rules"] = rules,
                    ["rule_set"] = ruleSets,
                    ["final"] = "direct",
                    ["auto_detect_interface"] = true,
                    ["find_process"] = true,
                    ["default_domain_resolver"] = MakeDefaultDomainResolver()
                };
                if (isSingbox114OrAbove)
                    directRoute["default_http_client"] = "direct-http";
                if (enableTun && !string.IsNullOrEmpty(outboundInterface))
                    directRoute["default_interface"] = outboundInterface;
                return directRoute;
            }

            var ruleSetDir = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), 
                "SwellProxy"
            );

            // Smart mode
            if (blockAds)
            {
                rules.Add(new JsonObject
                {
                    ["rule_set"] = new JsonArray { (JsonNode)"geosite-category-ads-all" },
                    ["action"] = "reject"
                });

                ruleSets.Add(new JsonObject
                {
                    ["type"] = "local",
                    ["tag"] = "geosite-category-ads-all",
                    ["format"] = "binary",
                    ["path"] = System.IO.Path.Combine(ruleSetDir, "geosite-category-ads-all.srs")
                });
            }

            if (bypassChina)
            {
                ruleSets.Add(new JsonObject
                {
                    ["type"] = "local",
                    ["tag"] = "geosite-cn",
                    ["format"] = "binary",
                    ["path"] = System.IO.Path.Combine(ruleSetDir, "geosite-cn.srs")
                });

                ruleSets.Add(new JsonObject
                {
                    ["type"] = "local",
                    ["tag"] = "geoip-cn",
                    ["format"] = "binary",
                    ["path"] = System.IO.Path.Combine(ruleSetDir, "geoip-cn.srs")
                });
            }

            string ResolveOutbound(string action)
            {
                string raw = action.StartsWith("node:", StringComparison.Ordinal) ? action.Substring(5) : action;
                if (raw == "proxy" || raw == "direct" || raw == "block" || raw == "reject")
                    return raw;
                // Fall back to "proxy" if referenced node was deleted or urltest ended up empty
                return validOutboundTags.Contains(raw) ? raw : "proxy";
            }

            // ── Custom user-defined rules & App rules (Injected only if Advanced Routing is enabled) ──
            if (AppSession.Instance.EnableAdvancedRouting)
            {
                var addedRuleSetTags = new HashSet<string>();
                foreach (var existingRs in ruleSets)
                {
                    if (existingRs is JsonObject rsObj &&
                        rsObj["tag"]?.ToString() is string existingTag)
                        addedRuleSetTags.Add(existingTag);
                }

                // User custom rules take precedence over built-in rules (first-match wins).
                var loadedRules = RoutingRulesService.LoadRules();
                foreach (var routingRule in loadedRules)
                {
                    if (!routingRule.IsBuiltIn)
                        AppendRoutingRule(rules, ruleSets, ruleSetDir, addedRuleSetTags, routingRule, ResolveOutbound, isSingbox114OrAbove);
                }
                foreach (var routingRule in loadedRules)
                {
                    if (routingRule.IsBuiltIn)
                        AppendRoutingRule(rules, ruleSets, ruleSetDir, addedRuleSetTags, routingRule, ResolveOutbound, isSingbox114OrAbove);
                }
            }

            if (bypassChina)
            {
                // Prevent Google Play download domains (googleapis.cn) from being sent direct by geosite-cn.
                rules.Add(new JsonObject
                {
                    ["domain_suffix"] = new JsonArray
                    {
                        (JsonNode)"googleapis.cn",
                        (JsonNode)"xn--ngstr-lra8j.com",
                        (JsonNode)"play.googleapis.com"
                    },
                    ["outbound"] = "proxy"
                });

                rules.Add(new JsonObject
                {
                    ["rule_set"] = new JsonArray { (JsonNode)"geosite-cn", (JsonNode)"geoip-cn" },
                    ["outbound"] = "direct"
                });
            }

            var smartRoute = new JsonObject
            {
                ["rules"] = rules,
                ["rule_set"] = ruleSets,
                ["final"] = "proxy",
                ["auto_detect_interface"] = true,
                ["find_process"] = true,
                ["default_domain_resolver"] = MakeDefaultDomainResolver()
            };
            if (isSingbox114OrAbove)
                smartRoute["default_http_client"] = "default-http";
            // TUN 模式下绑定物理网卡为默认接口，确保 direct 出站流量不被 TUN 重新捕获
            if (enableTun && !string.IsNullOrEmpty(outboundInterface))
                smartRoute["default_interface"] = outboundInterface;
            return smartRoute;
        }

        private static void AppendRoutingRule(
            JsonArray rules,
            JsonArray ruleSets,
            string ruleSetDir,
            HashSet<string> addedRuleSetTags,
            RoutingRuleItem routingRule,
            Func<string, string> resolveOutbound,
            bool isSingbox114OrAbove)
        {
            if (!routingRule.IsEnabled) return;
            if (string.IsNullOrWhiteSpace(routingRule.Match)) return;

            string outbound = resolveOutbound(string.IsNullOrWhiteSpace(routingRule.OutboundTag)
                ? "proxy"
                : routingRule.OutboundTag);

            var geositeEntries = new List<string>();
            var geoipEntries = new List<string>();
            var domainSuffixes = new List<string>();
            var domainExacts = new List<string>();
            var domainRegexes = new List<string>();
            var domainKeywords = new List<string>();
            var cidrEntries = new List<string>();
            var processNames = new List<string>();
            var processPaths = new List<string>();
            var remoteRuleSetUrls = new List<string>();

            var entries = SplitRuleEntriesPreservingBraces(routingRule.Match);

            foreach (var rawEntry in entries)
            {
                var entry = rawEntry.Trim();
                if (entry.Length == 0) continue;

                if (TryRemoteSrsRuleSet(entry, out var remoteRuleSetUrl))
                {
                    remoteRuleSetUrls.Add(remoteRuleSetUrl);
                    continue;
                }

                string type = routingRule.Type?.ToLowerInvariant() ?? "domain";
                if (type == "mixed")
                    ClassifyMixedRuleEntry(entry);
                else if (type == "ip")
                    ClassifyIpRuleEntry(entry);
                else if (type == "process")
                    ClassifyProcessRuleEntry(entry);
                else
                    ClassifyDomainRuleEntry(entry);
            }

            foreach (var tag in geositeEntries)
                AddRuleSetRule($"geosite-{tag}", outbound);

            foreach (var tag in geoipEntries)
                AddRuleSetRule($"geoip-{tag}", outbound);

            foreach (var url in remoteRuleSetUrls)
                AddRemoteRuleSetRule(CreateRemoteRuleSetTag(url), url, outbound);

            AddArrayRule("domain_suffix", domainSuffixes, outbound);
            AddArrayRule("domain", domainExacts, outbound);
            AddArrayRule("domain_regex", domainRegexes, outbound);
            AddArrayRule("domain_keyword", domainKeywords, outbound);
            AddArrayRule("ip_cidr", cidrEntries, outbound);
            AddArrayRule("process_name", processNames, outbound);
            AddArrayRule("process_path", processPaths, outbound);

            void ClassifyMixedRuleEntry(string entry)
            {
                if (TryPrefix(entry, "geosite:", out var geosite))
                    geositeEntries.Add(geosite.ToLowerInvariant());
                else if (TryPrefix(entry, "geoip:", out var geoip))
                    geoipEntries.Add(geoip.ToLowerInvariant());
                else if (TryPrefix(entry, "domain_suffix:", out var suffix))
                    domainSuffixes.Add(suffix.ToLowerInvariant());
                else if (TryPrefix(entry, "domain_keyword:", out var keyword))
                    domainKeywords.Add(keyword.ToLowerInvariant());
                else if (TryPrefix(entry, "domain:", out var exact))
                    domainExacts.Add(exact.ToLowerInvariant());
                else if (TryRegexPrefix(entry, out var regex))
                    domainRegexes.Add(regex);
                else if (TryPrefix(entry, "ip_cidr:", out var cidr))
                    cidrEntries.Add(NormalizeIpCidr(cidr));
                else if (TryPrefix(entry, "process_name:", out var processName))
                    processNames.Add(processName);
                else if (TryPrefix(entry, "process_path:", out var processPath))
                    processPaths.Add(processPath);
                else if (LooksLikeCidrOrIp(entry))
                    cidrEntries.Add(NormalizeIpCidr(entry));
                else
                    domainSuffixes.Add(entry.ToLowerInvariant());
            }

            void ClassifyDomainRuleEntry(string entry)
            {
                if (TryPrefix(entry, "geosite:", out var geosite))
                    geositeEntries.Add(geosite.ToLowerInvariant());
                else if (TryPrefix(entry, "domain_suffix:", out var suffix))
                    domainSuffixes.Add(suffix.ToLowerInvariant());
                else if (TryPrefix(entry, "domain_keyword:", out var keyword))
                    domainKeywords.Add(keyword.ToLowerInvariant());
                else if (TryPrefix(entry, "domain:", out var exact))
                    domainExacts.Add(exact.ToLowerInvariant());
                else if (TryRegexPrefix(entry, out var regex))
                    domainRegexes.Add(regex);
                else
                    domainSuffixes.Add(entry.ToLowerInvariant());
            }

            void ClassifyIpRuleEntry(string entry)
            {
                if (TryPrefix(entry, "geoip:", out var geoip))
                    geoipEntries.Add(geoip.ToLowerInvariant());
                else if (TryPrefix(entry, "ip_cidr:", out var cidr))
                    cidrEntries.Add(NormalizeIpCidr(cidr));
                else
                    cidrEntries.Add(NormalizeIpCidr(entry));
            }

            void ClassifyProcessRuleEntry(string entry)
            {
                if (TryPrefix(entry, "process_name:", out var processName))
                    processNames.Add(processName);
                else if (TryPrefix(entry, "process_path:", out var processPath))
                    processPaths.Add(processPath);
                else if (entry.Contains('\\') || entry.Contains('/'))
                    processPaths.Add(entry);
                else
                    processNames.Add(entry);
            }

            static void ApplyRuleTarget(JsonObject ruleObj, string ruleOutbound)
            {
                if (string.Equals(ruleOutbound, "block", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(ruleOutbound, "reject", StringComparison.OrdinalIgnoreCase))
                {
                    ruleObj["action"] = "reject";
                }
                else
                {
                    ruleObj["outbound"] = ruleOutbound;
                }
            }

            void AddRuleSetRule(string tag, string ruleOutbound)
            {
                string srsPath = System.IO.Path.Combine(ruleSetDir, $"{tag}.srs");
                if (!System.IO.File.Exists(srsPath))
                {
                    System.Diagnostics.Debug.WriteLine($"[ConfigBuilder] Skipping missing local rule-set: {srsPath}");
                    return;
                }

                var ruleObj = new JsonObject
                {
                    ["rule_set"] = new JsonArray { (JsonNode)tag }
                };
                ApplyRuleTarget(ruleObj, ruleOutbound);
                rules.Add(ruleObj);

                if (addedRuleSetTags.Add(tag))
                {
                    ruleSets.Add(new JsonObject
                    {
                        ["type"] = "local",
                        ["tag"] = tag,
                        ["format"] = "binary",
                        ["path"] = srsPath
                    });
                }
            }

            void AddRemoteRuleSetRule(string tag, string url, string ruleOutbound)
            {
                var ruleObj = new JsonObject
                {
                    ["rule_set"] = new JsonArray { (JsonNode)tag }
                };
                ApplyRuleTarget(ruleObj, ruleOutbound);
                rules.Add(ruleObj);

                if (!addedRuleSetTags.Add(tag))
                    return;

                var ruleSet = new JsonObject
                {
                    ["type"] = "remote",
                    ["tag"] = tag,
                    ["format"] = "binary",
                    ["url"] = url,
                    ["update_interval"] = "1d"
                };

                if (isSingbox114OrAbove)
                {
                    ruleSet["http_client"] = "default-http";
                }
                else
                {
                    ruleSet["download_detour"] = "proxy";
                }

                ruleSets.Add(ruleSet);
            }

            void AddArrayRule(string key, List<string> values, string ruleOutbound)
            {
                if (values.Count == 0) return;

                var arr = new JsonArray();
                foreach (var value in values)
                    arr.Add((JsonNode)value);

                var ruleObj = new JsonObject
                {
                    [key] = arr
                };
                ApplyRuleTarget(ruleObj, ruleOutbound);
                rules.Add(ruleObj);
            }
        }

        private static List<string> SplitRuleEntriesPreservingBraces(string match)
        {
            var result = new List<string>();
            if (string.IsNullOrWhiteSpace(match)) return result;

            int braceDepth = 0;
            int segStart = 0;
            for (int i = 0; i < match.Length; i++)
            {
                char c = match[i];
                if (c == '\\' && i + 1 < match.Length)
                {
                    i++; // skip escaped char
                    continue;
                }
                if (c == '{') braceDepth++;
                else if (c == '}' && braceDepth > 0) braceDepth--;
                else if (c == ',' && braceDepth == 0)
                {
                    var piece = match.Substring(segStart, i - segStart).Trim();
                    if (piece.Length > 0) result.Add(piece);
                    segStart = i + 1;
                }
            }
            if (segStart < match.Length)
            {
                var tail = match.Substring(segStart).Trim();
                if (tail.Length > 0) result.Add(tail);
            }
            return result;
        }

        private static bool TryRegexPrefix(string entry, out string value)
            => TryPrefix(entry, "domain_regex:", out value) ||
               TryPrefix(entry, "regex:", out value) ||
               TryPrefix(entry, "regexp:", out value);

        private static bool TryRemoteSrsRuleSet(string entry, out string url)
        {
            if (TryPrefix(entry, "ruleset:", out url) ||
                TryPrefix(entry, "rule_set:", out url) ||
                TryPrefix(entry, "srs:", out url))
            {
                return IsRemoteSrsUrl(url);
            }

            url = entry.Trim();
            return IsRemoteSrsUrl(url);
        }

        private static bool IsRemoteSrsUrl(string value)
        {
            if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
                return false;

            if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
                return false;

            return uri.AbsolutePath.EndsWith(".srs", StringComparison.OrdinalIgnoreCase);
        }

        private static string CreateRemoteRuleSetTag(string url)
        {
            var bytes = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(url.Trim().ToLowerInvariant()));
            return $"remote-srs-{Convert.ToHexString(bytes).Substring(0, 16).ToLowerInvariant()}";
        }

        private static bool TryPrefix(string entry, string prefix, out string value)
        {
            if (entry.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                value = entry.Substring(prefix.Length).Trim();
                return value.Length > 0;
            }

            value = string.Empty;
            return false;
        }

        private static string NormalizeIpCidr(string value)
        {
            var clean = value.Trim().ToLowerInvariant();
            var slashIndex = clean.IndexOf('/');
            var ipPart = slashIndex >= 0 ? clean.Substring(0, slashIndex) : clean;

            if (!System.Net.IPAddress.TryParse(ipPart, out var ipAddress))
                return clean;

            if (slashIndex >= 0)
                return clean;

            return ipAddress.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6
                ? $"{clean}/128"
                : $"{clean}/32";
        }

        private static bool LooksLikeCidrOrIp(string value)
        {
            var ipPart = value;
            var slashIndex = value.IndexOf('/');
            if (slashIndex >= 0)
                ipPart = value.Substring(0, slashIndex);

            return System.Net.IPAddress.TryParse(ipPart, out _);
        }
    }
}
