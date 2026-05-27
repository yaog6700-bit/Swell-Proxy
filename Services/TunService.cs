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
    /// 负责：(1) 探测默认出站网络接口；(2) 退出/异常时清理残留 TUN 路由。
    /// sing-box 自身（以管理员权限运行）通过 auto_route=true 接管路由；
    /// 此类只是 sing-box 异常退出时的兜底清理。
    /// </summary>
    public class TunService
    {
        /// <summary>与 ConfigBuilder.BuildTunInbound 中的 "tag" 字段保持一致。</summary>
        private const string DefaultTunInterfaceName = "singbox-tun";

        // ── Route cleanup ─────────────────────────────────────────────────────

        /// <summary>
        /// 兜底清理：sing-box 正常退出会自己删除路由；此方法仅在异常退出后调用。
        /// </summary>
        public void CleanupTunRoutes(string? serverAddress)
        {
            try
            {
                var batch = new List<string>
                {
                    $"netsh interface ipv4 delete route 0.0.0.0/0 \"{DefaultTunInterfaceName}\" store=active",
                    $"netsh interface ipv4 delete route 0.0.0.0/1 \"{DefaultTunInterfaceName}\" store=active",
                    $"netsh interface ipv4 delete route 128.0.0.0/1 \"{DefaultTunInterfaceName}\" store=active",
                    "route delete 0.0.0.0 mask 128.0.0.0",
                    "route delete 128.0.0.0 mask 128.0.0.0",
                };

                if (TryParseSafeIPv4(serverAddress, out var ip))
                {
                    batch.Add($"netsh interface ipv4 delete route {ip}/32 \"{DefaultTunInterfaceName}\" store=active");
                    batch.Add($"route delete {ip} mask 255.255.255.255");
                }

                RunBatch(batch);
                Debug.WriteLine("[TunService] TUN 路由兜底清理完成");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[TunService] TUN 路由清理失败: {ex.Message}");
            }
        }

        // ── Interface detection ───────────────────────────────────────────────

        /// <summary>
        /// 探测当前系统用于默认 IPv4 出站流量的物理网卡名称。
        /// TUN 模式下 sing-box 需要知道真实物理网卡，以避免出站流量再次进入 TUN 形成循环。
        /// </summary>
        public string? DetectDefaultOutboundInterfaceName()
        {
            try
            {
                IPAddress? outboundAddr;
                using (var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
                {
                    socket.Connect("8.8.8.8", 53);
                    outboundAddr = (socket.LocalEndPoint as IPEndPoint)?.Address;
                }

                if (outboundAddr == null)
                {
                    Debug.WriteLine("[TunService] 无法获取默认出站 IPv4 地址");
                    return null;
                }

                foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (nic.OperationalStatus != OperationalStatus.Up) continue;
                    if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback ||
                        nic.NetworkInterfaceType == NetworkInterfaceType.Tunnel) continue;

                    var label = $"{nic.Name} {nic.Description}";
                    if (ContainsAny(label, DefaultTunInterfaceName, "wintun", "singbox",
                                    "loopback", "pseudo-interface", "virtualbox",
                                    "vmware", "hyper-v virtual", "vethernet"))
                        continue;

                    foreach (var ua in nic.GetIPProperties().UnicastAddresses)
                    {
                        if (ua.Address.AddressFamily == AddressFamily.InterNetwork &&
                            ua.Address.Equals(outboundAddr))
                        {
                            Debug.WriteLine($"[TunService] 默认出站网卡: {nic.Name} ({outboundAddr})");
                            return nic.Name;
                        }
                    }
                }

                Debug.WriteLine($"[TunService] 未能将出站地址 {outboundAddr} 映射到网卡");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[TunService] 网卡探测失败: {ex.Message}");
            }
            return null;
        }

        // ── Helpers ───────────────────────────────────────────────────────────

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

        /// <summary>
        /// 在 elevated cmd.exe 中批量执行命令（一次 UAC 提示，失败不中断）。
        /// 如果当前已是管理员，则无需 UAC，直接重定向标准输出执行。
        /// </summary>
        private static void RunBatch(IReadOnlyList<string> commandLines)
        {
            if (commandLines.Count == 0) return;

            var combined = string.Join(" & ", commandLines);
            var cmdPath  = Path.Combine(Environment.SystemDirectory, "cmd.exe");
            var isAdmin  = Helpers.AdminHelper.IsAdministrator();

            var psi = new ProcessStartInfo
            {
                FileName         = cmdPath,
                Arguments        = "/c " + combined,
                WindowStyle      = ProcessWindowStyle.Hidden,
                CreateNoWindow   = true,
                UseShellExecute  = !isAdmin,
            };

            if (isAdmin)
            {
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError  = true;
            }
            else
            {
                psi.Verb = "runas";
            }

            try
            {
                using var proc = Process.Start(psi);
                proc?.WaitForExit(5000);
            }
            catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
            {
                Debug.WriteLine("[TunService] 管理员授权被用户取消");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[TunService] 批处理执行失败: {ex.Message}");
            }
        }
    }
}
