using System.Net.Sockets;
using Android.Content;
using Android.Net;
using CVPN.Android.Vpn;
using CVPN.Localization;
using CVPN.Platform;
using CVPN.Services;
using CVPN.Tunnel;

namespace CVPN.Android;

/// <summary>
/// Сервисы Android. Режим TUN - это VPN: VpnService + tun2socks + sing-box.
/// Без TUN ядро работает локальным прокси на 127.0.0.1 - для приложений,
/// которые умеют ходить через прокси сами.
/// </summary>
internal static class AndroidPlatform
{
    public static PlatformServices Configure(CommonServices common) => new()
    {
        Ui = common.Ui,
        Dialogs = common.Dialogs,
        Files = common.Files,
        Clipboard = common.Clipboard,
        Launcher = new AvaloniaLauncher(common.TopLevel, canOpenFiles: false),
        Lifetime = new AndroidLifetime(),
        Tunnel = new VpnTunnelProvider(),
        TunPrivileges = new VpnConsent(),

        // Ядро и ciadpi - только из поставки: запускать можно лишь то, что лежит в nativeLibraryDir
        CanPickBinaries = false,
        IsDesktop = false
    };

    private static Context AppContext => global::Android.App.Application.Context;

    private sealed class VpnTunnelProvider : ITunnelBackendProvider
    {
        public Task<ITunnelBackend> SelectAsync(AppSettings settings)
        {
            // Локальный резолвер ядра - DNS текущей сети (см. ConfigBuilder.AdaptForAndroid)
            ConfigBuilder.AndroidSystemDns = SystemDns();

            return Task.FromResult<ITunnelBackend>(new VpnBackend(settings.CorePath, settings.MixedPort, settings.TunEnabled));
        }

        /// <summary>Первый IPv4 DNS активной сети. Пусто - не нашли, ядро возьмёт запасной.</summary>
        private static string SystemDns()
        {
            try
            {
                var connectivity = (ConnectivityManager?)AppContext.GetSystemService(Context.ConnectivityService);
                var network = connectivity?.ActiveNetwork;
                var servers = network is null ? null : connectivity!.GetLinkProperties(network)?.DnsServers;

                return servers?
                    .Select(a => a.HostAddress ?? "")
                    .FirstOrDefault(a => System.Net.IPAddress.TryParse(a, out var ip)
                                         && ip.AddressFamily == AddressFamily.InterNetwork) ?? "";
            }
            catch (Exception)
            {
                return "";
            }
        }
    }

    /// <summary>
    /// Ядро - дочерний процесс (как на десктопе), поверх - VpnService.
    /// Порядок важен: сначала ядро и его mixed-порт, потом VPN - иначе первые
    /// соединения уйдут в ещё не слушающий порт.
    /// </summary>
    private sealed class VpnBackend(string corePath, int mixedPort, bool vpn) : ITunnelBackend
    {
        private readonly LocalCoreBackend _core = new(corePath, vpn ? "VPN" : "Proxy", needsTunPrivileges: vpn);

        public string Title => _core.Title;
        public bool NeedsTunPrivileges => vpn;

        public event Action<string>? LineReceived
        {
            add => _core.LineReceived += value;
            remove => _core.LineReceived -= value;
        }

        public event Action<CoreExit>? Exited;

        public async Task<(bool Ok, string Message)> StartAsync(string configPath)
        {
            _core.Exited += OnCoreExited;

            var (ok, message) = await _core.StartAsync(configPath);
            if (!ok || !vpn) return (ok, message);

            if (!await WaitForPortAsync(mixedPort))
            {
                await StopAsync();
                return (false, Loc.T("Android_CoreNotListening", mixedPort));
            }

            CvpnVpnService.Revoked += OnRevoked;
            CvpnVpnService.Failed += OnTunnelFailed;

            var problem = await CvpnVpnService.StartAsync(AppContext, mixedPort);
            if (problem.Length == 0) return (true, "");

            await StopAsync();
            return (false, problem);
        }

        public async Task StopAsync()
        {
            CvpnVpnService.Revoked -= OnRevoked;
            CvpnVpnService.Failed -= OnTunnelFailed;
            CvpnVpnService.Stop(AppContext);

            _core.Exited -= OnCoreExited;
            await _core.StopAsync();
        }

        private void OnCoreExited(CoreExit exit)
        {
            // Без ядра VPN превращается в чёрную дыру - снимаем его сразу
            if (!exit.Graceful) CvpnVpnService.Stop(AppContext);
            Exited?.Invoke(exit);
        }

        private void OnRevoked() => Exited?.Invoke(new CoreExit(-1, false, Loc.T("Android_VpnRevoked")));

        private void OnTunnelFailed(int code) =>
            Exited?.Invoke(new CoreExit(code, false, Loc.T("Android_TunnelFailed", code)));

        private static async Task<bool> WaitForPortAsync(int port)
        {
            for (var attempt = 0; attempt < 30; attempt++)
            {
                try
                {
                    using var client = new TcpClient();
                    using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
                    await client.ConnectAsync("127.0.0.1", port, cts.Token);
                    return true;
                }
                catch (Exception)
                {
                    await Task.Delay(150);
                }
            }

            return false;
        }
    }

    /// <summary>Согласие на VPN: системное окно Android вместо UAC.</summary>
    private sealed class VpnConsent : ITunPrivileges
    {
        public async Task<(PrivilegeOutcome Outcome, string Message)> EnsureAsync()
        {
            var consent = VpnService.Prepare(AppContext);
            MainActivity.Current?.RequestNotificationsOnce();

            if (consent is null) return (PrivilegeOutcome.Ready, "");

            if (MainActivity.Current is not { } activity)
                return (PrivilegeOutcome.Failed, Loc.T("Android_VpnNotPrepared"));

            return await activity.RequestVpnConsentAsync(consent)
                ? (PrivilegeOutcome.Ready, "")
                : (PrivilegeOutcome.Cancelled, Loc.T("Android_VpnDenied"));
        }
    }

    private sealed class AndroidLifetime : IAppLifetime
    {
        public void Shutdown()
        {
            MainActivity.Current?.FinishAndRemoveTask();
            Java.Lang.JavaSystem.Exit(0);
        }

        // Перезапустить процесс из него самого Android не даёт
        public bool Restart() => false;
    }
}
