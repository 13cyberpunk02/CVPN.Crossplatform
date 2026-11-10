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

        var common = new CommonServices(
            new AvaloniaUiDispatcher(),
            new AvaloniaFilePicker(() => _topLevel),
            new AvaloniaClipboard(() => _topLevel),
            () => _topLevel);

        var configure = ConfigurePlatform
                        ?? throw new InvalidOperationException("App.ConfigurePlatform is not set by the platform head");
        var platform = configure(common);

        // Если прошлый запуск завершился аварийно с включённым kill switch,
        // система осталась без интернета. Чиним до всего остального.
        _ = RestoreNetworkAsync(platform.KillSwitch);

        Shell = new MainViewModel(platform);

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = new MainWindow { DataContext = Shell };
            _topLevel = window;
            desktop.MainWindow = window;

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

    private Control CreateMainView()
    {
        var view = new MainView { DataContext = Shell };
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
