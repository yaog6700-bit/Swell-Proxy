using System;
using AnywhereWinUI.Helpers;
using AnywhereWinUI.Models;
using AnywhereWinUI.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Windows.ApplicationModel.DataTransfer;

namespace AnywhereWinUI.Views
{
    public sealed partial class ConnectionsPage : Page
    {
        public ConnectionsViewModel ViewModel { get; }

        public ConnectionsPage()
        {
            NavigationCacheMode = Microsoft.UI.Xaml.Navigation.NavigationCacheMode.Disabled;
            this.InitializeComponent();
            ViewModel = App.Current.Services.GetRequiredService<ConnectionsViewModel>();
        }

        public Visibility BoolToVisibility(bool value) => value ? Visibility.Visible : Visibility.Collapsed;

        public Visibility InvertBoolToVisibility(bool value) => value ? Visibility.Collapsed : Visibility.Visible;

        private void OnActualThemeChanged(FrameworkElement sender, object args)
        {
            foreach (var item in ViewModel.Connections) item.RefreshThemeColors();
        }

        protected override async void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            this.ActualThemeChanged += OnActualThemeChanged;
            await ViewModel.OnNavigatedToAsync();
        }

        protected override async void OnNavigatedFrom(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            this.ActualThemeChanged -= OnActualThemeChanged;
            await ViewModel.OnNavigatedFromAsync();
            this.Bindings.StopTracking();
        }

        private void AutoSuggestBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
        {
            if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
            {
                ViewModel.SearchText = sender.Text;
            }
        }

        private void ToggleTopologyButton_Click(object sender, RoutedEventArgs e)
        {
            bool isVisible = TopologyGraphHost.Visibility == Visibility.Visible;
            TopologyGraphHost.Visibility = isVisible ? Visibility.Collapsed : Visibility.Visible;
            TopologyChevronIcon.Glyph = isVisible ? "\uE70D" : "\uE70E";
        }

        private void ToggleAppRankButton_Click(object sender, RoutedEventArgs e)
        {
            bool isVisible = AppRankContentPanel.Visibility == Visibility.Visible;
            AppRankContentPanel.Visibility = isVisible ? Visibility.Collapsed : Visibility.Visible;
            AppRankChevronIcon.Glyph = isVisible ? "\uE70D" : "\uE70E";
        }

        private void AppRankingsListView_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is AppTrafficItem appItem)
            {
                ViewModel.ToggleAppFilter(appItem.AppName);
            }
        }

        private void ProcessCellButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is ConnectionItem conn)
            {
                ViewModel.ToggleAppFilter(conn.ProcessName);
            }
        }

        private void AppQuickRouteButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement anchor || anchor.Tag is not AppTrafficItem appItem)
                return;

            var flyout = BuildAppQuickRouteFlyout(appItem);
            flyout.ShowAt(anchor, new FlyoutShowOptions { Placement = FlyoutPlacementMode.BottomEdgeAlignedRight });
        }

        private void ConnQuickRouteButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement anchor || anchor.Tag is not ConnectionItem conn)
                return;

            var flyout = BuildConnectionQuickRouteFlyout(conn);
            flyout.ShowAt(anchor, new FlyoutShowOptions { Placement = FlyoutPlacementMode.BottomEdgeAlignedRight });
        }

        private void ConnectionsListView_RightTapped(object sender, RightTappedRoutedEventArgs e)
        {
            if (e.OriginalSource is FrameworkElement fe && fe.DataContext is ConnectionItem conn)
            {
                var flyout = BuildConnectionQuickRouteFlyout(conn);
                flyout.ShowAt(fe, new FlyoutShowOptions { Position = e.GetPosition(fe) });
                e.Handled = true;
            }
        }

        private async void ConnCloseButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is ConnectionItem conn)
            {
                await ViewModel.CloseSingleConnectionAsync(conn);
            }
        }

        private MenuFlyout BuildAppQuickRouteFlyout(AppTrafficItem appItem)
        {
            var flyout = new MenuFlyout();

            flyout.Items.Add(new MenuFlyoutItem
            {
                Text = string.Format(L.Conn_QuickRuleProcHeaderFmt, appItem.AppName),
                IsEnabled = false
            });

            AddRouteTargetsToItems(
                flyout.Items,
                type: "process",
                match: appItem.AppName,
                displayName: appItem.AppName);

            flyout.Items.Add(new MenuFlyoutSeparator());

            var filterItem = new MenuFlyoutItem
            {
                Text = appItem.IsSelectedFilter
                    ? L.Conn_ClearAppFilterMenu
                    : string.Format(L.Conn_FilterThisAppFmt, appItem.AppName),
                Icon = new FontIcon { Glyph = "\uE71C" }
            };
            filterItem.Click += (_, _) => ViewModel.ToggleAppFilter(appItem.AppName);
            flyout.Items.Add(filterItem);

            var copyProcItem = new MenuFlyoutItem
            {
                Text = string.Format(L.Conn_CopyProcMenuFmt, appItem.AppName),
                Icon = new FontIcon { Glyph = "\uE8C8" }
            };
            copyProcItem.Click += (_, _) => CopyTextToClipboard(appItem.AppName);
            flyout.Items.Add(copyProcItem);

            return flyout;
        }

        private MenuFlyout BuildConnectionQuickRouteFlyout(ConnectionItem conn)
        {
            var flyout = new MenuFlyout();

            // 1. Primary Site Domain (*.openai.com) or IP rule (just like bowel's actRouteBtn)
            if (!string.IsNullOrWhiteSpace(conn.SiteDomain))
            {
                flyout.Items.Add(new MenuFlyoutItem
                {
                    Text = string.Format(L.Conn_QuickRuleSiteHeaderFmt, conn.SiteDomain),
                    IsEnabled = false
                });

                AddRouteTargetsToItems(
                    flyout.Items,
                    type: "domain",
                    match: $"domain_suffix:{conn.SiteDomain}",
                    displayName: $"*.{conn.SiteDomain}");

                // Submenu for exact subdomain if different from registrable site domain
                if (conn.HasDistinctSubdomain)
                {
                    flyout.Items.Add(new MenuFlyoutSeparator());
                    var exactSubItem = new MenuFlyoutSubItem
                    {
                        Text = string.Format(L.Conn_QuickRuleExactSubmenuFmt, conn.RawHost),
                        Icon = new FontIcon { Glyph = "\uE774" }
                    };
                    AddRouteTargetsToItems(
                        exactSubItem.Items,
                        type: "domain",
                        match: $"domain:{conn.RawHost.ToLowerInvariant()}",
                        displayName: conn.RawHost.ToLowerInvariant());
                    flyout.Items.Add(exactSubItem);
                }
            }
            else if (!string.IsNullOrWhiteSpace(conn.RawIp))
            {
                string cidr = conn.RawIp.Contains(':') ? $"{conn.RawIp}/128" : $"{conn.RawIp}/32";
                flyout.Items.Add(new MenuFlyoutItem
                {
                    Text = string.Format(L.Conn_QuickRuleIpHeaderFmt, conn.RawIp),
                    IsEnabled = false
                });

                AddRouteTargetsToItems(
                    flyout.Items,
                    type: "ip",
                    match: $"ip_cidr:{cidr}",
                    displayName: conn.RawIp);
            }

            // 2. Process-level routing submenu (when process is resolved)
            if (conn.HasProcess)
            {
                flyout.Items.Add(new MenuFlyoutSeparator());
                var procSubItem = new MenuFlyoutSubItem
                {
                    Text = string.Format(L.Conn_QuickRuleProcSubmenuFmt, conn.ProcessName),
                    Icon = new FontIcon { Glyph = "\uECAA" }
                };
                AddRouteTargetsToItems(
                    procSubItem.Items,
                    type: "process",
                    match: conn.ProcessName,
                    displayName: conn.ProcessName);
                flyout.Items.Add(procSubItem);
            }

            flyout.Items.Add(new MenuFlyoutSeparator());

            // 3. Filter & Copy helpers
            if (conn.HasProcess)
            {
                bool isFilteredToThis = ViewModel.HasAppFilter &&
                    string.Equals(ViewModel.SelectedAppFilter, conn.ProcessName, StringComparison.OrdinalIgnoreCase);

                var filterAppItem = new MenuFlyoutItem
                {
                    Text = isFilteredToThis
                        ? L.Conn_ClearAppFilterMenu
                        : string.Format(L.Conn_FilterThisAppFmt, conn.ProcessName),
                    Icon = new FontIcon { Glyph = "\uE71C" }
                };
                filterAppItem.Click += (_, _) => ViewModel.ToggleAppFilter(conn.ProcessName);
                flyout.Items.Add(filterAppItem);
            }

            string copyTarget = !string.IsNullOrWhiteSpace(conn.RawHost)
                ? conn.RawHost
                : (!string.IsNullOrWhiteSpace(conn.RawIp) ? conn.RawIp : conn.Host);

            if (!string.IsNullOrWhiteSpace(copyTarget))
            {
                var copyHostItem = new MenuFlyoutItem
                {
                    Text = string.Format(L.Conn_CopyHostMenuFmt, copyTarget),
                    Icon = new FontIcon { Glyph = "\uE8C8" }
                };
                copyHostItem.Click += (_, _) => CopyTextToClipboard(copyTarget);
                flyout.Items.Add(copyHostItem);
            }

            if (conn.HasProcess)
            {
                var copyProcItem = new MenuFlyoutItem
                {
                    Text = string.Format(L.Conn_CopyProcMenuFmt, conn.ProcessName),
                    Icon = new FontIcon { Glyph = "\uE8C8" }
                };
                copyProcItem.Click += (_, _) => CopyTextToClipboard(conn.ProcessName);
                flyout.Items.Add(copyProcItem);
            }

            var closeConnItem = new MenuFlyoutItem
            {
                Text = L.Conn_CloseConnTooltip,
                Icon = new FontIcon { Glyph = "\uE711" }
            };
            closeConnItem.Click += async (_, _) => await ViewModel.CloseSingleConnectionAsync(conn);
            flyout.Items.Add(closeConnItem);

            return flyout;
        }

        private void AddRouteTargetsToItems(
            System.Collections.Generic.IList<MenuFlyoutItemBase> items,
            string type,
            string match,
            string displayName)
        {
            var proxyItem = new MenuFlyoutItem
            {
                Text = L.Routing_ActionProxy,
                Icon = new FontIcon { Glyph = "\uE724" }
            };
            proxyItem.Click += async (_, _) => await ViewModel.ApplyQuickRuleAsync(type, match, displayName, "proxy");
            items.Add(proxyItem);

            var directItem = new MenuFlyoutItem
            {
                Text = L.Routing_ActionDirect,
                Icon = new FontIcon { Glyph = "\uE945" }
            };
            directItem.Click += async (_, _) => await ViewModel.ApplyQuickRuleAsync(type, match, displayName, "direct");
            items.Add(directItem);

            var blockItem = new MenuFlyoutItem
            {
                Text = L.Routing_ActionBlock,
                Icon = new FontIcon { Glyph = "\uE733" }
            };
            blockItem.Click += async (_, _) => await ViewModel.ApplyQuickRuleAsync(type, match, displayName, "block");
            items.Add(blockItem);
        }

        private void CopyTextToClipboard(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            try
            {
                var pkg = new DataPackage();
                pkg.SetText(text);
                Clipboard.SetContent(pkg);
                ViewModel.ShowToast(string.Format(L.Conn_CopiedToastFmt, text));
            }
            catch
            {
            }
        }
    }
}
