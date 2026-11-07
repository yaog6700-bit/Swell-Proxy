using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using AnywhereWinUI.Helpers;
using AnywhereWinUI.Models;

namespace AnywhereWinUI.Services
{
    /// <summary>
    /// Lightweight, zero-dependency YAML configuration builder and validator for the Shoes (Rust) core.
    /// Avoids third-party reflection YAML libraries to keep single-file PublishTrimmed binary size and RAM minimal.
    /// </summary>
    public static class ShoesConfigBuilder
    {
        public const string EngineSingBox = "sing-box";
        public const string EngineShoes = "shoes";

        public static string NormalizeEngine(string? engine)
        {
            return string.Equals(engine, EngineShoes, StringComparison.OrdinalIgnoreCase)
                ? EngineShoes
                : EngineSingBox;
        }

        /// <summary>
        /// Ensures bundled *.srs rule-set files exist in %LocalAppData%\SwellProxy so both sing-box and Shoes
        /// share the exact same single copy of GeoIP/GeoSite rule-sets on disk.
        /// </summary>
        public static string EnsureLocalRuleSetDirectory()
        {
            var localDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SwellProxy");
            Directory.CreateDirectory(localDir);

            var sourceRulesDir = Path.Combine(AppContext.BaseDirectory, "Assets", "rules");
            if (Directory.Exists(sourceRulesDir))
            {
                foreach (var file in Directory.GetFiles(sourceRulesDir, "*.srs"))
                {
                    var destFile = Path.Combine(localDir, Path.GetFileName(file));
                    try
                    {
                        var srcInfo = new FileInfo(file);
                        var dstInfo = new FileInfo(destFile);
                        if (!dstInfo.Exists || dstInfo.Length != srcInfo.Length)
                        {
                            File.Copy(file, destFile, overwrite: true);
                        }
                    }
                    catch { }
                }
            }

            return localDir;
        }

        /// <summary>
        /// Attempts to generate a native Shoes YAML configuration for the current node and session settings.
        /// Returns false with a user-friendly <paramref name="fallbackReason"/> whenever sing-box is required
        /// (e.g., TUN mode, Tailscale, Custom JSON, Proxy Chain, or sing-box-exclusive protocols).
        /// </summary>
        public static bool TryBuild(
            PersistedNode? selectedNode,
            AppSession session,
            string apiSecret,
            out string yamlConfig,
            out string fallbackReason)
        {
            yamlConfig = string.Empty;
            fallbackReason = string.Empty;

            if (!CoreUpdateService.IsShoesInstalled)
            {
                fallbackReason = L.Shoes_ReasonNotInstalled;
                return false;
            }

            if (session.EnableTunMode)
            {
                fallbackReason = L.Shoes_ReasonTunMode;
                return false;
            }

            if (session.EnableTailscale)
            {
                fallbackReason = L.Shoes_ReasonTailscale;
                return false;
            }

            if (selectedNode == null || string.IsNullOrWhiteSpace(selectedNode.Host))
            {
                fallbackReason = L.Shoes_ReasonUnsupportedNode;
                return false;
            }

            if (CustomConfigService.IsCustomNode(selectedNode))
            {
                fallbackReason = L.Shoes_ReasonCustomConfig;
                return false;
            }

            if (selectedNode.IsShadowTls)
            {
                fallbackReason = L.Shoes_ReasonShadowTls;
                return false;
            }

            if (!string.IsNullOrWhiteSpace(selectedNode.ProxyChainId))
            {
                fallbackReason = L.Shoes_ReasonProxyChain;
                return false;
            }

            // Check advanced routing rules: if user explicitly configured urltest, per-rule extra node outbounds,
            // process-based routing, or remote HTTP rule-sets, borrow sing-box so routing semantics stay 100% faithful.
            List<RoutingRuleItem>? activeCustomRules = null;
            if (session.RoutingMode == "smart" && session.EnableAdvancedRouting)
            {
                activeCustomRules = RoutingRulesService.LoadRules();
                foreach (var rule in activeCustomRules)
                {
                    if (!rule.IsEnabled) continue;
                    var ob = rule.OutboundTag ?? "proxy";
                    if (ob == "urltest" || ob.StartsWith("node:", StringComparison.OrdinalIgnoreCase) ||
                        (ob != "proxy" && ob != "direct" && ob != "block" && ob != "reject"))
                    {
                        fallbackReason = L.Shoes_ReasonMultiNodeRule;
                        return false;
                    }

                    string ruleType = (rule.Type ?? "domain").Trim().ToLowerInvariant();
                    string matchText = rule.Match ?? string.Empty;
                    if (ruleType == "process" ||
                        matchText.Contains("process_name:", StringComparison.OrdinalIgnoreCase) ||
                        matchText.Contains("process_path:", StringComparison.OrdinalIgnoreCase) ||
                        matchText.Contains("https://", StringComparison.OrdinalIgnoreCase) ||
                        matchText.Contains("http://", StringComparison.OrdinalIgnoreCase))
                    {
                        fallbackReason = L.Shoes_ReasonMultiNodeRule;
                        return false;
                    }
                }
            }

            string proxyName = !string.IsNullOrWhiteSpace(selectedNode.Name)
                ? selectedNode.Name.Trim()
                : "proxy-node";

            if (!ShoesYamlSerializer.TryBuildClientProxyYaml(
                    selectedNode,
                    proxyName,
                    indent: 4,
                    out string clientProxyYaml,
                    out var rejectKind,
                    out var rejectDetail))
            {
                fallbackReason = rejectKind switch
                {
                    ShoesYamlSerializer.RejectKind.UnsupportedTransport => string.Format(L.Shoes_ReasonTransportFmt, rejectDetail),
                    ShoesYamlSerializer.RejectKind.UnsupportedProtocol => string.Format(L.Shoes_ReasonProtocolFmt, rejectDetail),
                    _ => L.Shoes_ReasonUnsupportedNode
                };
                return false;
            }

            string groupKey = GetShoesSelectorKey();
            string listenHost = session.AllowLanAccess ? "0.0.0.0" : "127.0.0.1";
            int mixedPort = session.MixedPort > 0 ? session.MixedPort : 2080;
            string effectiveSecret = !string.IsNullOrEmpty(apiSecret) ? apiSecret : "swellproxy";

            var sb = new StringBuilder(2048);
            sb.AppendLine("# Generated by Swell Proxy for Shoes core");

            // 1. Clash-compatible API controller (used by Swell Proxy stats, connections & topology)
            sb.Append("- clash_api_address: ").AppendLine(ShoesYamlSerializer.Quote("127.0.0.1:9090"));
            sb.Append("  secret: ").AppendLine(ShoesYamlSerializer.Quote(effectiveSecret));
            sb.AppendLine("  cors_origins:");
            sb.AppendLine("    - \"*\"");

            // 2. Selector Client Group
            sb.Append("- client_group: ").AppendLine(ShoesYamlSerializer.Quote("proxy"));
            sb.Append("  ").Append(groupKey).Append(": ").AppendLine(ShoesYamlSerializer.Quote("selector"));
            sb.Append("  default: ").AppendLine(ShoesYamlSerializer.Quote(proxyName));
            sb.AppendLine("  client_proxies:");
            sb.Append(clientProxyYaml);

            // 3. Mixed Inbound Server (SOCKS5 + HTTP + UDP)
            sb.Append("- name: ").AppendLine(ShoesYamlSerializer.Quote("mixed-in"));
            sb.Append("  address: ").AppendLine(ShoesYamlSerializer.Quote($"{listenHost}:{mixedPort}"));
            sb.AppendLine("  sniff: true");
            sb.AppendLine("  protocol:");
            sb.AppendLine("    type: \"mixed\"");
            sb.AppendLine("    udp_enabled: true");
            sb.AppendLine("  rules:");
            AppendRulesYaml(sb, session, activeCustomRules);

            yamlConfig = sb.ToString();
            return true;
        }

        private static string GetShoesSelectorKey()
        {
            var ver = CoreUpdateService.GetLocalShoesNumericVersion();
            if (ver != null && ver <= new Version(0, 3, 4))
            {
                return "strategy";
            }
            return "type";
        }

        private static void AppendRulesYaml(
            StringBuilder sb,
            AppSession session,
            List<RoutingRuleItem>? activeCustomRules)
        {
            // 1. Private/LAN IPs always go direct (no_resolve)
            sb.AppendLine("    - no_resolve: true");
            sb.AppendLine("      masks:");
            sb.AppendLine("        any:");
            sb.AppendLine("          - ip_cidr:");
            sb.AppendLine("              - \"10.0.0.0/8\"");
            sb.AppendLine("              - \"172.16.0.0/12\"");
            sb.AppendLine("              - \"192.168.0.0/16\"");
            sb.AppendLine("              - \"127.0.0.0/8\"");
            sb.AppendLine("              - \"fc00::/7\"");
            sb.AppendLine("      client_chain: \"direct\"");

            if (session.RoutingMode == "global")
            {
                sb.AppendLine("    - masks: \"0.0.0.0/0\"");
                sb.AppendLine("      client_chain: \"proxy\"");
                return;
            }

            if (session.RoutingMode == "direct")
            {
                sb.AppendLine("    - masks: \"0.0.0.0/0\"");
                sb.AppendLine("      client_chain: \"direct\"");
                return;
            }

            string ruleDir = EnsureLocalRuleSetDirectory();

            if (session.BlockAds)
            {
                string adsPath = Path.Combine(ruleDir, "geosite-category-ads-all.srs");
                if (File.Exists(adsPath))
                {
                    sb.AppendLine("    - masks:");
                    sb.Append("        path: ").AppendLine(ShoesYamlSerializer.Quote(ToForwardSlash(adsPath)));
                    sb.AppendLine("      action: \"block\"");
                }
            }

            if (activeCustomRules != null)
            {
                foreach (var rule in activeCustomRules)
                {
                    if (!rule.IsEnabled || string.IsNullOrWhiteSpace(rule.Match)) continue;
                    AppendSingleCustomRuleYaml(sb, rule, ruleDir);
                }
            }

            if (session.BypassChina)
            {
                string geositeCn = Path.Combine(ruleDir, "geosite-cn.srs");
                string geoipCn = Path.Combine(ruleDir, "geoip-cn.srs");

                sb.AppendLine("    - masks:");
                sb.AppendLine("        any:");
                if (File.Exists(geositeCn))
                {
                    sb.Append("          - path: ").AppendLine(ShoesYamlSerializer.Quote(ToForwardSlash(geositeCn)));
                }
                if (File.Exists(geoipCn))
                {
                    sb.Append("          - path: ").AppendLine(ShoesYamlSerializer.Quote(ToForwardSlash(geoipCn)));
                }
                sb.AppendLine("          - domain_suffix:");
                sb.AppendLine("              - \"cn\"");
                sb.AppendLine("      client_chain: \"direct\"");
            }

            sb.AppendLine("    - masks: \"0.0.0.0/0\"");
            sb.AppendLine("      client_chain: \"proxy\"");
        }

        private static void AppendSingleCustomRuleYaml(StringBuilder sb, RoutingRuleItem rule, string ruleDir)
        {
            string ruleType = (rule.Type ?? "domain").Trim().ToLowerInvariant();
            var rawEntries = rule.Match.Split(new[] { ',', ';', '\r', '\n' },
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (rawEntries.Length == 0) return;

            string target = (rule.OutboundTag ?? "proxy").Trim().ToLowerInvariant();
            bool isBlock = target == "block" || target == "reject";
            string chain = target == "direct" ? "direct" : "proxy";

            var domainSuffixes = new List<string>();
            var domainKeywords = new List<string>();
            var ipCidrs = new List<string>();
            var ruleSetPaths = new List<string>();

            foreach (var raw in rawEntries)
            {
                var entry = raw.Trim();
                if (entry.Length == 0) continue;

                if (entry.StartsWith("geosite:", StringComparison.OrdinalIgnoreCase))
                {
                    var tag = entry.Substring("geosite:".Length).Trim().ToLowerInvariant();
                    var candidate = Path.Combine(ruleDir, $"geosite-{tag}.srs");
                    if (File.Exists(candidate)) ruleSetPaths.Add(ToForwardSlash(candidate));
                }
                else if (entry.StartsWith("geoip:", StringComparison.OrdinalIgnoreCase))
                {
                    var tag = entry.Substring("geoip:".Length).Trim().ToLowerInvariant();
                    var candidate = Path.Combine(ruleDir, $"geoip-{tag}.srs");
                    if (File.Exists(candidate)) ruleSetPaths.Add(ToForwardSlash(candidate));
                }
                else if (entry.StartsWith("domain_keyword:", StringComparison.OrdinalIgnoreCase))
                {
                    domainKeywords.Add(entry.Substring("domain_keyword:".Length).Trim());
                }
                else if (entry.StartsWith("domain_suffix:", StringComparison.OrdinalIgnoreCase))
                {
                    domainSuffixes.Add(entry.Substring("domain_suffix:".Length).Trim().TrimStart('.'));
                }
                else if (entry.StartsWith("domain:", StringComparison.OrdinalIgnoreCase))
                {
                    domainSuffixes.Add(entry.Substring("domain:".Length).Trim().TrimStart('.'));
                }
                else if (entry.StartsWith("ip_cidr:", StringComparison.OrdinalIgnoreCase))
                {
                    ipCidrs.Add(NormalizeCidr(entry.Substring("ip_cidr:".Length).Trim()));
                }
                else if (ruleType == "ip" || System.Net.IPAddress.TryParse(entry.Split('/')[0], out _))
                {
                    ipCidrs.Add(NormalizeCidr(entry));
                }
                else
                {
                    domainSuffixes.Add(entry.TrimStart('.'));
                }
            }

            void EmitAction()
            {
                if (isBlock)
                    sb.AppendLine("      action: \"block\"");
                else
                    sb.Append("      client_chain: ").AppendLine(ShoesYamlSerializer.Quote(chain));
            }

            if (domainSuffixes.Count > 0)
            {
                sb.AppendLine("    - masks:");
                sb.AppendLine("        domain_suffix:");
                foreach (var v in domainSuffixes) sb.Append("          - ").AppendLine(ShoesYamlSerializer.Quote(v));
                EmitAction();
            }

            if (domainKeywords.Count > 0)
            {
                sb.AppendLine("    - masks:");
                sb.AppendLine("        domain_keyword:");
                foreach (var v in domainKeywords) sb.Append("          - ").AppendLine(ShoesYamlSerializer.Quote(v));
                EmitAction();
            }

            if (ipCidrs.Count > 0)
            {
                sb.AppendLine("    - masks:");
                sb.AppendLine("        ip_cidr:");
                foreach (var v in ipCidrs) sb.Append("          - ").AppendLine(ShoesYamlSerializer.Quote(v));
                EmitAction();
            }

            foreach (var rsPath in ruleSetPaths)
            {
                sb.AppendLine("    - masks:");
                sb.Append("        path: ").AppendLine(ShoesYamlSerializer.Quote(rsPath));
                EmitAction();
            }
        }

        private static string NormalizeCidr(string value)
        {
            if (value.Contains('/')) return value;
            return value.Contains(':') ? $"{value}/128" : $"{value}/32";
        }

        public static async Task<(bool Ok, string ErrorMessage)> ValidateAndAutoHealAsync(string shoesExePath, string yamlPath)
        {
            var (ok, output) = await RunCheckOnceAsync(shoesExePath, yamlPath).ConfigureAwait(false);
            if (ok) return (true, string.Empty);

            if (output.Contains("unknown field `strategy`", StringComparison.OrdinalIgnoreCase) ||
                output.Contains("unknown field `type`", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    string raw = await File.ReadAllTextAsync(yamlPath).ConfigureAwait(false);
                    string fixedYaml = output.Contains("unknown field `strategy`", StringComparison.OrdinalIgnoreCase)
                        ? raw.Replace("  strategy: \"selector\"", "  type: \"selector\"").Replace("  strategy: selector", "  type: selector")
                        : raw.Replace("  type: \"selector\"", "  strategy: \"selector\"").Replace("  type: selector", "  strategy: selector");

                    if (!string.Equals(raw, fixedYaml, StringComparison.Ordinal))
                    {
                        await File.WriteAllTextAsync(yamlPath, fixedYaml, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)).ConfigureAwait(false);
                        var retry = await RunCheckOnceAsync(shoesExePath, yamlPath).ConfigureAwait(false);
                        if (retry.Ok) return (true, string.Empty);
                        output = retry.Output;
                    }
                }
                catch { }
            }

            string clean = StripAnsi(output).Trim();
            if (clean.Length > 360) clean = clean[..360] + "…";
            return (false, clean);
        }

        private static async Task<(bool Ok, string Output)> RunCheckOnceAsync(string shoesExePath, string yamlPath)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = shoesExePath,
                    Arguments = $"check \"{yamlPath}\"",
                    WorkingDirectory = Path.GetDirectoryName(shoesExePath) ?? AppContext.BaseDirectory,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                };

                using var proc = Process.Start(psi);
                if (proc == null) return (false, "Failed to start shoes check process.");

                var stdoutTask = proc.StandardOutput.ReadToEndAsync();
                var stderrTask = proc.StandardError.ReadToEndAsync();
                using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(8));
                try
                {
                    await proc.WaitForExitAsync(cts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    try { proc.Kill(); } catch { }
                    return (false, "shoes check timed out.");
                }

                string stdout = await stdoutTask.ConfigureAwait(false);
                string stderr = await stderrTask.ConfigureAwait(false);
                string combined = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
                return (proc.ExitCode == 0, combined);
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }

        public static string? TryExtractClashSecret(string yamlContent) =>
            ShoesYamlSerializer.TryExtractClashSecret(yamlContent);

        private static string ToForwardSlash(string path) => path.Replace('\\', '/');

        private static string StripAnsi(string input)
        {
            if (string.IsNullOrEmpty(input)) return string.Empty;
            var sb = new StringBuilder(input.Length);
            for (int i = 0; i < input.Length; i++)
            {
                if (input[i] == '\x1b' && i + 1 < input.Length && input[i + 1] == '[')
                {
                    int j = i + 2;
                    while (j < input.Length)
                    {
                        char c = input[j++];
                        if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z')) break;
                    }
                    i = j - 1;
                    continue;
                }
                sb.Append(input[i]);
            }
            return sb.ToString();
        }
    }
}
