using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using CVPN.Converters;
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
    private readonly Bitmap? _connectGlyph;
    private readonly Bitmap? _disconnectGlyph;
    private readonly Bitmap? _serverGlyph;

    public TrayController(MainViewModel shell, Action show, Action exit)
    {
        _shell = shell;

        _idle = Load("cvpn-idle.ico");
        _connected = Load("cvpn-connected.ico");
        _failing = Load("cvpn-failing.ico");

        _connectGlyph = Glyph("connection", Accent);
        _disconnectGlyph = Glyph("connection", Danger);
        _serverGlyph = Glyph("Globe", Neutral);

        var open = new NativeMenuItem(Loc.T("Tray_Open")) { Icon = Glyph("App", Neutral) };
        open.Click += (_, _) => show();

        _toggle = new NativeMenuItem(Loc.T("Action_Connect"));
        _toggle.Click += (_, _) =>
        {
            if (shell.ToggleConnection.CanExecute(null)) shell.ToggleConnection.Execute(null);
        };

        // Сервер меняют чаще, чем открывают окно, - список прямо в меню
        _servers = new NativeMenuItem(Loc.T("Tray_Server")) { Menu = new NativeMenu(), Icon = Glyph("Globe", Accent) };

        var quit = new NativeMenuItem(Loc.T("Tray_Exit")) { Icon = Glyph("Close", Neutral) };
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

        var live = state is TunnelState.Connected or TunnelState.Connecting;
        _toggle.Header = live ? Loc.T("Action_Disconnect") : Loc.T("Action_Connect");
        _toggle.Icon = live ? _disconnectGlyph : _connectGlyph;
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
                IsChecked = ReferenceEquals(profile, _shell.Active),
                Icon = FlagConverter.Get(profile.CountryCode) ?? _serverGlyph
            };

            var chosen = profile;
            item.Click += (_, _) => _ = _shell.SelectServerAsync(chosen);

            menu.Items.Add(item);
        }

        // Пустое подменю выглядит как поломка - лучше показать его недоступным
        _servers.IsEnabled = menu.Items.Count > 0;
    }

    // Цвета значков меню: меню трея бывает и светлым, и тёмным (на Linux его рисует
    // сама оболочка), поэтому берём средние тона, читаемые на любом фоне
    private static readonly Color Accent = Color.Parse("#2E9FE6");
    private static readonly Color Danger = Color.Parse("#E5534B");
    private static readonly Color Neutral = Color.Parse("#8A97A6");

    /// <summary>
    /// Значок пункта меню из той же контурной иконки, что и в окне. Меню трея -
    /// не наша разметка, ему нужна картинка, поэтому путь рисуется в bitmap.
    /// </summary>
    private static Bitmap? Glyph(string key, Color color)
    {
        if (Application.Current?.TryFindResource($"Icon.{key}", out var resource) != true
            || resource is not Geometry geometry)
            return null;

        const int size = 32;
        var bitmap = new RenderTargetBitmap(new PixelSize(size, size), new Vector(96, 96));

        using (var context = bitmap.CreateDrawingContext())
        using (context.PushTransform(Matrix.CreateScale(size / 24.0, size / 24.0)))
        {
            var pen = new Pen(new SolidColorBrush(color), 2.2, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
            context.DrawGeometry(null, pen, geometry);
        }

        return bitmap;
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
