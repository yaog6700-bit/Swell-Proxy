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

        public static async System.Threading.Tasks.Task<string> BuildAsync(PersistedNode? selectedNode = null)
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

            if (selectedNode == null)
            {
                selectedNode = NodesManager.Instance.Nodes.Find(n => n.Id == NodesManager.Instance.SelectedNodeId);
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

            var enableTun = session.EnableTunMode;

            // TUN 模式下探测物理出站网卡，用于 direct outbound 绑定，防止直连流量被 TUN 重新截获
            string? outboundInterface = null;
            if (enableTun)
            {
                var tunSvc = new TunService();
                outboundInterface = tunSvc.DetectDefaultOutboundInterfaceName();
                System.Diagnostics.Debug.WriteLine($"[ConfigBuilder] TUN 出站网卡: {outboundInterface ?? "(未探测到)"}");
            }

            // 检测本地 sing-box 内核版本，用于按版本生成不同的配置节
            // 注意：预发布版本（如 v1.14.0-alpha.32）含非数字后缀，需先截断再解析
            var coreVersionStr = CoreUpdateService.GetLocalSingboxVersionText();
            bool isSingbox114OrAbove = false;
            if (coreVersionStr.StartsWith("v"))
            {
                var versionPart = coreVersionStr.Substring(1);
                // 截断 -alpha / -beta / -rc 等预发布标签，只保留纯数字部分
                var dashIndex = versionPart.IndexOf('-');
                if (dashIndex > 0) versionPart = versionPart.Substring(0, dashIndex);
                if (System.Version.TryParse(versionPart, out var coreVersion))
                {
                    isSingbox114OrAbove = coreVersion >= new System.Version(1, 14, 0);
                }
            }

            var config = new JsonObject
            {
                ["log"] = new JsonObject
                {
                    ["level"] = "warn",
                    ["timestamp"] = true
                },
                ["dns"] = BuildDns(bypassChina, blockIPv6, routingMode, enableTun),
                ["inbounds"] = await BuildInboundsAsync(enableTun, selectedNode),
                ["outbounds"] = BuildOutbounds(selectedNode, enableTun, outboundInterface, isSingbox114OrAbove, actions.ToArray()),
                ["route"] = BuildRoute(routingMode, bypassChina, blockAds, enableTun, outboundInterface, isSingbox114OrAbove),
                ["experimental"] = new JsonObject
                {
                    ["clash_api"] = new JsonObject
                    {
                        ["external_controller"] = "127.0.0.1:9090",
                        ["external_ui"] = "",
                        ["external_ui_download_url"] = "",
                        ["external_ui_download_detour"] = "direct",
                        ["secret"] = "",
                        ["default_mode"] = "rule"
                    }
                }
            };

            // sing-box >= 1.14.0：额外注入原生 API 服务（gRPC，监听 9091）
            // 同时保留 clash_api（9090）确保现有功能不受影响
            // 注意：1.13.x 不识别顶级 "services" 字段，注入会导致内核启动失败，必须做版本判断
            if (isSingbox114OrAbove)
            {
                config["services"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["type"] = "api",
                        ["tag"] = "sing-box dashboard",
                        ["listen"] = "127.0.0.1",
                        ["listen_port"] = 9091,
                        ["secret"] = "",
                        // 同时允许 http 和 https 两种协议的官方 Dashboard 域名
                        // 避免浏览器因混合内容（HTTPS 页面 → HTTP 本地端口）而拒绝请求
                        ["access_control_allow_origin"] = new JsonArray
                        {
                            (JsonNode)"http://sing-box-dashboard.sagernet.org",
                            (JsonNode)"https://sing-box-dashboard.sagernet.org",
                            (JsonNode)"http://dash.sing-box.app",
                            (JsonNode)"https://dash.sing-box.app"
                        },
                        ["access_control_allow_private_network"] = true,
                        // 启用内置 Dashboard：sing-box 自动下载并在 /dashboard/ 路径提供服务
                        // 用户也可直接访问 http://127.0.0.1:9091/dashboard/ 完全绕过 CORS
                        ["dashboard"] = true
                    }
                };
                System.Diagnostics.Debug.WriteLine($"[ConfigBuilder] 检测到 sing-box {coreVersionStr}，已启用原生 API（端口 9091）");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"[ConfigBuilder] 检测到 sing-box {coreVersionStr}（< 1.14.0），跳过原生 API 注入");
            }

            // 仅当用户启用 Tailscale 时，才注入 endpoints 字段
            if (session.EnableTailscale)
            {
                config["endpoints"] = BuildEndpoints(session);
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

        private static JsonObject BuildDns(bool bypassChina, bool blockIPv6, string routingMode, bool enableTun = false)
        {
            var session = AppSession.Instance;
            string proxyDns = !string.IsNullOrEmpty(session.ProxyDns) ? session.ProxyDns : "https://1.1.1.1/dns-query";
            string directDns = !string.IsNullOrEmpty(session.DirectDns) ? session.DirectDns : (enableTun ? "223.5.5.5" : "114.114.114.114");
            string strategy = !string.IsNullOrEmpty(session.DnsStrategy) ? session.DnsStrategy : "prefer_ipv4";

            var proxyDnsObj = new JsonObject { ["tag"] = "remote-dns", ["detour"] = "proxy", ["domain_resolver"] = "bootstrap-dns" };
            ParseAndSetDnsServer(proxyDnsObj, proxyDns);

            var localDnsObj = new JsonObject { ["tag"] = "local-dns", ["domain_resolver"] = "bootstrap-dns" };
            ParseAndSetDnsServer(localDnsObj, directDns);

            JsonObject bootstrapDnsObj;
            if (enableTun)
            {
                // In TUN mode, type: local uses OS resolver (svchost) which gets captured by TUN causing a loop.
                // Using an IP directly with type: tcp allows strict_route to bypass the sing-box process socket
                // and prevents UDP DNS blocking by ISPs.
                bootstrapDnsObj = new JsonObject { ["tag"] = "bootstrap-dns", ["type"] = "tcp", ["server"] = "223.5.5.5" };
            }
            else
            {
                bootstrapDnsObj = new JsonObject { ["tag"] = "bootstrap-dns", ["type"] = "local" };
            }

            var servers = new JsonArray { (JsonNode)proxyDnsObj, (JsonNode)localDnsObj, (JsonNode)bootstrapDnsObj };
            var rules = new JsonArray();

            // TUN 模式下（非纯 IPv6）拦�?AAAA 查询，防止大量代理节点对 IPv6 兼容性差导致连接超时
            if (enableTun && strategy != "ipv6_only")
            {
                rules.Add(new JsonObject
                {
                    ["query_type"] = new JsonArray { (JsonNode)"AAAA" },
                    ["action"] = "reject"
                });
            }
            else if (blockIPv6)
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
                    ["inet6_range"] = "fc00::/18"
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

            if (!session.EnableDnsCache)
            {
                dnsConfig["disable_cache"] = true;
                dnsConfig["disable_expire"] = true;
            }

            return dnsConfig;
        }

        private static void ParseAndSetDnsServer(JsonObject obj, string address)
        {
            if (string.IsNullOrEmpty(address)) return;

            if (Uri.TryCreate(address, UriKind.Absolute, out var uri) && uri.Scheme != "file")
            {
                string scheme = uri.Scheme.ToLower();
                obj["type"] = scheme;
                obj["server"] = uri.IdnHost;
                
                if (!uri.IsDefaultPort)
                {
                    obj["server_port"] = uri.Port;
                }

                if (scheme == "https" || scheme == "h3")
                {
                    // sing-box uses 'path' for the URL path
                    obj["path"] = uri.PathAndQuery;
                }
            }
            else
            {
                // Legacy plain IP format, assume UDP
                obj["type"] = "udp";
                obj["server"] = address;
            }
        }

        private static async System.Threading.Tasks.Task<JsonArray> BuildInboundsAsync(bool enableTun = false, PersistedNode? selectedNode = null)
        {
            var list = new JsonArray();

            if (enableTun)
            {
                var tunObj = new JsonObject
                {
                    ["type"]         = "tun",
                    ["tag"]          = "tun-in",
                    ["address"]      = new JsonArray { (JsonNode)"172.18.0.1/30", (JsonNode)"fdfe:dcba:9876::1/126" },
                    ["auto_route"]   = true,
                    // 关闭 strict_route 避免 WFP 拦截导致 WSAEACCES 报错（我们已经在 direct 出站绑定了物理网卡防止死循环）
                    ["strict_route"] = false,
                    ["stack"]        = NormalizeTunStack(AppSession.Instance.TunStack),
                    ["mtu"]          = 9000
                };

                // 排除私有地址段和代理服务器地址，防止 LAN 流量和出站流量被 TUN 截获造成 CPU 飙升
                var excludeAddresses = new JsonArray();
                // RFC1918 私有地址段（LAN 流量不走 TUN，减少内核连接压力）
                excludeAddresses.Add((JsonNode)"10.0.0.0/8");
                excludeAddresses.Add((JsonNode)"172.16.0.0/12");
                excludeAddresses.Add((JsonNode)"192.168.0.0/16");
                excludeAddresses.Add((JsonNode)"100.64.0.0/10");
                excludeAddresses.Add((JsonNode)"169.254.0.0/16");
                excludeAddresses.Add((JsonNode)"127.0.0.0/8");
                excludeAddresses.Add((JsonNode)"224.0.0.0/4");
                excludeAddresses.Add((JsonNode)"240.0.0.0/4");
                excludeAddresses.Add((JsonNode)"fc00::/7");
                excludeAddresses.Add((JsonNode)"fe80::/10");
                // 常用 bootstrap DNS 服务器，防止 DNS 环路
                excludeAddresses.Add((JsonNode)"223.5.5.5/32");
                excludeAddresses.Add((JsonNode)"114.114.114.114/32");
                excludeAddresses.Add((JsonNode)"8.8.8.8/32");
                excludeAddresses.Add((JsonNode)"1.1.1.1/32");

                // 排除所有的代理服务器 IP，防止域名节点导致的无限环路
                // 使用带超时保护的并行 DNS 解析，避免阻塞线程导致 TUN 模式启动失败
                var dnsResolveTasks = new System.Collections.Generic.List<System.Threading.Tasks.Task>();
                var excludeLock = new object();

                foreach (var node in NodesManager.Instance.Nodes)
                {
                    var (nHost, _) = NodeLinkParser.SplitHostPort(node.Host);
                    var cleanNHost = nHost.Trim('[', ']');
                    if (System.Net.IPAddress.TryParse(cleanNHost, out var ip))
                    {
                        string cidr = ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6 ? $"{ip}/128" : $"{ip}/32";
                        lock (excludeLock) { excludeAddresses.Add((JsonNode)cidr); }
                    }
                    else if (!string.IsNullOrEmpty(cleanNHost))
                    {
                        var capturedHost = cleanNHost;
                        var task = System.Threading.Tasks.Task.Run(async () =>
                        {
                            try
                            {
                                using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromMilliseconds(500));
                                var ips = await System.Net.Dns.GetHostAddressesAsync(capturedHost, cts.Token).ConfigureAwait(false);
                                lock (excludeLock)
                                {
                                    foreach (var resolvedIp in ips)
                                    {
                                        string cidr = resolvedIp.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6 ? $"{resolvedIp}/128" : $"{resolvedIp}/32";
                                        excludeAddresses.Add((JsonNode)cidr);
                                    }
                                }
                            }
                            catch { /* DNS 解析失败或超时，跳过该节点，不影响 TUN 启动 */ }
                        });
                        dnsResolveTasks.Add(task);
                    }
                }

                // 最多等待 1.5 秒让所有 DNS 任务完成，超时后直接跳过剩余解析继续构建配置
                if (dnsResolveTasks.Count > 0)
                {
                    await System.Threading.Tasks.Task.WhenAll(dnsResolveTasks)
                        .WaitAsync(System.TimeSpan.FromMilliseconds(1500))
                        .ConfigureAwait(false);
                }
                tunObj["route_exclude_address"] = excludeAddresses;
                list.Add(tunObj);
            }

            // mixed 入站：SOCKS5 + HTTP 共用端口（用户可在设置中自定义）
            list.Add(new JsonObject
            {
                ["type"]        = "mixed",
                ["tag"]         = "mixed-in",
                ["listen"]      = AppSession.Instance.AllowLanAccess ? "0.0.0.0" : "127.0.0.1",
                ["listen_port"] = AppSession.Instance.MixedPort
            });

            return await System.Threading.Tasks.Task.FromResult(list);
        }

        private static JsonArray BuildOutbounds(PersistedNode selectedNode, bool enableTun, string? outboundInterface, bool isSingbox114OrAbove, params string[] appActions)
        {
            var list = new JsonArray();
            var addedTags = new HashSet<string>();

            // 1. Generate main proxy outbound
            var (host, port) = NodeLinkParser.SplitHostPort(selectedNode.Host);
            if (port == 0) port = 443;
            
            var mainOutbounds = GenerateOutboundsForNode(selectedNode, host, port, "proxy", isSingbox114OrAbove);
            foreach (var ob in mainOutbounds)
            {
                list.Add(ob);
                addedTags.Add(ob["tag"]?.ToString() ?? "");
            }

            bool needsUrlTest = false;

            // 2. Check for extra nodes referenced in actions
            foreach (var action in appActions)
            {
                if (action == "urltest")
                {
                    needsUrlTest = true;
                    continue;
                }

                // Strip "node:" prefix if present to get the real node ID
                string resolvedId = action.StartsWith("node:") ? action.Substring(5) : action;

                if (resolvedId != "proxy" && resolvedId != "direct" && resolvedId != "block" && !addedTags.Contains(resolvedId))
                {
                    // Action is a Node ID
                    var extraNode = NodesManager.Instance.Nodes.Find(n => n.Id == resolvedId);
                    if (extraNode != null)
                    {
                        var (exHost, exPort) = NodeLinkParser.SplitHostPort(extraNode.Host);
                        if (exPort == 0) exPort = 443;
                        
                        var extraOutbounds = GenerateOutboundsForNode(extraNode, exHost, exPort, extraNode.Id, isSingbox114OrAbove);
                        foreach (var ob in extraOutbounds)
                        {
                            if (!addedTags.Contains(ob["tag"]?.ToString() ?? ""))
                            {
                                list.Add(ob);
                                addedTags.Add(ob["tag"]?.ToString() ?? "");
                            }
                        }
                    }
                }
            }

            if (needsUrlTest)
            {
                var urlTestOutbounds = new JsonArray();
                foreach (var node in NodesManager.Instance.Nodes)
                {
                    if (!addedTags.Contains(node.Id))
                    {
                        var (exHost, exPort) = NodeLinkParser.SplitHostPort(node.Host);
                        if (exPort == 0) exPort = 443;

                        var extraOutbounds = GenerateOutboundsForNode(node, exHost, exPort, node.Id, isSingbox114OrAbove);
                        foreach (var ob in extraOutbounds)
                        {
                            if (!addedTags.Contains(ob["tag"]?.ToString() ?? ""))
                            {
                                list.Add(ob);
                                addedTags.Add(ob["tag"]?.ToString() ?? "");
                            }
                        }
                    }
                    urlTestOutbounds.Add((JsonNode)node.Id);
                }

                if (urlTestOutbounds.Count > 0)
                {
                    list.Add(new JsonObject
                    {
                        ["type"] = "urltest",
                        ["tag"] = "urltest",
                        ["outbounds"] = urlTestOutbounds,
                        ["url"] = "https://www.gstatic.com/generate_204",
                        ["interval"] = "3m",
                        ["tolerance"] = 50
                    });
                }
            }

            // Add direct and block outbounds
            // TUN strict_route 模式下必须绑定物理网卡，否则 direct 流量被重新截获进 TUN 形成路由环路
            var directOutbound = new JsonObject { ["type"] = "direct", ["tag"] = "direct" };
            if (enableTun && !string.IsNullOrEmpty(outboundInterface))
            {
                directOutbound["bind_interface"] = outboundInterface;
            }
            list.Add(directOutbound);
            list.Add(new JsonObject { ["type"] = "block", ["tag"] = "block" });

            return list;
        }

        private static string NormalizeTunStack(string? stack)
        {
            return stack == "system" || stack == "gvisor" || stack == "mixed" ? stack : "mixed";
        }

        private static List<JsonObject> GenerateOutboundsForNode(PersistedNode node, string host, int port, string baseTag, bool isSingbox114OrAbove)
        {
            var list = new List<JsonObject>();

            // 查找前置级联代理节点
            PersistedNode? chainNode = null;
            if (!string.IsNullOrEmpty(node.ProxyChainId))
            {
                chainNode = NodesManager.Instance.Nodes.Find(n => n.Id == node.ProxyChainId);
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

            // 3. 如果有前置代理链，生成前置代理的出站配置 (chain-proxy)
            if (chainNode != null)
            {
                var (chainHost, chainPort) = NodeLinkParser.SplitHostPort(chainNode.Host);
                if (chainPort == 0) chainPort = 443;

                var chainOutbound = BuildSingleOutbound(chainNode, chainHost, chainPort, $"{baseTag}-chain-proxy", isSingbox114OrAbove);

                list.Add(chainOutbound);
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

            string protoLower = node.Protocol.ToLowerInvariant();
            
            // Common values
            string uuidVal = !string.IsNullOrEmpty(node.Uuid) ? node.Uuid : "de000000-0000-0000-0000-000000000000";
            string passwordVal = !string.IsNullOrEmpty(node.Password) ? node.Password : "anywhere-password";
            string securityVal = !string.IsNullOrEmpty(node.Security) ? node.Security.ToLower() : "none";
            string networkVal = !string.IsNullOrEmpty(node.Network) ? node.Network.ToLower() : "tcp";
            string pathVal = !string.IsNullOrEmpty(node.Path) ? node.Path : "";
            string wsHostVal = !string.IsNullOrEmpty(node.WsHost) ? node.WsHost : "";
            string sniVal = !string.IsNullOrEmpty(node.Sni) ? node.Sni : host;
            string fingerprintVal = !string.IsNullOrEmpty(node.Fingerprint) ? node.Fingerprint.ToLower() : "none";
            bool allowInsecureVal = node.AllowInsecure;

            if (protoLower.Contains("vless"))
            {
                proxyOutbound["type"] = "vless";
                proxyOutbound["uuid"] = uuidVal;
                if (!string.IsNullOrEmpty(node.Flow))
                {
                    proxyOutbound["flow"] = node.Flow;
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
                            ["public_key"] = !string.IsNullOrEmpty(node.PublicKey) ? node.PublicKey : "",
                            ["short_id"] = !string.IsNullOrEmpty(node.ShortId) ? node.ShortId : ""
                        };
                    }
                    else
                    {
                        tlsObj["server_name"] = sniVal;
                        tlsObj["insecure"] = allowInsecureVal;
                    }

                    if (fingerprintVal != "none")
                    {
                        tlsObj["utls"] = new JsonObject
                        {
                            ["enabled"] = true,
                            ["fingerprint"] = fingerprintVal
                        };
                    }

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

                if (securityVal == "tls")
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
                    if (fingerprintVal != "none")
                    {
                        tlsObj["utls"] = new JsonObject
                        {
                            ["enabled"] = true,
                            ["fingerprint"] = fingerprintVal
                        };
                    }
                    proxyOutbound["tls"] = tlsObj;
                }
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
                proxyOutbound["tls"] = tlsObj;
            }
            else if (protoLower == "http" || protoLower == "https")
            {
                proxyOutbound["type"] = "http";
                if (!string.IsNullOrEmpty(node.Username))
                    proxyOutbound["username"] = node.Username;
                if (!string.IsNullOrEmpty(node.Password))
                    proxyOutbound["password"] = node.Password;

                if (securityVal == "tls")
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

        private static void ApplyTransportToOutbound(JsonObject proxyOutbound, string networkVal, string pathVal, string wsHostVal)
        {
            if (networkVal == "ws")
            {
                proxyOutbound["transport"] = new JsonObject
                {
                    ["type"] = "ws",
                    ["path"] = pathVal,
                    ["headers"] = new JsonObject { ["Host"] = wsHostVal }
                };
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
                proxyOutbound["transport"] = new JsonObject
                {
                    ["type"] = "grpc",
                    ["service_name"] = pathVal
                };
            }
            else if (networkVal == "xhttp" || networkVal == "h2")
            {
                var transportObj = new JsonObject
                {
                    ["type"] = networkVal
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
            bool enableTun = false,
            string? outboundInterface = null,
            bool isSingbox114OrAbove = false)
        {
            var rules = new JsonArray
            {
                // Explicitly hijack port 53 to internal DNS for TUN mode
                new JsonObject
                {
                    ["port"] = new JsonArray { (JsonNode)53 },
                    ["action"] = "hijack-dns"
                },
                // Hijack DNS protocol
                new JsonObject
                {
                    ["protocol"] = "dns",
                    ["action"] = "hijack-dns"
                },
                // Sniff domain names
                new JsonObject
                {
                    ["action"] = "sniff"
                },
                // Private IP directly
                new JsonObject
                {
                    ["ip_is_private"] = true,
                    ["outbound"] = "direct"
                }
            };

            var ruleSets = new JsonArray();

            if (routingMode == "global")
            {
                var globalRoute = new JsonObject
                {
                    ["rules"] = rules,
                    ["rule_set"] = ruleSets,
                    ["final"] = "proxy",
                    ["auto_detect_interface"] = true,
                    ["default_domain_resolver"] = "local-dns"
                };
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
                    ["default_domain_resolver"] = "local-dns"
                };
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
                    ["outbound"] = "block"
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

            // Helper: resolve "node:xxx" action tag -> real outbound tag
            static string ResolveOutbound(string action)
                => action.StartsWith("node:") ? action.Substring(5) : action;

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

                foreach (var routingRule in RoutingRulesService.LoadRules())
                {
                    AppendRoutingRule(rules, ruleSets, ruleSetDir, addedRuleSetTags, routingRule, ResolveOutbound, isSingbox114OrAbove);
                }
            }

            if (bypassChina)
            {
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
                ["default_domain_resolver"] = "local-dns"
            };
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

            var entries = routingRule.Match
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

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

            void AddRuleSetRule(string tag, string ruleOutbound)
            {
                rules.Add(new JsonObject
                {
                    ["rule_set"] = new JsonArray { (JsonNode)tag },
                    ["outbound"] = ruleOutbound
                });

                if (addedRuleSetTags.Add(tag))
                {
                    ruleSets.Add(new JsonObject
                    {
                        ["type"] = "local",
                        ["tag"] = tag,
                        ["format"] = "binary",
                        ["path"] = System.IO.Path.Combine(ruleSetDir, $"{tag}.srs")
                    });
                }
            }

            void AddRemoteRuleSetRule(string tag, string url, string ruleOutbound)
            {
                rules.Add(new JsonObject
                {
                    ["rule_set"] = new JsonArray { (JsonNode)tag },
                    ["outbound"] = ruleOutbound
                });

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
                    ruleSet["http_client"] = new JsonObject
                    {
                        ["detour"] = "proxy"
                    };
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

                rules.Add(new JsonObject
                {
                    [key] = arr,
                    ["outbound"] = ruleOutbound
                });
            }
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
