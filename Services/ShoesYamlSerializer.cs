using System;
using System.Collections.Generic;
using System.Text;

namespace AnywhereWinUI.Services
{
    /// <summary>
    /// Pure BCL (WinRT-free) YAML serializer and protocol mapper for the Shoes (Rust) core.
    /// Shared between the WinUI app and the xUnit test suite.
    /// </summary>
    public static class ShoesYamlSerializer
    {
        public enum RejectKind
        {
            None = 0,
            InvalidHost,
            UnsupportedTransport,
            UnsupportedProtocol
        }

        private static readonly HashSet<string> SupportedSsCiphers = new(StringComparer.OrdinalIgnoreCase)
        {
            "aes-128-gcm",
            "aes-256-gcm",
            "chacha20-ietf-poly1305",
            "chacha20-poly1305",
            "xchacha20-ietf-poly1305",
            "2022-blake3-aes-128-gcm",
            "2022-blake3-aes-256-gcm",
            "2022-blake3-chacha20-poly1305",
            "none",
            "plain"
        };

        public static bool TryBuildClientProxyYaml(
            PersistedNode node,
            string tag,
            int indent,
            out string yaml,
            out RejectKind rejectKind,
            out string rejectDetail)
        {
            yaml = string.Empty;
            rejectKind = RejectKind.None;
            rejectDetail = string.Empty;

            if (node == null || string.IsNullOrWhiteSpace(node.Host))
            {
                rejectKind = RejectKind.InvalidHost;
                return false;
            }

            var (rawHost, parsedPort) = NodeLinkParser.SplitHostPort(node.Host);
            string cleanHost = rawHost.Trim('[', ']');
            int port = parsedPort > 0 ? parsedPort : 443;
            if (string.IsNullOrWhiteSpace(cleanHost))
            {
                rejectKind = RejectKind.InvalidHost;
                return false;
            }

            string address = cleanHost.Contains(':') ? $"[{cleanHost}]:{port}" : $"{cleanHost}:{port}";
            string protoLower = (node.Protocol ?? string.Empty).Trim().ToLowerInvariant();
            string network = string.IsNullOrWhiteSpace(node.Network) ? "tcp" : node.Network.Trim().ToLowerInvariant();
            string security = string.IsNullOrWhiteSpace(node.Security) ? "none" : node.Security.Trim().ToLowerInvariant();
            string sni = !string.IsNullOrWhiteSpace(node.Sni) ? node.Sni.Trim() : cleanHost;
            string uuid = !string.IsNullOrEmpty(node.Uuid) ? node.Uuid : "de000000-0000-0000-0000-000000000000";
            string password = !string.IsNullOrEmpty(node.Password) ? node.Password : "anywhere-password";

            if (network != "tcp" && network != "ws")
            {
                rejectKind = RejectKind.UnsupportedTransport;
                rejectDetail = node.Network ?? network;
                return false;
            }

            ProtoLayer? rootLayer;

            if (protoLower.Contains("vless"))
            {
                if (!string.IsNullOrWhiteSpace(node.VlessEncryption) &&
                    !string.Equals(node.VlessEncryption, "none", StringComparison.OrdinalIgnoreCase))
                {
                    rejectKind = RejectKind.UnsupportedProtocol;
                    rejectDetail = "VLESS-PQ";
                    return false;
                }

                bool isVision = !string.IsNullOrWhiteSpace(node.Flow) &&
                                node.Flow.Contains("vision", StringComparison.OrdinalIgnoreCase);

                var inner = new ProtoLayer("vless", ("user_id", Quote(uuid)));

                if (security == "reality")
                {
                    if (network != "tcp" || string.IsNullOrWhiteSpace(node.PublicKey))
                    {
                        rejectKind = RejectKind.UnsupportedProtocol;
                        rejectDetail = "VLESS Reality (" + network + ")";
                        return false;
                    }

                    var realityProps = new List<(string Key, string RawYamlValue)>
                    {
                        ("public_key", Quote(node.PublicKey.Trim())),
                        ("short_id", Quote((node.ShortId ?? string.Empty).Trim())),
                        ("sni_hostname", Quote(sni))
                    };
                    if (isVision)
                    {
                        realityProps.Add(("vision", "true"));
                    }
                    rootLayer = new ProtoLayer("reality", realityProps, inner);
                }
                else if (security == "tls")
                {
                    if (isVision)
                    {
                        rejectKind = RejectKind.UnsupportedProtocol;
                        rejectDetail = "VLESS XTLS-Vision";
                        return false;
                    }

                    ProtoLayer transportLayer = network == "ws"
                        ? WrapWebSocket(inner, node.Path, node.WsHost)
                        : inner;

                    rootLayer = WrapTls(transportLayer, sni, verify: !node.AllowInsecure);
                }
                else
                {
                    rootLayer = network == "ws"
                        ? WrapWebSocket(inner, node.Path, node.WsHost)
                        : inner;
                }
            }
            else if (protoLower.Contains("vmess"))
            {
                if (node.AlterId > 0)
                {
                    rejectKind = RejectKind.UnsupportedProtocol;
                    rejectDetail = "VMess Legacy (AlterId)";
                    return false;
                }

                string sec = (node.Encryption ?? string.Empty).Trim().ToLowerInvariant();
                if (string.IsNullOrEmpty(sec) || sec == "auto" || sec == "zero")
                {
                    sec = "aes-128-gcm";
                }
                else if (sec != "aes-128-gcm" && sec != "chacha20-poly1305" && sec != "none")
                {
                    rejectKind = RejectKind.UnsupportedProtocol;
                    rejectDetail = $"VMess ({sec})";
                    return false;
                }

                ProtoLayer layer = new ProtoLayer("vmess",
                    ("cipher", Quote(sec)),
                    ("user_id", Quote(uuid)));

                if (network == "ws")
                {
                    layer = WrapWebSocket(layer, node.Path, node.WsHost);
                }

                if (security == "tls")
                {
                    layer = WrapTls(layer, sni, verify: !node.AllowInsecure);
                }
                else if (security == "reality")
                {
                    rejectKind = RejectKind.UnsupportedProtocol;
                    rejectDetail = "VMess Reality";
                    return false;
                }

                rootLayer = layer;
            }
            else if (protoLower.Contains("trojan"))
            {
                if (security == "reality")
                {
                    rejectKind = RejectKind.UnsupportedProtocol;
                    rejectDetail = "Trojan Reality";
                    return false;
                }

                ProtoLayer layer = new ProtoLayer("trojan", ("password", Quote(password)));
                if (network == "ws")
                {
                    layer = WrapWebSocket(layer, node.Path, node.WsHost);
                }

                if (security != "none")
                {
                    layer = WrapTls(layer, sni, verify: !node.AllowInsecure);
                }

                rootLayer = layer;
            }
            else if (protoLower == "ss" || protoLower.Contains("shadowsocks"))
            {
                if (network != "tcp" || !string.IsNullOrWhiteSpace(node.ObfsType))
                {
                    rejectKind = RejectKind.UnsupportedProtocol;
                    rejectDetail = "Shadowsocks Plugin/WS";
                    return false;
                }

                string method = !string.IsNullOrWhiteSpace(node.Encryption) ? node.Encryption.Trim() : "aes-256-gcm";
                if (!SupportedSsCiphers.Contains(method))
                {
                    rejectKind = RejectKind.UnsupportedProtocol;
                    rejectDetail = $"Shadowsocks ({method})";
                    return false;
                }

                rootLayer = new ProtoLayer("shadowsocks",
                    ("cipher", Quote(method)),
                    ("password", Quote(password)));
            }
            else if (protoLower == "socks" || protoLower == "socks5")
            {
                if (network != "tcp" || (security != "none" && security != "tls"))
                {
                    rejectKind = RejectKind.UnsupportedProtocol;
                    rejectDetail = node.Protocol ?? "SOCKS";
                    return false;
                }

                var props = new List<(string, string)>();
                if (!string.IsNullOrEmpty(node.Username))
                {
                    props.Add(("username", Quote(node.Username)));
                    props.Add(("password", Quote(node.Password ?? string.Empty)));
                }

                ProtoLayer layer = new ProtoLayer("socks", props, null);
                if (security == "tls")
                {
                    layer = WrapTls(layer, sni, verify: !node.AllowInsecure);
                }
                rootLayer = layer;
            }
            else if (protoLower == "http" || protoLower == "https")
            {
                if (network != "tcp")
                {
                    rejectKind = RejectKind.UnsupportedTransport;
                    rejectDetail = node.Network ?? network;
                    return false;
                }

                var props = new List<(string, string)>();
                if (!string.IsNullOrEmpty(node.Username))
                {
                    props.Add(("username", Quote(node.Username)));
                    props.Add(("password", Quote(node.Password ?? string.Empty)));
                }

                ProtoLayer layer = new ProtoLayer("http", props, null);
                if (protoLower == "https" || security == "tls")
                {
                    layer = WrapTls(layer, sni, verify: !node.AllowInsecure);
                }
                rootLayer = layer;
            }
            else
            {
                rejectKind = RejectKind.UnsupportedProtocol;
                rejectDetail = string.IsNullOrWhiteSpace(node.Protocol) ? "Unknown" : node.Protocol;
                return false;
            }

            var sb = new StringBuilder(512);
            string pad = new(' ', indent);
            sb.Append(pad).Append("- name: ").AppendLine(Quote(tag));
            sb.Append(pad).Append("  address: ").AppendLine(Quote(address));
            sb.Append(pad).AppendLine("  protocol:");
            WriteProtoLayer(sb, rootLayer, indent + 4);

            yaml = sb.ToString();
            return true;
        }

        public static string? TryExtractClashSecret(string yamlContent)
        {
            if (string.IsNullOrEmpty(yamlContent)) return null;
            using var reader = new System.IO.StringReader(yamlContent);
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith("secret:", StringComparison.Ordinal))
                {
                    var val = trimmed.Substring("secret:".Length).Trim();
                    if (val.Length >= 2 && val[0] == '"' && val[^1] == '"')
                    {
                        val = val[1..^1].Replace("\\\"", "\"").Replace("\\\\", "\\");
                    }
                    return val;
                }
            }
            return null;
        }

        public static string Quote(string? value)
        {
            if (string.IsNullOrEmpty(value)) return "\"\"";
            var sb = new StringBuilder(value.Length + 4);
            sb.Append('"');
            foreach (char c in value)
            {
                switch (c)
                {
                    case '\\': sb.Append("\\\\"); break;
                    case '"':  sb.Append("\\\""); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20)
                            sb.Append($"\\x{(int)c:x2}");
                        else
                            sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
            return sb.ToString();
        }

        private static ProtoLayer WrapWebSocket(ProtoLayer inner, string? path, string? wsHost)
        {
            string cleanPath = string.IsNullOrWhiteSpace(path) ? "/" : path.Trim();
            var props = new List<(string, string)>
            {
                ("matching_path", Quote(cleanPath))
            };
            return new ProtoLayer("websocket", props, inner)
            {
                WsHostHeader = string.IsNullOrWhiteSpace(wsHost) ? null : wsHost.Trim()
            };
        }

        private static ProtoLayer WrapTls(ProtoLayer inner, string sni, bool verify)
        {
            return new ProtoLayer("tls",
                new List<(string, string)>
                {
                    ("sni_hostname", Quote(sni)),
                    ("verify", verify ? "true" : "false")
                },
                inner);
        }

        private static void WriteProtoLayer(StringBuilder sb, ProtoLayer layer, int indent)
        {
            string pad = new(' ', indent);
            sb.Append(pad).Append("type: ").AppendLine(Quote(layer.Type));
            foreach (var (k, v) in layer.Props)
            {
                sb.Append(pad).Append(k).Append(": ").AppendLine(v);
            }
            if (!string.IsNullOrEmpty(layer.WsHostHeader))
            {
                sb.Append(pad).AppendLine("matching_headers:");
                sb.Append(pad).Append("  Host: ").AppendLine(Quote(layer.WsHostHeader));
            }
            if (layer.Inner != null)
            {
                sb.Append(pad).AppendLine("protocol:");
                WriteProtoLayer(sb, layer.Inner, indent + 2);
            }
        }

        private sealed class ProtoLayer
        {
            public string Type { get; }
            public List<(string Key, string RawYamlValue)> Props { get; }
            public ProtoLayer? Inner { get; }
            public string? WsHostHeader { get; set; }

            public ProtoLayer(string type, params (string Key, string RawYamlValue)[] props)
            {
                Type = type;
                Props = new List<(string, string)>(props);
            }

            public ProtoLayer(string type, List<(string Key, string RawYamlValue)> props, ProtoLayer? inner)
            {
                Type = type;
                Props = props;
                Inner = inner;
            }
        }
    }
}

