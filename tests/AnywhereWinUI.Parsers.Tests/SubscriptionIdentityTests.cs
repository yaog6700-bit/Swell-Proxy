using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using AnywhereWinUI.Services;
using Xunit;

namespace AnywhereWinUI.Parsers.Tests
{
    /// <summary>
    /// NodesManager matches existing nodes with <c>$"{Protocol}|{Host}"</c> to preserve the node Id
    /// and IsFavorite flag. A Clash import must therefore produce exactly the same identity key as
    /// the share-link import of the same endpoint, otherwise switching a provider from a Base64
    /// subscription to a Clash subscription would drop favourites.
    /// </summary>
    public class SubscriptionIdentityTests
    {
        private static string Identity(PersistedNode node) => $"{node.Protocol}|{node.Host}";

        private static PersistedNode ParseClashEntry(string flowEntry)
            => Assert.Single(ClashSubscriptionParser.Parse("proxies:\n  - " + flowEntry + "\n", "sub"));

        [Theory]
        [InlineData(
            "{name: HK-SS-01, type: ss, server: hk1.example.com, port: 8388, cipher: aes-256-gcm, password: pass-ss}",
            "ss://YWVzLTI1Ni1nY206cGFzcw==@hk1.example.com:8388#HK-SS-01")]
        [InlineData(
            "{name: JP-VMess-WS, type: vmess, server: jp1.example.com, port: 443, uuid: 11111111-2222-3333-4444-555555555555, alterId: 0, cipher: auto, tls: true, network: ws}",
            "vmess://eyJ2IjoiMiIsInBzIjoiSlAtVk1lc3MiLCJhZGQiOiJqcDEuZXhhbXBsZS5jb20iLCJwb3J0IjoiNDQzIiwiaWQiOiIxMTExMTExMS0yMjIyLTMzMzMtNDQ0NC01NTU1NTU1NTU1NTUiLCJhaWQiOiIwIiwibmV0Ijoid3MiLCJ0eXBlIjoibm9uZSIsImhvc3QiOiJjZG4uZXhhbXBsZS5jb20iLCJwYXRoIjoiL3dzIiwidGxzIjoidGxzIn0=")]
        [InlineData(
            "{name: TW-Trojan, type: trojan, server: tw1.example.com, port: 443, password: trojan-pass, sni: tw1.example.com}",
            "trojan://trojan-pass@tw1.example.com:443?sni=tw1.example.com#TW-Trojan")]
        [InlineData(
            "{name: US-VLESS, type: vless, server: us1.example.com, port: 443, uuid: 66666666-7777-8888-9999-000000000000, network: tcp, tls: true, flow: xtls-rprx-vision, servername: www.microsoft.com}",
            "vless://66666666-7777-8888-9999-000000000000@us1.example.com:443?type=tcp&security=tls&sni=www.microsoft.com&flow=xtls-rprx-vision#US-VLESS")]
        [InlineData(
            "{name: KR-Hy2, type: hysteria2, server: kr1.example.com, port: 8443, password: hy2-pass, sni: kr1.example.com}",
            "hysteria2://hy2-pass@kr1.example.com:8443?sni=kr1.example.com#KR-Hy2")]
        public void ClashImportAndShareLinkImportProduceTheSameIdentityKey(string clashEntry, string shareLink)
        {
            var clashNode = ParseClashEntry(clashEntry);
            var linkNode = NodeLinkParser.Parse(shareLink);

            Assert.NotNull(linkNode);
            Assert.False(string.IsNullOrWhiteSpace(linkNode!.Protocol));
            Assert.Equal(Identity(linkNode), Identity(clashNode));
        }

        [Fact]
        public void FixtureA_EndpointsMatchTheirShareLinkForm()
        {
            var yaml = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "clash", "A-clash-meta-block.yaml"));
            var nodes = ClashSubscriptionParser.Parse(yaml, "sub-a");
            var identities = nodes.Select(Identity).ToHashSet(StringComparer.OrdinalIgnoreCase);

            // Endpoints that a provider would also expose as share links.
            Assert.Contains("Shadowsocks|hk1.example.com:8388", identities);
            Assert.Contains("VMess|jp1.example.com:443", identities);
            Assert.Contains("VLESS|us1.example.com:443", identities);
            Assert.Contains("Trojan|tw1.example.com:443", identities);
            Assert.Contains("Hysteria 2|kr1.example.com:8443", identities);
            Assert.Contains("TUIC|de1.example.com:443", identities);
            Assert.Contains("AnyTLS|fr1.example.com:443", identities);
            Assert.Contains("WireGuard|wg.example.com:51820", identities);
        }

        [Fact]
        public void ClashImportBracketsIpv6ServerAddresses()
        {
            // Clash stores the bare address, so the import path produces the canonical
            // "[addr]:port" form that SplitHostPort accepts. (Share-link imports of IPv6 are
            // affected by a pre-existing NodeLinkParser issue: Uri.Host already returns the
            // bracketed form, which then gets bracketed a second time.)
            const string yaml = @"proxies:
  - {name: HK-VLESS-v6, type: vless, server: '2001:db8::1', port: 443, uuid: 66666666-7777-8888-9999-000000000000, network: tcp, tls: true}
";
            var node = Assert.Single(ClashSubscriptionParser.Parse(yaml, "sub"));

            Assert.Equal("[2001:db8::1]:443", node.Host);
            Assert.True(NodeLinkParser.TrySplitHostPort(node.Host, out var host, out int port));
            Assert.Equal("2001:db8::1", host);
            Assert.Equal(443, port);
        }

        [Fact]
        public void FixtureC1_IsAValidBase64SubscriptionOfShareLinks()
        {
            var content = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "clash", "C1-base64-subscription.txt")).Trim();
            var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(content));

            Assert.Contains("://", decoded);

            var parsed = decoded
                .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
                .Select(line => NodeLinkParser.Parse(line.Trim()))
                .Where(n => n != null)
                .ToList();

            Assert.Equal(3, parsed.Count);
        }

        [Fact]
        public void SwitchingAProviderFromBase64ToClashKeepsFavourites()
        {
            // Mirrors NodesManager.UpdateSubscriptionAsync: old nodes are indexed by
            // "{Protocol}|{Host}" and a matching new node inherits Id + IsFavorite.
            var oldNodes = ParseBase64Subscription();
            oldNodes[0].IsFavorite = true;
            string favouriteId = oldNodes[0].Id;

            var newNodes = ClashSubscriptionParser.Parse(
                File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "clash", "A-clash-meta-block.yaml")),
                "sub-a");

            var oldByKey = oldNodes.ToDictionary(Identity, n => n, StringComparer.OrdinalIgnoreCase);
            foreach (var node in newNodes)
            {
                if (oldByKey.TryGetValue(Identity(node), out var match))
                {
                    node.Id = match.Id;
                    node.IsFavorite = match.IsFavorite;
                }
            }

            var favourites = newNodes.Where(n => n.IsFavorite).ToList();
            Assert.Single(favourites);
            Assert.Equal(favouriteId, favourites[0].Id);
            Assert.Equal("hk1.example.com:8388", favourites[0].Host);
            Assert.Equal("Shadowsocks", favourites[0].Protocol);
        }

        private static List<PersistedNode> ParseBase64Subscription()
        {
            var content = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "clash", "C1-base64-subscription.txt")).Trim();
            var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(content));

            return decoded
                .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
                .Select(line => NodeLinkParser.Parse(line.Trim()))
                .Where(n => n != null)
                .Select(n => n!)
                .ToList();
        }
    }
}
