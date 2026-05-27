using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using AnywhereWinUI.ViewModels;

namespace AnywhereWinUI.Controls
{
    public sealed partial class TopologyGraphControl : UserControl
    {
        public TopologyViewModel ViewModel { get; } = new TopologyViewModel();

        public TopologyGraphControl()
        {
            this.InitializeComponent();
            this.Loaded += UserControl_Loaded;
            this.Unloaded += UserControl_Unloaded;
        }

        private void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            ViewModel.IsActive = true;
        }

        private void UserControl_Unloaded(object sender, RoutedEventArgs e)
        {
            ViewModel.IsActive = false;
            ViewModel.Unsubscribe(); // 取消事件订阅，防止页面卸载后仍收到回调
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
