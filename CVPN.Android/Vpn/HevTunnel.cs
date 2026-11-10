using System.Runtime.InteropServices;
using System.Text;

namespace CVPN.Android.Vpn;

/// <summary>
/// tun2socks на базе hev-socks5-tunnel: читает IP-пакеты из TUN, который выдал
/// VpnService, и отдаёт соединения в SOCKS5 - mixed-порт sing-box.
///
/// Вызывается C-API библиотеки напрямую (P/Invoke). Её JNI-обёртка не нужна:
/// библиотека грузится через dlopen, JNI_OnLoad при этом не срабатывает, и
/// Java-класс hev.htproxy.TProxyService из AAR не требуется.
/// Сигнатуры - include/hev-socks5-tunnel.h (src/hev-main.h) версии 2.19.3.
/// </summary>
internal static class HevTunnel
{
    private const string Library = "hev-socks5-tunnel";

    /// <summary>Блокирует поток до hev_socks5_tunnel_quit. 0 - штатное завершение.</summary>
    [DllImport(Library, EntryPoint = "hev_socks5_tunnel_main_from_str")]
    private static extern int MainFromString(byte[] config, uint length, int tunFd);

    [DllImport(Library, EntryPoint = "hev_socks5_tunnel_quit")]
    private static extern void Quit();

    [DllImport(Library, EntryPoint = "hev_socks5_tunnel_stats")]
    private static extern void Stats(out nuint txPackets, out nuint txBytes, out nuint rxPackets, out nuint rxBytes);

    private static Thread? _thread;

    public static bool IsRunning => _thread is { IsAlive: true };

    /// <summary>Код выхода последнего запуска, если он завершился сам (ошибка конфига, закрытый fd).</summary>
    public static int? LastExitCode { get; private set; }

    /// <summary>Завершился сам, без Stop - туннель надо считать упавшим.</summary>
    public static event Action<int>? Exited;

    private static volatile bool _stopping;

    public static void Start(int tunFd, int socksPort, int mtu)
    {
        if (IsRunning) return;

        // Адреса внутри tun2socks должны совпадать с выданными VpnService.Builder:
        // по ним он отличает «свои» пакеты
        var config = $"""
                      tunnel:
                        mtu: {mtu}
                        ipv4: {CvpnVpnService.TunAddress}
                      socks5:
                        port: {socksPort}
                        address: 127.0.0.1
                        udp: 'udp'
                      misc:
                        log-level: warn
                      """;

        var bytes = Encoding.UTF8.GetBytes(config);
        _stopping = false;
        LastExitCode = null;

        _thread = new Thread(() =>
        {
            var code = MainFromString(bytes, (uint)bytes.Length, tunFd);
            LastExitCode = code;
            if (!_stopping) Exited?.Invoke(code);
        })
        {
            IsBackground = true,
            Name = "hev-socks5-tunnel"
        };
        _thread.Start();
    }

    public static void Stop()
    {
        if (!IsRunning) return;

        _stopping = true;
        Quit();
        _thread?.Join(TimeSpan.FromSeconds(3));
        _thread = null;
    }

    /// <summary>Байты через туннель: (отправлено, получено).</summary>
    public static (long Tx, long Rx) Traffic()
    {
        if (!IsRunning) return (0, 0);

        Stats(out _, out var tx, out _, out var rx);
        return ((long)tx, (long)rx);
    }
}
