using System.Runtime.Versioning;
using CVPN.Ipc;
using CVPN.Localization;
using CVPN.Platform;

namespace CVPN.Desktop.Platform.Windows;

// Тонкие адаптеры: перенесённые из WPF-клиента статические классы
// (SystemProxy, KillSwitch, AutoStart, ServiceInstaller, ElevatedTask, Elevation)
// под интерфейсы из CVPN.Core.

[SupportedOSPlatform("windows")]
public sealed class WindowsSystemProxy : ISystemProxy
{
    public bool IsSupported => true;
    public void Enable(int port) => SystemProxy.Enable(port);
    public void Restore() => SystemProxy.Restore();
}

[SupportedOSPlatform("windows")]
public sealed class WindowsKillSwitch : IKillSwitch
{
    public bool IsSupported => true;
    public bool IsActive => KillSwitch.IsActive;
    public Task<string> EnableAsync(string corePath, string? byeDpiPath) => KillSwitch.EnableAsync(corePath, byeDpiPath);
    public Task<string> DisableAsync() => KillSwitch.DisableAsync();
    public Task RestoreAfterCrashAsync() => KillSwitch.RestoreAfterCrashAsync();
}

[SupportedOSPlatform("windows")]
public sealed class WindowsAutoStart : IAutoStart
{
    public bool IsSupported => true;
    public void Apply(bool enabled) => AutoStart.Apply(enabled);
    public void Sync(bool enabled) => AutoStart.Sync(enabled);
}

[SupportedOSPlatform("windows")]
public sealed class WindowsServiceManager : ITunnelServiceManager
{
    public bool IsSupported => true;

    public Task<bool> IsAvailableAsync() => ServiceClient.IsAvailableAsync();

    public async Task<string> DescribeAsync() =>
        await ServiceClient.IsAvailableAsync()
            ? Loc.T("Settings_ServiceOk")
            : ServiceInstaller.IsInstalledOnDisk
                ? Loc.T("Settings_ServiceMissing")
                : Loc.T("Settings_ServiceFilesMissing", ServiceInstaller.ExecutablePath);

    public bool Install(out string error) => ServiceInstaller.Install(out error);
    public bool Uninstall(out string error) => ServiceInstaller.Uninstall(out error);
}

[SupportedOSPlatform("windows")]
public sealed class WindowsElevatedLauncher : IElevatedLauncher
{
    public bool IsSupported => true;
    public bool Exists => ElevatedTask.Exists;
    public bool PathMatchesCurrent() => ElevatedTask.PathMatchesCurrent();
    public string? RegisteredPath() => ElevatedTask.RegisteredPath();
    public bool Install(bool runAtLogon, out string error) => ElevatedTask.Install(runAtLogon, out error);
    public bool Uninstall(out string error) => ElevatedTask.Uninstall(out error);
}

/// <summary>
/// Права на TUN под Windows. Повысить права работающему процессу нельзя -
/// только перезапуском: через задачу планировщика (без окна UAC) или через runas.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsTunPrivileges(IDialogService dialogs, Action<string> log) : ITunPrivileges
{
    public async Task<(PrivilegeOutcome Outcome, string Message)> EnsureAsync()
    {
        if (Elevation.IsElevated) return (PrivilegeOutcome.Ready, "");

        // Задача планировщика уже несёт нужные права - окно UAC не понадобится
        if (ElevatedTask.Exists)
        {
            if (ElevatedTask.Launch(out var launchError)) return (PrivilegeOutcome.Restarting, "");

            log($"[cvpn] the scheduled task did not start: {launchError}");
        }

        if (!await dialogs.ConfirmAsync(Loc.T("Elevation_Question")))
            return (PrivilegeOutcome.Cancelled, Loc.T("Elevation_Cancelled"));

        return Elevation.RelaunchElevated()
            ? (PrivilegeOutcome.Restarting, "")
            : (PrivilegeOutcome.Failed, Loc.T("Elevation_Denied"));
    }
}
