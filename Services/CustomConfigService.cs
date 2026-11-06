using System;
using System.Buffers;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using AnywhereWinUI.Helpers;

namespace AnywhereWinUI.Services
{
    public sealed class CustomConfigSummary
    {
        public bool IsValidJson { get; init; }
        public string? ParseError { get; init; }
        public bool HasTunInbound { get; init; }
        public int? LocalHttpPort { get; init; }
        public int? AnyListenPort { get; init; }
        public int InboundCount { get; init; }
        public int OutboundCount { get; init; }
        public int RuleCount { get; init; }
        public string ShortSummary { get; init; } = "sing-box JSON";
        public string NetworkBadge { get; init; } = "JSON";
    }

    /// <summary>
    /// Manages native full sing-box JSON configuration profiles stored on disk under
    /// %LocalAppData%\SwellProxy\configs\ so large JSON payloads never bloat nodes_config.json or RAM.
    /// </summary>
    public static class CustomConfigService
    {
        public const string ProtocolName = "Custom";
        private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

        public static string AppDataDirectory => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SwellProxy");

        public static string ConfigsDirectory => Path.Combine(AppDataDirectory, "configs");

        public static bool IsCustomProtocol(string? protocol) =>
            string.Equals(protocol, ProtocolName, StringComparison.OrdinalIgnoreCase);

        public static bool IsCustomNode(PersistedNode? node) =>
            node != null && IsCustomProtocol(node.Protocol);

        public static bool RequiresTunAdmin(PersistedNode? node) =>
            IsCustomNode(node) && node!.CustomHasTun && !AdminHelper.IsAdministrator();

        public static string EnsureFileName(PersistedNode node)
        {
            var raw = Path.GetFileName(node.CustomConfigFile ?? string.Empty);
            if (string.IsNullOrWhiteSpace(raw))
            {
                var safeId = string.IsNullOrWhiteSpace(node.Id) ? Guid.NewGuid().ToString("N") : node.Id;
                raw = $"{safeId}.json";
                node.CustomConfigFile = raw;
            }
            else if (!raw.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                raw += ".json";
                node.CustomConfigFile = raw;
            }
            return raw;
        }

        public static string ResolveConfigFilePath(PersistedNode node)
        {
            string fileName = EnsureFileName(node);
            return Path.Combine(ConfigsDirectory, fileName);
        }

        public static async Task<string> ReadConfigTextAsync(PersistedNode node)
        {
            if (node == null) return string.Empty;
            string path = ResolveConfigFilePath(node);
            if (!File.Exists(path)) return string.Empty;
            return await File.ReadAllTextAsync(path, Encoding.UTF8).ConfigureAwait(false);
        }

        public static async Task<CustomConfigSummary> SaveConfigTextAsync(PersistedNode node, string jsonText)
        {
            Directory.CreateDirectory(ConfigsDirectory);
            string path = ResolveConfigFilePath(node);
            await File.WriteAllTextAsync(path, jsonText ?? string.Empty, Utf8NoBom).ConfigureAwait(false);

            var summary = InspectConfig(jsonText);
            node.Protocol = ProtocolName;
            node.Host = summary.ShortSummary;
            node.Network = summary.NetworkBadge;
            node.CustomHasTun = summary.HasTunInbound;
            return summary;
        }

        public static void DeleteConfigFile(PersistedNode? node)
        {
            if (!IsCustomNode(node) || node == null) return;
            try
            {
                var raw = Path.GetFileName(node.CustomConfigFile ?? string.Empty);
                if (string.IsNullOrWhiteSpace(raw))
                    raw = $"{node.Id}.json";

                var path = Path.Combine(ConfigsDirectory, raw);
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CustomConfigService] Failed to delete config file: {ex.Message}");
            }
        }

        public static CustomConfigSummary InspectConfig(string? jsonText)
        {
            if (string.IsNullOrWhiteSpace(jsonText))
            {
                return new CustomConfigSummary
                {
                    IsValidJson = false,
                    ParseError = L.CustomCfg_ErrEmptyJson
                };
            }

            try
            {
                using var doc = JsonDocument.Parse(jsonText, new JsonDocumentOptions
                {
                    CommentHandling = JsonCommentHandling.Skip,
                    AllowTrailingCommas = true,
                    MaxDepth = 64
                });

                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                {
                    return new CustomConfigSummary
                    {
                        IsValidJson = false,
                        ParseError = L.CustomCfg_ErrRootNotObject
                    };
                }

                bool hasTun = false;
                int? localHttpPort = null;
                string? localHttpType = null;
                int? anyListenPort = null;
                int inboundCount = 0;

                if (root.TryGetProperty("inbounds", out var inboundsEl) && inboundsEl.ValueKind == JsonValueKind.Array)
                {
                    foreach (var ib in inboundsEl.EnumerateArray())
                    {
                        if (ib.ValueKind != JsonValueKind.Object) continue;
                        inboundCount++;

                        string type = ib.TryGetProperty("type", out var tProp) && tProp.ValueKind == JsonValueKind.String
                            ? (tProp.GetString() ?? string.Empty)
                            : string.Empty;

                        int port = 0;
                        if (ib.TryGetProperty("listen_port", out var pProp) && pProp.ValueKind == JsonValueKind.Number)
                        {
                            pProp.TryGetInt32(out port);
                        }

                        if (port > 0 && port <= 65535 && anyListenPort == null)
                        {
                            anyListenPort = port;
                        }

                        if (string.Equals(type, "tun", StringComparison.OrdinalIgnoreCase))
                        {
                            hasTun = true;
                        }
                        else if ((string.Equals(type, "mixed", StringComparison.OrdinalIgnoreCase) ||
                                  string.Equals(type, "http", StringComparison.OrdinalIgnoreCase)) &&
                                 port > 0 && port <= 65535 && localHttpPort == null)
                        {
                            localHttpPort = port;
                            localHttpType = type.ToLowerInvariant();
                        }
                    }
                }

                int outboundCount = 0;
                if (root.TryGetProperty("outbounds", out var outboundsEl) && outboundsEl.ValueKind == JsonValueKind.Array)
                {
                    foreach (var ob in outboundsEl.EnumerateArray())
                    {
                        if (ob.ValueKind == JsonValueKind.Object)
                            outboundCount++;
                    }
                }

                int ruleCount = 0;
                if (root.TryGetProperty("route", out var routeEl) &&
                    routeEl.ValueKind == JsonValueKind.Object &&
                    routeEl.TryGetProperty("rules", out var rulesEl) &&
                    rulesEl.ValueKind == JsonValueKind.Array)
                {
                    ruleCount = rulesEl.GetArrayLength();
                }

                var sb = new StringBuilder();
                if (hasTun)
                    sb.Append("TUN");

                if (localHttpPort.HasValue)
                {
                    if (sb.Length > 0) sb.Append(" · ");
                    sb.Append(localHttpType ?? "mixed").Append(" :").Append(localHttpPort.Value);
                }
                else if (!hasTun && anyListenPort.HasValue)
                {
                    sb.Append(':').Append(anyListenPort.Value);
                }

                if (outboundCount > 0)
                {
                    if (sb.Length > 0) sb.Append(" · ");
                    sb.Append(outboundCount).Append(" out");
                }

                string shortSummary = sb.Length > 0 ? sb.ToString() : "sing-box JSON";
                string networkBadge = (hasTun, localHttpPort.HasValue) switch
                {
                    (true, true) => "TUN+MIXED",
                    (true, false) => "TUN",
                    (false, true) => (localHttpType ?? "MIXED").ToUpperInvariant(),
                    _ => "JSON"
                };

                return new CustomConfigSummary
                {
                    IsValidJson = true,
                    HasTunInbound = hasTun,
                    LocalHttpPort = localHttpPort,
                    AnyListenPort = anyListenPort,
                    InboundCount = inboundCount,
                    OutboundCount = outboundCount,
                    RuleCount = ruleCount,
                    ShortSummary = shortSummary,
                    NetworkBadge = networkBadge
                };
            }
            catch (JsonException jex)
            {
                long line = (jex.LineNumber ?? 0) + 1;
                long pos = (jex.BytePositionInLine ?? 0) + 1;
                return new CustomConfigSummary
                {
                    IsValidJson = false,
                    ParseError = Loc.Format("CustomCfg_ErrJsonSyntaxFmt", line, pos, jex.Message)
                };
            }
            catch (Exception ex)
            {
                return new CustomConfigSummary
                {
                    IsValidJson = false,
                    ParseError = ex.Message
                };
            }
        }

        public static bool TryFormatJson(string rawJson, out string formatted, out string? error)
        {
            formatted = string.Empty;
            error = null;

            if (string.IsNullOrWhiteSpace(rawJson))
            {
                error = L.CustomCfg_ErrEmptyJson;
                return false;
            }

            try
            {
                using var doc = JsonDocument.Parse(rawJson, new JsonDocumentOptions
                {
                    CommentHandling = JsonCommentHandling.Skip,
                    AllowTrailingCommas = true,
                    MaxDepth = 64
                });

                if (doc.RootElement.ValueKind != JsonValueKind.Object)
                {
                    error = L.CustomCfg_ErrRootNotObject;
                    return false;
                }

                var buffer = new ArrayBufferWriter<byte>(Math.Max(1024, rawJson.Length));
                using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions
                {
                    Indented = true,
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                }))
                {
                    doc.RootElement.WriteTo(writer);
                }

                formatted = Encoding.UTF8.GetString(buffer.WrittenSpan);
                return true;
            }
            catch (JsonException jex)
            {
                long line = (jex.LineNumber ?? 0) + 1;
                long pos = (jex.BytePositionInLine ?? 0) + 1;
                error = Loc.Format("CustomCfg_ErrJsonSyntaxFmt", line, pos, jex.Message);
                return false;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        public static string GetMinimalTemplate(int mixedPort)
        {
            if (mixedPort <= 0 || mixedPort > 65535) mixedPort = 2080;
            return $$"""
            {
              "log": {
                "level": "warn",
                "timestamp": true
              },
              "inbounds": [
                {
                  "type": "mixed",
                  "tag": "mixed-in",
                  "listen": "127.0.0.1",
                  "listen_port": {{mixedPort}}
                }
              ],
              "outbounds": [
                {
                  "type": "vless",
                  "tag": "proxy",
                  "server": "example.com",
                  "server_port": 443,
                  "uuid": "00000000-0000-0000-0000-000000000000",
                  "tls": {
                    "enabled": true,
                    "server_name": "example.com"
                  }
                },
                {
                  "type": "direct",
                  "tag": "direct"
                }
              ],
              "route": {
                "auto_detect_interface": true,
                "final": "proxy"
              }
            }
            """;
        }

        public static void EnsureRuleSetsCopied()
        {
            try
            {
                Directory.CreateDirectory(AppDataDirectory);
                var sourceRulesDir = Path.Combine(AppContext.BaseDirectory, "Assets", "rules");
                if (!Directory.Exists(sourceRulesDir)) return;

                foreach (var file in Directory.GetFiles(sourceRulesDir, "*.srs"))
                {
                    var destFile = Path.Combine(AppDataDirectory, Path.GetFileName(file));
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
            catch { }
        }

        public static async Task<(bool Ok, string Message)> ValidateWithCoreAsync(string jsonText, CancellationToken ct = default)
        {
            var summary = InspectConfig(jsonText);
            if (!summary.IsValidJson)
            {
                return (false, summary.ParseError ?? L.CustomCfg_ErrInvalidJson);
            }

            string exePath = CoreManager.Instance.ExePath;
            if (!File.Exists(exePath))
            {
                return (false, L.CustomCfg_CoreMissingError);
            }

            EnsureRuleSetsCopied();
            string tempFile = Path.Combine(AppDataDirectory, $"check_{Guid.NewGuid():N}.json");

            try
            {
                await File.WriteAllTextAsync(tempFile, jsonText, Utf8NoBom, ct).ConfigureAwait(false);

                var psi = new ProcessStartInfo
                {
                    FileName = exePath,
                    Arguments = $"check -c \"{tempFile}\"",
                    WorkingDirectory = AppDataDirectory,
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                };

                using var proc = new Process { StartInfo = psi };
                proc.Start();

                var stdoutTask = proc.StandardOutput.ReadToEndAsync(ct);
                var stderrTask = proc.StandardError.ReadToEndAsync(ct);

                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeoutCts.CancelAfter(TimeSpan.FromSeconds(8));

                await proc.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
                string stdout = (await stdoutTask.ConfigureAwait(false)).Trim();
                string stderr = (await stderrTask.ConfigureAwait(false)).Trim();

                if (proc.ExitCode == 0)
                {
                    string okDetail = Loc.Format(
                        "CustomCfg_CheckPassedFmt",
                        summary.InboundCount,
                        summary.OutboundCount,
                        summary.RuleCount);
                    return (true, okDetail);
                }

                string combined = string.IsNullOrEmpty(stderr) ? stdout : stderr;
                if (string.IsNullOrEmpty(combined))
                    combined = $"Exit code {proc.ExitCode}";

                return (false, combined);
            }
            catch (OperationCanceledException)
            {
                return (false, L.CustomCfg_CheckTimeout);
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
            finally
            {
                try
                {
                    if (File.Exists(tempFile))
                        File.Delete(tempFile);
                }
                catch { }
            }
        }

        public static async Task<string> BuildRuntimeConfigAsync(PersistedNode node)
        {
            string rawJson = await ReadConfigTextAsync(node).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(rawJson))
            {
                throw new InvalidOperationException(L.CustomCfg_ErrFileMissingOrEmpty);
            }

            var summary = InspectConfig(rawJson);
            if (!summary.IsValidJson)
            {
                throw new InvalidOperationException(summary.ParseError ?? L.CustomCfg_ErrInvalidJson);
            }

            var session = AppSession.Instance;
            session.IsActiveNodeCustom = true;
            session.ActiveCustomHasTun = summary.HasTunInbound;

            if (!node.CustomBridgeGui)
            {
                // Pure Raw Mode: pass user JSON completely untouched; do not mutate OS system proxy.
                session.ActiveCustomHttpProxyPort = summary.LocalHttpPort;
                session.ActiveCustomReadyPort = summary.LocalHttpPort ?? summary.AnyListenPort ?? 0;
                session.ActiveCustomSkipSystemProxy = true;
                session.ClashApiSecret = string.Empty;
                return rawJson;
            }

            var parsed = JsonNode.Parse(
                rawJson,
                nodeOptions: null,
                documentOptions: new JsonDocumentOptions
                {
                    CommentHandling = JsonCommentHandling.Skip,
                    AllowTrailingCommas = true,
                    MaxDepth = 64
                });

            if (parsed is not JsonObject rootObj)
            {
                throw new InvalidOperationException(L.CustomCfg_ErrRootNotObject);
            }

            // 1. Merge experimental.clash_api without clobbering existing experimental fields (e.g. cache_file)
            if (rootObj["experimental"] is not JsonObject expObj)
            {
                expObj = new JsonObject();
                rootObj["experimental"] = expObj;
            }

            if (expObj["clash_api"] is JsonObject existingClashApi &&
                existingClashApi["external_controller"]?.GetValue<string>() is string existingCtrl &&
                !string.IsNullOrWhiteSpace(existingCtrl))
            {
                session.ClashApiSecret = existingClashApi["secret"]?.GetValue<string>() ?? string.Empty;
            }
            else
            {
                var apiSecret = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
                session.ClashApiSecret = apiSecret;
                expObj["clash_api"] = new JsonObject
                {
                    ["external_controller"] = "127.0.0.1:9090",
                    ["external_ui"] = "",
                    ["external_ui_download_url"] = "",
                    ["external_ui_download_detour"] = "",
                    ["secret"] = apiSecret,
                    ["default_mode"] = "rule"
                };
            }

            // 2. Inject sing-box >= 1.14 dashboard service on 127.0.0.1:9091 if not already present
            var coreVersion = CoreUpdateService.GetLocalSingboxNumericVersion();
            bool isSingbox114OrAbove = coreVersion == null || coreVersion >= new Version(1, 14, 0);
            if (isSingbox114OrAbove)
            {
                if (rootObj["services"] is not JsonArray servicesArr)
                {
                    servicesArr = new JsonArray();
                    rootObj["services"] = servicesArr;
                }

                bool has9091OrApi = false;
                foreach (var svc in servicesArr)
                {
                    if (svc is JsonObject svcObj)
                    {
                        string? svcType = svcObj["type"]?.GetValue<string>();
                        int? svcPort = null;
                        if (svcObj["listen_port"] is JsonValue pv && pv.TryGetValue<int>(out int pVal))
                            svcPort = pVal;

                        if (svcPort == 9091 || string.Equals(svcType, "api", StringComparison.OrdinalIgnoreCase))
                        {
                            has9091OrApi = true;
                            break;
                        }
                    }
                }

                if (!has9091OrApi)
                {
                    servicesArr.Add(new JsonObject
                    {
                        ["type"] = "api",
                        ["tag"] = "sing-box dashboard",
                        ["listen"] = "127.0.0.1",
                        ["listen_port"] = 9091,
                        ["secret"] = "",
                        ["access_control_allow_origin"] = new JsonArray
                        {
                            (JsonNode)"http://sing-box-dashboard.sagernet.org",
                            (JsonNode)"https://sing-box-dashboard.sagernet.org",
                            (JsonNode)"http://dash.sing-box.app",
                            (JsonNode)"https://dash.sing-box.app"
                        },
                        ["access_control_allow_private_network"] = true,
                        ["dashboard"] = true
                    });
                }
            }

            session.ActiveCustomHttpProxyPort = summary.LocalHttpPort;
            session.ActiveCustomReadyPort = summary.LocalHttpPort ?? summary.AnyListenPort ?? 9090;
            session.ActiveCustomSkipSystemProxy = !summary.LocalHttpPort.HasValue;

            var outBuffer = new ArrayBufferWriter<byte>(Math.Max(1024, rawJson.Length + 512));
            using (var writer = new Utf8JsonWriter(outBuffer, new JsonWriterOptions
            {
                Indented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            }))
            {
                rootObj.WriteTo(writer);
            }

            return Encoding.UTF8.GetString(outBuffer.WrittenSpan);
        }
    }
}

