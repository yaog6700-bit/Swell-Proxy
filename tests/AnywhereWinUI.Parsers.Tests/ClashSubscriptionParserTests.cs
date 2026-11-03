using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AnywhereWinUI.Services;
using Xunit;

namespace AnywhereWinUI.Parsers.Tests
{
    /// <summary>
    /// Covers the fixtures described in section 5 of the Clash subscription plan:
    /// A (block style), B (flow style + anchors) and C1..C4 (must not be misdetected).
    /// </summary>
    public class ClashSubscriptionParserTests
    {
        private const string SubA = "sub-a";
        private const string SubB = "sub-b";

        private static string ReadFixture(string fileName)
            => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "clash", fileName));

        private static List<PersistedNode> ParseFixtureA()
            => ClashSubscriptionParser.Parse(ReadFixture("A-clash-meta-block.yaml"), SubA);

        private static PersistedNode Node(IReadOnlyCollection<PersistedNode> nodes, string name)
        {
            var matches = nodes.Where(n => n.Name == name).ToList();
            Assert.True(matches.Count == 1, $"expected exactly one node named '{name}', found {matches.Count}");
            return matches[0];
        }

        // ── Fixture A: block style ────────────────────────────────────────────

        [Fact]
        public void FixtureA_ImportsSupportedNodesAndSkipsTheRest()
        {
            var nodes = ParseFixtureA();

            // 17 proxy entries: 14 importable, 3 rejected (ss+obfs, tuic v4, ssr).
            Assert.Equal(14, nodes.Count);
            Assert.All(nodes, n => Assert.Equal(SubA, n.SubscriptionId));
            Assert.All(nodes, n => Assert.False(string.IsNullOrWhiteSpace(n.Protocol)));
        }

        [Fact]
        public void FixtureA_SkipsUnsupportedTypes()
        {
            var names = ParseFixtureA().Select(n => n.Name).ToList();

            Assert.DoesNotContain("HK-SS-obfs-应跳过", names);
            Assert.DoesNotContain("DE-TUIC-v4-应跳过", names);
            Assert.DoesNotContain("SSR-应跳过", names);
        }

        [Fact]
        public void FixtureA_IgnoresProxyGroupsAndRules()
        {
            var names = ParseFixtureA().Select(n => n.Name).ToList();

            Assert.DoesNotContain("PROXY", names);
            Assert.DoesNotContain("reject", names);
        }

        [Fact]
        public void FixtureA_ShadowsocksNode()
        {
            var node = Node(ParseFixtureA(), "HK-SS-01");

            Assert.Equal("Shadowsocks", node.Protocol);
            Assert.Equal("hk1.example.com:8388", node.Host);
            Assert.Equal("aes-256-gcm", node.Encryption);
            Assert.Equal("pass-ss", node.Password);
            Assert.Equal("tcp", node.Network);
            Assert.False(node.IsShadowTls);
        }

        [Fact]
        public void FixtureA_ShadowsocksShadowTlsPlugin()
        {
            var node = Node(ParseFixtureA(), "HK-SS-ShadowTLS");

            Assert.Equal("Shadowsocks", node.Protocol);
            Assert.True(node.IsShadowTls);
            Assert.Equal(3, node.ShadowTlsVersion);
            Assert.Equal("stls-pass", node.ShadowTlsPassword);
            Assert.Equal("cloud.tencent.com", node.Sni);
            Assert.Equal("2022-blake3-aes-128-gcm", node.Encryption);
        }

        [Fact]
        public void FixtureA_VmessNodeWithQuotedPortAndWebSocketTransport()
        {
            var node = Node(ParseFixtureA(), "JP-VMess-WS");

            Assert.Equal("VMess", node.Protocol);
            Assert.Equal("jp1.example.com:443", node.Host); // port was the string "443"
            Assert.Equal("11111111-2222-3333-4444-555555555555", node.Uuid);
            Assert.Equal(0, node.AlterId);
            Assert.Equal("auto", node.Encryption);
            Assert.Equal("none", node.HeaderType);
            Assert.Equal("tls", node.Security);
            Assert.Equal("cdn.example.com", node.Sni);
            Assert.False(node.AllowInsecure);
            Assert.Equal("ws", node.Network);
            Assert.Equal("/ws", node.Path);
            Assert.Equal("cdn.example.com", node.WsHost);
        }

        [Fact]
        public void FixtureA_VlessRealityNode()
        {
            var node = Node(ParseFixtureA(), "US-VLESS-Reality");

            Assert.Equal("VLESS", node.Protocol);
            Assert.Equal("us1.example.com:443", node.Host);
            Assert.Equal("66666666-7777-8888-9999-000000000000", node.Uuid);
            Assert.Equal("xtls-rprx-vision", node.Flow);
            Assert.Equal("none", node.Encryption);
            Assert.Equal("reality", node.Security);
            Assert.Equal("www.microsoft.com", node.Sni);
            Assert.Equal("chrome", node.Fingerprint);
            Assert.Equal("pbk_base64_placeholder", node.PublicKey);
            Assert.Equal("0123abcd", node.ShortId);
            Assert.Equal("tcp", node.Network);
        }

        [Fact]
        public void FixtureA_VlessGrpcNode()
        {
            var node = Node(ParseFixtureA(), "SG-VLESS-gRPC");

            Assert.Equal("VLESS", node.Protocol);
            Assert.Equal("grpc", node.Network);
            Assert.Equal("grpcsvc", node.Path);
            Assert.Equal("sg1.example.com", node.Sni);
            Assert.Equal("tls", node.Security);
        }

        [Fact]
        public void FixtureA_TrojanNode()
        {
            var node = Node(ParseFixtureA(), "TW-Trojan");

            Assert.Equal("Trojan", node.Protocol);
            Assert.Equal("tw1.example.com:443", node.Host);
            Assert.Equal("trojan-pass", node.Password);
            Assert.Equal("tls", node.Security);
            Assert.Equal("tw1.example.com", node.Sni);
            Assert.True(node.AllowInsecure);
            Assert.Equal("h2,http/1.1", node.Alpn);
        }

        [Fact]
        public void FixtureA_Hysteria2Node()
        {
            var node = Node(ParseFixtureA(), "KR-Hy2");

            Assert.Equal("Hysteria 2", node.Protocol);
            Assert.Equal("kr1.example.com:8443", node.Host);
            Assert.Equal("hy2-pass", node.Password);
            Assert.Equal("tls", node.Security);
            Assert.Equal("salamander", node.ObfsType);
            Assert.Equal("obfs-pass", node.ObfsPassword);
            Assert.Equal("udp", node.Network);
        }

        [Fact]
        public void FixtureA_Hysteria2PortHoppingDegradesToTheLowerBound()
        {
            var node = Node(ParseFixtureA(), "KR-Hy2-ports-降级");

            Assert.Equal("Hysteria 2", node.Protocol);
            Assert.Equal("kr2.example.com:20000", node.Host);
        }

        [Fact]
        public void FixtureA_TuicNode()
        {
            var node = Node(ParseFixtureA(), "DE-TUIC");

            Assert.Equal("TUIC", node.Protocol);
            Assert.Equal("12345678-1234-1234-1234-123456789abc", node.Uuid);
            Assert.Equal("tuic-pass", node.Password);
            Assert.Equal("tls", node.Security);
            Assert.Equal("h3", node.Alpn);
            Assert.Equal("udp", node.Network);
        }

        [Fact]
        public void FixtureA_WireGuardNode()
        {
            var node = Node(ParseFixtureA(), "WG-Home");

            Assert.Equal("WireGuard", node.Protocol);
            Assert.Equal("wg.example.com:51820", node.Host);
            Assert.Equal("cHJpdmF0ZS1rZXktcGxhY2Vob2xkZXI=", node.WgPrivateKey);
            Assert.Equal("cHVibGljLWtleS1wbGFjZWhvbGRlcg==", node.PublicKey);
            Assert.Equal("cHNrLXBsYWNlaG9sZGVy", node.WgPreSharedKey);
            Assert.Equal("10.0.0.2/32,fd00::2/128", node.WgLocalAddress);
            Assert.Equal(1280, node.WgMtu);
            Assert.Equal("udp", node.Network);
        }

        [Fact]
        public void FixtureA_AnyTlsNode()
        {
            var node = Node(ParseFixtureA(), "FR-AnyTLS");

            Assert.Equal("AnyTLS", node.Protocol);
            Assert.Equal("anytls-pass", node.Password);
            Assert.Equal("tls", node.Security);
            Assert.Equal("fr1.example.com", node.Sni);
            Assert.Equal("chrome", node.Fingerprint);
        }

        [Fact]
        public void FixtureA_SnellNode()
        {
            var node = Node(ParseFixtureA(), "HK-Snell");

            Assert.Equal("snell", node.Protocol); // lower-case, as produced by NodeLinkParser
            Assert.Equal("hk4.example.com:44046", node.Host);
            Assert.Equal("snell-psk", node.Password);
            Assert.Equal(4, node.SnellVersion);
            Assert.Equal("tls", node.ObfsType);
            Assert.Equal("bing.com", node.WsHost);
        }

        [Fact]
        public void FixtureA_Socks5Node()
        {
            var node = Node(ParseFixtureA(), "Local-SOCKS5");

            Assert.Equal("SOCKS5", node.Protocol);
            Assert.Equal("127.0.0.1:1080", node.Host);
            Assert.Equal("u", node.Username);
            Assert.Equal("p", node.Password);
        }

        [Fact]
        public void FixtureA_HttpOverTlsNode()
        {
            var node = Node(ParseFixtureA(), "Local-HTTP-TLS");

            Assert.Equal("HTTP", node.Protocol);
            Assert.Equal("proxy.example.com:8443", node.Host);
            Assert.Equal("tls", node.Security);
            Assert.Equal("proxy.example.com", node.Sni);
            Assert.Equal("u", node.Username);
        }

        // ── Fixture B: flow style + anchors ───────────────────────────────────

        [Fact]
        public void FixtureB_ImportsFlowStyleAndMergedAnchorNodes()
        {
            var nodes = ClashSubscriptionParser.Parse(ReadFixture("B-clash-flow-anchors.yaml"), SubB);

            Assert.Equal(4, nodes.Count);
            Assert.Equal(
                new[] { "A-ss", "B-vmess", "B-vmess-copy", "C-trojan" },
                nodes.Select(n => n.Name).ToArray());
        }

        [Fact]
        public void FixtureB_MergedAnchorNodeInheritsAndOverridesFields()
        {
            var nodes = ClashSubscriptionParser.Parse(ReadFixture("B-clash-flow-anchors.yaml"), SubB);
            var copy = Node(nodes, "B-vmess-copy");

            Assert.Equal("VMess", copy.Protocol);
            Assert.Equal("b2.example.com:443", copy.Host); // overridden by the merge-key entry
            Assert.Equal("11111111-2222-3333-4444-555555555555", copy.Uuid); // inherited from the anchor
            Assert.Equal("ws", copy.Network);
            Assert.Equal("/path", copy.Path);
            Assert.Equal("b.example.com", copy.WsHost);
            Assert.Equal("tls", copy.Security);
        }

        [Fact]
        public void FixtureB_TrojanAndShadowsocksNodes()
        {
            var nodes = ClashSubscriptionParser.Parse(ReadFixture("B-clash-flow-anchors.yaml"), SubB);

            var ss = Node(nodes, "A-ss");
            Assert.Equal("Shadowsocks", ss.Protocol);
            Assert.Equal("a.example.com:443", ss.Host);
            Assert.Equal("chacha20-ietf-poly1305", ss.Encryption);

            var trojan = Node(nodes, "C-trojan");
            Assert.Equal("Trojan", trojan.Protocol);
            Assert.Equal("c.example.com:443", trojan.Host);
            Assert.Equal("tls", trojan.Security);
        }

        // ── Fixture C: detection must not misfire ─────────────────────────────

        [Fact]
        public void LooksLikeClashYaml_AcceptsClashFixtures()
        {
            Assert.True(ClashSubscriptionParser.LooksLikeClashYaml(ReadFixture("A-clash-meta-block.yaml")));
            Assert.True(ClashSubscriptionParser.LooksLikeClashYaml(ReadFixture("B-clash-flow-anchors.yaml")));
        }

        [Fact]
        public void LooksLikeClashYaml_RejectsBase64Subscription()
        {
            var content = ReadFixture("C1-base64-subscription.txt");

            Assert.False(ClashSubscriptionParser.LooksLikeClashYaml(content));
            Assert.Empty(ClashSubscriptionParser.Parse(content, SubA));
        }

        [Fact]
        public void LooksLikeClashYaml_RejectsSingboxJson()
        {
            var content = ReadFixture("C2-singbox.json");

            Assert.False(ClashSubscriptionParser.LooksLikeClashYaml(content));
            Assert.Empty(ClashSubscriptionParser.Parse(content, SubA));
        }

        [Fact]
        public void LooksLikeClashYaml_RejectsShareLinkTextMentioningProxies()
        {
            var content = ReadFixture("C3-sharelinks-with-proxies-word.txt");

            // A node name may contain "proxies:", but the regex only matches a line that starts
            // with the key itself.
            Assert.False(ClashSubscriptionParser.LooksLikeClashYaml(content));
            Assert.Empty(ClashSubscriptionParser.Parse(content, SubA));
        }

        [Fact]
        public void LooksLikeClashYaml_RejectsProxyProvidersOnlyConfig()
        {
            var content = ReadFixture("C4-proxy-providers-only.yaml");

            Assert.False(ClashSubscriptionParser.LooksLikeClashYaml(content));
            Assert.Empty(ClashSubscriptionParser.Parse(content, SubA));
        }

        // ── Robustness ────────────────────────────────────────────────────────

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("not yaml at all")]
        [InlineData("proxies:")]
        [InlineData("proxies: []")]
        [InlineData("[1,2,3]")]
        public void Parse_NeverThrowsOnDegenerateInput(string yaml)
        {
            var nodes = ClashSubscriptionParser.Parse(yaml, SubA);

            Assert.NotNull(nodes);
        }

        [Fact]
        public void Parse_RecoversWhenTheDocumentHasDuplicateKeys()
        {
            const string yaml = @"proxies:
  - {name: ""X-ss"", type: ss, server: x.example.com, port: 443, cipher: aes-256-gcm, password: pw}
dns:
  nameserver: [1.1.1.1]
  nameserver: [8.8.8.8]
";
            var nodes = ClashSubscriptionParser.Parse(yaml, SubA);

            Assert.Equal("x.example.com:443", Assert.Single(nodes).Host);
        }

        [Fact]
        public void Parse_TabIndentedDocumentDoesNotThrow()
        {
            const string yaml = "proxies:\n\t- {name: tabbed, type: ss, server: tab.example.com, port: 443, cipher: aes-256-gcm, password: pw}\n";

            var nodes = ClashSubscriptionParser.Parse(yaml, SubA);

            Assert.NotNull(nodes);
        }

        [Theory]
        [InlineData("port: 70000")]
        [InlineData("port: 0")]
        [InlineData("port: -1")]
        public void Parse_SkipsOutOfRangePorts(string portLine)
        {
            string yaml = $"proxies:\n  - {{name: bad, type: ss, server: bad.example.com, {portLine}, cipher: aes-256-gcm, password: pw}}\n";

            Assert.Empty(ClashSubscriptionParser.Parse(yaml, SubA));
        }

        [Fact]
        public void Parse_SkipsEntriesWithoutServer()
        {
            const string yaml = @"proxies:
  - {name: no-server, type: ss, port: 443, cipher: aes-256-gcm, password: pw}
  - {name: ok, type: ss, server: ok.example.com, port: 443, cipher: aes-256-gcm, password: pw}
";
            var nodes = ClashSubscriptionParser.Parse(yaml, SubA);

            Assert.Equal("ok.example.com:443", Assert.Single(nodes).Host);
        }

        [Fact]
        public void Parse_ToleratesQuotedScalars()
        {
            const string yaml = @"proxies:
  - name: quoted
    type: vmess
    server: q.example.com
    port: ""443""
    uuid: 11111111-2222-3333-4444-555555555555
    alterId: ""0""
    cipher: auto
    tls: ""true""
    servername: q.example.com
    network: ws
    ws-opts:
      path: ""/quoted""
      headers:
        Host: q.example.com
";
            var node = Assert.Single(ClashSubscriptionParser.Parse(yaml, SubA));

            Assert.Equal("q.example.com:443", node.Host);
            Assert.Equal(0, node.AlterId);
            Assert.Equal("tls", node.Security);
            Assert.Equal("/quoted", node.Path);
            Assert.Equal("q.example.com", node.WsHost);
        }

        [Fact]
        public void Parse_LegacyWsPathAndHeaderKeysAreStillRead()
        {
            const string yaml = @"proxies:
  - name: legacy-ws
    type: vmess
    server: legacy.example.com
    port: 443
    uuid: 11111111-2222-3333-4444-555555555555
    network: ws
    ws-path: /legacy
    ws-headers:
      Host: legacy.example.com
";
            var node = Assert.Single(ClashSubscriptionParser.Parse(yaml, SubA));

            Assert.Equal("/legacy", node.Path);
            Assert.Equal("legacy.example.com", node.WsHost);
        }

        [Fact]
        public void Parse_HttpNetworkCamouflageDegradesToTcpWithHeaderType()
        {
            const string yaml = @"proxies:
  - {name: http-camouflage, type: vmess, server: camo.example.com, port: 80, uuid: 11111111-2222-3333-4444-555555555555, network: http}
";
            var node = Assert.Single(ClashSubscriptionParser.Parse(yaml, SubA));

            Assert.Equal("tcp", node.Network);
            Assert.Equal("http", node.HeaderType);
        }

        [Fact]
        public void Parse_UppercaseKeysAndHttpUpgradeAreSupported()
        {
            const string yaml = @"proxies:
  - name: upper
    Type: vmess
    Server: upper.example.com
    Port: 443
    UUID: 11111111-2222-3333-4444-555555555555
    Network: httpupgrade
";
            var node = Assert.Single(ClashSubscriptionParser.Parse(yaml, SubA));

            Assert.Equal("VMess", node.Protocol);
            Assert.Equal("httpupgrade", node.Network);
        }

        [Fact]
        public void Parse_UnknownAndUnsupportedTypesAreSkipped()
        {
            const string yaml = @"proxies:
  - {name: h1, type: hysteria, server: h1.example.com, port: 443, auth: x}
  - {name: ssr, type: ssr, server: s.example.com, port: 443, cipher: aes-256-cfb, password: x}
  - {name: relay, type: relay, server: r.example.com, port: 443}
  - {name: tls-socks, type: socks5, server: s5.example.com, port: 1080, tls: true}
  - {name: tuic-v4, type: tuic, server: t4.example.com, port: 443, token: legacy}
  - {name: obfs-ss, type: ss, server: o.example.com, port: 443, cipher: aes-256-gcm, password: x, plugin: v2ray-plugin}
  - {name: keep, type: ss, server: keep.example.com, port: 443, cipher: aes-256-gcm, password: x}
";
            var nodes = ClashSubscriptionParser.Parse(yaml, SubA);

            Assert.Equal("keep.example.com:443", Assert.Single(nodes).Host);
        }

        [Fact]
        public void Parse_NameFallsBackToHost()
        {
            const string yaml = @"proxies:
  - {type: ss, server: noname.example.com, port: 443, cipher: aes-256-gcm, password: x}
";
            var node = Assert.Single(ClashSubscriptionParser.Parse(yaml, SubA));

            Assert.Equal("noname.example.com:443", node.Name);
        }

        [Fact]
        public void Parse_WireGuardPeerBlockIsUsedWhenThePeerHoldsTheEndpoint()
        {
            const string yaml = @"proxies:
  - name: wg-peered
    type: wireguard
    ip: 10.8.0.2
    private-key: ""cHJpdmF0ZS1rZXktcGxhY2Vob2xkZXI=""
    mtu: 1420
    peers:
      - server: peer.example.com
        port: 51820
        public-key: ""cHVibGljLWtleS1wbGFjZWhvbGRlcg==""
        pre-shared-key: ""cHNrLXBsYWNlaG9sZGVy""
";
            var node = Assert.Single(ClashSubscriptionParser.Parse(yaml, SubA));

            Assert.Equal("peer.example.com:51820", node.Host);
            Assert.Equal("cHVibGljLWtleS1wbGFjZWhvbGRlcg==", node.PublicKey);
            Assert.Equal("cHNrLXBsYWNlaG9sZGVy", node.WgPreSharedKey);
            Assert.Equal("10.8.0.2/32", node.WgLocalAddress);
            Assert.Equal(1420, node.WgMtu);
        }

        [Fact]
        public void Parse_AlpnAcceptsAScalarValue()
        {
            const string yaml = @"proxies:
  - {name: scalar-alpn, type: trojan, server: a.example.com, port: 443, password: p, alpn: ""h2,http/1.1""}
";
            var node = Assert.Single(ClashSubscriptionParser.Parse(yaml, SubA));

            Assert.Equal("h2,http/1.1", node.Alpn);
        }
    }
}
