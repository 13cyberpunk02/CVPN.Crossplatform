using System.Collections.ObjectModel;
using System.Windows.Input;
using CVPN.Core;
using CVPN.Localization;
using CVPN.Models;
using CVPN.Models.Enums;
using CVPN.Platform;
using CVPN.Services;
using CVPN.Shared;
using CVPN.Tunnel;

namespace CVPN.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private const int MaxLogLines = 500;

    private readonly StoredState _state;
    private readonly IUiTimer _uptimeTimer;
    private ITunnelBackend? _backend;
    private bool? _sessionTun;
    private string? _sessionTitle;
    private bool _adapterErrorSeen;
    private bool _adapterRetryUsed;
    private RoutingProfile _routing;
    private ClashApiClient? _stats;
    private DateTime _connectedAt;

    private TunnelState _tunnel = TunnelState.Disconnected;
    private ServerProfile? _active;
    private string _uptime = "00:00:00";
    private string _download = "0.0";
    private string _upload = "0.0";
    private string _downloadUnit = Loc.T("Unit_KBps");
    private string _uploadUnit = Loc.T("Unit_KBps");
    private string _singBoxVersion = Loc.T("Core_NotFound");
    private string _status = "";

    public MainViewModel(PlatformServices platform)
    {
        Platform = platform;
        CommandRequery.Post = platform.Ui.Post;

        _state = ProfileStore.Load();

        Profiles = new ObservableCollection<ServerProfile>(_state.Profiles);
        RoutingProfiles = new ObservableCollection<RoutingProfile>(_state.RoutingProfiles);
        Settings = _state.Settings;

        _routing = RoutingProfiles.FirstOrDefault(r => r.Name == _state.ActiveRoutingProfile)
                   ?? RoutingProfiles[0];

        Active = Profiles.FirstOrDefault(p => p.Name == _state.ActiveProfileName)
                 ?? Profiles.FirstOrDefault();

        // Импорт добавляет профили в уже открытое приложение: если активного нет, берём первый
        Profiles.CollectionChanged += (_, _) => Active ??= Profiles.FirstOrDefault();

        Settings.PropertyChanged += (_, _) => RaiseMode();

        ConnectionsPage = new ConnectionsViewModel(this);
        LogsPage = new LogsViewModel(this);
        RoutingPage = new RoutingViewModel(this);
        ProfilesPage = new ProfilesViewModel(this);
        SettingsPage = new SettingsViewModel(this);
        ByeDpiPage = new ByeDpiViewModel(this);

        SubscribeRules();

        ToggleConnection = new RelayCommand(async () => await ToggleAsync());
        OpenConfig = new RelayCommand(async () => await ShowGeneratedConfigAsync(), () => Platform.Launcher.CanOpenFiles);
        MeasureDelay = new RelayCommand(async () => await MeasureAsync(), () => IsConnected);
        CheckConfig = new RelayCommand(async () => await CheckAsync());

        _uptimeTimer = platform.Ui.CreateTimer(TimeSpan.FromSeconds(1), () =>
        {
            Uptime = (DateTime.Now - _connectedAt).ToString(@"hh\:mm\:ss");
            Session.RaiseElapsed();
        });

        // Система могла разойтись с настройкой, если приложение перенесли
        if (platform.AutoStart.IsSupported)
        {
            platform.AutoStart.Sync(Settings.AutoStart);
            Settings.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(AppSettings.AutoStart)) platform.AutoStart.Apply(Settings.AutoStart);
            };
        }

        StampBuild();

        if (ProfileStore.LastLoadWarning.Length > 0)
        {
            Status = ProfileStore.LastLoadWarning;
            Append($"[cvpn] {Status.ToLowerInvariant()}");
        }

        _ = DetectCoreAsync();

        _ = SettingsPage.StartupCheckAsync();
        _ = ProfilesPage.StartupRefreshAsync();
    }

    /// <summary>Платформенные сервисы - для страниц.</summary>
    public PlatformServices Platform { get; }

    public ObservableCollection<ServerProfile> Profiles { get; }

    /// <summary>
    /// Страница соединений. Экземпляр один: навигация пересоздаёт разметку,
    /// но состояние страницы должно переживать переходы.
    /// </summary>
    public ConnectionsViewModel ConnectionsPage { get; }

    public RoutingViewModel RoutingPage { get; }
    public LogsViewModel LogsPage { get; }
    public ProfilesViewModel ProfilesPage { get; }
    public SettingsViewModel SettingsPage { get; }
    public ByeDpiViewModel ByeDpiPage { get; }

    /// <summary>Имя активного профиля для круга подключения.</summary>
    public string ActiveName => Active?.Name ?? Loc.T("Connection_NoProfile");

    public ObservableCollection<RoutingProfile> RoutingProfiles { get; }

    /// <summary>Правила активного набора. При смене набора коллекция подменяется целиком.</summary>
    public ObservableCollection<RouteRule> Rules => ActiveRouting.Rules;

    /// <summary>
    /// Активный набор правил. Смена на лету требует перезапуска ядра:
    /// маршруты живут в конфиге, а не в Clash API, в отличие от выбора сервера.
    /// </summary>
    public RoutingProfile ActiveRouting
    {
        get => _routing;
        set
        {
            if (value is null || ReferenceEquals(value, _routing)) return;

            UnsubscribeRules();
            Set(ref _routing, value);
            SubscribeRules();

            _state.ActiveRoutingProfile = value.Name;

            Raise(nameof(Rules));
            Raise(nameof(ActiveRuleCount));
            Persist();

            if (IsConnected) Status = Loc.T("Routing_SetAfterReconnect");
        }
    }

    public ObservableCollection<LogEntry> Log { get; } = [];

    /// <summary>Итоги текущей сессии: сколько прошло трафика и за какое время.</summary>
    public SessionStats Session { get; } = new();

    /// <summary>Скорость за последнюю минуту - для графика на главной странице.</summary>
    public TrafficHistory Traffic { get; } = new();

    public AppSettings Settings { get; }

    public ICommand ToggleConnection { get; }
    public ICommand OpenConfig { get; }
    public ICommand MeasureDelay { get; }
    public ICommand CheckConfig { get; }

    // ===================== состояние =====================

    public TunnelState State
    {
        get => _tunnel;
        private set
        {
            if (!Set(ref _tunnel, value)) return;
            Raise(nameof(StateLabel));
            Raise(nameof(IsConnected));
            Raise(nameof(IsConnecting));
            Raise(nameof(IsFailing));
            Raise(nameof(PrimaryActionLabel));
        }
    }

    public bool IsConnected => State == TunnelState.Connected;

    // Отдельные флаги - для классов в разметке Avalonia (Classes.connected="{Binding IsConnected}")
    public bool IsConnecting => State == TunnelState.Connecting;
    public bool IsFailing => State == TunnelState.Failing;

    // ===================== навигация =====================

    /// <summary>Пункты меню. Страницы живут всё время работы, переход меняет только разметку.</summary>
    public IReadOnlyList<NavItem> NavItems => _navItems ??=
    [
        new("connection", Loc.T("Nav_Connection"), this),
        new("profiles", Loc.T("Nav_Profiles"), ProfilesPage),
        new("routing", Loc.T("Nav_Routing"), RoutingPage),
        new("connections", Loc.T("Nav_Connections"), ConnectionsPage),
        new("logs", Loc.T("Nav_Logs"), LogsPage),
        new("byedpi", Loc.T("Nav_ByeDpi"), ByeDpiPage),
        new("settings", Loc.T("Nav_Settings"), SettingsPage)
    ];

    private IReadOnlyList<NavItem>? _navItems;
    private NavItem? _selectedNav;

    /// <summary>
    /// Выбранный пункт. Уход со страницы останавливает её таймеры, приход -
    /// запускает: раньше это делали Loaded/Unloaded каждой вьюхи.
    /// </summary>
    public NavItem SelectedNav
    {
        get => _selectedNav ??= NavItems[0];
        set
        {
            if (value is null || ReferenceEquals(value, _selectedNav)) return;

            (SelectedNav.Page as PageViewModel)?.Deactivate();
            _selectedNav = value;
            (value.Page as PageViewModel)?.Activate();

            Raise();
        }
    }

    /// <summary>Доступ к Clash API для страниц.</summary>
    public ClashApiClient? Api => _stats;

    /// <summary>Сообщение в строке состояния и в логе - общая точка для страниц.</summary>
    public void Notify(string message)
    {
        Status = message;
        Append($"[cvpn] {message.ToLowerInvariant()}");
    }

    /// <summary>Режим запущенной сессии; если ядро не работает - то, что выбрано в настройках.</summary>
    private bool EffectiveTun => _sessionTun ?? Settings.TunEnabled;

    public string ModeTitle => EffectiveTun
        ? _sessionTitle ?? "TUN"
        : Loc.T("Mode_SystemProxy");

    public string ModeDetail => EffectiveTun
        ? Loc.T("Mode_AllTraffic")
        : $"127.0.0.1:{Settings.MixedPort}";

    /// <summary>Режим активен только при живом туннеле - иначе это просто настройка.</summary>
    public bool ModeActive => _sessionTun is not null;

    /// <summary>Настройку поменяли на ходу: к текущей сессии она не относится.</summary>
    public bool ModePending => _sessionTun is not null && _sessionTun != Settings.TunEnabled;

    public string ModePendingHint => Settings.TunEnabled
        ? Loc.T("Mode_PendingTun")
        : Loc.T("Mode_PendingProxy");

    private void RaiseMode()
    {
        Raise(nameof(ModeTitle));
        Raise(nameof(ModeDetail));
        Raise(nameof(ModeActive));
        Raise(nameof(ModePending));
        Raise(nameof(ModePendingHint));
    }

    public string StateLabel => State switch
    {
        TunnelState.Connected => Loc.T("State_ConnectedCaps"),
        TunnelState.Connecting => Loc.T("State_ConnectingCaps"),
        TunnelState.Failing => Loc.T("State_FailedCaps"),
        _ => Loc.T("State_DisconnectedCaps")
    };

    public string PrimaryActionLabel => State is TunnelState.Connected or TunnelState.Connecting
        ? Loc.T("Action_Disconnect")
        : Loc.T("Action_Connect");

    public ServerProfile? Active
    {
        get => _active;
        set
        {
            if (_active is not null) _active.IsActive = false;
            Set(ref _active, value);
            if (_active is not null) _active.IsActive = true;
            _state.ActiveProfileName = _active?.Name;
            Raise(nameof(ActiveName));
            Raise(nameof(ChosenServer));
        }
    }

    /// <summary>
    /// Выбор сервера прямо на главной странице: то же, что «Выбрать» в профилях,
    /// включая переключение без перезапуска ядра.
    /// </summary>
    public ServerProfile? ChosenServer
    {
        get => _active;
        set
        {
            if (value is not null && !ReferenceEquals(value, _active)) _ = SelectServerAsync(value);
        }
    }

    /// <summary>Ядра нет или оно не отвечает - подключиться не выйдет, это надо видеть сразу.</summary>
    public bool CoreProblem { get; private set; } = true;

    public string Uptime
    {
        get => _uptime;
        private set => Set(ref _uptime, value);
    }

    public string Download
    {
        get => _download;
        private set => Set(ref _download, value);
    }

    public string Upload
    {
        get => _upload;
        private set => Set(ref _upload, value);
    }

    public string DownloadUnit
    {
        get => _downloadUnit;
        private set => Set(ref _downloadUnit, value);
    }

    public string UploadUnit
    {
        get => _uploadUnit;
        private set => Set(ref _uploadUnit, value);
    }

    public string SingBoxVersion
    {
        get => _singBoxVersion;
        private set => Set(ref _singBoxVersion, value);
    }

    public string Status
    {
        get => _status;
        private set => Set(ref _status, value);
    }

    public int ActiveRuleCount => Rules.Count(r => r.Enabled);

    // ===================== подключение =====================

    private async Task ToggleAsync()
    {
        // Ручное нажатие - новая попытка: прошлый неудачный запуск не должен
        // блокировать автоповтор навсегда
        _adapterRetryUsed = false;

        if (State is TunnelState.Connected or TunnelState.Connecting)
        {
            await DisconnectAsync();
            return;
        }

        await ConnectAsync();
    }

    private async Task ConnectAsync()
    {
        // Второй запуск поверх живого туннеля затирал ссылку на первое ядро:
        // процесс sing-box оставался без хозяина - ни отключить, ни снять при выходе
        if (State is TunnelState.Connected or TunnelState.Connecting)
        {
            Append("[cvpn] already connected, skipping a second start");
            return;
        }

        // Остатки прошлой попытки (ядро упало, а объект ещё жив) - снимаем до нового запуска
        if (_backend is not null) await TeardownAsync();

        if (Active is null)
        {
            // Раньше кнопка в этом случае просто не нажималась, и причина была не видна
            Status = Profiles.Count == 0
                ? Loc.T("Connect_NoProfiles")
                : Loc.T("Connect_NoActiveProfile");
            Append($"[cvpn] {Status}");
            return;
        }

        if (!File.Exists(Settings.CorePath))
        {
            Fail(Loc.T("Core_FileNotFound", Settings.CorePath));
            return;
        }

        var backend = await Platform.Tunnel.SelectAsync(Settings);

        // TUN не поднимется без прав. Служба и VpnService права приносят сами.
        if (Settings.TunEnabled && backend.NeedsTunPrivileges && !await EnsureTunPrivilegesAsync()) return;

        State = TunnelState.Connecting;
        Append("[cvpn] building configuration");

        var byeDpiProblem = await ByeDpiPage.EnsureStartedAsync();
        if (byeDpiProblem.Length > 0)
        {
            // Туннель не рушим: без ByeDPI пострадают только сайты по его правилам
            Append($"[cvpn] ByeDPI: {byeDpiProblem}");
            Notify(byeDpiProblem);
        }

        string configPath;
        try
        {
            configPath = ConfigBuilder.Write([.. Profiles], Active, ActiveRouting, Settings);
        }
        catch (Exception ex)
        {
            Fail(Loc.T("Config_BuildFailed", ex.Message));
            return;
        }

        ExplainRouting();

        _backend = backend;
        backend.LineReceived += OnCoreLine;
        backend.Exited += OnCoreExited;

        var (ok, message) = await backend.StartAsync(configPath);
        if (!ok)
        {
            Fail(message);
            await TeardownAsync();
            return;
        }

        // Без TUN трафик надо кому-то отдать: прописываем mixed-порт системным прокси
        if (!Settings.TunEnabled && Platform.SystemProxy.IsSupported)
        {
            try
            {
                Platform.SystemProxy.Enable(Settings.MixedPort);
                Append($"[cvpn] system proxy: 127.0.0.1:{Settings.MixedPort}");
            }
            catch (Exception ex)
            {
                Append($"[cvpn] could not set the system proxy: {ex.Message}");
            }
        }

        _sessionTun = Settings.TunEnabled;
        _sessionTitle = backend.Title;
        RaiseMode();

        _stats = new ClashApiClient(Settings.ClashApiPort);
        _stats.TrafficReceived += OnTraffic;
        _stats.Start();
        await ApplySelectionAsync();

        if (Settings.KillSwitch && Platform.KillSwitch.IsSupported)
        {
            var problem = await Platform.KillSwitch.EnableAsync(
                Settings.CorePath,
                ConfigBuilder.ByeDpiActive(Settings) ? Settings.ByeDpiPath : null);

            if (problem.Length > 0)
            {
                Append($"[cvpn] {problem}");
                Status = problem;
            }
            else
            {
                Append("[cvpn] kill switch on: traffic outside the tunnel is blocked");
            }
        }

        _adapterErrorSeen = false;
        Session.Begin(Active.Name);
        Traffic.Clear();
        _connectedAt = DateTime.Now;
        _uptimeTimer.Start();
        State = TunnelState.Connected;
        Status = "";
        Append(Settings.AutoSelectFastest && Profiles.Count > 1
            ? $"[cvpn] auto-selecting the fastest of {Profiles.Count} servers"
            : $"[cvpn] connecting to {Active.Name} ({Active.ProtocolLabel})");

        // Первый замер с задержкой: ядру нужно поднять Clash API и сам туннель
        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(2));
            await MeasureAsync();
        });
    }

    /// <summary>
    /// Перед установкой обновления: туннель снимается штатно - с возвратом прокси
    /// и kill switch, - а настройки сохраняются. Иначе установщик застал бы
    /// запущенное ядро и занятые файлы.
    /// </summary>
    public async Task StopForUpdateAsync()
    {
        if (State is TunnelState.Connected or TunnelState.Connecting or TunnelState.Failing)
            await DisconnectAsync();

        Persist();
    }

    private async Task DisconnectAsync()
    {
        _adapterRetryUsed = false;
        _adapterErrorSeen = false;

        Platform.SystemProxy.Restore();
        _uptimeTimer.Stop();
        if (Active is not null) Active.LatencyMs = -1;
        Uptime = "00:00:00";
        Download = "0.0";
        Upload = "0.0";
        DownloadUnit = Loc.T("Unit_KBps");
        UploadUnit = Loc.T("Unit_KBps");

        await TeardownAsync();

        State = TunnelState.Disconnected;
        Append("[cvpn] disconnected");
    }

    private async Task TeardownAsync()
    {
        await ByeDpiPage.StopAsync();

        if (Platform.KillSwitch.IsActive)
        {
            var problem = await Platform.KillSwitch.DisableAsync();
            Append(problem.Length > 0
                ? $"[cvpn] could not disable the kill switch: {problem}"
                : "[cvpn] kill switch off");
        }

        _sessionTun = null;
        _sessionTitle = null;
        RaiseMode();

        if (_stats is not null)
        {
            _stats.TrafficReceived -= OnTraffic;
            await _stats.StopAsync();
            _stats = null;
        }

        var backend = _backend;
        if (backend is null) return;

        _backend = null;
        backend.LineReceived -= OnCoreLine;
        backend.Exited -= OnCoreExited;
        await backend.StopAsync();
    }

    /// <summary>
    /// Права на TUN. На Windows это может закончиться перезапуском приложения
    /// с правами администратора - тогда текущий процесс сохраняет состояние и выходит.
    /// </summary>
    private async Task<bool> EnsureTunPrivilegesAsync()
    {
        var (outcome, message) = await Platform.TunPrivileges.EnsureAsync();

        switch (outcome)
        {
            case PrivilegeOutcome.Ready:
                return true;

            case PrivilegeOutcome.Restarting:
                Persist();
                Platform.Lifetime.Shutdown();
                return false;

            case PrivilegeOutcome.Cancelled:
                Status = message.Length > 0 ? message : Loc.T("Elevation_Cancelled");
                State = TunnelState.Disconnected;
                return false;

            default:
                Fail(message.Length > 0 ? message : Loc.T("Elevation_Denied"));
                return false;
        }
    }

    private async Task CheckAsync()
    {
        if (Active is null) return;

        try
        {
            var path = ConfigBuilder.Write([.. Profiles], Active, ActiveRouting, Settings);
            var service = new SingBoxService(Settings.CorePath);
            var (ok, message) = await service.CheckConfigAsync(path);
            Status = ok ? Loc.T("Core_ConfigValid") : message;
            Append($"[cvpn] check: {Status}");
        }
        catch (Exception ex)
        {
            Fail(ex.Message);
        }
    }

    /// <summary>Перечитать версию ядра - например, после смены пути.</summary>
    public void RefreshCoreVersion() => _ = DetectCoreAsync();

    private async Task DetectCoreAsync()
    {
        if (!File.Exists(Settings.CorePath))
        {
            SetCore(Loc.T("Core_NotFound"), problem: true);
            return;
        }

        try
        {
            var service = new SingBoxService(Settings.CorePath);
            // «sing-box version 1.14.3» не помещается в боковое меню - слово version лишнее
            var version = await service.GetVersionAsync();
            SetCore(version.Replace("sing-box version ", "sing-box ", StringComparison.Ordinal), problem: false);
        }
        catch
        {
            SetCore(Loc.T("Core_NotResponding"), problem: true);
        }
    }

    private void SetCore(string label, bool problem)
    {
        SingBoxVersion = label;
        CoreProblem = problem;
        Raise(nameof(CoreProblem));
    }

    /// <summary>Вызывается при закрытии приложения: нельзя оставить систему с чужим прокси.</summary>
    public async Task ShutdownAsync()
    {
        Platform.SystemProxy.Restore();
        Persist();
        await TeardownAsync();
    }

    // ===================== события ядра =====================

    private void OnCoreLine(string line) => Dispatch(() => Append(line));

    private void OnCoreExited(CoreExit exit) => Dispatch(async () =>
    {
        if (exit.Graceful) return;

        Platform.SystemProxy.Restore();
        _uptimeTimer.Stop();

        // Одна автоматическая попытка: Windows освобождает адаптер с задержкой,
        // и повторный запуск через пару секунд обычно проходит
        if (_adapterErrorSeen && !_adapterRetryUsed)
        {
            _adapterRetryUsed = true;
            _adapterErrorSeen = false;

            Append("[cvpn] stale TUN adapter from a previous run, retrying in 3 s");
            Status = Loc.T("Tun_Releasing");
            State = TunnelState.Connecting;

            await TeardownAsync();
            await Task.Delay(TimeSpan.FromSeconds(3));
            await ConnectAsync();

            return;
        }

        State = TunnelState.Failing;
        Status = exit.Message ?? Loc.T("Core_Exited", exit.Code);
        Append($"[cvpn] core stopped, code {exit.Code}");
    });

    /// <summary>Задержку меряет само ядро через Clash API - свой пинг мимо туннеля бессмыслен.</summary>
    private async Task MeasureAsync()
    {
        if (_stats is null || Active is null) return;

        var ms = await _stats.MeasureDelayAsync();

        Dispatch(() =>
        {
            if (Active is null) return;

            Active.LatencyMs = ms;
            if (ms < 0) Append("[cvpn] latency probe failed");
        });
    }

    /// <summary>Счётчик активных правил зависит и от состава списка, и от галочки в каждом правиле.</summary>
    private void SubscribeRules()
    {
        Rules.CollectionChanged += OnRulesChanged;
        foreach (var rule in Rules) rule.PropertyChanged += OnRulePropertyChanged;
    }

    private void UnsubscribeRules()
    {
        Rules.CollectionChanged -= OnRulesChanged;
        foreach (var rule in Rules) rule.PropertyChanged -= OnRulePropertyChanged;
    }

    private void OnRulesChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        foreach (var rule in e.NewItems?.OfType<RouteRule>() ?? [])
            rule.PropertyChanged += OnRulePropertyChanged;
        foreach (var rule in e.OldItems?.OfType<RouteRule>() ?? [])
            rule.PropertyChanged -= OnRulePropertyChanged;

        Raise(nameof(ActiveRuleCount));
    }

    private void OnRulePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(RouteRule.Enabled)) Dispatch(() => Raise(nameof(ActiveRuleCount)));
    }

    private void OnTraffic(long up, long down) => Dispatch(() =>
    {
        (Upload, UploadUnit) = ByteFormat.Rate(up);
        (Download, DownloadUnit) = ByteFormat.Rate(down);

        Session.Add(up, down);
        Traffic.Push(up, down);
    });

    public void AddRule(MatchKind match, string value, RouteAction action)
    {
        if (string.IsNullOrWhiteSpace(value)) return;

        Rules.Add(new RouteRule { Match = match, Value = value.Trim(), Action = action });
        Raise(nameof(ActiveRuleCount));
        Persist();
    }

    public void Persist()
    {
        _state.Profiles = [.. Profiles];
        _state.RoutingProfiles = [.. RoutingProfiles];
        _state.ActiveRoutingProfile = ActiveRouting.Name;
        _state.Settings = Settings;
        ProfileStore.Save(_state);
    }

    /// <summary>Открывает сгенерированный config.json - удобно сверить с рабочим вручную.</summary>
    private async Task ShowGeneratedConfigAsync()
    {
        try
        {
            if (Active is not null) ConfigBuilder.Write([.. Profiles], Active, ActiveRouting, Settings);

            if (!File.Exists(AppPaths.GeneratedConfig))
            {
                Status = Loc.T("Config_NotBuilt");
                return;
            }

            if (!await Platform.Launcher.OpenFileAsync(AppPaths.GeneratedConfig))
                Status = Loc.T("Config_OpenFailed", AppPaths.GeneratedConfig);
        }
        catch (Exception ex)
        {
            Status = Loc.T("Config_OpenFailed", ex.Message);
        }
    }

    /// <summary>Автоподключение при старте - вызывается окном после загрузки.</summary>
    public async Task StartupAsync()
    {
        if (!Settings.AutoConnect || Active is null) return;

        Append("[cvpn] auto-connecting");
        await ConnectAsync();
    }

    /// <summary>
    /// Пишет в лог, как разошлись правила. Помогает понять, почему сайт всё ещё
    /// идёт через прокси: чаще всего домена просто нет в наборе.
    /// </summary>
    private void ExplainRouting()
    {
        var active = Rules.Where(r => r.Enabled).ToList();
        var direct = active.Count(r => r.Action == RouteAction.Direct);
        var blocked = active.Count(r => r.Action == RouteAction.Block);

        Append($"[cvpn] set “{ActiveRouting.Name}”: {active.Count} rules " +
               $"(direct {direct}, blocked {blocked}), " +
               $"everything else {(ActiveRouting.ProxyByDefault ? "through proxy" : "direct")}");

        // В DNS переносятся только доменные условия и локальные наборы:
        // остальное на момент инициализации ядру недоступно
        var dnsUnfriendly = active
            .Where(r => r.Action == RouteAction.Direct)
            .Where(r => r.Match is MatchKind.Geoip or MatchKind.Geosite
                or MatchKind.RuleSetRemote or MatchKind.Process)
            .Select(r => $"{r.MatchLabel} {r.DisplayValue}")
            .ToList();

        if (dnsUnfriendly.Count > 0)
        {
            Append($"[cvpn] direct rules ({string.Join(", ", dnsUnfriendly)}) apply " +
                   "to connections only, not to DNS: those domains resolve through the tunnel. " +
                   "For geo-balanced sites add a domain rule.");
        }
    }

    /// <summary>
    /// cache_file запоминает выбор селектора между запусками, и ядро берёт его
    /// вместо default из конфига. Поэтому сервер выбирается явно, когда Clash API
    /// уже поднят, и в журнал пишется то, что реально выбрано.
    /// </summary>
    private async Task ApplySelectionAsync()
    {
        if (_stats is null || Active is null) return;

        var tag = Settings.AutoSelectFastest && Profiles.Count > 1
            ? ConfigBuilder.AutoTag
            : ConfigBuilder.BuildTags([.. Profiles]).GetValueOrDefault(Active);

        if (tag is null) return;

        for (var attempt = 0; attempt < 10; attempt++)
        {
            if (await _stats.SelectAsync(tag))
            {
                var now = await _stats.GetSelectedAsync();
                Append($"[cvpn] selector: {now ?? tag}");
                return;
            }

            await Task.Delay(300);
        }

        Append($"[cvpn] could not select {Active.Name} through the Clash API: the core may use a saved choice");
    }

    /// <summary>
    /// Смена сервера. При живом туннеле идёт через селектор Clash API -
    /// ядро не перезапускается, существующие соединения не рвутся.
    /// Перегенерировать конфиг нужно только чтобы выбор пережил перезапуск.
    /// </summary>
    public async Task SelectServerAsync(ServerProfile profile)
    {
        Active = profile;
        Persist();

        if (!IsConnected || _stats is null) return;

        var tag = ConfigBuilder.BuildTags([.. Profiles]).GetValueOrDefault(profile);
        if (tag is null) return;

        if (await _stats.SelectAsync(tag))
        {
            Append($"[cvpn] switched to {profile.Name} without restarting the core");
            ConfigBuilder.Write([.. Profiles], profile, ActiveRouting, Settings);
            await MeasureAsync();
        }
        else
        {
            Status = Loc.T("Server_SwitchFailed");
            Append("[cvpn] the selector did not answer, reconnect needed");
        }
    }

    /// <summary>
    /// Версия и путь к запущенному файлу - первое, что нужно знать при разборе
    /// «правка не подействовала»: часто работает не та сборка, которую собрали.
    /// </summary>
    private void StampBuild()
    {
        var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;

        Append($"[cvpn] build {version} · {Environment.ProcessPath}");

        var launcher = Platform.ElevatedLauncher;
        if (!launcher.IsSupported || !launcher.Exists || launcher.PathMatchesCurrent()) return;

        Append($"[cvpn] warning: the scheduled task launches a different file - {launcher.RegisteredPath()}");
        Append("[cvpn] recreate the task in settings, otherwise your changes will not apply");
    }

    /// <summary>Отметка о нажатии на круг - для диагностики привязок.</summary>
    public void NoteDialClick() =>
        Append($"[cvpn] dial clicked: state {StateLabel}, profile {Active?.Name ?? "none"}");

    // ===================== служебное =====================

    private void Fail(string message)
    {
        State = TunnelState.Failing;
        Status = message;
        Append($"[cvpn] {message}");
    }

    /// <summary>
    /// Признак «осиротевшего» TUN-адаптера: прошлый процесс sing-box был снят
    /// принудительно и не успел удалить интерфейс. Создать новый нельзя -
    /// он уже есть, открыть существующий тоже нельзя - запись повреждена.
    /// </summary>
    private static bool IsStaleAdapterError(string line) =>
        line.Contains("configure tun interface", StringComparison.OrdinalIgnoreCase)
        || line.Contains("create adapter", StringComparison.OrdinalIgnoreCase);

    public void Append(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return;

        if (IsStaleAdapterError(line)) _adapterErrorSeen = true;

        // В файл пишем всё; на экране остаются последние строки
        FileLog.Current.Write(line);

        Log.Add(LogEntry.Parse(line, DateTime.Now));
        while (Log.Count > MaxLogLines) Log.RemoveAt(0);
    }

    /// <summary>Вывод ядра приходит из фонового потока, а коллекции UI этого не прощают.</summary>
    public void Dispatch(Action action)
    {
        if (Platform.Ui.CheckAccess()) action();
        else Platform.Ui.Post(action);
    }
}
