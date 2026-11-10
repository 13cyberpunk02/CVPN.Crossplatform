using Avalonia.Controls;
using Avalonia.Interactivity;

namespace CVPN.Views;

/// <summary>Вопрос «да / нет» - замена MessageBox из WPF.</summary>
public partial class ConfirmView : UserControl
{
    private readonly Action<bool>? _close;

    // Для дизайнера
    public ConfirmView() => InitializeComponent();

    public ConfirmView(string message, Action<bool> close) : this()
    {
        Message.Text = message;
        _close = close;
    }

    private void OnYes(object? sender, RoutedEventArgs e) => _close?.Invoke(true);

    private void OnNo(object? sender, RoutedEventArgs e) => _close?.Invoke(false);
}
