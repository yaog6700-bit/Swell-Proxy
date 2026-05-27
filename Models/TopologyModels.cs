using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml.Media;

namespace AnywhereWinUI.Models
{
    public partial class TopologyNode : ObservableObject
    {
        [ObservableProperty]
        private string _id = string.Empty;

        [ObservableProperty]
        private string _name = string.Empty;

        [ObservableProperty]
        private string _type = string.Empty; // source, rule, outbound

        [ObservableProperty]
        private double _value;

        [ObservableProperty]
        private double _x;

        [ObservableProperty]
        private double _y;

        [ObservableProperty]
        private double _height;

        [ObservableProperty]
        private SolidColorBrush? _color;

        [ObservableProperty]
        private double _opacity = 1.0;
    }

    public partial class TopologyLink : ObservableObject
    {
        [ObservableProperty]
        private string _sourceId = string.Empty;

        [ObservableProperty]
        private string _targetId = string.Empty;

        [ObservableProperty]
        private double _value;

        [ObservableProperty]
        private Geometry? _pathGeometry;

        [ObservableProperty]
        private Brush? _color;

        [ObservableProperty]
        private double _opacity = 0.15;
    }
}
