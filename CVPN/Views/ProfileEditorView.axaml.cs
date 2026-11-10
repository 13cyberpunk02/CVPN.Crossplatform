using Avalonia.Controls;
using Avalonia.Interactivity;
using CVPN.ViewModels;

namespace CVPN.Views;

/// <summary>Разметка редактора профиля. Логика и проверки - в ProfileEditorViewModel.</summary>
public partial class ProfileEditorView : UserControl
{
    private readonly Action<bool>? _close;

    // Для дизайнера
    public ProfileEditorView() => InitializeComponent();

    public ProfileEditorView(ProfileEditorViewModel vm, Action<bool> close) : this()
    {
        DataContext = vm;
        _close = close;
    }

    private void OnSave(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ProfileEditorViewModel vm && vm.TrySave()) _close?.Invoke(true);
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => _close?.Invoke(false);
}
