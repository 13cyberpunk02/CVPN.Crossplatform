using Avalonia;
using Avalonia.Controls;

namespace CVPN.Views;

public partial class MainView : UserControl
{
    /// <summary>Уже этой ширины меню уходит вниз: сбоку оно съело бы треть экрана.</summary>
    private const double NarrowWidth = 720;

    public MainView() => InitializeComponent();

    /// <summary>Слой для диалогов - его забирает OverlayDialogService.</summary>
    public Panel Overlay => OverlayLayer;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == BoundsProperty) ApplyLayout(Bounds.Width);
    }

    private void ApplyLayout(double width)
    {
        if (width <= 0) return;

        var narrow = width < NarrowWidth;

        Classes.Set("narrow", narrow);
        Sidebar.IsVisible = !narrow;
        BottomBar.IsVisible = narrow;
    }
}
