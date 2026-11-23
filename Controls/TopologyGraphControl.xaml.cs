using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using AnywhereWinUI.Services;
using AnywhereWinUI.ViewModels;

namespace AnywhereWinUI.Controls
{
    public sealed partial class TopologyGraphControl : UserControl
    {
        public TopologyViewModel ViewModel { get; } = new TopologyViewModel();
        private bool _isLoadedInTree;

        public TopologyGraphControl()
        {
            this.InitializeComponent();
            this.Loaded += UserControl_Loaded;
            this.Unloaded += UserControl_Unloaded;
        }

        private void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            _isLoadedInTree = true;
            MainWindow.VisibilityToUserChanged -= OnMainWindowVisibilityChanged;
            MainWindow.VisibilityToUserChanged += OnMainWindowVisibilityChanged;

            if (MainWindow.IsVisibleToUser)
            {
                ViewModel.Subscribe();
                ViewModel.IsActive = true;
                _ = SingboxApiClient.Instance.StartAsync();
            }
        }

        private void UserControl_Unloaded(object sender, RoutedEventArgs e)
        {
            _isLoadedInTree = false;
            MainWindow.VisibilityToUserChanged -= OnMainWindowVisibilityChanged;
            ViewModel.IsActive = false;
            ViewModel.Unsubscribe();
            ViewModel.Nodes.Clear();
            ViewModel.Links.Clear();
        }

        private void OnMainWindowVisibilityChanged(object? sender, bool isVisible)
        {
            DispatcherQueue?.TryEnqueue(() =>
            {
                if (!_isLoadedInTree) return;
                if (isVisible)
                {
                    ViewModel.Subscribe();
                    ViewModel.IsActive = true;
                    _ = SingboxApiClient.Instance.StartAsync();
                }
                else
                {
                    ViewModel.IsActive = false;
                    ViewModel.Unsubscribe();
                    ViewModel.Nodes.Clear();
                    ViewModel.Links.Clear();
                }
            });
        }

        private void UserControl_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (e.NewSize.Width > 0)
            {
                ViewModel.UpdateWidth(e.NewSize.Width);
            }
        }

        private void Node_PointerEntered(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (sender is FrameworkElement element && element.DataContext is AnywhereWinUI.Models.TopologyNode node)
            {
                ViewModel.HighlightNode(node.Id);
            }
        }

        private void Link_PointerEntered(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (sender is FrameworkElement element && element.DataContext is AnywhereWinUI.Models.TopologyLink link)
            {
                ViewModel.HighlightLink(link.SourceId, link.TargetId);
            }
        }

        private void Element_PointerExited(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            ViewModel.ClearHighlight();
        }
    }
}
