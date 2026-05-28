using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AnywhereWinUI.Services
{
    public static class ConfigBuilder
    {
        private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

        public static string Build(PersistedNode? selectedNode = null)
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
            var config = new JsonObject
            {
                ["log"] = new JsonObject
                {
                    ["level"] = "info",
                    ["timestamp"] = true
                },
                ["dns"] = BuildDns(bypassChina, blockIPv6, routingMode, enableTun),
                ["inbounds"] = BuildInbounds(enableTun, selectedNode),
                ["outbounds"] = BuildOutbounds(selectedNode, actions.ToArray()),
                ["route"] = BuildRoute(routingMode, bypassChina, blockAds),
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

            return config.ToJsonString(JsonOpts);
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

            var bootstrapDnsObj = new JsonObject { ["tag"] = "bootstrap-dns", ["type"] = "udp", ["server"] = "223.5.5.5" };

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

            if (session.EnableFakeDns)
            {
                dnsConfig["fakeip"] = new JsonObject
                {
                    ["enabled"] = true,
                    ["inet4_range"] = "198.18.0.0/15",
                    ["inet6_range"] = "fc00::/18"
                };
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

        private static JsonArray BuildInbounds(bool enableTun = false, PersistedNode? selectedNode = null)
        {
            var list = new JsonArray();

            if (enableTun)
            {
                var tunObj = new JsonObject
                {
                    ["type"]         = "tun",
                    ["tag"]          = "tun-in",
                    ["address"]      = new JsonArray { (JsonNode)"172.18.0.1/30" },
                    ["auto_route"]   = true,
                    // 使用 strict_route=true 开启 WFP 底层分流，彻底解决 sing-box direct 出站（如直连国内 IP 时）的死循环问题
                    ["strict_route"] = true,
                    ["stack"]        = "system",
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
                foreach (var node in NodesManager.Instance.Nodes)
                {
                    var (nHost, _) = NodeLinkParser.SplitHostPort(node.Host);
                    var cleanNHost = nHost.Trim('[', ']');
                    if (System.Net.IPAddress.TryParse(cleanNHost, out var ip))
                    {
                        string cidr = ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6 ? $"{ip}/128" : $"{ip}/32";
                        excludeAddresses.Add((JsonNode)cidr);
                    }
                    else if (!string.IsNullOrEmpty(cleanNHost))
                    {
                        try
                        {
                            var ips = System.Net.Dns.GetHostAddresses(cleanNHost);
                            foreach (var resolvedIp in ips)
                            {
                                string cidr = resolvedIp.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6 ? $"{resolvedIp}/128" : $"{resolvedIp}/32";
                                excludeAddresses.Add((JsonNode)cidr);
                            }
                        }
                        catch { /* 忽略解析失败的节点 */ }
                    }
                }
                tunObj["route_exclude_address"] = excludeAddresses;
                list.Add(tunObj);
            }

            // mixed 入站：SOCKS5 + HTTP 共用端口 2080
            list.Add(new JsonObject
            {
                ["type"]        = "mixed",
                ["tag"]         = "mixed-in",
                ["listen"]      = "127.0.0.1",
                ["listen_port"] = 2080
            });

            return list;
        }

        private static JsonArray BuildOutbounds(PersistedNode selectedNode, params string[] appActions)
        {
            var list = new JsonArray();
            var addedTags = new HashSet<string>();

            // 1. Generate main proxy outbound
            var (host, port) = NodeLinkParser.SplitHostPort(selectedNode.Host);
            if (port == 0) port = 443;
            
            var mainOutbounds = GenerateOutboundsForNode(selectedNode, host, port, "proxy");
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
                        
                        var extraOutbounds = GenerateOutboundsForNode(extraNode, exHost, exPort, extraNode.Id);
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

                        var extraOutbounds = GenerateOutboundsForNode(node, exHost, exPort, node.Id);
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
            list.Add(new JsonObject { ["type"] = "direct", ["tag"] = "direct" });
            list.Add(new JsonObject { ["type"] = "block", ["tag"] = "block" });

            // 终极防环路：强制将所有物理出站绑定到真实网卡，彻底绕过 WFP 的潜在失效
            try
            {
                var ifName = new TunService().DetectDefaultOutboundInterfaceName();
                if (!string.IsNullOrEmpty(ifName))
                {
                    var logicalTypes = new HashSet<string> { "urltest", "selector", "block", "dns" };
                    foreach (var ob in list)
                    {
                        if (ob is JsonObject jsonObj && jsonObj.ContainsKey("type"))
                        {
                            var typeStr = jsonObj["type"]?.ToString();
                            if (!string.IsNullOrEmpty(typeStr) && !logicalTypes.Contains(typeStr))
                            {
                                jsonObj["bind_interface"] = ifName;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ConfigBuilder] 绑定出站接口失败: {ex.Message}");
            }

            return list;
        }

        private static List<JsonObject> GenerateOutboundsForNode(PersistedNode node, string host, int port, string baseTag)
        {
            var list = new List<JsonObject>();

            // 查找前置级联代理节点
            PersistedNode? chainNode = null;
            if (!string.IsNullOrEmpty(node.ProxyChainId))
            {
                chainNode = NodesManager.Instance.Nodes.Find(n => n.Id == node.ProxyChainId);
            }

            // 1. Generate main proxy outbound
            var proxyOutbound = BuildSingleOutbound(node, host, port, baseTag);

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
                    ["type"] = "shadow-tls",
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

                var chainOutbound = BuildSingleOutbound(chainNode, chainHost, chainPort, $"{baseTag}-chain-proxy");
                list.Add(chainOutbound);
            }

            return list;
        }

        private static JsonObject BuildSingleOutbound(PersistedNode node, string host, int port, string tag)
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
                proxyOutbound["username"] = uuidVal;
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
                // Snell protocol (reF1nd/sing-box fork)
                // psk = Password, version = SnellVersion (default 4), obfs via ObfsType/WsHost
                proxyOutbound["type"] = "snell";
                proxyOutbound["psk"] = passwordVal;
                proxyOutbound["version"] = node.SnellVersion > 0 ? node.SnellVersion : 4;

                // Optional HTTP obfuscation
                string snellObfs = !string.IsNullOrEmpty(node.ObfsType) ? node.ObfsType.ToLower() : "none";
                if (snellObfs == "http")
                {
                    var obfsObj = new JsonObject { ["mode"] = "http" };
                    if (!string.IsNullOrEmpty(node.WsHost))
                        obfsObj["host"] = node.WsHost;
                    proxyOutbound["obfs"] = obfsObj;
                }
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

        private static JsonObject BuildRoute(string routingMode, bool bypassChina, bool blockAds)
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
                return new JsonObject
                {
                    ["rules"] = rules,
                    ["rule_set"] = ruleSets,
                    ["final"] = "proxy",
                    ["auto_detect_interface"] = true,
                    ["default_domain_resolver"] = "local-dns"
                };
            }

            if (routingMode == "direct")
            {
                return new JsonObject
                {
                    ["rules"] = rules,
                    ["rule_set"] = ruleSets,
                    ["final"] = "direct",
                    ["auto_detect_interface"] = true,
                    ["default_domain_resolver"] = "local-dns"
                };
            }

            var ruleSetDir = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), 
                "AnywhereProxy"
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
                rules.Add(new JsonObject
                {
                    ["rule_set"] = new JsonArray { (JsonNode)"geosite-cn", (JsonNode)"geoip-cn" },
                    ["outbound"] = "direct"
                });

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
                var customRules = AppSession.Instance.CustomRules;
                if (customRules != null && customRules.Count > 0)
                {
                    var addedRuleSetTags = new System.Collections.Generic.HashSet<string>();

                    // Collect existing rule_set tags to avoid duplicates
                    foreach (var existingRs in ruleSets)
                    {
                        if (existingRs is System.Text.Json.Nodes.JsonObject rsObj &&
                            rsObj["tag"]?.ToString() is string existingTag)
                            addedRuleSetTags.Add(existingTag);
                    }

                    foreach (var cr in customRules)
                    {
                        if (!cr.IsEnabled) continue;
                        if (string.IsNullOrWhiteSpace(cr.Match)) continue;

                        string outbound = ResolveOutbound(cr.OutboundTag ?? "proxy");
                        var entries = cr.Match
                            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

                        if (cr.Type == "domain")
                        {
                            // Separate geosite: entries from regular domains
                            var geositeEntries = new System.Collections.Generic.List<string>();
                            var domainSuffixes  = new System.Collections.Generic.List<string>();
                            var domainRegexes   = new System.Collections.Generic.List<string>();

                            foreach (var entry in entries)
                            {
                                if (entry.StartsWith("geosite:", StringComparison.OrdinalIgnoreCase))
                                {
                                    geositeEntries.Add(entry.Substring(8).ToLower());
                                }
                                else if (entry.StartsWith("regex:", StringComparison.OrdinalIgnoreCase))
                                {
                                    domainRegexes.Add(entry.Substring(6));
                                }
                                else
                                {
                                    domainSuffixes.Add(entry.ToLower());
                                }
                            }

                            // Emit one rule per geosite tag (uses rule_set)
                            foreach (var gsTag in geositeEntries)
                            {
                                string rsTag = $"geosite-{gsTag}";
                                rules.Add(new System.Text.Json.Nodes.JsonObject
                                {
                                    ["rule_set"] = new System.Text.Json.Nodes.JsonArray { (JsonNode)rsTag },
                                    ["outbound"] = outbound
                                });

                                if (addedRuleSetTags.Add(rsTag))
                                {
                                    ruleSets.Add(new System.Text.Json.Nodes.JsonObject
                                    {
                                        ["type"]   = "local",
                                        ["tag"]    = rsTag,
                                        ["format"] = "binary",
                                        ["path"]   = System.IO.Path.Combine(ruleSetDir, $"{rsTag}.srs") // Requires user to have the .srs file downloaded in the directory, or we can use a remote rule_set. For now, local.
                                    });
                                }
                            }

                            if (domainSuffixes.Count > 0)
                            {
                                var arr = new System.Text.Json.Nodes.JsonArray();
                                foreach (var d in domainSuffixes) arr.Add((JsonNode)d);
                                rules.Add(new System.Text.Json.Nodes.JsonObject
                                {
                                    ["domain_suffix"] = arr,
                                    ["outbound"]      = outbound
                                });
                            }

                            if (domainRegexes.Count > 0)
                            {
                                var arr = new System.Text.Json.Nodes.JsonArray();
                                foreach (var d in domainRegexes) arr.Add((JsonNode)d);
                                rules.Add(new System.Text.Json.Nodes.JsonObject
                                {
                                    ["domain_regex"] = arr,
                                    ["outbound"]     = outbound
                                });
                            }
                        }
                        else if (cr.Type == "ip")
                        {
                            var geoipEntries = new System.Collections.Generic.List<string>();
                            var cidrEntries  = new System.Collections.Generic.List<string>();

                            foreach (var entry in entries)
                            {
                                if (entry.StartsWith("geoip:", StringComparison.OrdinalIgnoreCase))
                                {
                                    geoipEntries.Add(entry.Substring(6).ToLower());
                                }
                                else
                                {
                                    cidrEntries.Add(entry.ToLower());
                                }
                            }

                            foreach (var giTag in geoipEntries)
                            {
                                string rsTag = $"geoip-{giTag}";
                                rules.Add(new System.Text.Json.Nodes.JsonObject
                                {
                                    ["rule_set"] = new System.Text.Json.Nodes.JsonArray { (JsonNode)rsTag },
                                    ["outbound"] = outbound
                                });

                                if (addedRuleSetTags.Add(rsTag))
                                {
                                    ruleSets.Add(new System.Text.Json.Nodes.JsonObject
                                    {
                                        ["type"]   = "local",
                                        ["tag"]    = rsTag,
                                        ["format"] = "binary",
                                        ["path"]   = System.IO.Path.Combine(ruleSetDir, $"{rsTag}.srs")
                                    });
                                }
                            }

                            if (cidrEntries.Count > 0)
                            {
                                var arr = new System.Text.Json.Nodes.JsonArray();
                                foreach (var c in cidrEntries) arr.Add((JsonNode)c);
                                rules.Add(new System.Text.Json.Nodes.JsonObject
                                {
                                    ["ip_cidr"]  = arr,
                                    ["outbound"] = outbound
                                });
                            }
                        }
                        else if (cr.Type == "process")
                        {
                            var processNames = new System.Collections.Generic.List<string>();
                            var processPaths = new System.Collections.Generic.List<string>();

                            foreach (var entry in entries)
                            {
                                if (entry.Contains('\\') || entry.Contains('/'))
                                    processPaths.Add(entry);
                                else
                                    processNames.Add(entry);
                            }

                            if (processNames.Count > 0)
                            {
                                var arr = new System.Text.Json.Nodes.JsonArray();
                                foreach (var n in processNames) arr.Add((JsonNode)n);
                                rules.Add(new System.Text.Json.Nodes.JsonObject
                                {
                                    ["process_name"] = arr,
                                    ["outbound"]     = outbound
                                });
                            }

                            if (processPaths.Count > 0)
                            {
                                var arr = new System.Text.Json.Nodes.JsonArray();
                                foreach (var p in processPaths) arr.Add((JsonNode)p);
                                rules.Add(new System.Text.Json.Nodes.JsonObject
                                {
                                    ["process_path"] = arr,
                                    ["outbound"]     = outbound
                                });
                            }
                        }
                    }
                }

                // App RuleSets
                if (!string.IsNullOrEmpty(AppSession.Instance.RuleGoogleAction))
                {
                    rules.Add(new JsonObject
                    {
                        ["domain_suffix"] = new JsonArray { (JsonNode)"google.com", (JsonNode)"googleapis.com", (JsonNode)"gstatic.com", (JsonNode)"googlevideo.com" },
                        ["outbound"] = ResolveOutbound(AppSession.Instance.RuleGoogleAction)
                    });
                }

                if (!string.IsNullOrEmpty(AppSession.Instance.RuleTelegramAction))
                {
                    rules.Add(new JsonObject
                    {
                        ["domain_suffix"] = new JsonArray { (JsonNode)"telegram.org", (JsonNode)"t.me", (JsonNode)"tdesktop.com" },
                        ["ip_cidr"] = new JsonArray { (JsonNode)"91.108.4.0/22", (JsonNode)"91.108.8.0/22", (JsonNode)"91.108.12.0/22", (JsonNode)"91.108.16.0/22", (JsonNode)"91.108.56.0/22", (JsonNode)"149.154.160.0/20" },
                        ["outbound"] = ResolveOutbound(AppSession.Instance.RuleTelegramAction)
                    });
                }

                if (!string.IsNullOrEmpty(AppSession.Instance.RuleNetflixAction))
                {
                    rules.Add(new JsonObject
                    {
                        ["domain_suffix"] = new JsonArray { (JsonNode)"netflix.com", (JsonNode)"netflix.net", (JsonNode)"nflximg.net", (JsonNode)"nflxext.com", (JsonNode)"nflxso.net", (JsonNode)"nflxvideo.net" },
                        ["outbound"] = ResolveOutbound(AppSession.Instance.RuleNetflixAction)
                    });
                }

                if (!string.IsNullOrEmpty(AppSession.Instance.RuleYouTubeAction))
                {
                    rules.Add(new JsonObject
                    {
                        ["domain_suffix"] = new JsonArray { (JsonNode)"youtube.com", (JsonNode)"youtu.be", (JsonNode)"ytimg.com", (JsonNode)"ggpht.com" },
                        ["outbound"] = ResolveOutbound(AppSession.Instance.RuleYouTubeAction)
                    });
                }

                if (!string.IsNullOrEmpty(AppSession.Instance.RuleTikTokAction))
                {
                    rules.Add(new JsonObject
                    {
                        ["domain_suffix"] = new JsonArray { (JsonNode)"tiktok.com", (JsonNode)"tiktokv.com", (JsonNode)"tiktokcdn.com", (JsonNode)"byteoversea.com" },
                        ["outbound"] = ResolveOutbound(AppSession.Instance.RuleTikTokAction)
                    });
                }

                if (!string.IsNullOrEmpty(AppSession.Instance.RuleChatGPTAction))
                {
                    rules.Add(new JsonObject
                    {
                        ["domain_suffix"] = new JsonArray { (JsonNode)"openai.com", (JsonNode)"chatgpt.com", (JsonNode)"ai.com", (JsonNode)"oaistatic.com", (JsonNode)"oaiusercontent.com" },
                        ["outbound"] = ResolveOutbound(AppSession.Instance.RuleChatGPTAction)
                    });
                }

                if (!string.IsNullOrEmpty(AppSession.Instance.RuleClaudeAction))
                {
                    rules.Add(new JsonObject
                    {
                        ["domain_suffix"] = new JsonArray { (JsonNode)"anthropic.com", (JsonNode)"claude.ai" },
                        ["outbound"] = ResolveOutbound(AppSession.Instance.RuleClaudeAction)
                    });
                }
            }

            return new JsonObject
            {
                ["rules"] = rules,
                ["rule_set"] = ruleSets,
                ["final"] = "proxy",
                ["auto_detect_interface"] = true,
                ["default_domain_resolver"] = "local-dns"
            };
        }
    }
}
