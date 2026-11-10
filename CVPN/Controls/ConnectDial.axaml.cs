using Avalonia.Controls;
using Avalonia.Interactivity;
using CVPN.ViewModels;

namespace CVPN.Controls;

public partial class ConnectDial : UserControl
{
    public ConnectDial() => InitializeComponent();

    /// <summary>
    /// Команда вызывается здесь, а не привязкой: заодно пишем отметку
    /// о нажатии в лог - она помогала разбирать «круг не реагирует».
    /// </summary>
    private void OnCoreClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;

        vm.NoteDialClick();

        if (vm.ToggleConnection.CanExecute(null)) vm.ToggleConnection.Execute(null);
    }
}
