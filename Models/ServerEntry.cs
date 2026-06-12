using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AnywhereWinUI.Models
{
    /// <summary>
    /// MVVM-observable server entry used by all ViewModels.
    /// Loaded from / saved back to PersistedNode via NodeEntryMapper.
    /// </summary>
    public partial class ServerEntry : ObservableObject
    {
        private string _id = System.Guid.NewGuid().ToString("N");

        public ServerEntry()
        {
            SubscriptionId     = string.Empty;
            Name               = string.Empty;
            Host               = string.Empty;
            Protocol           = string.Empty;
            Encryption         = string.Empty;
            Username           = string.Empty;
            Password           = string.Empty;
            Uuid               = string.Empty;
            Network            = "tcp";
            Path               = string.Empty;
            WsHost             = string.Empty;
            Security           = string.Empty;
            Sni                = string.Empty;
            Fingerprint        = string.Empty;
            PublicKey          = string.Empty;
            ShortId            = string.Empty;
            SpiderX            = string.Empty;
            Flow               = string.Empty;
            Alpn               = string.Empty;
            HeaderType         = "none";
            ObfsType           = "none";
            ObfsPassword       = string.Empty;
            ShadowTlsPassword  = string.Empty;
            ProxyChainId       = string.Empty;
            VlessEncryption    = string.Empty;
            WgPrivateKey       = string.Empty;
            WgLocalAddress     = string.Empty;
            WgPreSharedKey     = string.Empty;
        }

        /// <summary>ID of the subscription this node was imported from; empty = manually added.</summary>
        [ObservableProperty] public partial string SubscriptionId { get; set; }

        [ObservableProperty] public partial string Name { get; set; }
        [ObservableProperty] public partial string Host { get; set; }
        [ObservableProperty] public partial int Port { get; set; }

        /// <summary>ss | vmess | vless | hysteria2 | trojan | naive | tuic | anytls | socks</summary>
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(DisplayProtocol))]
        public partial string Protocol { get; set; }

        /// <summary>Cipher for ss; "TLS" or "Reality" label for vless/vmess/hysteria2</summary>
        [ObservableProperty] public partial string Encryption { get; set; }

        [ObservableProperty]
        [JsonIgnore]
        public partial bool IsActive { get; set; }

        [ObservableProperty] public partial bool IsFavorite { get; set; }

        // Auth
        [ObservableProperty] public partial string Username { get; set; }
        [ObservableProperty] public partial string Password { get; set; }
        [ObservableProperty] public partial string Uuid { get; set; }

        // Transport
        [ObservableProperty] public partial string Network { get; set; }
        [ObservableProperty] public partial string Path { get; set; }
        [ObservableProperty] public partial string WsHost { get; set; }
        [ObservableProperty] public partial int AlterId { get; set; }

        // TLS / Security
        [ObservableProperty] public partial string Security { get; set; }
        [ObservableProperty] public partial string Sni { get; set; }
        [ObservableProperty] public partial string Fingerprint { get; set; }
        [ObservableProperty] public partial bool AllowInsecure { get; set; }

        // VLESS Reality
        [ObservableProperty] public partial string PublicKey { get; set; }
        [ObservableProperty] public partial string ShortId { get; set; }
        [ObservableProperty] public partial string SpiderX { get; set; }

        /// <summary>VLESS flow: "xtls-rprx-vision" or empty string.</summary>
        [ObservableProperty] public partial string Flow { get; set; }

        [ObservableProperty] public partial string Spec { get; set; }


        [ObservableProperty] public partial string VlessEncryption { get; set; }
        [ObservableProperty] public partial string Alpn { get; set; }
        [ObservableProperty] public partial string HeaderType { get; set; }

        // Hysteria2 obfuscation
        [ObservableProperty] public partial string ObfsType { get; set; }
        [ObservableProperty] public partial string ObfsPassword { get; set; }

        // ShadowTLS
        [ObservableProperty] public partial bool IsShadowTls { get; set; }
        [ObservableProperty] public partial int ShadowTlsVersion { get; set; }
        [ObservableProperty] public partial string ShadowTlsPassword { get; set; }

        // WireGuard
        [ObservableProperty] public partial string WgPrivateKey { get; set; }
        [ObservableProperty] public partial string WgLocalAddress { get; set; }
        [ObservableProperty] public partial string WgPreSharedKey { get; set; }
        [ObservableProperty] public partial int WgMtu { get; set; }

        // Snell
        /// <summary>Snell protocol version: 1, 2, 3, 4, or 5. Default 4.</summary>
        [ObservableProperty] public partial int SnellVersion { get; set; }

        /// <summary>前置代理节点的 Id。空 = 直连（不使用代理链）。</summary>
        [ObservableProperty] public partial string ProxyChainId { get; set; }

        [ObservableProperty]
        [JsonIgnore]
        [NotifyPropertyChangedFor(nameof(LatencyDisplay))]
        public partial string LatencyText { get; set; } = "NotTested";

        public string Id
        {
            get => _id;
            set => SetProperty(ref _id, string.IsNullOrWhiteSpace(value) ? System.Guid.NewGuid().ToString("N") : value);
        }

        public string DisplayProtocol => Protocol.ToLowerInvariant() switch
        {
            "ss"         => "Shadowsocks",
            "vmess"      => "VMess",
            "vless"      => "VLESS",
            "hysteria2"  => "Hysteria 2",
            "trojan"     => "Trojan",
            "naive"      => "NaiveProxy",
            "tuic"       => "TUIC",
            "anytls"     => "AnyTLS",
            "socks"      => "SOCKS",
            "http"       => "HTTP",
            "wireguard"  => "WireGuard",
            "snell"      => "Snell",
            "nowhere"    => "Nowhere",
            _            => Protocol ?? string.Empty
        };

        [JsonIgnore]
        public string LatencyDisplay => string.IsNullOrEmpty(LatencyText) || LatencyText == "NotTested"
            ? string.Empty : LatencyText;

        public void RefreshProtocolColor() => OnPropertyChanged(nameof(Protocol));
    }
}
