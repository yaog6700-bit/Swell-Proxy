using AnywhereWinUI.Models;
using AnywhereWinUI.Services;

namespace AnywhereWinUI.Helpers
{
    /// <summary>
    /// Converts between the persistence model (PersistedNode) and the MVVM model (ServerEntry).
    /// </summary>
    public static class NodeEntryMapper
    {
        public static ServerEntry ToServerEntry(PersistedNode node)
        {
            // Parse host:port from PersistedNode.Host
            string host = node.Host ?? string.Empty;
            int port = 443;
            var colonIdx = host.LastIndexOf(':');
            if (colonIdx > 0 && int.TryParse(host.AsSpan(colonIdx + 1), out int p))
            {
                port = p;
                host = host[..colonIdx];
            }

            return new ServerEntry
            {
                Id               = node.Id,
                SubscriptionId   = node.SubscriptionId ?? string.Empty,
                Name             = node.Name ?? string.Empty,
                Protocol         = NormalizeProtocol(node.Protocol),
                Host             = host,
                Port             = port,
                Uuid             = node.Uuid ?? string.Empty,
                Password         = node.Password ?? string.Empty,
                Username         = node.Username ?? string.Empty,
                Encryption       = node.Encryption ?? string.Empty,
                VlessEncryption  = node.VlessEncryption ?? string.Empty,
                Network          = node.Network ?? "tcp",
                Path             = node.Path ?? string.Empty,
                WsHost           = node.WsHost ?? string.Empty,
                HeaderType       = node.HeaderType ?? "none",
                Alpn             = node.Alpn ?? string.Empty,
                AlterId          = node.AlterId,
                Security         = node.Security ?? string.Empty,
                Sni              = node.Sni ?? string.Empty,
                Fingerprint      = node.Fingerprint ?? string.Empty,
                AllowInsecure    = node.AllowInsecure,
                PublicKey        = node.PublicKey ?? string.Empty,
                ShortId          = node.ShortId ?? string.Empty,
                SpiderX          = node.SpiderX ?? string.Empty,
                Flow             = node.Flow ?? string.Empty,
                ObfsType         = node.ObfsType ?? "none",
                ObfsPassword     = node.ObfsPassword ?? string.Empty,
                IsShadowTls      = node.IsShadowTls,
                ShadowTlsVersion = node.ShadowTlsVersion == 0 ? 3 : node.ShadowTlsVersion,
                ShadowTlsPassword = node.ShadowTlsPassword ?? string.Empty,
                WgPrivateKey     = node.WgPrivateKey ?? string.Empty,
                WgLocalAddress   = node.WgLocalAddress ?? string.Empty,
                WgPreSharedKey   = node.WgPreSharedKey ?? string.Empty,
                WgMtu            = node.WgMtu,
                ProxyChainId     = node.ProxyChainId ?? string.Empty,
                IsFavorite       = node.IsFavorite,
            };
        }

        public static PersistedNode ToPersistedNode(ServerEntry entry)
        {
            return new PersistedNode
            {
                Id               = entry.Id,
                SubscriptionId   = entry.SubscriptionId,
                Name             = entry.Name,
                Protocol         = entry.Protocol,
                Host             = $"{entry.Host}:{entry.Port}",
                Uuid             = entry.Uuid,
                Password         = entry.Password,
                Username         = entry.Username,
                Encryption       = entry.Encryption,
                VlessEncryption  = entry.VlessEncryption,
                Network          = entry.Network,
                Path             = entry.Path,
                WsHost           = entry.WsHost,
                HeaderType       = entry.HeaderType,
                Alpn             = entry.Alpn,
                AlterId          = entry.AlterId,
                Security         = entry.Security,
                Sni              = entry.Sni,
                Fingerprint      = entry.Fingerprint,
                AllowInsecure    = entry.AllowInsecure,
                PublicKey        = entry.PublicKey,
                ShortId          = entry.ShortId,
                SpiderX          = entry.SpiderX,
                Flow             = entry.Flow,
                ObfsType         = entry.ObfsType,
                ObfsPassword     = entry.ObfsPassword,
                IsShadowTls      = entry.IsShadowTls,
                ShadowTlsVersion = entry.ShadowTlsVersion,
                ShadowTlsPassword = entry.ShadowTlsPassword,
                WgPrivateKey     = entry.WgPrivateKey,
                WgLocalAddress   = entry.WgLocalAddress,
                WgPreSharedKey   = entry.WgPreSharedKey,
                WgMtu            = entry.WgMtu,
                ProxyChainId     = entry.ProxyChainId,
                IsFavorite       = entry.IsFavorite,
            };
        }

        /// <summary>
        /// Normalizes display-name protocols (e.g. "Shadowsocks", "Hysteria 2") to internal
        /// lowercase keys (e.g. "ss", "hysteria2") used by ViewModels and config builders.
        /// </summary>
        private static string NormalizeProtocol(string? proto)
        {
            return (proto ?? string.Empty).ToLowerInvariant() switch
            {
                "shadowsocks"          => "ss",
                "hysteria 2"           => "hysteria2",
                "hysteria2"            => "hysteria2",
                "naiveproxy"           => "naive",
                "naive"                => "naive",
                "vmess"                => "vmess",
                "vless"                => "vless",
                "trojan"               => "trojan",
                "tuic"                 => "tuic",
                "anytls"               => "anytls",
                "socks"                => "socks",
                "socks5"               => "socks",
                "ss"                   => "ss",
                "http"                 => "http",
                "https"                => "http",
                "wireguard"            => "wireguard",
                "wg"                   => "wireguard",
                var x                  => x
            };
        }
    }
}
