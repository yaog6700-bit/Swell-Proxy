using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Windows.UI;
using AnywhereWinUI.Services;
using AnywhereWinUI.Helpers;

namespace AnywhereWinUI.ViewModels
{
    public partial class SettingsViewModel : ObservableObject
    {
        [ObservableProperty]
        private Color _ssColor;

        [ObservableProperty]
        private Color _vlessColor;

        [ObservableProperty]
        private Color _vmessColor;

        [ObservableProperty]
        private Color _hysteria2Color;

        [ObservableProperty]
        private Color _trojanColor;

        [ObservableProperty]
        private Color _fallbackColor;

        [ObservableProperty]
        private bool _bypassChina;

        [ObservableProperty]
        private bool _blockAds;

        [ObservableProperty]
        private bool _blockIPv6;

        [ObservableProperty]
        private bool _flushDNS;

        [ObservableProperty]
        private bool _enableAdvancedRouting;

        [ObservableProperty]
        private string _directDns = "223.5.5.5";

        [ObservableProperty]
        private string _proxyDns = "https://1.1.1.1/dns-query";

        [ObservableProperty]
        private string _dnsStrategy = "prefer_ipv4";

        [ObservableProperty]
        private bool _enableDnsCache = true;

        [ObservableProperty]
        private bool _enableFakeDns;

        [ObservableProperty]
        private bool _enableClassicDashboard;

        [ObservableProperty]
        private bool _enablePlugins;

        [ObservableProperty]
        private bool _isPrivacyModeActive;

        [ObservableProperty]
        private bool _autoStart;

        [ObservableProperty]
        private int _mixedPort = 2080;

        // Guard flag: prevents OnAutoStartChanged from writing to the registry
        // while LoadSettings() is initialising the property from the registry.
        private bool _isLoading;

        [ObservableProperty]
        private string _coreVersionText = "Checking...";

        [ObservableProperty]
        private string _appVersionText = "Checking...";

        // ── Tailscale Endpoint ───────────────────────────────────────────────
        [ObservableProperty]
        private bool _enableTailscale;

        [ObservableProperty]
        private string _tailscaleAuthKey = string.Empty;

        [ObservableProperty]
        private string _tailscaleHostname = string.Empty;

        [ObservableProperty]
        private bool _tailscaleEphemeral;

        [ObservableProperty]
        private string _tailscaleStateDirectory = string.Empty;

        [ObservableProperty]
        private string _tailscaleControlUrl = string.Empty;

        [ObservableProperty]
        private bool _tailscaleAcceptRoutes;

        [ObservableProperty]
        private string _tailscaleAdvertiseRoutes = string.Empty;

        [ObservableProperty]
        private string _tailscaleExitNode = string.Empty;

        [ObservableProperty]
        private bool _tailscaleAdvertiseExitNode;

        public SettingsViewModel()
        {
            LoadSettings();
        }

        public void LoadSettings()
        {
            _ssColor = ProtocolColorStore.Ss;
            _vlessColor = ProtocolColorStore.Vless;
            _vmessColor = ProtocolColorStore.Vmess;
            _hysteria2Color = ProtocolColorStore.Hysteria2;
            _trojanColor = ProtocolColorStore.Trojan;
            _fallbackColor = ProtocolColorStore.Fallback;

            _bypassChina = AppSession.Instance.BypassChina;
            _blockAds = AppSession.Instance.BlockAds;
            _blockIPv6 = AppSession.Instance.BlockIPv6;
            _flushDNS = AppSession.Instance.FlushDNS;
            _enableAdvancedRouting = AppSession.Instance.EnableAdvancedRouting;

            _directDns = AppSession.Instance.DirectDns;
            _proxyDns = AppSession.Instance.ProxyDns;
            _dnsStrategy = AppSession.Instance.DnsStrategy;
            _enableDnsCache = AppSession.Instance.EnableDnsCache;
            _enableFakeDns = AppSession.Instance.EnableFakeDns;
            _enableClassicDashboard = AppSession.Instance.EnableClassicDashboard;
            _enablePlugins = AppSession.Instance.EnablePlugins;
            _isPrivacyModeActive = AppSession.Instance.IsPrivacyModeActive;

            // Tailscale
            _enableTailscale = AppSession.Instance.EnableTailscale;
            _tailscaleAuthKey = AppSession.Instance.TailscaleAuthKey;
            _tailscaleHostname = AppSession.Instance.TailscaleHostname;
            _tailscaleEphemeral = AppSession.Instance.TailscaleEphemeral;
            _tailscaleStateDirectory = AppSession.Instance.TailscaleStateDirectory;
            _tailscaleControlUrl = AppSession.Instance.TailscaleControlUrl;
            _tailscaleAcceptRoutes = AppSession.Instance.TailscaleAcceptRoutes;
            _tailscaleAdvertiseRoutes = AppSession.Instance.TailscaleAdvertiseRoutes;
            _tailscaleExitNode = AppSession.Instance.TailscaleExitNode;
            _tailscaleAdvertiseExitNode = AppSession.Instance.TailscaleAdvertiseExitNode;

            try
            {
                _isLoading = true;
                AutoStart = AutostartManager.IsAutostartEnabled();
                MixedPort = AppSession.Instance.MixedPort;
            }
            catch { }
            finally
            {
                _isLoading = false;
            }

            var dispatcher = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
            Task.Run(() =>
            {
                var ver = CoreUpdateService.GetLocalSingboxVersionText();
                var appVer = AppUpdateService.CurrentVersion;
                dispatcher?.TryEnqueue(() =>
                {
                    CoreVersionText = $"当前核心版本: {ver}";
                    AppVersionText = $"版本 v{appVer.Major}.{appVer.Minor}.{appVer.Build}";
                });
            });
        }

        public void SaveColors()
        {
            ProtocolColorStore.Ss = SsColor;
            ProtocolColorStore.Vless = VlessColor;
            ProtocolColorStore.Vmess = VmessColor;
            ProtocolColorStore.Hysteria2 = Hysteria2Color;
            ProtocolColorStore.Trojan = TrojanColor;
            ProtocolColorStore.Fallback = FallbackColor;
            ProtocolColorStore.NotifyColorsChanged();

            var mgr = NodesManager.Instance;
            mgr.ColorSs = $"#{SsColor.R:X2}{SsColor.G:X2}{SsColor.B:X2}";
            mgr.ColorVless = $"#{VlessColor.R:X2}{VlessColor.G:X2}{VlessColor.B:X2}";
            mgr.ColorVmess = $"#{VmessColor.R:X2}{VmessColor.G:X2}{VmessColor.B:X2}";
            mgr.ColorHysteria2 = $"#{Hysteria2Color.R:X2}{Hysteria2Color.G:X2}{Hysteria2Color.B:X2}";
            mgr.ColorTrojan = $"#{TrojanColor.R:X2}{TrojanColor.G:X2}{TrojanColor.B:X2}";
            mgr.ColorFallback = $"#{FallbackColor.R:X2}{FallbackColor.G:X2}{FallbackColor.B:X2}";
            mgr.Save();
        }

        partial void OnBypassChinaChanged(bool value)
        {
            AppSession.Instance.BypassChina = value;
            Helpers.LocalSettingsHelper.SetValue("bypassChina", value);
            _ = TriggerCoreRestartIfNeeded();
        }

        partial void OnBlockAdsChanged(bool value)
        {
            AppSession.Instance.BlockAds = value;
            Helpers.LocalSettingsHelper.SetValue("blockAds", value);
            _ = TriggerCoreRestartIfNeeded();
        }

        partial void OnBlockIPv6Changed(bool value)
        {
            AppSession.Instance.BlockIPv6 = value;
            Helpers.LocalSettingsHelper.SetValue("blockIPv6", value);
            _ = TriggerCoreRestartIfNeeded();
        }

        partial void OnFlushDNSChanged(bool value)
        {
            AppSession.Instance.FlushDNS = value;
            Helpers.LocalSettingsHelper.SetValue("flushDNS", value);
        }

        partial void OnEnableAdvancedRoutingChanged(bool value)
        {
            AppSession.Instance.EnableAdvancedRouting = value;
            Helpers.LocalSettingsHelper.SetValue("enableAdvancedRouting", value);
            // Notice MainWindow to update NavigationView
            MainWindow.Instance?.UpdateRoutingNavVisibility();
            _ = TriggerCoreRestartIfNeeded();
        }

        partial void OnDirectDnsChanged(string value)
        {
            AppSession.Instance.DirectDns = value;
            Helpers.LocalSettingsHelper.SetValue("directDns", value);
            _ = TriggerCoreRestartIfNeeded();
        }

        partial void OnProxyDnsChanged(string value)
        {
            AppSession.Instance.ProxyDns = value;
            Helpers.LocalSettingsHelper.SetValue("proxyDns", value);
            _ = TriggerCoreRestartIfNeeded();
        }

        partial void OnDnsStrategyChanged(string value)
        {
            AppSession.Instance.DnsStrategy = value;
            Helpers.LocalSettingsHelper.SetValue("dnsStrategy", value);
            _ = TriggerCoreRestartIfNeeded();
        }

        partial void OnEnableDnsCacheChanged(bool value)
        {
            AppSession.Instance.EnableDnsCache = value;
            Helpers.LocalSettingsHelper.SetValue("enableDnsCache", value);
            _ = TriggerCoreRestartIfNeeded();
        }

        partial void OnEnableFakeDnsChanged(bool value)
        {
            AppSession.Instance.EnableFakeDns = value;
            Helpers.LocalSettingsHelper.SetValue("enableFakeDns", value);
            _ = TriggerCoreRestartIfNeeded();
        }

        partial void OnEnableClassicDashboardChanged(bool value)
        {
            AppSession.Instance.EnableClassicDashboard = value;
            Helpers.LocalSettingsHelper.SetValue("enableClassicDashboard", value);
            MainWindow.Instance?.UpdateDashboardNavVisibility();
        }

        partial void OnEnablePluginsChanged(bool value)
        {
            AppSession.Instance.EnablePlugins = value;
            Helpers.LocalSettingsHelper.SetValue("enablePlugins", value);
            MainWindow.Instance?.UpdatePluginsNavVisibility();
        }

        partial void OnIsPrivacyModeActiveChanged(bool value)
        {
            AppSession.Instance.IsPrivacyModeActive = value;
            Helpers.LocalSettingsHelper.SetValue("isPrivacyModeActive", value);
        }

        partial void OnAutoStartChanged(bool value)
        {
            if (_isLoading) return;
            try
            {
                if (value)
                    AutostartManager.EnableAutostart();
                else
                    AutostartManager.DisableAutostart();
            }
            catch { }
        }

        partial void OnMixedPortChanged(int value)
        {
            if (_isLoading) return;
            if (value < 1 || value > 65535) return;
            AppSession.Instance.MixedPort = value;
            Helpers.LocalSettingsHelper.SetValue("mixedPort", value);
            _ = TriggerCoreRestartIfNeeded();
        }

        // ── Tailscale Change Handlers ─────────────────────────────────────────
        partial void OnEnableTailscaleChanged(bool value)
        {
            AppSession.Instance.EnableTailscale = value;
            Helpers.LocalSettingsHelper.SetValue("enableTailscale", value);
            _ = TriggerCoreRestartIfNeeded();
        }

        partial void OnTailscaleAuthKeyChanged(string value)
        {
            AppSession.Instance.TailscaleAuthKey = value;
            Helpers.LocalSettingsHelper.SetValue("tailscaleAuthKey", value);
        }

        partial void OnTailscaleHostnameChanged(string value)
        {
            AppSession.Instance.TailscaleHostname = value;
            Helpers.LocalSettingsHelper.SetValue("tailscaleHostname", value);
        }

        partial void OnTailscaleEphemeralChanged(bool value)
        {
            AppSession.Instance.TailscaleEphemeral = value;
            Helpers.LocalSettingsHelper.SetValue("tailscaleEphemeral", value);
        }

        partial void OnTailscaleStateDirectoryChanged(string value)
        {
            AppSession.Instance.TailscaleStateDirectory = value;
            Helpers.LocalSettingsHelper.SetValue("tailscaleStateDirectory", value);
        }

        partial void OnTailscaleControlUrlChanged(string value)
        {
            AppSession.Instance.TailscaleControlUrl = value;
            Helpers.LocalSettingsHelper.SetValue("tailscaleControlUrl", value);
        }

        partial void OnTailscaleAcceptRoutesChanged(bool value)
        {
            AppSession.Instance.TailscaleAcceptRoutes = value;
            Helpers.LocalSettingsHelper.SetValue("tailscaleAcceptRoutes", value);
        }

        partial void OnTailscaleAdvertiseRoutesChanged(string value)
        {
            AppSession.Instance.TailscaleAdvertiseRoutes = value;
            Helpers.LocalSettingsHelper.SetValue("tailscaleAdvertiseRoutes", value);
        }

        partial void OnTailscaleExitNodeChanged(string value)
        {
            AppSession.Instance.TailscaleExitNode = value;
            Helpers.LocalSettingsHelper.SetValue("tailscaleExitNode", value);
        }

        partial void OnTailscaleAdvertiseExitNodeChanged(bool value)
        {
            AppSession.Instance.TailscaleAdvertiseExitNode = value;
            Helpers.LocalSettingsHelper.SetValue("tailscaleAdvertiseExitNode", value);
        }

        private async Task TriggerCoreRestartIfNeeded()
        {
            if (CoreManager.Instance.IsRunning)
            {
                CoreManager.Instance.AppendLog("[系统] 检测到路由分流规则更新，正在自动重启代理引擎以重新加载配置...");
                var node = NodesManager.Instance.Nodes.Find(n => n.Id == NodesManager.Instance.SelectedNodeId);
                string realConfig = node != null ? await ConfigBuilder.BuildAsync(node) : await ConfigBuilder.BuildAsync();
                await CoreManager.Instance.StopAsync();
                await CoreManager.Instance.StartAsync(realConfig);
            }
        }

        partial void OnSsColorChanged(Color value) => SaveColors();
        partial void OnVlessColorChanged(Color value) => SaveColors();
        partial void OnVmessColorChanged(Color value) => SaveColors();
        partial void OnHysteria2ColorChanged(Color value) => SaveColors();
        partial void OnTrojanColorChanged(Color value) => SaveColors();
        partial void OnFallbackColorChanged(Color value) => SaveColors();

        [RelayCommand]
        private void ResetColors()
        {
            SsColor = Color.FromArgb(255, 96, 165, 250);
            VlessColor = Color.FromArgb(255, 52, 211, 153);
            VmessColor = Color.FromArgb(255, 167, 139, 250);
            Hysteria2Color = Color.FromArgb(255, 251, 146, 60);
            TrojanColor = Color.FromArgb(255, 248, 113, 113);
            FallbackColor = Color.FromArgb(255, 148, 163, 184);
            SaveColors();
        }
    }
}
