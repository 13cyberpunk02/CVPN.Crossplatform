using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Net;
using Android.OS;
using Android.Runtime;
using CVPN.Localization;

namespace CVPN.Android.Vpn;

/// <summary>
/// VPN-интерфейс Android. Схема:
///
///   приложения ─► TUN (VpnService) ─► hev-socks5-tunnel ─► SOCKS5 127.0.0.1:mixed ─► sing-box ─► сервер
///
/// sing-box - обычный дочерний процесс приложения. Чтобы его собственные
/// соединения не уходили обратно в VPN, приложение исключено из своего
/// туннеля (AddDisallowedApplication): это заменяет protect() у сокетов,
/// до которых из отдельного процесса не дотянуться.
///
/// Служба объявлена в AndroidManifest.xml вручную: для foreground-службы
/// с типом specialUse нужен вложенный property, атрибут [Service] его не умеет.
/// </summary>
[Register("com.cyberpunk.cvpn.CvpnVpnService")]
public sealed class CvpnVpnService : VpnService
{
    public const string TunAddress = "172.19.0.1";
    private const string TunDns = "172.19.0.2";
    private const int Mtu = 8500;

    private const string ActionStart = "com.cyberpunk.cvpn.action.START";
    private const string ActionStop = "com.cyberpunk.cvpn.action.STOP";
    private const string ExtraPort = "port";
    private const string Channel = "cvpn_vpn";
    private const int NotificationId = 1;

    private static TaskCompletionSource<string>? _starting;
    private ParcelFileDescriptor? _tun;

    /// <summary>Система отозвала VPN (пользователь включил другой VPN или выключил наш в настройках).</summary>
    public static event Action? Revoked;

    /// <summary>tun2socks завершился сам - туннель больше не работает.</summary>
    public static event Action<int>? Failed;

    public static bool IsActive { get; private set; }

    /// <summary>
    /// Поднимает VPN. Пустая строка - успех, иначе текст ошибки.
    /// Права (VpnService.Prepare) к этому моменту уже должны быть выданы.
    /// </summary>
    public static async Task<string> StartAsync(Context context, int socksPort)
    {
        _starting = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

        var intent = new Intent(context, typeof(CvpnVpnService))
            .SetAction(ActionStart)
            .PutExtra(ExtraPort, socksPort);

        if (OperatingSystem.IsAndroidVersionAtLeast(26)) context.StartForegroundService(intent);
        else context.StartService(intent);

        var finished = await Task.WhenAny(_starting.Task, Task.Delay(TimeSpan.FromSeconds(10)));
        return finished == _starting.Task ? await _starting.Task : "VpnService did not start in 10 s";
    }

    public static void Stop(Context context)
    {
        if (!IsActive) return;
        context.StartService(new Intent(context, typeof(CvpnVpnService)).SetAction(ActionStop));
    }

    [return: GeneratedEnum]
    public override StartCommandResult OnStartCommand(Intent? intent, [GeneratedEnum] StartCommandFlags flags, int startId)
    {
        if (intent?.Action == ActionStop)
        {
            Shutdown();
            StopSelf();
            return StartCommandResult.NotSticky;
        }

        // Уведомление - в первые секунды после StartForegroundService, иначе система убьёт службу
        GoForeground();

        var problem = Establish(intent?.GetIntExtra(ExtraPort, 2080) ?? 2080);
        _starting?.TrySetResult(problem);

        if (problem.Length > 0)
        {
            Shutdown();
            StopSelf();
        }

        // Без автоперезапуска: после гибели процесса ядра всё равно нет
        return StartCommandResult.NotSticky;
    }

    private string Establish(int socksPort)
    {
        try
        {
            var builder = new Builder(this)
                .SetSession("CVPN")!
                .SetMtu(Mtu)!
                .AddAddress(TunAddress, 30)!
                .AddRoute("0.0.0.0", 0)!
                .AddDnsServer(TunDns)!;

            // IPv6 тоже в туннель: иначе он уйдёт мимо прокси напрямую.
            // Ядро такие соединения отклоняет, приложения откатываются на IPv4.
            builder.AddAddress("fdfe:dcba:9876::1", 126);
            builder.AddRoute("::", 0);

            // Своё приложение - мимо туннеля, иначе соединения ядра пошли бы по кругу
            builder.AddDisallowedApplication(PackageName!);

            if (OperatingSystem.IsAndroidVersionAtLeast(29)) builder.SetMetered(false);

            _tun = builder.Establish();
            if (_tun is null) return Loc.T("Android_VpnNotPrepared");

            HevTunnel.Exited -= OnTunnelExited;
            HevTunnel.Exited += OnTunnelExited;
            HevTunnel.Start(_tun.Fd, socksPort, Mtu);

            IsActive = true;
            return "";
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    private void OnTunnelExited(int code)
    {
        Shutdown();
        StopSelf();
        Failed?.Invoke(code);
    }

    /// <summary>Пользователь выключил VPN в системе или включил другой.</summary>
    public override void OnRevoke()
    {
        Shutdown();
        StopSelf();
        Revoked?.Invoke();
    }

    public override void OnDestroy()
    {
        Shutdown();
        base.OnDestroy();
    }

    private void Shutdown()
    {
        HevTunnel.Exited -= OnTunnelExited;
        HevTunnel.Stop();

        try
        {
            _tun?.Close();
        }
        catch (Java.IO.IOException)
        {
            // Уже закрыт системой
        }

        _tun = null;
        IsActive = false;

        if (OperatingSystem.IsAndroidVersionAtLeast(24)) StopForeground(StopForegroundFlags.Remove);
#pragma warning disable CA1422 // старый вызов - только для Android 6, где нового нет
        else StopForeground(true);
#pragma warning restore CA1422
    }

    private void GoForeground()
    {
        var manager = (NotificationManager)GetSystemService(NotificationService)!;

        if (OperatingSystem.IsAndroidVersionAtLeast(26))
        {
            // Низкая важность: значок в шторке без звука - VPN работает долго
            manager.CreateNotificationChannel(
                new NotificationChannel(Channel, "VPN", NotificationImportance.Low));
        }

        var open = PendingIntent.GetActivity(this, 0,
            new Intent(this, typeof(MainActivity)).SetFlags(ActivityFlags.SingleTop),
            PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent);

#pragma warning disable CA1422 // Builder без канала - только для Android 7 и старше
        var builder = OperatingSystem.IsAndroidVersionAtLeast(26)
            ? new Notification.Builder(this, Channel)
            : new Notification.Builder(this);
#pragma warning restore CA1422

        var notification = builder
            .SetContentTitle("CVPN")!
            .SetContentText(Loc.T("State_Connected"))!
            .SetSmallIcon(Resource.Drawable.Icon)!
            .SetContentIntent(open)!
            .SetOngoing(true)!
            .Build()!;

        if (OperatingSystem.IsAndroidVersionAtLeast(34))
            StartForeground(NotificationId, notification, ForegroundService.TypeSpecialUse);
        else
            StartForeground(NotificationId, notification);
    }
}
