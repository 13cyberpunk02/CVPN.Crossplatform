using CVPN.Ipc;
using CVPN.Localization;
using CVPN.Platform;
using CVPN.Services;
using CVPN.Tunnel;

namespace CVPN.Desktop.Platform;

/// <summary>
/// Выбор способа запуска ядра на десктопе: служба, если она включена в настройках
/// и отвечает, иначе дочерний процесс. Служба поднимает TUN своими правами,
/// дочернему процессу права нужны от приложения.
/// </summary>
public sealed class DesktopTunnelProvider(ITunnelServiceManager service) : ITunnelBackendProvider
{
    public async Task<ITunnelBackend> SelectAsync(AppSettings settings)
    {
        if (settings.UseService && service.IsSupported && await ServiceClient.IsAvailableAsync())
            return new ServiceCoreBackend(Loc.T("Mode_TunService"));

        Func<Task<string>>? beforeStart = null;

        // Зависший адаптер от прошлого сеанса - самая частая причина отказа TUN на Windows
        if (OperatingSystem.IsWindows() && settings.TunEnabled)
            beforeStart = () => OperatingSystem.IsWindows() ? Windows.TunAdapterCleaner.RemoveStaleAsync() : Task.FromResult("");

        return new LocalCoreBackend(
            settings.CorePath,
            "TUN",
            needsTunPrivileges: settings.TunEnabled,
            beforeStart);
    }
}
