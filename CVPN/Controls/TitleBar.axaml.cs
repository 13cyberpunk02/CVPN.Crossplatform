using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Styling;

namespace CVPN.Controls;

public partial class TitleBar : UserControl
{
    private Window? _window;

    public TitleBar()
    {
        InitializeComponent();

        // Запасной путь, если платформа не поддерживает роль TitleBar:
        // тогда событие доходит до нас, и окно двигаем сами
        DragArea.PointerPressed += OnDragPressed;
        DragArea.DoubleTapped += (_, _) => ToggleMaximize();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        _window = TopLevel.GetTopLevel(this) as Window;
        if (_window is not null) _window.PropertyChanged += OnWindowChanged;

        if (Application.Current is { } app) app.ActualThemeVariantChanged += OnThemeChanged;

        SyncMaximizeIcon();
        SyncThemeIcon();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);

        if (_window is not null) _window.PropertyChanged -= OnWindowChanged;
        if (Application.Current is { } app) app.ActualThemeVariantChanged -= OnThemeChanged;
        _window = null;
    }

    private void OnWindowChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == Window.WindowStateProperty) SyncMaximizeIcon();
    }

    private void OnThemeChanged(object? sender, EventArgs e) => SyncThemeIcon();

    private bool IsDark => Application.Current?.ActualThemeVariant == ThemeVariant.Dark;

    /// <summary>Иконка показывает, куда переключит кнопка: в тёмной - солнце, в светлой - луна.</summary>
    private void SyncThemeIcon() =>
        ThemeIcon.Data = (Avalonia.Media.Geometry?)this.FindResource(IsDark ? "Icon.Sun" : "Icon.Moon");

    private void SyncMaximizeIcon() =>
        MaximizeIcon.Data = (Avalonia.Media.Geometry?)this.FindResource(
            _window?.WindowState == WindowState.Maximized ? "Icon.Restore" : "Icon.Maximize");

    /// <summary>Явный выбор темы: из «как в системе» кнопка переводит в противоположную текущей.</summary>
    private void OnToggleTheme(object? sender, RoutedEventArgs e)
    {
        if (App.Shell is { } shell) shell.Settings.Theme = IsDark ? "light" : "dark";
    }

    private void OnMinimize(object? sender, RoutedEventArgs e)
    {
        if (_window is not null) _window.WindowState = WindowState.Minimized;
    }

    private void OnMaximize(object? sender, RoutedEventArgs e) => ToggleMaximize();

    private void OnClose(object? sender, RoutedEventArgs e) => _window?.Close();

    private void ToggleMaximize()
    {
        if (_window is null) return;

        _window.WindowState = _window.WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
    }

    private void OnDragPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_window is null || e.ClickCount > 1) return;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

        _window.BeginMoveDrag(e);
    }
}
