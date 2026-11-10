using CVPN.Localization;
using CVPN.Platform;
using CVPN.Services;
using CVPN.Tunnel;

namespace CVPN.Android;

/// <summary>
/// Сервисы Android. Пока без VpnService (этап 4): ядро запускается дочерним
/// процессом и работает как локальный прокси, режим TUN честно сообщает,
/// что ещё не готов.
/// </summary>
internal static class AndroidPlatform
{
    public static PlatformServices Configure(CommonServices common) => new()
    {
        Ui = common.Ui,
        Dialogs = new BasicDialogService(common.TopLevel, common.Clipboard),
        Files = common.Files,
        Clipboard = common.Clipboard,
        Launcher = new AvaloniaLauncher(common.TopLevel, canOpenFiles: false),
        Lifetime = new AndroidLifetime(),
        Tunnel = new ProxyOnlyProvider(),
        TunPrivileges = new VpnNotReady(),

        // Ядро и ciadpi - только из поставки: запускать можно лишь то, что лежит в nativeLibraryDir
        CanPickBinaries = false
    };

    private sealed class ProxyOnlyProvider : ITunnelBackendProvider
    {
        public Task<ITunnelBackend> SelectAsync(AppSettings settings) =>
            Task.FromResult<ITunnelBackend>(new LocalCoreBackend(settings.CorePath, "VPN", needsTunPrivileges: true));
    }

    private sealed class VpnNotReady : ITunPrivileges
    {
        public Task<(PrivilegeOutcome Outcome, string Message)> EnsureAsync() =>
            Task.FromResult((PrivilegeOutcome.Failed, Loc.T("Android_VpnNotReady")));
    }

    private sealed class AndroidLifetime : IAppLifetime
    {
        public void Shutdown() => Java.Lang.JavaSystem.Exit(0);

        // Перезапустить процесс из него самого Android не даёт
        public bool Restart() => false;
    }
}
