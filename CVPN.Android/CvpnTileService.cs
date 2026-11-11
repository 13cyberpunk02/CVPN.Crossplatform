using System.ComponentModel;
using Android.App;
using Android.Content;
using Android.Net;
using Android.OS;
using Android.Service.QuickSettings;
using CVPN.Localization;
using CVPN.Models.Enums;
using CVPN.ViewModels;

namespace CVPN.Android;

/// <summary>
/// Плитка в шторке быстрых настроек - рядом с Wi-Fi и Bluetooth. Включает и
/// выключает VPN, не открывая приложение. Если приложение не запущено или
/// Android ещё не разрешил VPN, плитка открывает его с просьбой подключиться:
/// без окна активности системный запрос согласия не показать.
/// </summary>
[Service(
    Name = "com.cyberpunk.cvpn.CvpnTileService",
    Label = "CVPN",
    Icon = "@drawable/ic_stat_cvpn",
    Exported = true,
    Permission = "android.permission.BIND_QUICK_SETTINGS_TILE")]
[IntentFilter(["android.service.quicksettings.action.QS_TILE"])]
[MetaData("android.service.quicksettings.TOGGLEABLE_TILE", Value = "true")]
public class CvpnTileService : TileService
{
    /// <summary>Экстра в Intent главной активности: «подключиться после запуска».</summary>
    public const string ConnectExtra = "cvpn.connect";

    private MainViewModel? _shell;

    public override void OnStartListening()
    {
        base.OnStartListening();

        _shell = App.Shell;
        if (_shell is not null) _shell.PropertyChanged += OnShellChanged;

        Refresh();
    }

    public override void OnStopListening()
    {
        if (_shell is not null) _shell.PropertyChanged -= OnShellChanged;
        _shell = null;

        base.OnStopListening();
    }

    public override void OnClick()
    {
        base.OnClick();

        var shell = App.Shell;

        // Приложение не запущено или нужен системный вопрос о VPN - открываем его
        if (shell is null || VpnService.Prepare(this) is not null)
        {
            OpenAppAndConnect();
            return;
        }

        shell.Dispatch(shell.ToggleFromOutside);
    }

    private void OnShellChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.State) or nameof(MainViewModel.Active)) Refresh();
    }

    /// <summary>Состояние плитки: горит, пока туннель поднят; под названием - сервер.</summary>
    private void Refresh()
    {
        var tile = QsTile;
        if (tile is null) return;

        var state = App.Shell?.State ?? TunnelState.Disconnected;

        tile.State = state is TunnelState.Connected or TunnelState.Connecting
            ? TileState.Active
            : TileState.Inactive;
        tile.Label = "CVPN";

        if (OperatingSystem.IsAndroidVersionAtLeast(29))
        {
            tile.Subtitle = state switch
            {
                TunnelState.Connected => App.Shell?.Active?.Name ?? Loc.T("State_Connected"),
                TunnelState.Connecting => Loc.T("State_Connecting"),
                TunnelState.Failing => Loc.T("State_Failed"),
                _ => Loc.T("State_Disconnected")
            };
        }

        tile.UpdateTile();
    }

    /// <summary>
    /// Открыть приложение, свернув шторку. Android 14 принимает только PendingIntent,
    /// старые версии - обычный Intent.
    /// </summary>
    private void OpenAppAndConnect()
    {
        var intent = new Intent(this, typeof(MainActivity))
            .AddFlags(ActivityFlags.NewTask | ActivityFlags.SingleTop)
            .PutExtra(ConnectExtra, true);

        if (OperatingSystem.IsAndroidVersionAtLeast(34))
        {
            var pending = PendingIntent.GetActivity(this, 0, intent,
                PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent);
            StartActivityAndCollapse(pending!);
        }
        else
        {
#pragma warning disable CA1422 // Intent-вариант - для Android 13 и старше
            StartActivityAndCollapse(intent);
#pragma warning restore CA1422
        }
    }

    /// <summary>
    /// Попросить систему обновить плитку, даже если шторка закрыта: туннель
    /// поменял состояние из приложения, плитка должна это показать.
    /// </summary>
    public static void RequestUpdate(Context context)
    {
        try
        {
            RequestListeningState(context,
                new ComponentName(context, Java.Lang.Class.FromType(typeof(CvpnTileService))));
        }
        catch (Exception)
        {
            // Плитку не добавили в шторку - обновлять нечего
        }
    }
}
