using Avalonia;
using CVPN.Desktop.Platform;
using CVPN.Desktop.Platform.Linux;
using CVPN.Desktop.Platform.Windows;
using CVPN.Platform;

namespace CVPN.Desktop;

internal sealed class Program
{
    // До AppMain нельзя трогать Avalonia и всё, что зависит от SynchronizationContext.
    [STAThread]
    public static void Main(string[] args)
    {
        // Отдельный каталог данных: для проверок без риска для настоящих профилей
        // и для портативного запуска с флешки
        var dataDir = Environment.GetEnvironmentVariable("CVPN_DATA_DIR");
        if (!string.IsNullOrWhiteSpace(dataDir)) Services.AppPaths.Configure(dataDir: dataDir);

        App.ConfigurePlatform = Configure;

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Используется и визуальным дизайнером - не удалять.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();

    /// <summary>Сервисы десктопа: общие из UI-проекта плюс свои для Windows или Linux.</summary>
    private static PlatformServices Configure(CommonServices common)
    {
        var dialogs = common.Dialogs;
        var launcher = new AvaloniaLauncher(common.TopLevel, canOpenFiles: true);
        var lifetime = new DesktopLifetime();

        // Сообщения о правах идут в лог оболочки
        static void Log(string line) => App.Shell?.Append(line);

        if (OperatingSystem.IsWindows())
        {
            var service = new WindowsServiceManager();

            return new PlatformServices
            {
                Ui = common.Ui,
                Dialogs = dialogs,
                Files = common.Files,
                Clipboard = common.Clipboard,
                Launcher = launcher,
                Lifetime = lifetime,
                Tunnel = new DesktopTunnelProvider(service),
                TunPrivileges = new WindowsTunPrivileges(dialogs, Log),
                SystemProxy = new WindowsSystemProxy(),
                KillSwitch = new WindowsKillSwitch(),
                AutoStart = new WindowsAutoStart(),
                Service = service,
                ElevatedLauncher = new WindowsElevatedLauncher(),
                Updater = new WindowsUpdater(lifetime)
            };
        }

        if (OperatingSystem.IsLinux())
        {
            static string CorePath() => App.Shell?.Settings.CorePath ?? "";

            var service = new LinuxServiceManager(CorePath);

            return new PlatformServices
            {
                Ui = common.Ui,
                Dialogs = dialogs,
                Files = common.Files,
                Clipboard = common.Clipboard,
                Launcher = launcher,
                Lifetime = lifetime,
                Tunnel = new DesktopTunnelProvider(service),
                TunPrivileges = new LinuxTunPrivileges(CorePath),
                SystemProxy = new GnomeSystemProxy(),
                KillSwitch = new LinuxKillSwitch(Log),
                AutoStart = new LinuxAutoStart(),
                Service = service,
                Updater = new LinuxUpdater(lifetime)
            };
        }

        throw new PlatformNotSupportedException("CVPN runs on Windows and Linux desktops");
    }
}
