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
        InstallUpdate = new RelayCommand(async () => await InstallUpdateAsync(), () => Update is not null && !IsUpdating);
        CancelUpdate = new RelayCommand(() => _updateCts?.Cancel(), () => IsUpdating);

        // Кнопки отражают состояние системы: установить можно только то, чего нет
        InstallService = new RelayCommand(InstallTunnelService, () => CanInstallService);
        UninstallService = new RelayCommand(UninstallTunnelService, () => ServiceInstalled);
        InstallTask = new RelayCommand(InstallElevatedTask, () => CanInstallTask);
        UninstallTask = new RelayCommand(UninstallElevatedTask, () => TaskExists);

        // Состояние читаем сразу, а не при первом открытии страницы: иначе
        // кнопки успевают показаться в неверном виде
        if (ShowService) ServiceInstalled = Platform.Service.IsInstalled;
        if (ShowElevatedTask) _ = Task.Run(RefreshTaskStatusInBackground);

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

    /// <summary>Темы. Применяются сразу, без перезапуска.</summary>
    public IReadOnlyList<LanguageOption> Themes { get; } =
    [
        new("", Loc.T("Theme_System")),
        new("dark", Loc.T("Theme_Dark")),
        new("light", Loc.T("Theme_Light"))
    ];

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
    public ICommand InstallUpdate { get; }
    public ICommand CancelUpdate { get; }
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
            Raise(nameof(CanSelfUpdate));
            Raise(nameof(UpdateTitle));
        }
    }

    public bool HasUpdate => Update is not null;

    public string UpdateTitle => Update is null ? "" : Loc.T("Update_Title", Update.Version);

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

    // ===================== самообновление =====================

    private CancellationTokenSource? _updateCts;

    /// <summary>Эта установка умеет обновиться сама: нашёлся подходящий файл релиза.</summary>
    public bool CanSelfUpdate => Update is not null && Platform.Updater.PickAsset(Update) is not null;

    /// <summary>Идёт скачивание или запуск установки.</summary>
    public bool IsUpdating
    {
        get;
        private set
        {
            if (!Set(ref field, value)) return;

            CommandRequery.Suggest();
        }
    }

    /// <summary>Скачано, 0..100.</summary>
    public double UpdateProgress
    {
        get;
        private set => Set(ref field, value);
    }

    /// <summary>«12,3 МБ из 40 МБ».</summary>
    public string UpdateProgressLabel
    {
        get;
        private set => Set(ref field, value);
    } = "";

    /// <summary>
    /// Скачать, сверить сумму, отключить туннель и поставить. Туннель снимается
    /// только после удачного скачивания: если сеть оборвётся на середине,
    /// пользователь не останется без VPN зря.
    /// </summary>
    private async Task InstallUpdateAsync()
    {
        var release = Update;
        if (release is null || IsUpdating) return;

        var asset = Platform.Updater.PickAsset(release);
        if (asset is null)
        {
            // Эта установка обновляться сама не умеет - ведём на страницу релиза
            await OpenReleasePageAsync();
            return;
        }

        var question = Shell.IsConnected
            ? Loc.T("Update_ConfirmConnected", release.Version, ByteFormat.Size(asset.Size))
            : Loc.T("Update_Confirm", release.Version, ByteFormat.Size(asset.Size));

        if (!await Platform.Dialogs.ConfirmAsync(question)) return;

        IsUpdating = true;
        UpdateProgress = 0;
        UpdateProgressLabel = Loc.T("Update_Starting");
        _updateCts = new CancellationTokenSource();

        var progress = new Progress<(long Done, long Total)>(p =>
        {
            UpdateProgress = p.Total > 0 ? p.Done * 100.0 / p.Total : 0;
            UpdateProgressLabel = p.Total > 0
                ? Loc.T("Update_Progress", ByteFormat.Size(p.Done), ByteFormat.Size(p.Total))
                : ByteFormat.Size(p.Done);
        });

        try
        {
            Shell.Append($"[cvpn] downloading update {release.Version}: {asset.Name}");

            var file = await UpdateDownloader.DownloadAsync(
                release, asset, Platform.Updater.DownloadDirectory, progress, _updateCts.Token);

            Shell.Append("[cvpn] update downloaded, checksum verified");
            UpdateProgressLabel = Loc.T("Update_Installing");

            await Shell.StopForUpdateAsync();

            var problem = await Platform.Updater.InstallAsync(file);
            if (problem.Length > 0)
            {
                Shell.Append($"[cvpn] update: {problem}");
                Shell.Notify(problem);
            }
        }
        catch (OperationCanceledException)
        {
            Shell.Notify(Loc.T("Update_Cancelled"));
        }
        catch (UpdateException ex)
        {
            Shell.Append($"[cvpn] update: {ex.Message}");
            Shell.Notify(ex.Message);
        }
        finally
        {
            _updateCts?.Dispose();
            _updateCts = null;
            IsUpdating = false;
        }
    }

#if DEBUG
    /// <summary>Отладочный снимок: показать найденное обновление без запроса к GitHub.</summary>
    public void ShowDemoUpdate(ReleaseInfo release) => Update = release;
#endif

    private async Task OpenReleasePageAsync()
    {
        if (Update is null) return;

        if (!await Platform.Launcher.OpenUrlAsync(Update.Url))
            Shell.Notify(Loc.T("Settings_OpenPageFailed", Update.Url));
    }

    // ===================== служба =====================

    private async Task RefreshServiceStatusAsync()
    {
        ServiceInstalled = Platform.Service.IsInstalled;
        ServiceStatus = await Platform.Service.DescribeAsync();
    }

    /// <summary>
    /// Служба зарегистрирована в системе. Пока она есть, предлагаем только
    /// удаление: повторная установка закончилась бы ошибкой sc.exe.
    /// </summary>
    public bool ServiceInstalled
    {
        get;
        private set
        {
            Set(ref field, value);
            Raise(nameof(CanInstallService));
        }
    }

    public bool CanInstallService => !ServiceInstalled;

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

    private void RefreshTaskStatus() => ApplyTaskState(QueryTask());

    /// <summary>
    /// schtasks запускается процессом - на старте не держим из-за него поток UI.
    /// Результат применяется уже на нём.
    /// </summary>
    private void RefreshTaskStatusInBackground()
    {
        var state = QueryTask();
        Shell.Dispatch(() => ApplyTaskState(state));
    }

    private (bool Exists, bool Stale) QueryTask()
    {
        var launcher = Platform.ElevatedLauncher;
        var exists = launcher.Exists;
        return (exists, exists && !launcher.PathMatchesCurrent());
    }

    private void ApplyTaskState((bool Exists, bool Stale) state)
    {
        TaskExists = state.Exists;
        TaskStale = state.Stale;
        TaskStatus = !state.Exists
            ? Loc.T("Settings_TaskMissing")
            : state.Stale
                ? Loc.T("Settings_TaskStale")
                : Loc.T("Settings_TaskOk");
    }

    /// <summary>Задача есть в планировщике.</summary>
    public bool TaskExists
    {
        get;
        private set
        {
            Set(ref field, value);
            Raise(nameof(CanInstallTask));
        }
    }

    /// <summary>Задача запускает другой файл - её надо пересоздать.</summary>
    public bool TaskStale
    {
        get;
        private set
        {
            Set(ref field, value);
            Raise(nameof(CanInstallTask));
            Raise(nameof(InstallTaskLabel));
        }
    }

    /// <summary>Создавать нечего, если рабочая задача уже есть; устаревшую - можно пересоздать.</summary>
    public bool CanInstallTask => !TaskExists || TaskStale;

    public string InstallTaskLabel => TaskStale ? Loc.T("Settings_RecreateTask") : Loc.T("Settings_CreateTask");

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
