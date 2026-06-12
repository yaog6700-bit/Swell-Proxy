using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;

namespace AnywhereWinUI.Services
{
    /// <summary>
    /// Parses proxy share links (ss://, vmess://, vless://, hysteria2://, trojan://,
    /// tuic://, naive://, naive+https://, anytls://, socks5://) into PersistedNode instances.
    /// Returns null on any parse failure.
    /// Ported from XrayUI-dev-net10 NodeLinkParser.cs and adapted to AnywhereWinUI's PersistedNode model.
    /// </summary>
    public static class NodeLinkParser
    {
        public static PersistedNode? Parse(string rawLink)
        {
            if (string.IsNullOrWhiteSpace(rawLink))
                return null;

            rawLink = rawLink.Trim();

            if (rawLink.StartsWith("ss://",          StringComparison.OrdinalIgnoreCase)) return ParseSs(rawLink);
            if (rawLink.StartsWith("vmess://",       StringComparison.OrdinalIgnoreCase)) return ParseVmess(rawLink);
            if (rawLink.StartsWith("vless://",       StringComparison.OrdinalIgnoreCase)) return ParseVless(rawLink);
            if (rawLink.StartsWith("hysteria2://",   StringComparison.OrdinalIgnoreCase)) return ParseHysteria2(rawLink);
            if (rawLink.StartsWith("hy2://",         StringComparison.OrdinalIgnoreCase)) return ParseHysteria2(rawLink);
            if (rawLink.StartsWith("trojan://",      StringComparison.OrdinalIgnoreCase)) return ParseTrojan(rawLink);
            if (rawLink.StartsWith("tuic://",        StringComparison.OrdinalIgnoreCase)) return ParseTuic(rawLink);
            if (rawLink.StartsWith("naive+https://", StringComparison.OrdinalIgnoreCase)) return ParseNaive(rawLink, "naive+https://");
            if (rawLink.StartsWith("naive+http2://", StringComparison.OrdinalIgnoreCase)) return ParseNaive(rawLink, "naive+http2://");
            if (rawLink.StartsWith("naive+http://",  StringComparison.OrdinalIgnoreCase)) return ParseNaive(rawLink, "naive+http://");
            if (rawLink.StartsWith("naive://",       StringComparison.OrdinalIgnoreCase)) return ParseNaive(rawLink, "naive://");
            if (rawLink.StartsWith("http2://",       StringComparison.OrdinalIgnoreCase)) return ParseNaive(rawLink, "http2://");
            if (rawLink.StartsWith("anytls://",      StringComparison.OrdinalIgnoreCase)) return ParseAnyTls(rawLink);
            if (rawLink.StartsWith("socks5://",      StringComparison.OrdinalIgnoreCase)) return ParseSocks(rawLink);
            if (rawLink.StartsWith("socks://",       StringComparison.OrdinalIgnoreCase)) return ParseSocks(rawLink);
            if (rawLink.StartsWith("wireguard://",   StringComparison.OrdinalIgnoreCase)) return ParseWireGuard(rawLink);
            if (rawLink.StartsWith("nowhere://",     StringComparison.OrdinalIgnoreCase)) return ParseNowhere(rawLink);
            if (rawLink.StartsWith("https://",       StringComparison.OrdinalIgnoreCase)) return ParseHttp(rawLink);
            // Note: http:// must come last among http* prefixes to avoid shadowing http2://
            if (rawLink.StartsWith("http://",        StringComparison.OrdinalIgnoreCase)) return ParseHttp(rawLink);

            return null;
        }

        // ── Shadowsocks ───────────────────────────────────────────────────────

        private static PersistedNode? ParseSs(string link)
        {
            try
            {
                var rest = link.Substring("ss://".Length);

                // Extract fragment (name)
                string name = string.Empty;
                var hashIdx = rest.IndexOf('#');
                if (hashIdx >= 0)
                {
                    name = Uri.UnescapeDataString(rest.Substring(hashIdx + 1));
                    rest = rest.Substring(0, hashIdx);
                }

                // Reject SIP003 plugin links (xray has no SIP003 outbound)
                var queryIdx = rest.IndexOf('?');
                if (queryIdx >= 0)
                {
                    var ssQuery = ParseQuery(rest.Substring(queryIdx));
                    if (ssQuery.ContainsKey("plugin"))
                        return null;
                    rest = rest.Substring(0, queryIdx);
                }

                string method, password, host;
                int port;

                int atIdx = rest.LastIndexOf('@');
                if (atIdx > 0)
                {
                    var userinfoPart = rest.Substring(0, atIdx);
                    var hostPart     = rest.Substring(atIdx + 1);

                    var decoded = TryBase64Decode(userinfoPart);
                    if (decoded != null && decoded.Contains(':'))
                    {
                        // Valid SIP002 Base64
                        var colonIdx = decoded.IndexOf(':');
                        method   = decoded.Substring(0, colonIdx);
                        password = decoded.Substring(colonIdx + 1);
                        (host, port) = SplitHostPort(hostPart);
                    }
                    else
                    {
                        // Raw SIP002: method:password@host:port
                        var unescaped = Uri.UnescapeDataString(userinfoPart);
                        if (unescaped.Contains(':'))
                        {
                            var colonIdx = unescaped.IndexOf(':');
                            method   = unescaped.Substring(0, colonIdx);
                            password = unescaped.Substring(colonIdx + 1);
                            (host, port) = SplitHostPort(hostPart);
                        }
                        else
                        {
                            return ParseSsLegacy(rest, name);
                        }
                    }
                }
                else
                {
                    // Legacy: entire string is BASE64(method:password@host:port)
                    return ParseSsLegacy(rest, name);
                }

                var entry = new PersistedNode
                {
                    Name       = name,
                    Protocol   = "Shadowsocks",
                    Encryption = method,
                    Password   = password,
                    Host       = FormatHostPort(host, port),
                    Network    = "tcp"
                };

                var query = ParseQuery(link.Contains('?') ? link.Substring(link.IndexOf('?')) : "");
                ApplyShadowTls(entry, query);
                return entry;
            }
            catch
            {
                return null;
            }
        }

        private static PersistedNode? ParseSsLegacy(string base64Part, string name)
        {
            var decoded = TryBase64Decode(base64Part);
            if (decoded == null) return null;

            var atIdx = decoded.LastIndexOf('@');
            if (atIdx < 0) return null;

            var userinfo  = decoded.Substring(0, atIdx);
            var hostPart  = decoded.Substring(atIdx + 1);
            var colonIdx  = userinfo.IndexOf(':');
            if (colonIdx < 0) return null;

            var method   = userinfo.Substring(0, colonIdx);
            var password = userinfo.Substring(colonIdx + 1);
            var (host, port) = SplitHostPort(hostPart);

            return new PersistedNode
            {
                Name       = name,
                Protocol   = "Shadowsocks",
                Encryption = method,
                Password   = password,
                Host       = FormatHostPort(host, port),
                Network    = "tcp"
            };
        }

        // ── VMess ─────────────────────────────────────────────────────────────

        private static PersistedNode? ParseVmess(string link)
        {
            try
            {
                var base64 = link.Substring("vmess://".Length);
                var json   = TryBase64Decode(base64);
                if (json == null) return null;

                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                string GetStr(string key, string def = "") =>
                    root.TryGetProperty(key, out var v) ? v.GetString() ?? def : def;
                int GetInt(string key, int def = 0) =>
                    root.TryGetProperty(key, out var v)
                        ? (v.ValueKind == JsonValueKind.Number ? v.GetInt32()
                           : int.TryParse(v.GetString(), out int n) ? n : def)
                        : def;

                var net      = GetStr("net", "tcp");
                var tls      = GetStr("tls", "");
                var security = tls == "tls" ? "tls" : "none";
                var allowInsecure = IsTruthy(GetStr("allowInsecure")) || IsTruthy(GetStr("insecure"));

                var host = GetStr("add");
                var port = GetInt("port");

                return new PersistedNode
                {
                    Name        = GetStr("ps"),
                    Protocol    = "VMess",
                    Host        = FormatHostPort(host, port),
                    Uuid        = GetStr("id"),
                    AlterId     = GetInt("aid"),
                    Network     = net,
                    Path        = GetStr("path"),
                    WsHost      = GetStr("host"),
                    Security    = security,
                    Sni         = GetStr("sni"),
                    Fingerprint = GetStr("fp"),
                    AllowInsecure = allowInsecure,
                    Encryption  = GetStr("scy", "auto"),
                    HeaderType  = GetStr("type", "none"),
                    Alpn        = GetStr("alpn", "")
                };
            }
            catch
            {
                return null;
            }
        }

        // ── VLESS ─────────────────────────────────────────────────────────────

        private static PersistedNode? ParseVless(string link)
        {
            try
            {
                var uri  = new Uri(link);
                var uuid = uri.UserInfo;
                var host = uri.Host;
                var port = uri.Port;
                var name = string.IsNullOrEmpty(uri.Fragment)
                    ? string.Empty
                    : Uri.UnescapeDataString(uri.Fragment.TrimStart('#'));

                var query = ParseQuery(uri.Query);

                var network  = Q(query, "type",     "tcp") ?? "tcp";
                var security = Q(query, "security", "none") ?? "none";
                var sni      = Q(query, "sni") ?? Q(query, "servername") ?? string.Empty;
                var fp       = Q(query, "fp",   string.Empty) ?? string.Empty;
                var pk       = Q(query, "pbk",  string.Empty) ?? string.Empty;
                var sid      = Q(query, "sid",  string.Empty) ?? string.Empty;
                var spx      = Q(query, "spx",  string.Empty) ?? string.Empty;
                var path     = Q(query, "path") ?? Q(query, "serviceName") ?? string.Empty;
                var wsHost   = Q(query, "host", string.Empty) ?? string.Empty;
                var flow     = Q(query, "flow", string.Empty) ?? string.Empty;
                var alpn     = Q(query, "alpn", string.Empty) ?? string.Empty;
                var headerType = Q(query, "headerType", "none") ?? "none";
                var allowInsecure = IsTruthy(Q(query, "allowInsecure")) || IsTruthy(Q(query, "insecure"));

                var entry = new PersistedNode
                {
                    Name            = name,
                    Protocol        = "VLESS",
                    Host            = FormatHostPort(host, port),
                    Uuid            = uuid,
                    Network         = network,
                    Security        = security,
                    Sni             = sni,
                    Fingerprint     = fp,
                    AllowInsecure   = allowInsecure,
                    PublicKey       = pk,
                    ShortId         = sid,
                    SpiderX         = spx,
                    Path            = path,
                    WsHost          = wsHost,
                    Flow            = flow,
                    Alpn            = alpn,
                    HeaderType      = headerType,
                    Encryption      = "none",
                    VlessEncryption = Q(query, "encryption", string.Empty) ?? string.Empty
                };

                ApplyShadowTls(entry, query);
                return entry;
            }
            catch
            {
                return null;
            }
        }

        // ── Hysteria2 ─────────────────────────────────────────────────────────

        private static PersistedNode? ParseHysteria2(string link)
        {
            try
            {
                // Normalize hy2:// -> hysteria2:// for Uri parsing
                var normalized = link.StartsWith("hy2://", StringComparison.OrdinalIgnoreCase)
                    ? "hysteria2://" + link.Substring(6)
                    : link;

                var uri      = new Uri(normalized);
                var password = Uri.UnescapeDataString(uri.UserInfo);
                var host     = uri.Host;
                var port     = uri.Port;
                var name     = string.IsNullOrEmpty(uri.Fragment)
                    ? string.Empty
                    : Uri.UnescapeDataString(uri.Fragment.TrimStart('#'));

                var query = ParseQuery(uri.Query);
                var sni   = Q(query, "sni", string.Empty) ?? string.Empty;
                var fp    = Q(query, "fp",  string.Empty) ?? string.Empty;
                var obfsType     = Q(query, "obfs") ?? "none";
                var obfsPassword = Q(query, "obfs-password") ?? "";
                var alpn  = Q(query, "alpn", string.Empty) ?? string.Empty;
                var allowInsecure = IsTruthy(Q(query, "allowInsecure")) || IsTruthy(Q(query, "insecure"));

                var entry = new PersistedNode
                {
                    Name          = name,
                    Protocol      = "Hysteria 2",
                    Host          = FormatHostPort(host, port),
                    Password      = password,
                    Network       = "udp",
                    Security      = "tls",
                    Sni           = sni,
                    Fingerprint   = fp,
                    AllowInsecure = allowInsecure,
                    Encryption    = "none",
                    ObfsType      = obfsType,
                    ObfsPassword  = obfsPassword,
                    Alpn          = alpn
                };

                ApplyShadowTls(entry, query);
                return entry;
            }
            catch
            {
                return null;
            }
        }

        // ── Trojan ────────────────────────────────────────────────────────────

        private static PersistedNode? ParseTrojan(string link)
        {
            try
            {
                var uri      = new Uri(link);
                var password = Uri.UnescapeDataString(uri.UserInfo);
                if (string.IsNullOrEmpty(password)) return null;

                var host = uri.Host;
                var port = uri.Port > 0 ? uri.Port : 443;
                var name = string.IsNullOrEmpty(uri.Fragment)
                    ? string.Empty
                    : Uri.UnescapeDataString(uri.Fragment.TrimStart('#'));

                var query = ParseQuery(uri.Query);
                var network  = NormalizeTrojanNetwork(Q(query, "type") ?? Q(query, "network") ?? "tcp");
                var security = Q(query, "security", "tls") ?? "tls";
                if (string.IsNullOrWhiteSpace(security)) security = "tls";
                security = security.ToLowerInvariant();

                var sni    = Q(query, "sni") ?? Q(query, "servername") ?? Q(query, "peer") ?? string.Empty;
                var fp     = Q(query, "fp",  string.Empty) ?? string.Empty;
                var path   = Q(query, "path") ?? Q(query, "serviceName") ?? string.Empty;
                var wsHost = Q(query, "host", string.Empty) ?? string.Empty;
                var alpn   = Q(query, "alpn", string.Empty) ?? string.Empty;
                var headerType = Q(query, "headerType", "none") ?? "none";
                var allowInsecure = IsTruthy(Q(query, "allowInsecure")) || IsTruthy(Q(query, "insecure"));

                var entry = new PersistedNode
                {
                    Name          = name,
                    Protocol      = "Trojan",
                    Host          = FormatHostPort(host, port),
                    Password      = password,
                    Network       = network,
                    Security      = security,
                    Sni           = sni,
                    Fingerprint   = fp,
                    AllowInsecure = allowInsecure,
                    Path          = path,
                    WsHost        = wsHost,
                    Alpn          = alpn,
                    HeaderType    = headerType,
                    Encryption    = "none"
                };

                ApplyShadowTls(entry, query);
                return entry;
            }
            catch
            {
                return null;
            }
        }

        // ── TUIC ──────────────────────────────────────────────────────────────
        // tuic://uuid:password@host:port?sni=&alpn=&insecure=1#name

        private static PersistedNode? ParseTuic(string link)
        {
            try
            {
                var uri  = new Uri(link);
                var name = string.IsNullOrEmpty(uri.Fragment)
                    ? string.Empty
                    : Uri.UnescapeDataString(uri.Fragment.TrimStart('#'));

                var userInfo = uri.UserInfo;
                var colonIdx = userInfo.IndexOf(':');
                string uuid, password;
                if (colonIdx >= 0)
                {
                    uuid     = Uri.UnescapeDataString(userInfo.Substring(0, colonIdx));
                    password = Uri.UnescapeDataString(userInfo.Substring(colonIdx + 1));
                }
                else
                {
                    uuid     = Uri.UnescapeDataString(userInfo);
                    password = string.Empty;
                }

                var query = ParseQuery(uri.Query);
                var sni   = Q(query, "sni") ?? Q(query, "servername") ?? string.Empty;
                var alpn  = Q(query, "alpn", string.Empty) ?? string.Empty;
                var allowInsecure = IsTruthy(Q(query, "allowInsecure")) || IsTruthy(Q(query, "insecure"));
                var fp    = Q(query, "fp", string.Empty) ?? string.Empty;

                var port = uri.Port > 0 ? uri.Port : 443;

                return new PersistedNode
                {
                    Name          = name,
                    Protocol      = "TUIC",
                    Host          = FormatHostPort(uri.Host, port),
                    Uuid          = uuid,
                    Password      = password,
                    Security      = "tls",
                    Sni           = sni,
                    Alpn          = alpn,
                    AllowInsecure = allowInsecure,
                    Fingerprint   = fp,
                    Network       = "udp"
                };
            }
            catch { return null; }
        }

        // ── Naive ─────────────────────────────────────────────────────────────
        // naive+https://username:password@host:port#name

        private static PersistedNode? ParseNaive(string link, string scheme)
        {
            try
            {
                var normalised = "https://" + link.Substring(scheme.Length);
                var uri  = new Uri(normalised);
                var name = string.IsNullOrEmpty(uri.Fragment)
                    ? string.Empty
                    : Uri.UnescapeDataString(uri.Fragment.TrimStart('#'));

                var userInfo = uri.UserInfo;
                var colonIdx = userInfo.IndexOf(':');
                string username, password;
                if (colonIdx >= 0)
                {
                    username = Uri.UnescapeDataString(userInfo.Substring(0, colonIdx));
                    password = Uri.UnescapeDataString(userInfo.Substring(colonIdx + 1));
                }
                else
                {
                    username = Uri.UnescapeDataString(userInfo);
                    password = string.Empty;
                }

                var query = ParseQuery(uri.Query);
                var sni   = Q(query, "sni") ?? Q(query, "servername") ?? string.Empty;
                var allowInsecure = IsTruthy(Q(query, "allowInsecure")) || IsTruthy(Q(query, "insecure"));

                var port = uri.Port > 0 ? uri.Port : 443;

                return new PersistedNode
                {
                    Name          = name,
                    Protocol      = "Naive",
                    Host          = FormatHostPort(uri.Host, port),
                    Username      = username,
                    Password      = password,
                    Security      = "tls",
                    Sni           = sni,
                    AllowInsecure = allowInsecure,
                    Network       = "tcp"
                };
            }
            catch { return null; }
        }

        // ── AnyTLS ────────────────────────────────────────────────────────────
        // anytls://password@host:port?sni=&insecure=1&fp=#name

        private static PersistedNode? ParseAnyTls(string link)
        {
            try
            {
                var uri      = new Uri(link);
                var password = Uri.UnescapeDataString(uri.UserInfo);
                var name     = string.IsNullOrEmpty(uri.Fragment)
                    ? string.Empty
                    : Uri.UnescapeDataString(uri.Fragment.TrimStart('#'));

                var query = ParseQuery(uri.Query);
                var sni   = Q(query, "sni") ?? Q(query, "servername") ?? string.Empty;
                var fp    = Q(query, "fp",  string.Empty) ?? string.Empty;
                var alpn  = Q(query, "alpn", string.Empty) ?? string.Empty;
                var allowInsecure = IsTruthy(Q(query, "allowInsecure")) || IsTruthy(Q(query, "insecure"));

                var port = uri.Port > 0 ? uri.Port : 443;

                return new PersistedNode
                {
                    Name          = name,
                    Protocol      = "AnyTLS",
                    Host          = FormatHostPort(uri.Host, port),
                    Password      = password,
                    Security      = "tls",
                    Sni           = sni,
                    Alpn          = alpn,
                    Fingerprint   = fp,
                    AllowInsecure = allowInsecure,
                    Network       = "tcp"
                };
            }
            catch { return null; }
        }

        // ── HTTP / HTTPS Proxy ────────────────────────────────────────────────
        // http://[user:pass@]host:port[?sni=...&insecure=1][#name]
        // https://[user:pass@]host:port[?sni=...&insecure=1][#name]  → TLS enabled

        private static PersistedNode? ParseHttp(string link)
        {
            try
            {
                var isHttps = link.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
                var uri      = new Uri(link);
                var name     = string.IsNullOrEmpty(uri.Fragment)
                    ? string.Empty
                    : Uri.UnescapeDataString(uri.Fragment.TrimStart('#'));

                var userInfo = uri.UserInfo ?? string.Empty;
                string username = string.Empty, password = string.Empty;
                var colonIdx = userInfo.IndexOf(':');
                if (colonIdx >= 0)
                {
                    username = Uri.UnescapeDataString(userInfo.Substring(0, colonIdx));
                    password = Uri.UnescapeDataString(userInfo.Substring(colonIdx + 1));
                }
                else if (!string.IsNullOrEmpty(userInfo))
                {
                    username = Uri.UnescapeDataString(userInfo);
                }

                var query        = ParseQuery(uri.Query);
                var sni          = Q(query, "sni") ?? Q(query, "servername") ?? string.Empty;
                var fp           = Q(query, "fp", string.Empty) ?? string.Empty;
                var allowInsecure = IsTruthy(Q(query, "allowInsecure")) || IsTruthy(Q(query, "insecure"));
                var port         = uri.Port > 0 ? uri.Port : (isHttps ? 443 : 8080);

                return new PersistedNode
                {
                    Name          = name,
                    Protocol      = "HTTP",
                    Host          = FormatHostPort(uri.Host, port),
                    Username      = username,
                    Password      = password,
                    Security      = isHttps ? "tls" : "none",
                    Sni           = sni,
                    Fingerprint   = fp,
                    AllowInsecure = allowInsecure,
                    Network       = "tcp"
                };
            }
            catch { return null; }
        }

        // ── WireGuard ─────────────────────────────────────────────────────────
        // wireguard://PRIVATE_KEY@host:port?publickey=...&address=...&presharedkey=...&mtu=...#name

        private static PersistedNode? ParseWireGuard(string link)
        {
            try
            {
                var uri        = new Uri(link);
                var privateKey = Uri.UnescapeDataString(uri.UserInfo);
                var name       = string.IsNullOrEmpty(uri.Fragment)
                    ? string.Empty
                    : Uri.UnescapeDataString(uri.Fragment.TrimStart('#'));

                var query        = ParseQuery(uri.Query);
                var publicKey    = Q(query, "publickey")    ?? Q(query, "public_key")    ?? Q(query, "peer")  ?? string.Empty;
                var localAddress = Q(query, "address")      ?? Q(query, "local_address") ?? "10.0.0.2/32";
                var preSharedKey = Q(query, "presharedkey") ?? Q(query, "pre_shared_key") ?? string.Empty;
                var mtuStr       = Q(query, "mtu") ?? "0";
                int.TryParse(mtuStr, out int mtu);

                var port = uri.Port > 0 ? uri.Port : 51820;

                return new PersistedNode
                {
                    Name           = name,
                    Protocol       = "WireGuard",
                    Host           = FormatHostPort(uri.Host, port),
                    WgPrivateKey   = privateKey,
                    PublicKey      = publicKey,    // peer public key reuses the existing PublicKey field
                    WgLocalAddress = localAddress,
                    WgPreSharedKey = preSharedKey,
                    WgMtu          = mtu,
                    Network        = "udp"
                };
            }
            catch { return null; }
        }

        // ── Nowhere ───────────────────────────────────────────────────────────
        // nowhere://key@host:port?spec=...&alpn=...&insecure=1#name

        private static PersistedNode? ParseNowhere(string link)
        {
            try
            {
                var uri = new Uri(link);
                var key = Uri.UnescapeDataString(uri.UserInfo);
                var name = string.IsNullOrEmpty(uri.Fragment)
                    ? string.Empty
                    : Uri.UnescapeDataString(uri.Fragment.TrimStart('#'));

                var query = ParseQuery(uri.Query);
                var spec = Q(query, "spec") ?? string.Empty;
                var alpn = Q(query, "alpn") ?? string.Empty;
                var allowInsecure = IsTruthy(Q(query, "allowInsecure")) || IsTruthy(Q(query, "insecure"));

                var port = uri.Port > 0 ? uri.Port : 11111;

                return new PersistedNode
                {
                    Name = name,
                    Protocol = "Nowhere",
                    Host = FormatHostPortPublic(uri.Host, port),
                    Password = key,
                    Spec = spec,
                    Alpn = alpn,
                    Network = "tcp",
                    Security = "tls",
                    AllowInsecure = allowInsecure
                };
            }
            catch { return null; }
        }

        // ── SOCKS5 ────────────────────────────────────────────────────────────
        // socks5://user:pass@host:port#name  (user:pass optional)

        private static PersistedNode? ParseSocks(string link)
        {
            try
            {
                var uri  = new Uri(link);
                var name = string.IsNullOrEmpty(uri.Fragment)
                    ? string.Empty
                    : Uri.UnescapeDataString(uri.Fragment.TrimStart('#'));

                var userInfo = uri.UserInfo ?? string.Empty;
                var colonIdx = userInfo.IndexOf(':');
                string username = string.Empty, password = string.Empty;
                if (colonIdx >= 0)
                {
                    username = Uri.UnescapeDataString(userInfo.Substring(0, colonIdx));
                    password = Uri.UnescapeDataString(userInfo.Substring(colonIdx + 1));
                }
                else if (!string.IsNullOrEmpty(userInfo))
                {
                    username = Uri.UnescapeDataString(userInfo);
                }

                var port = uri.Port > 0 ? uri.Port : 1080;

                return new PersistedNode
                {
                    Name     = name,
                    Protocol = "SOCKS5",
                    Host     = FormatHostPort(uri.Host, port),
                    Username = username,
                    Password = password,
                    Network  = "tcp"
                };
            }
            catch { return null; }
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static string? TryBase64Decode(string input)
        {
            try
            {
                input = input.Replace('-', '+').Replace('_', '/');
                var pad = input.Length % 4;
                if (pad == 2)      input += "==";
                else if (pad == 3) input += "=";

                var bytes = Convert.FromBase64String(input);
                return Encoding.UTF8.GetString(bytes);
            }
            catch { return null; }
        }

        private static Dictionary<string, string> ParseQuery(string query)
        {
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(query)) return d;

            var raw = query.TrimStart('?');
            foreach (var part in raw.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var eqIdx = part.IndexOf('=');
                if (eqIdx < 0)
                    d[Uri.UnescapeDataString(part)] = string.Empty;
                else
                    d[Uri.UnescapeDataString(part.Substring(0, eqIdx))] =
                        Uri.UnescapeDataString(part.Substring(eqIdx + 1));
            }
            return d;
        }

        private static string? Q(Dictionary<string, string> d, string key, string? def = null)
            => d.TryGetValue(key, out var v) ? v : def;

        private static void ApplyShadowTls(PersistedNode entry, Dictionary<string, string> query)
        {
            if (IsTruthy(Q(query, "shadow-tls")) || IsTruthy(Q(query, "shadowtls")))
            {
                entry.IsShadowTls = true;
                if (int.TryParse(Q(query, "shadow-tls-version") ?? Q(query, "shadowtls-version") ?? "3", out int v))
                    entry.ShadowTlsVersion = v;
                else
                    entry.ShadowTlsVersion = 3;

                entry.ShadowTlsPassword = Q(query, "shadow-tls-password") ?? Q(query, "shadowtls-password") ?? "";

                var stlsSni = Q(query, "shadow-tls-sni") ?? Q(query, "shadowtls-sni");
                if (!string.IsNullOrWhiteSpace(stlsSni))
                    entry.Sni = stlsSni;
            }
        }

        private static bool IsTruthy(string? value)
            => !string.IsNullOrWhiteSpace(value)
               && (value == "1"
                   || value.Equals("true", StringComparison.OrdinalIgnoreCase)
                   || value.Equals("yes",  StringComparison.OrdinalIgnoreCase));

        private static string NormalizeTrojanNetwork(string network)
        {
            if (string.IsNullOrWhiteSpace(network)) return "tcp";
            network = network.ToLowerInvariant();
            return network == "original" ? "tcp" : network;
        }

        public static (string host, int port) SplitHostPort(string hostPort)
        {
            if (hostPort.StartsWith("["))
            {
                var close = hostPort.IndexOf(']');
                var h     = hostPort.Substring(1, close - 1);
                var p     = int.Parse(hostPort.Substring(close + 2));
                return (h, p);
            }

            var idx = hostPort.LastIndexOf(':');
            if (idx < 0) return (hostPort, 0);
            return (hostPort.Substring(0, idx), int.Parse(hostPort.Substring(idx + 1)));
        }

        /// <summary>
        /// Formats host:port, wrapping IPv6 addresses in brackets so the
        /// result can be round-tripped through SplitHostPort.
        /// </summary>
        private static string FormatHostPort(string host, int port)
            => FormatHostPortPublic(host, port);

        /// <summary>
        /// 格式化 host:port，IPv6 地址自动加方括号。供外部调用。
        /// </summary>
        public static string FormatHostPortPublic(string host, int port)
        {
            return host.Contains(':') ? $"[{host}]:{port}" : $"{host}:{port}";
        }
    }
}
