using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows.Input;
using CVPN.Core;
using CVPN.Localization;
using CVPN.Models;
using CVPN.Shared;

namespace CVPN.ViewModels;

/// <summary>Какие строки показывать.</summary>
public enum LogFilter
{
    All,
    Warnings,
    Errors
}

/// <summary>
/// Страница логов. Сама коллекция живёт в оболочке - в неё пишет ядро,
/// и она нужна независимо от того, открыта ли страница. Здесь - то, что
/// видно на экране: поиск, фильтр по уровню и пауза.
/// </summary>
public sealed class LogsViewModel : PageViewModel
{
    private bool _wasEmpty = true;

    public LogsViewModel(MainViewModel shell) : base(shell)
    {
        Clear = new RelayCommand(() =>
        {
            shell.Log.Clear();
            Pending = 0;
        }, () => shell.Log.Count > 0);
        Copy = new RelayCommand(async () => await CopyAsync(Visible), () => Visible.Count > 0);
        OpenFolder = new RelayCommand(async () => await ShowFolderAsync(), () => CanOpenFolder);
        TogglePause = new RelayCommand(() => IsPaused = !IsPaused);
        SetFilter = new RelayCommand(p =>
        {
            if (p is string s && Enum.TryParse<LogFilter>(s, out var level)) Level = level;
        });

        shell.Log.CollectionChanged += OnLogChanged;
        Rebuild();
    }

    /// <summary>Строки на экране: после поиска и фильтра, на паузе - замороженные.</summary>
    public ObservableCollection<LogEntry> Visible { get; } = [];

    public ICommand Clear { get; }
    public ICommand Copy { get; }
    public ICommand OpenFolder { get; }
    public ICommand TogglePause { get; }
    public ICommand SetFilter { get; }

    /// <summary>На Android папку открыть нечем - кнопку прячем.</summary>
    public bool CanOpenFolder => Shell.Platform.Launcher.CanOpenFiles;

    public string Search
    {
        get;
        set
        {
            if (Set(ref field, value ?? "")) Rebuild();
        }
    } = string.Empty;

    public LogFilter Level
    {
        get;
        set
        {
            if (Set(ref field, value)) Rebuild();
        }
    }

    /// <summary>
    /// Пауза замораживает список: новые строки копятся, но экран не двигается -
    /// можно спокойно читать и выделять. Снятие паузы показывает всё накопленное.
    /// </summary>
    public bool IsPaused
    {
        get;
        set
        {
            if (!Set(ref field, value)) return;

            if (!value) Rebuild();
            Raise(nameof(PauseLabel));
        }
    }

    public string PauseLabel => IsPaused ? Loc.T("Logs_Resume") : Loc.T("Logs_Pause");

    /// <summary>Сколько строк пришло за время паузы.</summary>
    public int Pending
    {
        get;
        private set
        {
            if (!Set(ref field, value)) return;

            Raise(nameof(HasPending));
            Raise(nameof(PendingLabel));
        }
    }

    public bool HasPending => Pending > 0;

    public string PendingLabel => Loc.T("Logs_Pending", Pending);

    public int ErrorCount => Shell.Log.Count(e => e.IsError);
    public int WarnCount => Shell.Log.Count(e => e.IsWarn);
    public int TotalCount => Shell.Log.Count;

    /// <summary>«Показано 120 из 500».</summary>
    public string ShownLabel => Loc.T("Logs_Shown", Visible.Count, Shell.Log.Count);

    public bool IsEmpty => Visible.Count == 0;

    /// <summary>Строки есть, но поиск или фильтр их все скрыли.</summary>
    public bool NothingFound => Shell.Log.Count > 0 && Visible.Count == 0;

    public bool Accepts(LogEntry entry) => Level switch
    {
        LogFilter.Errors => entry.IsError,
        LogFilter.Warnings => entry.IsError || entry.IsWarn,
        _ => true
    } && entry.Matches(Search.Trim());

    private void OnLogChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Add when IsPaused:
                Pending += e.NewItems?.Count ?? 0;
                break;

            case NotifyCollectionChangedAction.Add:
                foreach (var entry in e.NewItems?.OfType<LogEntry>() ?? [])
                    if (Accepts(entry)) Visible.Add(entry);
                break;

            // Старые строки вытесняются по одной сверху - убираем их и с экрана.
            // На паузе экран не трогаем: строки остаются, пока пауза не снята
            case NotifyCollectionChangedAction.Remove:
                if (IsPaused) break;
                foreach (var entry in e.OldItems?.OfType<LogEntry>() ?? [])
                    if (Visible.Count > 0 && ReferenceEquals(Visible[0], entry)) Visible.RemoveAt(0);
                break;

            default:
                Rebuild();
                return;
        }

        RaiseCounts();
    }

    private void Rebuild()
    {
        Visible.Clear();

        foreach (var entry in Shell.Log.Where(Accepts))
            Visible.Add(entry);

        Pending = 0;
        RaiseCounts();
    }

    private void RaiseCounts()
    {
        Raise(nameof(ErrorCount));
        Raise(nameof(WarnCount));
        Raise(nameof(TotalCount));
        Raise(nameof(ShownLabel));
        Raise(nameof(IsEmpty));
        Raise(nameof(NothingFound));

        // Доступность кнопок меняется только на границе «пусто / не пусто»:
        // перепроверять все команды на каждую строку лога незачем
        var empty = Shell.Log.Count == 0 || Visible.Count == 0;
        if (empty == _wasEmpty) return;

        _wasEmpty = empty;
        CommandRequery.Suggest();
    }

    /// <summary>Копирует строки как есть - в том виде, в каком их ждут в issue.</summary>
    public async Task CopyAsync(IEnumerable<LogEntry> entries)
    {
        var lines = entries.Select(e => e.Raw).ToList();
        if (lines.Count == 0) return;

        try
        {
            await Shell.Platform.Clipboard.SetTextAsync(string.Join(Environment.NewLine, lines));
            Shell.Notify(Loc.T("Logs_CopiedLines", lines.Count));
        }
        catch (Exception ex)
        {
            Shell.Notify(Loc.T("Logs_CopyFailed", ex.Message));
        }
    }

    /// <summary>Открывает папку с логами - то, что просят приложить к issue.</summary>
    private async Task ShowFolderAsync()
    {
        try
        {
            FileLog.Current.Prepare();

            if (!await Shell.Platform.Launcher.OpenFolderAsync(FileLog.Current.Directory))
                Shell.Notify(Loc.T("Logs_FolderFailed", FileLog.Current.Directory));
        }
        catch (Exception ex)
        {
            Shell.Notify(Loc.T("Logs_FolderFailed", ex.Message));
        }
    }
}
