using System;
using System.Collections.Generic;

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
        public int     SnellVersion { get; set; }      // Snell protocol version: 1-6 (default 4)
        public string? SnellMode { get; set; }         // Snell v6 traffic shaping mode (default, unshaped, unsafe-raw)

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
}
