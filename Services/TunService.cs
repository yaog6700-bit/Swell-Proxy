using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace AnywhereWinUI.Services
{
    /// <summary>
    /// TUN 模式辅助服务。
    /// 负责：(1) 探测默认出站网络接口；(2) 异常退出后兜底清理残留 TUN 路由。
    /// </summary>
    public class TunService
    {
        /// <summary>与 ConfigBuilder.BuildInboundsAsync 中的 "interface_name" 保持一致。</summary>
        public const string DefaultTunInterfaceName = "singbox-tun";

        public void CleanupTunRoutes(string? serverAddress)
        {
            // Never trigger an interactive UAC prompt during route cleanup; if unelevated, TUN wasn't created by us or cannot be cleaned silently.
            if (!Helpers.AdminHelper.IsAdministrator())
                return;

            try
            {
                var batch = new List<string>
                {
                    $"netsh interface ipv4 delete route 0.0.0.0/0 \"{DefaultTunInterfaceName}\" store=active",
                    $"netsh interface ipv4 delete route 0.0.0.0/1 \"{DefaultTunInterfaceName}\" store=active",
                    $"netsh interface ipv4 delete route 128.0.0.0/1 \"{DefaultTunInterfaceName}\" store=active",
                    "netsh interface ipv4 delete route 0.0.0.0/0 \"tun0\" store=active",
                    "netsh interface ipv4 delete route 0.0.0.0/1 \"tun0\" store=active",
                    "netsh interface ipv4 delete route 128.0.0.0/1 \"tun0\" store=active",
                    $"netsh interface ipv6 delete route ::/0 \"{DefaultTunInterfaceName}\" store=active",
                    $"netsh interface ipv6 delete route ::/1 \"{DefaultTunInterfaceName}\" store=active",
                    $"netsh interface ipv6 delete route 8000::/1 \"{DefaultTunInterfaceName}\" store=active",
                };

                if (TryParseSafeIPv4(serverAddress, out var ip))
                {
                    batch.Add($"netsh interface ipv4 delete route {ip}/32 \"{DefaultTunInterfaceName}\" store=active");
                }

                RunBatchAdmin(batch);
                Debug.WriteLine("[TunService] TUN 路由兜底清理完成");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[TunService] TUN 路由清理失败: {ex.Message}");
            }
        }

        public string? DetectDefaultOutboundInterfaceName()
        {
            try
            {
                IPAddress? outboundAddr;
                using (var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
                {
                    socket.Connect("223.5.5.5", 53);
                    outboundAddr = (socket.LocalEndPoint as IPEndPoint)?.Address;
                }

                if (outboundAddr == null)
                {
                    Debug.WriteLine("[TunService] 无法获取默认出站 IPv4 地址");
                    return null;
                }

                string? vEthernetFallback = null;

                foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (nic.OperationalStatus != OperationalStatus.Up) continue;
                    if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback ||
                        nic.NetworkInterfaceType == NetworkInterfaceType.Tunnel) continue;

                    var label = $"{nic.Name} {nic.Description}";
                    if (ContainsAny(label, DefaultTunInterfaceName, "tun0", "wintun", "singbox", "sing-box",
                                    "loopback", "pseudo-interface", "virtualbox", "vmware", "wsl", "default switch"))
                        continue;

                    foreach (var ua in nic.GetIPProperties().UnicastAddresses)
                    {
                        if (ua.Address.AddressFamily == AddressFamily.InterNetwork &&
                            ua.Address.Equals(outboundAddr))
                        {
                            // Prefer real physical NIC, but allow Hyper-V External Virtual Switch if it owns the host's primary IP
                            if (ContainsAny(label, "hyper-v virtual", "vethernet"))
                            {
                                vEthernetFallback ??= nic.Name;
                                continue;
                            }

                            Debug.WriteLine($"[TunService] 默认出站网卡: {nic.Name} ({outboundAddr})");
                            return nic.Name;
                        }
                    }
                }

                if (vEthernetFallback != null)
                {
                    Debug.WriteLine($"[TunService] 使用 Hyper-V 外部虚拟交换机网卡: {vEthernetFallback} ({outboundAddr})");
                    return vEthernetFallback;
                }

                Debug.WriteLine($"[TunService] 未能将出站地址 {outboundAddr} 映射到网卡");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[TunService] 网卡探测失败: {ex.Message}");
            }
            return null;
        }

        private static bool TryParseSafeIPv4(string? value, out string address)
        {
            address = string.Empty;
            if (string.IsNullOrWhiteSpace(value)) return false;
            if (!IPAddress.TryParse(value, out var ip) ||
                ip.AddressFamily != AddressFamily.InterNetwork) return false;
            address = ip.ToString();
            return true;
        }

        private static bool ContainsAny(string value, params string[] needles)
        {
            foreach (var n in needles)
                if (value.Contains(n, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static void RunBatchAdmin(IReadOnlyList<string> commandLines)
        {
            if (commandLines.Count == 0) return;

            var combined = string.Join(" & ", commandLines);
            var cmdPath  = Path.Combine(Environment.SystemDirectory, "cmd.exe");

            var psi = new ProcessStartInfo
            {
                FileName               = cmdPath,
                Arguments              = "/c " + combined,
                WindowStyle            = ProcessWindowStyle.Hidden,
                CreateNoWindow         = true,
                UseShellExecute        = false,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
            };

            try
            {
                using var proc = Process.Start(psi);
                proc?.WaitForExit(4000);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[TunService] 批处理执行失败: {ex.Message}");
            }
        }
    }
}
