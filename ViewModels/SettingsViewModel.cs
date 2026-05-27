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
        private bool _autoStart;

        [ObservableProperty]
        private string _coreVersionText = "Checking...";

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

            try
            {
                _autoStart = AutostartManager.IsAutostartEnabled();
            }
            catch { }

            Task.Run(() =>
            {
                var ver = CoreUpdateService.GetLocalSingboxVersionText();
                Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread()?.TryEnqueue(() =>
                {
                    CoreVersionText = $"当前版本: {ver}";
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

        partial void OnAutoStartChanged(bool value)
        {
            try
            {
                if (value)
                    AutostartManager.EnableAutostart();
                else
                    AutostartManager.DisableAutostart();
            }
            catch { }
        }

        private async Task TriggerCoreRestartIfNeeded()
        {
            if (CoreManager.Instance.IsRunning)
            {
                CoreManager.Instance.AppendLog("[系统] 检测到路由分流规则更新，正在自动重启代理引擎以重新加载配置...");
                var node = NodesManager.Instance.Nodes.Find(n => n.Id == NodesManager.Instance.SelectedNodeId);
                string realConfig = node != null ? ConfigBuilder.Build(node) : ConfigBuilder.Build();
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
