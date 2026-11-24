using System;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Microsoft.Win32;

namespace AnywhereWinUI.Helpers
{
    /// <summary>
    /// Detects Windows corporate VPN / Tailscale split-DNS policies (NRPT) and active tunnel
    /// adapters so intranet domains and routes stay direct even under Global or TUN mode.
    /// Inspired by bowel's internal/devnet/env_windows.go.
    /// </summary>
    public static class WindowsIntranetDetector
    {
        private static readonly string[] NrptKeyPaths =
        {
            @"SYSTEM\CurrentControlSet\Services\Dnscache\Parameters\DnsPolicyConfig",
            @"SOFTWARE\Policies\Microsoft\Windows NT\DNSClient\DnsPolicyConfig"
        };

        private static readonly string[] OwnTunKeywords =
        {
            "singbox-tun",
            "sing-box",
            "tun0"
        };

        private static readonly string[] TunnelAdapterKeywords =
        {
            "tailscale",
            "wireguard",
            "anyconnect",
            "globalprotect",
            "palo alto",
            "pan-gp",
            "fortinet",
            "fortissl",
            "openvpn",
            "wintun",
            "sangfor",
            "easyconnect",
            "pulse secure",
            "ivanti",
            "zerotier",
            "cloudflare warp",
            "tap-windows",
            "vpn"
        };

        public sealed class IntranetDetectionResult
        {
            public List<string> DirectDomainSuffixes { get; } = new();
            public List<string> DirectIpCidrs { get; } = new();
            public List<string> TunnelInterfaceNames { get; } = new();
            public Dictionary<string, List<string>> SplitDnsServerToDomains { get; } = new(StringComparer.OrdinalIgnoreCase);
        }

        public static IntranetDetectionResult Detect()
        {
            var result = new IntranetDetectionResult();
            var domainSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var cidrSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                ReadNrptPolicies(result, domainSet);
            }
            catch
            {
                // Best-effort registry read
            }

            try
            {
                ReadTunnelAdapters(result, domainSet, cidrSet);
            }
            catch
            {
                // Best-effort adapter inspection
            }

            result.DirectDomainSuffixes.AddRange(domainSet);
            result.DirectIpCidrs.AddRange(cidrSet);
            return result;
        }

        private static void ReadNrptPolicies(IntranetDetectionResult result, HashSet<string> domainSet)
        {
            foreach (var keyPath in NrptKeyPaths)
            {
                using RegistryKey? root = Registry.LocalMachine.OpenSubKey(keyPath, writable: false);
                if (root == null) continue;

                foreach (var subName in root.GetSubKeyNames())
                {
                    using RegistryKey? sub = root.OpenSubKey(subName, writable: false);
                    if (sub == null) continue;

                    var rawNames = sub.GetValue("Name") as string[];
                    var rawServers = sub.GetValue("GenericDNSServers") as string;
                    if (rawNames == null || rawNames.Length == 0) continue;

                    string? firstValidDns = ExtractFirstIPv4Dns(rawServers);

                    foreach (var rawName in rawNames)
                    {
                        string domain = NormalizeDomainSuffix(rawName);
                        if (string.IsNullOrEmpty(domain)) continue;

                        domainSet.Add(domain);

                        if (!string.IsNullOrEmpty(firstValidDns))
                        {
                            AddSplitDnsMapping(result, firstValidDns, domain);
                        }
                    }
                }
            }
        }

        private static void ReadTunnelAdapters(
            IntranetDetectionResult result,
            HashSet<string> domainSet,
            HashSet<string> cidrSet)
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up) continue;
                if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;

                string label = $"{nic.Name} {nic.Description}";
                if (ContainsAny(label, OwnTunKeywords)) continue;

                bool isTunnel = nic.NetworkInterfaceType == NetworkInterfaceType.Tunnel ||
                                ContainsAny(label, TunnelAdapterKeywords);
                if (!isTunnel) continue;

                IPInterfaceProperties props;
                try { props = nic.GetIPProperties(); }
                catch { continue; }

                // Skip Swell Proxy's own TUN subnet (172.18.0.1/30) if an unnamed Wintun adapter is still up during hot-restart
                bool isOwnTunAddress = false;
                foreach (var ua in props.UnicastAddresses)
                {
                    if (ua.Address.Equals(IPAddress.Parse("172.18.0.1")))
                    {
                        isOwnTunAddress = true;
                        break;
                    }
                }
                if (isOwnTunAddress) continue;

                if (!string.IsNullOrWhiteSpace(nic.Name) &&
                    !result.TunnelInterfaceNames.Exists(x => string.Equals(x, nic.Name, StringComparison.OrdinalIgnoreCase)))
                {
                    result.TunnelInterfaceNames.Add(nic.Name);
                }

                string suffix = NormalizeDomainSuffix(props.DnsSuffix);
                if (!string.IsNullOrEmpty(suffix))
                {
                    domainSet.Add(suffix);
                    foreach (var dns in props.DnsAddresses)
                    {
                        if (dns.AddressFamily == AddressFamily.InterNetwork &&
                            !IPAddress.IsLoopback(dns) &&
                            !dns.Equals(IPAddress.Any))
                        {
                            AddSplitDnsMapping(result, dns.ToString(), suffix);
                            break;
                        }
                    }
                }

                // Include Tailscale CGNAT (100.64.0.0/10) or tunnel adapter's own IPv4 subnet as direct CIDR
                foreach (var ua in props.UnicastAddresses)
                {
                    if (ua.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                    var bytes = ua.Address.GetAddressBytes();
                    if (bytes[0] == 100 && bytes[1] >= 64 && bytes[1] <= 127)
                    {
                        cidrSet.Add("100.64.0.0/10");
                    }
                    else if (ua.PrefixLength is >= 8 and <= 30)
                    {
                        string? networkCidr = ComputeNetworkCidr(ua.Address, ua.PrefixLength);
                        if (!string.IsNullOrEmpty(networkCidr))
                        {
                            cidrSet.Add(networkCidr);
                        }
                    }
                }
            }
        }

        private static void AddSplitDnsMapping(IntranetDetectionResult result, string dnsServer, string domain)
        {
            if (!result.SplitDnsServerToDomains.TryGetValue(dnsServer, out var list))
            {
                list = new List<string>();
                result.SplitDnsServerToDomains[dnsServer] = list;
            }
            if (!list.Exists(d => string.Equals(d, domain, StringComparison.OrdinalIgnoreCase)))
            {
                list.Add(domain);
            }
        }

        private static string? ExtractFirstIPv4Dns(string? rawServers)
        {
            if (string.IsNullOrWhiteSpace(rawServers)) return null;

            var tokens = rawServers.Split(new[] { ';', ',', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var token in tokens)
            {
                string trimmed = token.Trim();
                if (IPAddress.TryParse(trimmed, out var ip) &&
                    ip.AddressFamily == AddressFamily.InterNetwork &&
                    !IPAddress.IsLoopback(ip) &&
                    !ip.Equals(IPAddress.Any))
                {
                    return ip.ToString();
                }
            }
            return null;
        }

        private static string NormalizeDomainSuffix(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
            string s = raw.Trim().TrimStart('.').TrimEnd('.').ToLowerInvariant();
            if (string.IsNullOrEmpty(s) || s.Contains('*') || s.Contains(' ') || s.Contains('/'))
                return string.Empty;
            if (IPAddress.TryParse(s, out _))
                return string.Empty;
            return s;
        }

        private static string? ComputeNetworkCidr(IPAddress ip, int prefixLength)
        {
            if (prefixLength is < 8 or > 32) return null;
            var bytes = ip.GetAddressBytes();
            uint addr = ((uint)bytes[0] << 24) | ((uint)bytes[1] << 16) | ((uint)bytes[2] << 8) | bytes[3];
            uint mask = prefixLength == 0 ? 0u : uint.MaxValue << (32 - prefixLength);
            uint net = addr & mask;
            var netBytes = new[]
            {
                (byte)((net >> 24) & 0xFF),
                (byte)((net >> 16) & 0xFF),
                (byte)((net >> 8) & 0xFF),
                (byte)(net & 0xFF)
            };
            return $"{new IPAddress(netBytes)}/{prefixLength}";
        }

        private static bool ContainsAny(string text, string[] keywords)
        {
            foreach (var k in keywords)
            {
                if (text.Contains(k, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }
    }
}
