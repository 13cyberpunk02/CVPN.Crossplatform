using Android;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Avalonia.Android;

namespace CVPN.Android;

[Activity(
    Label = "CVPN",
    Theme = "@style/MyTheme.NoActionBar",
    Icon = "@drawable/icon",
    MainLauncher = true,
    LaunchMode = LaunchMode.SingleTop,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode)]
public class MainActivity : AvaloniaMainActivity
{
    private const int VpnConsentRequest = 0x7601;
    private const int NotificationsRequest = 0x7602;

    private static TaskCompletionSource<bool>? _vpnConsent;

    /// <summary>Текущая активность - для системных окон согласия. null, пока приложение в фоне.</summary>
    public static MainActivity? Current { get; private set; }

    /// <summary>Плитка в шторке попросила подключиться - выполняем, когда приложение готово.</summary>
    private static bool _connectRequested;

    /// <summary>Подписка плитки на состояние туннеля - одна на процесс.</summary>
    private static bool _tileHooked;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        Current = this;
        TakeConnectRequest(Intent);
    }

    // SingleTop: приложение уже открыто - новый запрос приходит сюда, а не в OnCreate
    protected override void OnNewIntent(Intent? intent)
    {
        base.OnNewIntent(intent);
        TakeConnectRequest(intent);
    }

    protected override void OnResume()
    {
        base.OnResume();
        Current = this;

        HookTile();

        // Подключаемся после OnResume: системному окну согласия на VPN нужна видимая активность
        if (_connectRequested && App.Shell is { } shell)
        {
            _connectRequested = false;
            shell.Dispatch(shell.ConnectFromOutside);
        }
    }

    private static void TakeConnectRequest(Intent? intent)
    {
        if (intent?.GetBooleanExtra(CvpnTileService.ConnectExtra, false) != true) return;

        _connectRequested = true;
        intent.RemoveExtra(CvpnTileService.ConnectExtra);
    }

    /// <summary>
    /// Плитка в шторке обновляется сама, только пока шторка открыта. Остальное
    /// время просим систему обновить её при каждой смене состояния туннеля.
    /// </summary>
    private void HookTile()
    {
        if (_tileHooked || App.Shell is not { } shell) return;

        _tileHooked = true;
        var context = ApplicationContext!;
        shell.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(ViewModels.MainViewModel.State) or nameof(ViewModels.MainViewModel.Active))
                CvpnTileService.RequestUpdate(context);
        };
    }

    protected override void OnDestroy()
    {
        if (ReferenceEquals(Current, this)) Current = null;
        base.OnDestroy();
    }

    /// <summary>
    /// Системное окно «Разрешить CVPN создать VPN-подключение?». Показывается один
    /// раз: дальше Prepare возвращает null, пока пользователь не отзовёт разрешение.
    /// </summary>
    public Task<bool> RequestVpnConsentAsync(Intent consent)
    {
        _vpnConsent?.TrySetResult(false);
        _vpnConsent = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        StartActivityForResult(consent, VpnConsentRequest);
        return _vpnConsent.Task;
    }

    /// <summary>
    /// Android 13+: без разрешения уведомление VPN не видно в шторке. Туннель
    /// работает и без него, поэтому не ждём ответа и не настаиваем.
    /// </summary>
    public void RequestNotificationsOnce()
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(33)) return;
        if (CheckSelfPermission(Manifest.Permission.PostNotifications) == Permission.Granted) return;

        RequestPermissions([Manifest.Permission.PostNotifications], NotificationsRequest);
    }

    protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);

        if (requestCode == VpnConsentRequest)
            _vpnConsent?.TrySetResult(resultCode == Result.Ok);
    }
}
