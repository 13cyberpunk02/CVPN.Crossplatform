using System.Windows.Input;
using CVPN.Core;
using CVPN.Localization;
using CVPN.Platform;
using CVPN.Services;

namespace CVPN.ViewModels;

/// <summary>
/// Страница настроек: путь к ядру, служба, задача планировщика, kill switch
/// и обновления.
///
/// Сами настройки живут в оболочке - их читает сборка конфигурации. Здесь
/// действия над окружением: установить, удалить, проверить, восстановить.
/// Разделы, которых на платформе нет, страница скрывает по флагам Show*.
/// </summary>
public sealed class SettingsViewModel : PageViewModel
{
    public SettingsViewModel(MainViewModel shell) : base(shell)
    {
        BrowseCore = new RelayCommand(async () => await PickCoreAsync(), () => Platform.CanPickBinaries);
        RestoreNetwork = new RelayCommand(async () => await RestoreNetworkAsync());

        CheckUpdate = new RelayCommand(async () => await CheckUpdateAsync(manual: true));
        OpenRelease = new RelayCommand(async () => await OpenReleasePageAsync(), () => Update is not null);

        InstallService = new RelayCommand(InstallTunnelService);
        UninstallService = new RelayCommand(UninstallTunnelService);
        InstallTask = new RelayCommand(InstallElevatedTask);
        UninstallTask = new RelayCommand(UninstallElevatedTask);

        _startupLanguage = Settings.Language;

        // Настройки сохранялись только при штатном выходе: если приложение
        // снять из диспетчера, изменения пропадали
        Settings.PropertyChanged += OnSettingChanged;
    }

    private readonly string _startupLanguage;

    private PlatformServices Platform => Shell.Platform;

    private void OnSettingChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        Shell.Persist();

        // Язык читается при запуске, поэтому спрашиваем прямо - но только если
        // он действительно отличается от того, с которым приложение запущено
        if (e.PropertyName == nameof(AppSettings.Language) && Settings.Language != _startupLanguage)
            _ = OfferRestartAsync();
    }

    public AppSettings Settings => Shell.Settings;

    /// <summary>Языки интерфейса для выпадающего списка.</summary>
    public IReadOnlyList<LanguageOption> Languages => Loc.Available;

    // Что показывать на этой платформе
    public bool ShowCorePicker => Platform.CanPickBinaries;
    public bool ShowService => Platform.Service.IsSupported;
    public bool ShowElevatedTask => Platform.ElevatedLauncher.IsSupported;
    public bool ShowKillSwitch => Platform.KillSwitch.IsSupported;
    public bool ShowAutoStart => Platform.AutoStart.IsSupported;
    public bool ShowSystemProxy => Platform.SystemProxy.IsSupported;
    public bool ShowCloseToTray => Platform.IsDesktop;

    /// <summary>strict_route и IPv6 у TUN - параметры TUN самого sing-box, на Android его поднимает VpnService.</summary>
    public bool ShowTunAdvanced => Platform.IsDesktop;

    /// <summary>Есть ли что показать в разделе службы и задачи планировщика.</summary>
    public bool ShowPrivileged => ShowService || ShowElevatedTask;

    public ICommand BrowseCore { get; }
    public ICommand RestoreNetwork { get; }
    public ICommand CheckUpdate { get; }
    public ICommand OpenRelease { get; }
    public ICommand InstallService { get; }
    public ICommand UninstallService { get; }
    public ICommand InstallTask { get; }
    public ICommand UninstallTask { get; }

    /// <summary>Состояние службы для страницы настроек.</summary>
    public string ServiceStatus
    {
        get;
        private set => Set(ref field, value);
    } = Loc.T("Settings_Checking");

    /// <summary>Состояние задачи планировщика.</summary>
    public string TaskStatus
    {
        get;
        private set => Set(ref field, value);
    } = "";

    /// <summary>Найденное обновление; null - установлена последняя версия.</summary>
    public ReleaseInfo? Update
    {
        get;
        private set
        {
            Set(ref field, value);
            Raise(nameof(HasUpdate));
            Raise(nameof(UpdateLabel));
        }
    }

    public bool HasUpdate => Update is not null;

    public string UpdateLabel => Update is null
        ? Loc.T("Settings_VersionInstalled", UpdateChecker.CurrentVersion.ToString(3))
        : Loc.T("Settings_VersionAvailable", Update.Version);

    /// <summary>Состояние окружения могло измениться, пока страница была закрыта.</summary>
    public override void Activate()
    {
        if (ShowService) _ = RefreshServiceStatusAsync();
        if (ShowElevatedTask) RefreshTaskStatus();
    }

    /// <summary>
    /// Автоматическая проверка при запуске. Вызывается оболочкой с задержкой,
    /// чтобы не соперничать со стартом за сеть и внимание.
    /// </summary>
    public async Task StartupCheckAsync()
    {
        if (!Settings.CheckUpdates) return;

        await Task.Delay(TimeSpan.FromSeconds(5));
        await CheckUpdateAsync(manual: false);
    }

    /// <summary>
    /// Язык применяется при разборе разметки, то есть только при запуске.
    /// Молчаливое «ничего не изменилось» выглядит как поломка, поэтому
    /// предлагаем перезапуск сразу.
    /// </summary>
    private async Task OfferRestartAsync()
    {
        Shell.Persist();

        if (!await Platform.Dialogs.ConfirmAsync(Loc.T("Settings_RestartQuestion")))
        {
            Shell.Notify(Loc.T("Settings_LanguageHint"));
            return;
        }

        if (!Platform.Lifetime.Restart())
            Shell.Notify(Loc.T("Settings_LanguageHint"));
    }

    // ===================== ядро =====================

    private async Task PickCoreAsync()
    {
        var current = Path.GetDirectoryName(Settings.CorePath) ?? "";

        var file = await Platform.Files.OpenAsync(
            Loc.T("Settings_PickCore"),
            FileFilter.ParseWpf(Loc.T("Settings_CoreFilter")),
            Directory.Exists(current) ? current : AppContext.BaseDirectory);

        // Ядро запускается как процесс, поэтому нужен настоящий путь
        if (file?.LocalPath is null) return;

        Settings.CorePath = file.LocalPath;
        Shell.Persist();
        Shell.RefreshCoreVersion();
    }

    // ===================== сеть =====================

    /// <summary>
    /// Аварийная кнопка: снимает правила брандмауэра и системный прокси,
    /// если что-то пошло не так и интернет пропал вместе с туннелем.
    /// </summary>
    private async Task RestoreNetworkAsync()
    {
        Platform.SystemProxy.Restore();
        var problem = await Platform.KillSwitch.DisableAsync();

        Shell.Notify(problem.Length > 0
            ? Loc.T("Settings_RestoreFailed", problem)
            : Loc.T("Settings_RestoreDone"));
    }

    // ===================== обновления =====================

    private async Task CheckUpdateAsync(bool manual)
    {
        var release = await UpdateChecker.CheckAsync();

        Shell.Dispatch(() =>
        {
            Update = release;

            if (release is not null)
                Shell.Notify(Loc.T("Settings_UpdateFound", release.Version));

            // При автоматической проверке молчим: сообщать «обновлений нет»
            // на каждом запуске - лишний шум
            else if (manual) Shell.Notify(Loc.T("Settings_UpToDate"));
        });
    }

    private async Task OpenReleasePageAsync()
    {
        if (Update is null) return;

        if (!await Platform.Launcher.OpenUrlAsync(Update.Url))
            Shell.Notify(Loc.T("Settings_OpenPageFailed", Update.Url));
    }

    // ===================== служба =====================

    private async Task RefreshServiceStatusAsync()
    {
        ServiceStatus = await Platform.Service.DescribeAsync();
    }

    private void InstallTunnelService()
    {
        Shell.Notify(Platform.Service.Install(out var error)
            ? Loc.T("Settings_ServiceInstalled")
            : Loc.T("Settings_ServiceInstallFailed", error));

        _ = RefreshServiceStatusAsync();
    }

    private void UninstallTunnelService()
    {
        Shell.Notify(Platform.Service.Uninstall(out var error)
            ? Loc.T("Settings_ServiceRemoved")
            : Loc.T("Settings_ServiceRemoveFailed", error));

        _ = RefreshServiceStatusAsync();
    }

    // ===================== задача планировщика =====================

    private void RefreshTaskStatus()
    {
        var launcher = Platform.ElevatedLauncher;

        if (!launcher.Exists)
        {
            TaskStatus = Loc.T("Settings_TaskMissing");
            return;
        }

        TaskStatus = launcher.PathMatchesCurrent()
            ? Loc.T("Settings_TaskOk")
            : Loc.T("Settings_TaskStale");
    }

    private void InstallElevatedTask()
    {
        Shell.Notify(Platform.ElevatedLauncher.Install(Settings.AutoStart, out var error)
            ? Loc.T("Settings_TaskCreated")
            : Loc.T("Settings_TaskCreateFailed", error));

        RefreshTaskStatus();
    }

    private void UninstallElevatedTask()
    {
        Shell.Notify(Platform.ElevatedLauncher.Uninstall(out var error)
            ? Loc.T("Settings_TaskDeleted")
            : Loc.T("Settings_TaskDeleteFailed", error));

        RefreshTaskStatus();
    }
}
