using System.IO;
using AnywhereWinUI.Services;
using Xunit;
using YamlDotNet.RepresentationModel;

namespace AnywhereWinUI.Parsers.Tests
{
    public class ShoesYamlSerializerTests
    {
        private static YamlMappingNode ParseFirstSequenceItem(string yamlFragment)
        {
            var stream = new YamlStream();
            using var reader = new StringReader(yamlFragment);
            stream.Load(reader);
            var seq = Assert.IsType<YamlSequenceNode>(stream.Documents[0].RootNode);
            return Assert.IsType<YamlMappingNode>(seq.Children[0]);
        }

        [Fact]
        public void VlessTls_ValidUuidV4_GeneratesValidShoesNestedProtocolYaml()
        {
            var node = new PersistedNode
            {
                Name = "HK #1 \"TLS\" [IPv6]",
                Protocol = "VLESS",
                Host = "[2001:db8::1]:443",
                Uuid = "8768c449-f7c7-4cc6-ac53-2d9869ee98f8",
                Security = "tls",
                Network = "tcp",
                Sni = "www.microsoft.com"
            };

            bool ok = ShoesYamlSerializer.TryBuildClientProxyYaml(
                node, node.Name, indent: 0, out var yaml, out var kind, out _);

            Assert.True(ok);
            Assert.Equal(ShoesYamlSerializer.RejectKind.None, kind);

            var root = ParseFirstSequenceItem(yaml);
            Assert.Equal("HK #1 \"TLS\" [IPv6]", root.Children["name"].ToString());
            Assert.Equal("[2001:db8::1]:443", root.Children["address"].ToString());

            var proto = Assert.IsType<YamlMappingNode>(root.Children["protocol"]);
            Assert.Equal("tls", proto.Children["type"].ToString());
            Assert.Equal("www.microsoft.com", proto.Children["sni_hostname"].ToString());

            var inner = Assert.IsType<YamlMappingNode>(proto.Children["protocol"]);
            Assert.Equal("vless", inner.Children["type"].ToString());
            Assert.Equal("8768c449-f7c7-4cc6-ac53-2d9869ee98f8", inner.Children["user_id"].ToString());
        }

        [Fact]
        public void VlessRealityVision_RejectedForSingboxUtlsFallback()
        {
            var node = new PersistedNode
            {
                Name = "HK Reality Vision",
                Protocol = "VLESS",
                Host = "pzhkt.example.com:52473",
                Uuid = "8768c449-f7c7-4cc6-ac53-2d9869ee98f8",
                Security = "reality",
                Network = "tcp",
                Flow = "xtls-rprx-vision",
                Fingerprint = "chrome",
                Sni = "blog.api.www.cloudflare.com",
                PublicKey = "OKMwndkiAlluBrTUU3IM2DUIUpgvGCm3F12h2KbEC0E",
                ShortId = "3b"
            };

            bool ok = ShoesYamlSerializer.TryBuildClientProxyYaml(
                node, node.Name, indent: 0, out _, out var kind, out var detail);

            Assert.False(ok);
            Assert.Equal(ShoesYamlSerializer.RejectKind.UnsupportedProtocol, kind);
            Assert.Contains("Reality", detail);
        }

        [Theory]
        [InlineData("VLESS", "eab701c8-afc2-380e-b9c5-f0dd2df114e4")]
        [InlineData("VMess", "eab701c8-afc2-380e-b9c5-f0dd2df114e4")]
        public void NonVersion4Uuid_RejectedForSingboxFallback(string protocol, string uuidV3)
        {
            var node = new PersistedNode
            {
                Name = "Airport UUIDv3 Node",
                Protocol = protocol,
                Host = "g-002.example.com:41593",
                Uuid = uuidV3,
                Security = "tls",
                Network = "tcp",
                Sni = "bgpvless.example.com"
            };

            bool ok = ShoesYamlSerializer.TryBuildClientProxyYaml(
                node, node.Name, indent: 0, out _, out var kind, out var detail);

            Assert.False(ok);
            Assert.Equal(ShoesYamlSerializer.RejectKind.UnsupportedProtocol, kind);
            Assert.Contains("UUID", detail);
        }

        [Fact]
        public void VmessWsTls_AutoCipherNormalizedAndWrappedTlsOverWebSocket()
        {
            var node = new PersistedNode
            {
                Name = "JP-VMess-WS",
                Protocol = "VMess",
                Host = "jp.example.com:8443",
                Uuid = "aaaaaaaa-bbbb-4ccc-8ddd-eeeeeeeeeeee",
                Encryption = "auto",
                Network = "ws",
                Path = "/ws?ed=2048",
                WsHost = "cdn.example.com",
                Security = "tls",
                Sni = "cdn.example.com",
                AllowInsecure = false
            };

            bool ok = ShoesYamlSerializer.TryBuildClientProxyYaml(
                node, node.Name, indent: 0, out var yaml, out _, out _);

            Assert.True(ok);
            var root = ParseFirstSequenceItem(yaml);
            var tlsLayer = Assert.IsType<YamlMappingNode>(root.Children["protocol"]);
            Assert.Equal("tls", tlsLayer.Children["type"].ToString());
            Assert.Equal("cdn.example.com", tlsLayer.Children["sni_hostname"].ToString());
            Assert.Equal("true", tlsLayer.Children["verify"].ToString());

            var wsLayer = Assert.IsType<YamlMappingNode>(tlsLayer.Children["protocol"]);
            Assert.Equal("websocket", wsLayer.Children["type"].ToString());
            Assert.Equal("/ws?ed=2048", wsLayer.Children["matching_path"].ToString());
            var headers = Assert.IsType<YamlMappingNode>(wsLayer.Children["matching_headers"]);
            Assert.Equal("cdn.example.com", headers.Children["Host"].ToString());

            var vmessLayer = Assert.IsType<YamlMappingNode>(wsLayer.Children["protocol"]);
            Assert.Equal("vmess", vmessLayer.Children["type"].ToString());
            Assert.Equal("aes-128-gcm", vmessLayer.Children["cipher"].ToString());
        }

        [Fact]
        public void Shadowsocks2022_EscapesBase64PasswordWithColonsAndQuotes()
        {
            var node = new PersistedNode
            {
                Name = "SG-SS2022",
                Protocol = "Shadowsocks",
                Host = "1.2.3.4:9000",
                Encryption = "2022-blake3-aes-256-gcm",
                Password = "keyA==:keyB==\"\\test"
            };

            bool ok = ShoesYamlSerializer.TryBuildClientProxyYaml(
                node, node.Name, indent: 0, out var yaml, out _, out _);

            Assert.True(ok);
            var root = ParseFirstSequenceItem(yaml);
            var ssLayer = Assert.IsType<YamlMappingNode>(root.Children["protocol"]);
            Assert.Equal("shadowsocks", ssLayer.Children["type"].ToString());
            Assert.Equal("2022-blake3-aes-256-gcm", ssLayer.Children["cipher"].ToString());
            Assert.Equal("keyA==:keyB==\"\\test", ssLayer.Children["password"].ToString());
        }

        [Theory]
        [InlineData("Hysteria 2", "tcp", ShoesYamlSerializer.RejectKind.UnsupportedProtocol)]
        [InlineData("TUIC", "tcp", ShoesYamlSerializer.RejectKind.UnsupportedProtocol)]
        [InlineData("AnyTLS", "tcp", ShoesYamlSerializer.RejectKind.UnsupportedProtocol)]
        [InlineData("WireGuard", "tcp", ShoesYamlSerializer.RejectKind.UnsupportedProtocol)]
        [InlineData("Custom", "tcp", ShoesYamlSerializer.RejectKind.UnsupportedProtocol)]
        [InlineData("VLESS", "grpc", ShoesYamlSerializer.RejectKind.UnsupportedTransport)]
        [InlineData("VMess", "httpupgrade", ShoesYamlSerializer.RejectKind.UnsupportedTransport)]
        public void UnsupportedProtocolsOrTransports_ReturnCleanRejectForSeamlessSingboxFallback(
            string protocol,
            string network,
            ShoesYamlSerializer.RejectKind expectedKind)
        {
            var node = new PersistedNode
            {
                Name = "FallbackTest",
                Protocol = protocol,
                Host = "example.com:443",
                Network = network
            };

            bool ok = ShoesYamlSerializer.TryBuildClientProxyYaml(
                node, node.Name, indent: 0, out _, out var kind, out var detail);

            Assert.False(ok);
            Assert.Equal(expectedKind, kind);
            Assert.False(string.IsNullOrWhiteSpace(detail));
        }

        [Fact]
        public void Shadowsocks_ManualUiSavedNodeWithObfsNone_TranslatesCleanly()
        {
            var node = new PersistedNode
            {
                Name = "SS-2022 Manual",
                Protocol = "Shadowsocks",
                Host = "1.2.3.4:8388",
                Encryption = "2022-BLAKE3-AES-256-GCM",
                Password = " AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA= ",
                Network = "tcp",
                ObfsType = "none",
                HeaderType = "none",
                Security = "none"
            };

            bool ok = ShoesYamlSerializer.TryBuildClientProxyYaml(
                node, node.Name, indent: 0, out string yaml, out var kind, out _);

            Assert.True(ok);
            Assert.Equal(ShoesYamlSerializer.RejectKind.None, kind);

            var root = ParseFirstSequenceItem(yaml);
            var proto = Assert.IsType<YamlMappingNode>(root.Children["protocol"]);
            Assert.Equal("shadowsocks", proto.Children["type"].ToString());
            Assert.Equal("2022-blake3-aes-256-gcm", proto.Children["cipher"].ToString());
            Assert.Equal("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=", proto.Children["password"].ToString());
        }

        [Fact]
        public void TryExtractClashSecret_RoundTripsQuotedSecret()
        {
            string yaml = "- clash_api_address: \"127.0.0.1:9090\"\n  secret: \"f0e1d2c3b4a59687\"\n";
            Assert.Equal("f0e1d2c3b4a59687", ShoesYamlSerializer.TryExtractClashSecret(yaml));
        }
    }
}

