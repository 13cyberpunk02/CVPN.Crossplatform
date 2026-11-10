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
#endif

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = new MainWindow { DataContext = Shell };
            _topLevel = window;
            _dialogs.Attach(window.Main.Overlay);
            desktop.MainWindow = window;
            ScheduleSnapshot();

            window.Opened += async (_, _) => await Shell.StartupAsync();
            desktop.ShutdownRequested += OnShutdownRequested;
            desktop.Exit += (_, e) => FileLog.Current.Write($"=== exit, code {e.ApplicationExitCode}");
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

        var spec = arg["--snapshot=".Length..];
        var width = 980;
        var sep = spec.LastIndexOf('|');
        if (sep > 0 && int.TryParse(spec[(sep + 1)..], out var w))
        {
            width = w;
            spec = spec[..sep];
        }

        window.Width = width;
        window.Height = width < 720 ? 820 : 660;

        window.Opened += async (_, _) =>
        {
            await Task.Delay(1500);

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
        view.AttachedToVisualTree += (_, _) => _topLevel = TopLevel.GetTopLevel(view);
        return view;
    }

    /// <summary>
    /// Перед выходом надо вернуть системный прокси и снять kill switch, иначе
    /// пользователь останется без интернета. Это асинхронно, поэтому первый
    /// запрос на выход откладываем, прибираемся и выходим уже сами.
    /// </summary>
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
