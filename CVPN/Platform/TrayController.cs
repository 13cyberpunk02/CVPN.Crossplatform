using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using CVPN.Localization;
using CVPN.Models.Enums;
using CVPN.ViewModels;

namespace CVPN.Platform;

/// <summary>
/// Значок в трее: состояние туннеля, подключение, выбор сервера, выход.
/// TrayIcon из Avalonia - на Windows это обычная область уведомлений, на Linux
/// StatusNotifierItem (KDE, Cinnamon, XFCE; в GNOME - с расширением AppIndicator).
/// </summary>
public sealed class TrayController : IDisposable
{
    private readonly MainViewModel _shell;
    private readonly TrayIcon _icon;
    private readonly NativeMenuItem _toggle;
    private readonly NativeMenuItem _servers;
    private readonly WindowIcon _idle;
    private readonly WindowIcon _connected;
    private readonly WindowIcon _failing;

    public TrayController(MainViewModel shell, Action show, Action exit)
    {
        _shell = shell;

        _idle = Load("cvpn-idle.ico");
        _connected = Load("cvpn-connected.ico");
        _failing = Load("cvpn-failing.ico");

        var open = new NativeMenuItem(Loc.T("Tray_Open"));
        open.Click += (_, _) => show();

        _toggle = new NativeMenuItem(Loc.T("Action_Connect"));
        _toggle.Click += (_, _) =>
        {
            if (shell.ToggleConnection.CanExecute(null)) shell.ToggleConnection.Execute(null);
        };

        // Сервер меняют чаще, чем открывают окно, - список прямо в меню
        _servers = new NativeMenuItem(Loc.T("Tray_Server")) { Menu = new NativeMenu() };

        var quit = new NativeMenuItem(Loc.T("Tray_Exit"));
        quit.Click += (_, _) => exit();

        _icon = new TrayIcon
        {
            Icon = _idle,
            ToolTipText = "CVPN",
            Menu = new NativeMenu
            {
                Items = { open, new NativeMenuItemSeparator(), _toggle, _servers, new NativeMenuItemSeparator(), quit }
            }
        };
        _icon.Clicked += (_, _) => show();

        TrayIcon.SetIcons(Application.Current!, [_icon]);

        shell.PropertyChanged += OnShellChanged;
        shell.Profiles.CollectionChanged += OnProfilesChanged;

        RefreshState();
        RefreshServers();
    }

    private void OnShellChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.State) or nameof(MainViewModel.Active))
        {
            RefreshState();
            RefreshServers();
        }
    }

    private void OnProfilesChanged(object? sender, NotifyCollectionChangedEventArgs e) => RefreshServers();

    private void RefreshState()
    {
        var state = _shell.State;

        _icon.Icon = state switch
        {
            TunnelState.Connected => _connected,
            TunnelState.Failing => _failing,
            _ => _idle
        };

        var status = state switch
        {
            TunnelState.Connected => Loc.T("State_Connected"),
            TunnelState.Connecting => Loc.T("State_Connecting"),
            TunnelState.Failing => Loc.T("State_Failed"),
            _ => Loc.T("State_Disconnected")
        };

        var text = _shell.Active is { } active ? $"CVPN - {status} · {active.Name}" : $"CVPN - {status}";

        // Windows обрезает подсказку на 63 символах
        _icon.ToolTipText = text.Length > 63 ? text[..63] : text;

        _toggle.Header = state is TunnelState.Connected or TunnelState.Connecting
            ? Loc.T("Action_Disconnect")
            : Loc.T("Action_Connect");
    }

    /// <summary>Подменю серверов пересобирается целиком - список короткий, а так проще.</summary>
    private void RefreshServers()
    {
        var menu = _servers.Menu!;
        menu.Items.Clear();

        foreach (var profile in _shell.Profiles)
        {
            var item = new NativeMenuItem(profile.Name)
            {
                ToggleType = MenuItemToggleType.Radio,
                IsChecked = ReferenceEquals(profile, _shell.Active)
            };

            var chosen = profile;
            item.Click += (_, _) => _ = _shell.SelectServerAsync(chosen);

            menu.Items.Add(item);
        }

        // Пустое подменю выглядит как поломка - лучше показать его недоступным
        _servers.IsEnabled = menu.Items.Count > 0;
    }

    private static WindowIcon Load(string fileName) =>
        new(AssetLoader.Open(new Uri($"avares://CVPN.UI/Assets/{fileName}")));

    public void Dispose()
    {
        _shell.PropertyChanged -= OnShellChanged;
        _shell.Profiles.CollectionChanged -= OnProfilesChanged;

        // Без явного скрытия значок остаётся висеть в трее до наведения мышью
        _icon.IsVisible = false;
        _icon.Dispose();
    }
}
