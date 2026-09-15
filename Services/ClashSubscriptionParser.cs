using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using YamlDotNet.RepresentationModel;

namespace AnywhereWinUI.Services
{
    /// <summary>
    /// Parses Clash / Clash Meta (mihomo) YAML subscriptions, converting the entries of the
    /// top-level <c>proxies:</c> sequence into <see cref="PersistedNode"/> instances.
    /// <para>
    /// Only nodes are imported: <c>proxy-groups</c>, <c>rules</c>, <c>rule-providers</c>,
    /// <c>proxy-providers</c>, <c>dns</c>, <c>tun</c> and <c>listeners</c> are ignored. The
    /// application keeps its own routing model.
    /// </para>
    /// <para>
    /// Implementation notes: only YamlDotNet's RepresentationModel is used (never
    /// <c>Deserializer</c> / reflection), so the Release build stays trim-safe. All failures
    /// degrade to "return an empty list + debug log"; nothing is thrown to the caller.
    /// </para>
    /// </summary>
    public static class ClashSubscriptionParser
    {
        // Matches a line whose only content is the top-level `proxies:` key (block or flow form).
        private static readonly Regex ProxiesLineRegex = new(
            @"^[ \t]*proxies[ \t]*:[ \t]*(\[|$)",
            RegexOptions.Multiline | RegexOptions.CultureInvariant);

        // Matches the start of the `proxies:` line regardless of what follows on it.
        private static readonly Regex ProxiesKeyLineRegex = new(
            @"^[ \t]*proxies[ \t]*:",
            RegexOptions.Multiline | RegexOptions.CultureInvariant);

        private static readonly Regex FirstNumberRegex = new(
            @"\d+",
            RegexOptions.CultureInvariant);

        /// <summary>Low-cost sniffing: does this text look like a Clash configuration?</summary>
        public static bool LooksLikeClashYaml(string content)
        {
            if (string.IsNullOrWhiteSpace(content))
                return false;

            // JSON payloads are handled by the sing-box branch that runs before us.
            var head = content.TrimStart();
            if (head.StartsWith("{", StringComparison.Ordinal) || head.StartsWith("[", StringComparison.Ordinal))
                return false;

            return ProxiesKeyLineRegex.IsMatch(content);
        }

        /// <summary>Parses the <c>proxies:</c> list. Returns an empty list on failure; never throws.</summary>
        public static List<PersistedNode> Parse(string yaml, string subId)
        {
            var nodes = new List<PersistedNode>();
            if (string.IsNullOrWhiteSpace(yaml))
                return nodes;

            var skipped = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var warnings = new List<string>();

            var proxies = ExtractProxiesSequence(yaml);
            if (proxies == null)
            {
                Debug.WriteLine("[ClashParser] proxies section not found or not parsable");
                return nodes;
            }

            foreach (var item in proxies.Children)
            {
                if (item is not YamlMappingNode rawMapping)
                {
                    CountSkip(skipped, "(non-mapping entry)");
                    continue;
                }

                // Merge keys (`<<: *anchor`) are not resolved by the RepresentationModel.
                var mapping = ResolveMergeKeys(rawMapping);

                string type = (Str(mapping, "type") ?? string.Empty).Trim().ToLowerInvariant();
                if (string.IsNullOrEmpty(type))
                {
                    CountSkip(skipped, "(no type)");
                    continue;
                }

                try
                {
                    var node = ConvertProxy(mapping, type, subId, skipped, warnings);
                    if (node == null)
                        continue;

                    if (!NodeLinkParser.TrySplitHostPort(node.Host, out _, out _))
                    {
                        CountSkip(skipped, type + " (invalid host)");
                        continue;
                    }

                    // The node model has HeaderType, but ConfigBuilder does not consume it yet.
                    if (node.HeaderType != null && node.HeaderType.Equals("http", StringComparison.OrdinalIgnoreCase))
                        warnings.Add($"network: http camouflage header dropped for '{node.Name}'");

                    nodes.Add(node);
                }
                catch (Exception ex)
                {
                    CountSkip(skipped, type);
                    Debug.WriteLine($"[ClashParser] node conversion failed ({type}): {ex.Message}");
                }
            }

            int skippedTotal = 0;
            var skipParts = new List<string>(skipped.Count);
            foreach (var kv in skipped)
            {
                skippedTotal += kv.Value;
                skipParts.Add($"{kv.Key}x{kv.Value}");
            }

            string skipDetail = skipParts.Count == 0 ? "none" : string.Join(", ", skipParts);

            Debug.WriteLine($"[ClashParser] parsed {nodes.Count} node(s), skipped {skippedTotal} ({skipDetail})");

            foreach (var warning in warnings)
                Debug.WriteLine($"[ClashParser] note: {warning}");

            return nodes;
        }

        // ── YAML loading ──────────────────────────────────────────────────────

        private static YamlSequenceNode? ExtractProxiesSequence(string yaml)
        {
            if (TryReadProxies(yaml, out var proxies))
                return proxies;

            // Fallback: some subscriptions carry characters the parser rejects (tabs used for
            // indentation, duplicate keys, stray `%` directives). Re-parse only the `proxies:`
            // block so the rest of the file cannot poison the parse.
            var section = ExtractProxiesSection(yaml);
            if (section != null && TryReadProxies(section, out var sectionProxies))
            {
                Debug.WriteLine("[ClashParser] full document failed; recovered by parsing the proxies block only");
                return sectionProxies;
            }

            return null;
        }

        private static bool TryReadProxies(string text, out YamlSequenceNode proxies)
        {
            proxies = null!;
            var stream = new YamlStream();
            try
            {
                using var reader = new StringReader(text);
                stream.Load(reader);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ClashParser] YAML load failed: {ex.Message}");
                return false;
            }

            foreach (var document in stream.Documents)
            {
                if (document.RootNode is not YamlMappingNode mapping)
                    continue;

                if (TryGetChild(mapping, "proxies", out var node) && node is YamlSequenceNode sequence)
                {
                    proxies = sequence;
                    return true;
                }
            }

            return false;
        }

        /// <summary>Carves out the text from the `proxies:` line up to the next top-level key.</summary>
        private static string? ExtractProxiesSection(string yaml)
        {
            var lines = yaml.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

            int start = -1;
            for (int i = 0; i < lines.Length; i++)
            {
                if (ProxiesKeyLineRegex.IsMatch(lines[i]))
                {
                    start = i;
                    break;
                }
            }

            if (start < 0)
                return null;

            var builder = new StringBuilder();
            for (int i = start; i < lines.Length; i++)
            {
                string line = lines[i];

                if (i > start && line.Length > 0 && !char.IsWhiteSpace(line[0]))
                {
                    // Any column-0 token ends the block (next top-level key, `---`, `...`).
                    if (line.StartsWith("#", StringComparison.Ordinal))
                    {
                        builder.AppendLine(line);
                        continue;
                    }
                    break;
                }

                builder.AppendLine(line);
            }

            return builder.ToString();
        }

        // ── Node conversion ───────────────────────────────────────────────────

        private static PersistedNode? ConvertProxy(
            YamlMappingNode p,
            string type,
            string subId,
            Dictionary<string, int> skipped,
            List<string> warnings)
        {
            switch (type)
            {
                case "ss":
                case "shadowsocks":
                    return ConvertShadowsocks(p, subId, skipped);

                case "vmess":
                    return ConvertVmess(p, subId, skipped);

                case "vless":
                    return ConvertVless(p, subId, skipped);

                case "trojan":
                    return ConvertTrojan(p, subId, skipped);

                case "hysteria2":
                case "hy2":
                    return ConvertHysteria2(p, subId, skipped, warnings);

                case "tuic":
                    return ConvertTuic(p, subId, skipped, warnings);

                case "wireguard":
                    return ConvertWireGuard(p, subId, skipped);

                case "anytls":
                    return ConvertAnyTls(p, subId, skipped);

                case "snell":
                    return ConvertSnell(p, subId, skipped);

                case "socks5":
                case "socks":
                    return ConvertSocks5(p, subId, skipped);

                case "http":
                case "https":
                    return ConvertHttp(p, subId);

                default:
                    // hysteria v1, ssr, mieru, ssh, direct, reject, dns, relay, unknown types, ...
                    CountSkip(skipped, type);
                    return null;
            }
        }

        private static PersistedNode? ConvertShadowsocks(
            YamlMappingNode p, string subId, Dictionary<string, int> skipped)
        {
            string? plugin = Str(p, "plugin");
            var pluginOpts = Map(p, "plugin-opts");

            bool isShadowTls = false;
            if (!string.IsNullOrWhiteSpace(plugin))
            {
                string pluginName = plugin.Trim().ToLowerInvariant();

                // Only shadow-tls is representable in this app's node model. SIP003 obfs /
                // v2ray-plugin / restls are rejected, mirroring NodeLinkParser's policy.
                if (pluginName != "shadow-tls")
                {
                    CountSkip(skipped, "ss+" + pluginName);
                    return null;
                }

                isShadowTls = true;
            }

            if (!TryResolveEndpoint(p, out string server, out int port))
            {
                CountSkip(skipped, "ss (no server/port)");
                return null;
            }

            var node = NewNode(p, server, port, subId);
            node.Protocol = "Shadowsocks";
            node.Encryption = Str(p, "cipher");
            node.Password = Str(p, "password") ?? string.Empty;
            node.Network = "tcp";

            if (isShadowTls)
            {
                node.IsShadowTls = true;
                node.ShadowTlsVersion = Int(pluginOpts, "version", 3);
                node.ShadowTlsPassword = Str(pluginOpts, "password") ?? string.Empty;
                node.Sni = Str(pluginOpts, "host") ?? Str(pluginOpts, "sni");
                node.AllowInsecure = Bool(pluginOpts, "skip-cert-verify", false);
            }

            return node;
        }

        private static PersistedNode? ConvertVmess(
            YamlMappingNode p, string subId, Dictionary<string, int> skipped)
        {
            string? uuid = Trimmed(Str(p, "uuid"));
            if (uuid == null)
            {
                CountSkip(skipped, "vmess (no uuid)");
                return null;
            }

            if (!TryResolveEndpoint(p, out string server, out int port))
            {
                CountSkip(skipped, "vmess (no server/port)");
                return null;
            }

            var node = NewNode(p, server, port, subId);
            node.Protocol = "VMess";
            node.Uuid = uuid;
            node.AlterId = Int(p, "alterId", 0);
            node.Encryption = Str(p, "cipher") ?? "auto";
            node.HeaderType = "none";

            ApplyTls(node, p, defaultTls: false);
            ApplyTransport(node, p);
            return node;
        }

        private static PersistedNode? ConvertVless(
            YamlMappingNode p, string subId, Dictionary<string, int> skipped)
        {
            string? uuid = Trimmed(Str(p, "uuid"));
            if (uuid == null)
            {
                CountSkip(skipped, "vless (no uuid)");
                return null;
            }

            if (!TryResolveEndpoint(p, out string server, out int port))
            {
                CountSkip(skipped, "vless (no server/port)");
                return null;
            }

            var node = NewNode(p, server, port, subId);
            node.Protocol = "VLESS";
            node.Uuid = uuid;
            node.Flow = Trimmed(Str(p, "flow"));
            node.Encryption = "none";

            ApplyTls(node, p, defaultTls: false);
            ApplyReality(node, p);
            ApplyTransport(node, p);
            return node;
        }

        private static PersistedNode? ConvertTrojan(
            YamlMappingNode p, string subId, Dictionary<string, int> skipped)
        {
            string? password = Str(p, "password");
            if (string.IsNullOrEmpty(password))
            {
                CountSkip(skipped, "trojan (no password)");
                return null;
            }

            if (!TryResolveEndpoint(p, out string server, out int port))
            {
                CountSkip(skipped, "trojan (no server/port)");
                return null;
            }

            var node = NewNode(p, server, port, subId);
            node.Protocol = "Trojan";
            node.Password = password;
            node.Security = "tls"; // trojan implies TLS
            node.Sni = Trimmed(Str(p, "sni")) ?? Trimmed(Str(p, "servername"));
            node.AllowInsecure = Bool(p, "skip-cert-verify", false);
            node.Fingerprint = Trimmed(Str(p, "client-fingerprint"));
            ApplyAlpn(node, p);

            ApplyTransport(node, p);
            return node;
        }

        private static PersistedNode? ConvertHysteria2(
            YamlMappingNode p, string subId, Dictionary<string, int> skipped, List<string> warnings)
        {
            string? password = Trimmed(Str(p, "password")) ?? Trimmed(Str(p, "auth"));
            if (password == null)
            {
                CountSkip(skipped, "hysteria2 (no password)");
                return null;
            }

            if (!TryResolveEndpoint(p, out string server, out int port, out bool portDegraded))
            {
                CountSkip(skipped, "hysteria2 (no server/port)");
                return null;
            }

            if (portDegraded)
                warnings.Add($"hysteria2 port hopping degraded to a single port ({server}:{port})");

            var node = NewNode(p, server, port, subId);
            node.Protocol = "Hysteria 2";
            node.Password = password;
            node.Security = "tls";
            node.Sni = Trimmed(Str(p, "sni")) ?? Trimmed(Str(p, "servername"));
            node.AllowInsecure = Bool(p, "skip-cert-verify", false);
            node.Fingerprint = Trimmed(Str(p, "client-fingerprint"));
            node.ObfsType = Trimmed(Str(p, "obfs")) ?? "none";
            node.ObfsPassword = Str(p, "obfs-password");
            node.Network = "udp";
            ApplyAlpn(node, p);
            return node;
        }

        private static PersistedNode? ConvertTuic(
            YamlMappingNode p, string subId, Dictionary<string, int> skipped, List<string> warnings)
        {
            string? uuid = Trimmed(Str(p, "uuid"));
            if (uuid == null)
            {
                // TUIC v4 used a bare token; the bundled sing-box only speaks v5.
                if (Trimmed(Str(p, "token")) != null)
                {
                    CountSkip(skipped, "tuic v4 (token only)");
                    return null;
                }

                CountSkip(skipped, "tuic (no uuid)");
                return null;
            }

            if (!TryResolveEndpoint(p, out string server, out int port))
            {
                CountSkip(skipped, "tuic (no server/port)");
                return null;
            }

            var node = NewNode(p, server, port, subId);
            node.Protocol = "TUIC";
            node.Uuid = uuid;
            node.Password = Str(p, "password") ?? string.Empty;
            node.Security = "tls";
            node.Sni = Trimmed(Str(p, "sni")) ?? Trimmed(Str(p, "servername"));
            node.AllowInsecure = Bool(p, "skip-cert-verify", false);
            node.Network = "udp";
            ApplyAlpn(node, p);
            return node;
        }

        private static PersistedNode? ConvertWireGuard(
            YamlMappingNode p, string subId, Dictionary<string, int> skipped)
        {
            // Modern Clash Meta moves the peer endpoint into `peers:`; the legacy form keeps
            // `server` / `port` / `public-key` at the top level. Only the first peer is used.
            var peer = FirstSequenceMapping(p, "peers");

            string? server = Trimmed(Str(p, "server")) ?? Trimmed(Str(peer, "server"));
            int port = Int(p, "port", 0);
            if (port <= 0)
                port = Int(peer, "port", 0);

            if (server == null || port <= 0 || port > 65535)
            {
                CountSkip(skipped, "wireguard (no server/port)");
                return null;
            }

            string? privateKey = Trimmed(Str(p, "private-key"));
            string? publicKey = Trimmed(Str(p, "public-key")) ?? Trimmed(Str(peer, "public-key"));
            if (privateKey == null || publicKey == null)
            {
                CountSkip(skipped, "wireguard (no key pair)");
                return null;
            }

            var node = NewNode(p, server, port, subId);
            node.Protocol = "WireGuard";
            node.WgPrivateKey = privateKey;
            node.PublicKey = publicKey;
            node.WgPreSharedKey = Trimmed(Str(p, "pre-shared-key")) ?? Trimmed(Str(peer, "pre-shared-key"));
            node.WgLocalAddress = BuildWireGuardAddress(Str(p, "ip"), Str(p, "ipv6"));
            node.WgMtu = Int(p, "mtu", 0);
            node.Network = "udp";
            return node;
        }

        private static PersistedNode? ConvertAnyTls(
            YamlMappingNode p, string subId, Dictionary<string, int> skipped)
        {
            string? password = Str(p, "password");
            if (string.IsNullOrEmpty(password))
            {
                CountSkip(skipped, "anytls (no password)");
                return null;
            }

            if (!TryResolveEndpoint(p, out string server, out int port))
            {
                CountSkip(skipped, "anytls (no server/port)");
                return null;
            }

            var node = NewNode(p, server, port, subId);
            node.Protocol = "AnyTLS";
            node.Password = password;
            node.Security = "tls";
            node.Sni = Trimmed(Str(p, "sni")) ?? Trimmed(Str(p, "servername"));
            node.AllowInsecure = Bool(p, "skip-cert-verify", false);
            node.Fingerprint = Trimmed(Str(p, "client-fingerprint"));
            ApplyAlpn(node, p);
            return node;
        }

        private static PersistedNode? ConvertSnell(
            YamlMappingNode p, string subId, Dictionary<string, int> skipped)
        {
            string? psk = Str(p, "psk");
            if (string.IsNullOrEmpty(psk))
            {
                CountSkip(skipped, "snell (no psk)");
                return null;
            }

            if (!TryResolveEndpoint(p, out string server, out int port))
            {
                CountSkip(skipped, "snell (no server/port)");
                return null;
            }

            var obfs = Map(p, "obfs-opts");

            var node = NewNode(p, server, port, subId);
            node.Protocol = "snell"; // lower-case, matches NodeLinkParser
            node.Password = psk;
            node.SnellVersion = Int(p, "version", 4);
            node.ObfsType = Trimmed(Str(obfs, "mode"));
            node.WsHost = Trimmed(Str(obfs, "host"));
            return node;
        }

        private static PersistedNode? ConvertSocks5(
            YamlMappingNode p, string subId, Dictionary<string, int> skipped)
        {
            if (!TryResolveEndpoint(p, out string server, out int port))
            {
                CountSkip(skipped, "socks5 (no server/port)");
                return null;
            }

            // ConfigBuilder never writes a TLS block for socks outbounds.
            if (Bool(p, "tls", false))
            {
                CountSkip(skipped, "socks5 over TLS");
                return null;
            }

            var node = NewNode(p, server, port, subId);
            node.Protocol = "SOCKS5";
            node.Username = Trimmed(Str(p, "username"));
            node.Password = Str(p, "password");
            node.Network = "tcp";
            return node;
        }

        private static PersistedNode? ConvertHttp(YamlMappingNode p, string subId)
        {
            if (!TryResolveEndpoint(p, out string server, out int port))
                return null;

            var node = NewNode(p, server, port, subId);
            node.Protocol = "HTTP";
            node.Username = Trimmed(Str(p, "username"));
            node.Password = Str(p, "password");
            node.Network = "tcp";

            if (Bool(p, "tls", false))
            {
                node.Security = "tls";
                node.Sni = Trimmed(Str(p, "sni")) ?? Trimmed(Str(p, "servername"));
                node.AllowInsecure = Bool(p, "skip-cert-verify", false);
            }

            return node;
        }

        // ── Shared mapping helpers ────────────────────────────────────────────

        private static PersistedNode NewNode(YamlMappingNode p, string server, int port, string subId)
        {
            string? name = Trimmed(Str(p, "name"));
            string host = NodeLinkParser.FormatHostPortPublic(server, port);

            return new PersistedNode
            {
                Name = name ?? host,
                Host = host,
                SubscriptionId = subId
            };
        }

        /// <summary>Reads tls / servername / sni / skip-cert-verify / fingerprint / alpn.</summary>
        private static void ApplyTls(PersistedNode node, YamlMappingNode p, bool defaultTls)
        {
            bool tls = Bool(p, "tls", defaultTls);
            node.Security = tls ? "tls" : "none";
            node.Sni = Trimmed(Str(p, "servername")) ?? Trimmed(Str(p, "sni"));
            node.AllowInsecure = Bool(p, "skip-cert-verify", false);
            node.Fingerprint = Trimmed(Str(p, "client-fingerprint"));
            ApplyAlpn(node, p);
        }

        private static void ApplyReality(PersistedNode node, YamlMappingNode p)
        {
            var reality = Map(p, "reality-opts");
            if (reality == null)
                return;

            node.Security = "reality";
            node.PublicKey = Trimmed(Str(reality, "public-key"));
            node.ShortId = Trimmed(Str(reality, "short-id"));
        }

        private static void ApplyAlpn(PersistedNode node, YamlMappingNode p)
        {
            var alpn = TextList(p, "alpn");
            if (alpn.Count > 0)
                node.Alpn = string.Join(",", alpn);
        }

        private static void ApplyTransport(PersistedNode node, YamlMappingNode p)
        {
            string network = (Str(p, "network") ?? "tcp").Trim().ToLowerInvariant();
            switch (network)
            {
                case "http":
                    // Clash's HTTP header camouflage for plain TCP. The app's node model has
                    // HeaderType, but ConfigBuilder does not consume it yet, so the header is
                    // dropped (documented limitation).
                    node.Network = "tcp";
                    node.HeaderType = "http";
                    break;

                case "ws":
                case "grpc":
                case "h2":
                case "httpupgrade":
                    node.Network = network;
                    break;

                case "":
                case "tcp":
                default:
                    node.Network = "tcp";
                    break;
            }

            var wsOpts = Map(p, "ws-opts");
            if (wsOpts != null)
            {
                if (node.Path == null)
                    node.Path = Trimmed(Str(wsOpts, "path"));

                if (node.WsHost == null)
                {
                    var headers = Map(wsOpts, "headers");
                    node.WsHost = headers != null
                        ? Trimmed(Str(headers, "Host"))
                        : Trimmed(Str(wsOpts, "headers"));
                }
            }

            if (node.Path == null)
                node.Path = Trimmed(Str(p, "ws-path"));

            if (node.WsHost == null)
            {
                var legacyHeaders = Map(p, "ws-headers");
                node.WsHost = legacyHeaders != null
                    ? Trimmed(Str(legacyHeaders, "Host"))
                    : Trimmed(Str(p, "ws-headers"));
            }

            var grpcOpts = Map(p, "grpc-opts");
            if (grpcOpts != null && node.Path == null)
                node.Path = Trimmed(Str(grpcOpts, "grpc-service-name"));

            var h2Opts = Map(p, "h2-opts");
            if (h2Opts != null)
            {
                if (node.Path == null)
                    node.Path = Trimmed(Str(h2Opts, "path"));

                if (node.WsHost == null)
                {
                    var hosts = TextList(h2Opts, "host");
                    if (hosts.Count > 0)
                        node.WsHost = hosts[0];
                }
            }
        }

        private static bool TryResolveEndpoint(YamlMappingNode p, out string server, out int port)
            => TryResolveEndpoint(p, out server, out port, out _);

        private static bool TryResolveEndpoint(
            YamlMappingNode p, out string server, out int port, out bool portDegraded)
        {
            portDegraded = false;
            server = (Str(p, "server") ?? string.Empty).Trim();
            port = Int(p, "port", 0);

            if (port <= 0 || port > 65535)
            {
                // Hysteria2 port hopping (`ports: 20000-30000`, `[20000, 30000]`, ...).
                int jumpPort = ReadPortHopping(p);
                if (jumpPort > 0)
                {
                    port = jumpPort;
                    portDegraded = true;
                }
            }

            if (string.IsNullOrWhiteSpace(server))
                return false;

            return port > 0 && port <= 65535;
        }

        private static int ReadPortHopping(YamlMappingNode p)
        {
            if (!TryGetChild(p, "ports", out var node))
                return 0;

            switch (node)
            {
                case YamlScalarNode scalar:
                    return ParsePortRange(scalar.Value);

                case YamlSequenceNode sequence:
                    foreach (var child in sequence.Children)
                    {
                        int candidate = ParsePortRange(ScalarText(child));
                        if (candidate > 0)
                            return candidate;
                    }
                    break;
            }

            return 0;
        }

        private static int ParsePortRange(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return 0;

            var match = FirstNumberRegex.Match(value);
            if (match.Success
                && int.TryParse(match.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)
                && parsed > 0
                && parsed <= 65535)
            {
                return parsed;
            }

            return 0;
        }

        private static string? BuildWireGuardAddress(string? ip, string? ipv6)
        {
            var parts = new List<string>();

            AddAddress(parts, ip, "/32");
            AddAddress(parts, ipv6, "/128");

            return parts.Count > 0 ? string.Join(",", parts) : null;
        }

        private static void AddAddress(List<string> parts, string? value, string suffix)
        {
            if (string.IsNullOrWhiteSpace(value))
                return;

            var address = value.Trim().Trim('"', '\'');
            if (address.Length == 0)
                return;

            // Clash values are usually bare addresses, but some providers already ship CIDR.
            parts.Add(address.Contains('/') ? address : address + suffix);
        }

        // ── YamlDotNet RepresentationModel helpers ────────────────────────────

        /// <summary>
        /// Resolves `&lt;&lt;: *anchor` merge keys, which the RepresentationModel leaves as a
        /// literal "&lt;&lt;" entry. Later sources win; explicit keys beat merged ones.
        /// </summary>
        private static YamlMappingNode ResolveMergeKeys(YamlMappingNode map)
        {
            if (!TryGetChild(map, "<<", out var mergeNode))
                return map;

            var sources = new List<YamlMappingNode>();
            switch (mergeNode)
            {
                case YamlMappingNode single:
                    sources.Add(single);
                    break;

                case YamlSequenceNode sequence:
                    foreach (var child in sequence.Children)
                    {
                        if (child is YamlMappingNode childMapping)
                            sources.Add(childMapping);
                    }
                    break;
            }

            if (sources.Count == 0)
                return map;

            var merged = new YamlMappingNode();

            foreach (var source in sources)
            {
                foreach (var kv in source.Children)
                {
                    if (IsMergeKey(kv.Key))
                        continue;

                    merged.Children[kv.Key] = kv.Value;
                }
            }

            foreach (var kv in map.Children)
            {
                if (IsMergeKey(kv.Key))
                    continue;

                merged.Children[kv.Key] = kv.Value;
            }

            return merged;
        }

        private static bool IsMergeKey(YamlNode key)
            => key is YamlScalarNode scalar && scalar.Value == "<<";

        private static bool TryGetChild(YamlMappingNode map, string key, out YamlNode value)
        {
            foreach (var kv in map.Children)
            {
                if (kv.Key is YamlScalarNode scalar
                    && string.Equals(scalar.Value, key, StringComparison.OrdinalIgnoreCase))
                {
                    value = kv.Value;
                    return true;
                }
            }

            value = null!;
            return false;
        }

        private static YamlMappingNode? Map(YamlMappingNode? map, string key)
        {
            if (map == null || !TryGetChild(map, key, out var node))
                return null;

            return node as YamlMappingNode;
        }

        private static YamlMappingNode? FirstSequenceMapping(YamlMappingNode map, string key)
        {
            if (!TryGetChild(map, key, out var node) || node is not YamlSequenceNode sequence)
                return null;

            foreach (var child in sequence.Children)
            {
                if (child is YamlMappingNode mapping)
                    return mapping;
            }

            return null;
        }

        private static string? Str(YamlMappingNode? map, string key)
        {
            if (map == null || !TryGetChild(map, key, out var node))
                return null;

            return ScalarText(node);
        }

        private static string? ScalarText(YamlNode? node)
            => node is YamlScalarNode scalar ? scalar.Value : null;

        private static string? Trimmed(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            return value.Trim();
        }

        private static int Int(YamlMappingNode? map, string key, int fallback)
        {
            string? text = Trimmed(Str(map, key));
            if (text == null)
                return fallback;

            return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)
                ? parsed
                : fallback;
        }

        private static bool Bool(YamlMappingNode? map, string key, bool fallback)
        {
            string? text = Trimmed(Str(map, key));
            if (text == null)
                return fallback;

            switch (text.ToLowerInvariant())
            {
                case "true":
                case "yes":
                case "on":
                case "1":
                    return true;

                case "false":
                case "no":
                case "off":
                case "0":
                    return false;

                default:
                    return fallback;
            }
        }

        /// <summary>Accepts both a YAML sequence and a bare scalar (single value or CSV).</summary>
        private static List<string> TextList(YamlMappingNode? map, string key)
        {
            var result = new List<string>();
            if (map == null || !TryGetChild(map, key, out var node))
                return result;

            switch (node)
            {
                case YamlSequenceNode sequence:
                    foreach (var child in sequence.Children)
                    {
                        string? item = Trimmed(ScalarText(child));
                        if (item != null)
                            result.Add(item);
                    }
                    break;

                case YamlScalarNode scalar:
                    foreach (var part in (scalar.Value ?? string.Empty).Split(','))
                    {
                        string? item = Trimmed(part);
                        if (item != null)
                            result.Add(item);
                    }
                    break;
            }

            return result;
        }

        private static void CountSkip(Dictionary<string, int> skipped, string reason)
        {
            skipped.TryGetValue(reason, out int count);
            skipped[reason] = count + 1;
        }
    }
}
