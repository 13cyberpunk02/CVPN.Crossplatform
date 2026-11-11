using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using CVPN.Localization;
using CVPN.Platform;
using CVPN.Services;
using CVPN.Shared;
using CVPN.ViewModels;
using CVPN.Views;

namespace CVPN;

public partial class App : Application
{
    private TopLevel? _topLevel;
    private OverlayDialogService? _dialogs;
    private TrayController? _tray;
    private bool _shutdownDone;

    /// <summary>
    /// Сборка платформенных сервисов. Задаётся головой приложения (Desktop, Android)
    /// до запуска Avalonia: только она знает, что умеет её платформа.
    /// </summary>
    public static Func<CommonServices, PlatformServices>? ConfigurePlatform { get; set; }

    /// <summary>Оболочка приложения - одна на процесс.</summary>
    public static MainViewModel? Shell { get; private set; }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
#if DEBUG
        this.AttachDeveloperTools();
#endif
    }

    public override void OnFrameworkInitializationCompleted()
    {
        AppPaths.EnsureCreated();

        // Язык применяется до создания разметки: она читает переводы при разборе
        Loc.Apply(ProfileStore.Load().Settings.Language);

        FileLog.Initialize(Path.Combine(AppPaths.DataDir, "logs"));
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        FileLog.Current.Write($"=== CVPN {version} started · {Environment.ProcessPath} · {Environment.OSVersion}");

        HookExceptions();

        var clipboard = new AvaloniaClipboard(() => _topLevel);
        _dialogs = new OverlayDialogService(clipboard);

        var common = new CommonServices(
            new AvaloniaUiDispatcher(),
            new AvaloniaFilePicker(() => _topLevel),
            clipboard,
            _dialogs,
            () => _topLevel);

        var configure = ConfigurePlatform
                        ?? throw new InvalidOperationException("App.ConfigurePlatform is not set by the platform head");
        var platform = configure(common);

        // Если прошлый запуск завершился аварийно с включённым kill switch,
        // система осталась без интернета. Чиним до всего остального.
        _ = RestoreNetworkAsync(platform.KillSwitch);

        Shell = new MainViewModel(platform);

        // Тема применяется сразу и меняется на лету - разметка берёт цвета через DynamicResource
        ApplyTheme(Shell.Settings.Theme);
        Shell.Settings.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AppSettings.Theme)) ApplyTheme(Shell.Settings.Theme);
        };

#if DEBUG
        // --page=routing - сразу открыть страницу: удобно проверять вёрстку
        var page = Environment.GetCommandLineArgs()
            .FirstOrDefault(a => a.StartsWith("--page=", StringComparison.Ordinal))?["--page=".Length..];
        var target = Shell.NavItems.FirstOrDefault(n => n.Key == page);
        if (target is not null) Shell.SelectedNav = target;

        // --theme=light - тема только на этот запуск, в настройки не пишется
        var theme = Environment.GetCommandLineArgs()
            .FirstOrDefault(a => a.StartsWith("--theme=", StringComparison.Ordinal))?["--theme=".Length..];
        if (theme is not null) ApplyTheme(theme);

        // --demo - график и список соединений с выдуманными данными: для снимков вёрстки
        if (Environment.GetCommandLineArgs().Contains("--demo")) FillDemo(Shell);
#endif

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = new MainWindow { DataContext = Shell };
            _topLevel = window;
            _dialogs.Attach(window.Main.Overlay);
            desktop.MainWindow = window;
            ScheduleSnapshot();

            // Opened приходит при каждом показе окна, в том числе из трея. Автоподключение -
            // только при первом: иначе каждое «Открыть» запускало ядро заново
            window.Opened += async (_, _) =>
            {
                if (_startupDone) return;
                _startupDone = true;
                await Shell.StartupAsync();
            };
            desktop.ShutdownRequested += OnShutdownRequested;
            desktop.Exit += (_, e) =>
            {
                _tray?.Dispose();
                FileLog.Current.Write($"=== exit, code {e.ApplicationExitCode}");
            };

            SetupTray(desktop, window);
        }
        else if (ApplicationLifetime is IActivityApplicationLifetime activity)
        {
            activity.MainViewFactory = CreateMainView;
        }
        else if (ApplicationLifetime is ISingleViewApplicationLifetime single)
        {
            single.MainView = CreateMainView();
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// Debug: --snapshot=путь.png[:ширина] - отрисовать окно в картинку и выйти.
    /// Рисуется только наше дерево элементов, а не экран: в кадр не попадёт
    /// ничего постороннего. Нужно, чтобы проверять вёрстку без ручных кликов.
    /// </summary>
    [System.Diagnostics.Conditional("DEBUG")]
    private void ScheduleSnapshot()
    {
        var arg = Environment.GetCommandLineArgs()
            .FirstOrDefault(a => a.StartsWith("--snapshot=", StringComparison.Ordinal));
        if (arg is null || _topLevel is not Window window) return;

        // Формат: путь.png[|ширина[|высота]]
        var parts = arg["--snapshot=".Length..].Split('|');
        var spec = parts[0];
        var width = parts.Length > 1 && int.TryParse(parts[1], out var w) ? w : 980;
        var height = parts.Length > 2 && int.TryParse(parts[2], out var h) ? h : width < 720 ? 820 : 660;

        window.Width = width;
        window.Height = height;

        var scrollEnd = Environment.GetCommandLineArgs().Contains("--scroll-end");

        window.Opened += async (_, _) =>
        {
            await Task.Delay(1200);

            // --scroll-end: показать низ длинной страницы - окно не бывает выше экрана
            if (scrollEnd)
            {
                foreach (var scroll in Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(window).OfType<ScrollViewer>())
                    scroll.ScrollToEnd();
                await Task.Delay(500);
            }

            var size = new PixelSize((int)window.Bounds.Width, (int)window.Bounds.Height);
            using var bitmap = new Avalonia.Media.Imaging.RenderTargetBitmap(size);
            bitmap.Render(window);
            // Отладочный код: PNG по умолчанию устраивает, новая перегрузка с параметрами кодека не нужна
#pragma warning disable CS0618
            bitmap.Save(spec);
#pragma warning restore CS0618

            (ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown();
        };
    }

#if DEBUG
    private static void FillDemo(MainViewModel shell)
    {
        var random = new Random(7);

        shell.Session.Begin("Amsterdam - Reality");
        for (var i = 0; i < 60; i++)
        {
            var wave = Math.Sin(i / 6.0) * 0.5 + 0.5;
            var down = (long)((300 + 2200 * wave + random.Next(0, 600)) * 1024);
            var up = (long)((40 + 260 * (1 - wave) + random.Next(0, 90)) * 1024);
            if (i is > 38 and < 44) down *= 2;

            shell.Session.Add(up, down);
            shell.Traffic.Push(up, down);
        }

        foreach (var line in (string[])
        [
            "[cvpn] building configuration",
            "[cvpn] connecting to Amsterdam - Reality (vless · reality)",
            "+0300 2026-11-10 14:02:11 INFO network: updated default interface Ethernet, index 12",
            "+0300 2026-11-10 14:02:11 INFO inbound/tun[tun-in]: started at tun0",
            "+0300 2026-11-10 14:02:11 INFO sing-box started (0.41s)",
            "+0300 2026-11-10 14:02:12 INFO [3527064221 0ms] inbound/tun[tun-in]: inbound connection to www.youtube.com:443",
            "+0300 2026-11-10 14:02:12 INFO [3527064221 12ms] outbound/vless[proxy]: outbound connection to www.youtube.com:443",
            "+0300 2026-11-10 14:02:13 DEBUG dns: exchanged api.telegram.org A 149.154.167.220 300",
            "+0300 2026-11-10 14:02:14 WARN [1873009917 5.01s] outbound/direct[direct]: dial tcp 10.0.0.15:445: i/o timeout",
            "+0300 2026-11-10 14:02:15 INFO [2208815310 0ms] inbound/tun[tun-in]: inbound connection to steamcdn-a.akamaihd.net:443",
            "+0300 2026-11-10 14:02:16 ERROR [998120454 30.2s] outbound/vless[proxy]: read tcp 192.168.1.20:51432->203.0.113.7:443: connection reset by peer",
            "+0300 2026-11-10 14:02:17 INFO [3014456627 0ms] inbound/tun[tun-in]: inbound packet connection to 1.1.1.1:53",
            "[cvpn] latency: 84 ms",
            "+0300 2026-11-10 14:02:19 INFO [1220993844 0ms] outbound/direct[direct]: outbound connection to gosuslugi.ru:443"
        ])
            shell.Append(line);

        var now = DateTimeOffset.Now;
        Models.ConnectionInfo Make(string host, int port, string outbound, string rule, string process, string net,
            long down, long up, int seconds) => new()
        {
            Id = host, Host = host, Port = port, Outbound = outbound, Rule = rule, Process = process,
            Network = net, Download = down, Upload = up, Started = now.AddSeconds(-seconds)
        };

        shell.ConnectionsPage.ShowDemo(
        [
            Make("rr4---sn-n8v7znsl.googlevideo.com", 443, "proxy", "final", "chrome.exe", "tcp", 48_200_000, 1_100_000, 312),
            Make("api.telegram.org", 443, "proxy", "domain_suffix=telegram.org", "Telegram.exe", "tcp", 6_400_000, 820_000, 1260),
            Make("steamcdn-a.akamaihd.net", 443, "direct", "domain_suffix=akamaihd.net", "steam.exe", "tcp", 21_000_000, 240_000, 95),
            Make("discord.com", 443, "byedpi", "domain_suffix=discord.com", "Discord.exe", "udp", 3_100_000, 1_900_000, 640),
            Make("gosuslugi.ru", 443, "direct", "geosite-ru", "msedge.exe", "tcp", 900_000, 120_000, 48),
            Make("github.com", 443, "proxy", "final", "git.exe", "tcp", 310_000, 64_000, 12)
        ]);
    }
#endif

    /// <summary>«dark» / «light» / пусто - как в системе (Avalonia следит за ней сама).</summary>
    private void ApplyTheme(string code) => RequestedThemeVariant = code switch
    {
        "dark" => Avalonia.Styling.ThemeVariant.Dark,
        "light" => Avalonia.Styling.ThemeVariant.Light,
        _ => Avalonia.Styling.ThemeVariant.Default
    };

    private Control CreateMainView()
    {
        var view = new MainView { DataContext = Shell };
        _dialogs?.Attach(view.Overlay);
        view.AttachedToVisualTree += async (_, _) =>
        {
            _topLevel = TopLevel.GetTopLevel(view);

            // Автоподключение - один раз за процесс: Android пересоздаёт разметку
            // при повороте экрана и возврате из фона
            if (_startupDone || Shell is null) return;
            _startupDone = true;
            await Shell.StartupAsync();
        };
        return view;
    }

    private bool _startupDone;

    /// <summary>
    /// Перед выходом надо вернуть системный прокси и снять kill switch, иначе
    /// пользователь останется без интернета. Это асинхронно, поэтому первый
    /// запрос на выход откладываем, прибираемся и выходим уже сами.
    /// </summary>
    /// <summary>
    /// Трей и жизнь окна. Приложение выходит только явно (меню трея, крестик
    /// без «сворачивать в трей») - иначе спрятанное окно завершало бы процесс
    /// вместе с туннелем.
    /// </summary>
    private void SetupTray(IClassicDesktopStyleApplicationLifetime desktop, Window window)
    {
        // Отладочный снимок не должен мелькать значком в трее
        if (Environment.GetCommandLineArgs().Any(a => a.StartsWith("--snapshot=", StringComparison.Ordinal))) return;

        desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

        void ShowWindow()
        {
            window.Show();
            if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
            window.Activate();
        }

        _tray = new TrayController(Shell!, ShowWindow, () => desktop.TryShutdown());

        window.Closing += (_, e) =>
        {
            // Идёт настоящий выход - окно закрывается вместе с приложением
            if (_shutdownDone) return;

            e.Cancel = true;

            if (Shell!.Settings.CloseToTray)
            {
                window.Hide();
                if (Shell.IsConnected) Shell.Notify(Loc.T("Tray_StillRunningHint"));
                return;
            }

            desktop.TryShutdown();
        };

        // Автозапуск: окно не показываем, приложение живёт в трее
        // Прячем один раз: Opened приходит и при открытии из трея, и окно тут же исчезало бы снова
        if (Environment.GetCommandLineArgs().Contains("--minimized"))
        {
            void HideOnce(object? sender, EventArgs e)
            {
                window.Opened -= HideOnce;
                window.Hide();
            }

            window.Opened += HideOnce;
        }
    }

    private async void OnShutdownRequested(object? sender, ShutdownRequestedEventArgs e)
    {
        if (_shutdownDone || Shell is null) return;

        e.Cancel = true;
        _shutdownDone = true;

        try
        {
            await Shell.ShutdownAsync();
        }
        catch (Exception ex)
        {
            Record("Shutdown", ex);
        }

        (ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown();
    }

    private static void HookExceptions()
    {
        // Исключение в UI-потоке: приложение можно оставить живым - принудительное
        // закрытие с открытым туннелем хуже, пользователь останется без интернета
        Dispatcher.UIThread.UnhandledException += (_, e) =>
        {
            Record("UI exception", e.Exception);
            Shell?.Notify(Loc.T("Crash_Message", e.Exception.Message, FileLog.Current.CurrentFile));
            e.Handled = true;
        };

        // Исключение в фоновом потоке: процесс уже не спасти, но записать успеем
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Record("Unhandled exception", args.ExceptionObject as Exception);

        // Задача упала, и её результат никто не проверил
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Record("Exception in a background task", args.Exception);
            args.SetObserved();
        };
    }

    private static async Task RestoreNetworkAsync(IKillSwitch killSwitch)
    {
        if (!killSwitch.IsSupported || !killSwitch.IsActive) return;

        FileLog.Current.Write("[cvpn] kill switch rules left from a previous run, removing them");

        var problem = await killSwitch.DisableAsync();

        FileLog.Current.Write(problem.Length > 0
            ? $"[cvpn] could not remove the rules: {problem}"
            : "[cvpn] rules removed, network restored");
    }

    private static void Record(string title, Exception? exception)
    {
        if (exception is null) return;

        FileLog.Current.WriteBlock(title, exception.ToString());
    }
}
