using CVPN.Tunnel;

namespace CVPN.Platform;

/// <summary>
/// Набор платформенных реализаций. Собирается головой приложения при запуске
/// и передаётся в MainViewModel. То, чего на платформе нет, остаётся заглушкой
/// с IsSupported=false - страница просто скрывает соответствующий раздел.
/// </summary>
public sealed class PlatformServices
{
    public required IUiDispatcher Ui { get; init; }
    public required IDialogService Dialogs { get; init; }
    public required IFilePicker Files { get; init; }
    public required IClipboardService Clipboard { get; init; }
    public required ILauncher Launcher { get; init; }
    public required IAppLifetime Lifetime { get; init; }
    public required ITunnelBackendProvider Tunnel { get; init; }
    public required ITunPrivileges TunPrivileges { get; init; }

    public ISystemProxy SystemProxy { get; init; } = Unsupported.Instance;
    public IKillSwitch KillSwitch { get; init; } = Unsupported.Instance;
    public IAutoStart AutoStart { get; init; } = Unsupported.Instance;
    public ITunnelServiceManager Service { get; init; } = Unsupported.Instance;
    public IElevatedLauncher ElevatedLauncher { get; init; } = Unsupported.Instance;

    /// <summary>Можно ли выбрать свой файл ядра и ciadpi (на Android бинарники только из поставки).</summary>
    public bool CanPickBinaries { get; init; } = true;
}

/// <summary>Заглушка для того, чего на платформе нет.</summary>
public sealed class Unsupported :
    ISystemProxy, IKillSwitch, IAutoStart, ITunnelServiceManager, IElevatedLauncher
{
    public static Unsupported Instance { get; } = new();

    private Unsupported()
    {
    }

    public bool IsSupported => false;

    // ISystemProxy
    public void Enable(int port)
    {
    }

    public void Restore()
    {
    }

    // IKillSwitch
    public bool IsActive => false;
    public Task<string> EnableAsync(string corePath, string? byeDpiPath) => Task.FromResult("");
    public Task<string> DisableAsync() => Task.FromResult("");
    public Task RestoreAfterCrashAsync() => Task.CompletedTask;

    // IAutoStart
    public void Apply(bool enabled)
    {
    }

    public void Sync(bool enabled)
    {
    }

    // ITunnelServiceManager
    public Task<bool> IsAvailableAsync() => Task.FromResult(false);
    public Task<string> DescribeAsync() => Task.FromResult("");

    public bool Install(out string error)
    {
        error = "not supported on this platform";
        return false;
    }

    public bool Uninstall(out string error)
    {
        error = "not supported on this platform";
        return false;
    }

    // IElevatedLauncher
    public bool Exists => false;
    public bool PathMatchesCurrent() => true;
    public string? RegisteredPath() => null;

    public bool Install(bool runAtLogon, out string error) => Install(out error);
}
