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

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        Current = this;
    }

    protected override void OnResume()
    {
        base.OnResume();
        Current = this;
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
