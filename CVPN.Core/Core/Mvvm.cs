using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace CVPN.Core;

public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void Raise([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        CommandRequery.Suggest();
    }

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        Raise(name);
        return true;
    }
}

/// <summary>
/// Замена CommandManager.RequerySuggested из WPF, которого в Avalonia нет.
/// Любое изменение свойства в модели просит команды перепроверить CanExecute -
/// так ведут себя все RelayCommand из старого кода без правок во ViewModels.
/// Уведомления склеиваются: сколько бы свойств ни поменялось подряд,
/// команды перепроверятся один раз, на потоке UI.
/// </summary>
public static class CommandRequery
{
    private static int _pending;

    /// <summary>Как поставить работу в очередь UI. Задаётся UI-проектом при запуске.</summary>
    public static Action<Action> Post { get; set; } = action => action();

    public static event EventHandler? Suggested;

    public static void Suggest()
    {
        if (Interlocked.Exchange(ref _pending, 1) == 1) return;

        Post(() =>
        {
            Volatile.Write(ref _pending, 0);
            Suggested?.Invoke(null, EventArgs.Empty);
        });
    }
}

public sealed class RelayCommand(Action<object?> execute, Predicate<object?>? canExecute = null)
    : ICommand
{
    public RelayCommand(Action execute, Func<bool>? canExecute = null)
        : this(_ => execute(), canExecute is null ? null : _ => canExecute()) { }

    public bool CanExecute(object? parameter) => canExecute?.Invoke(parameter) ?? true;

    public void Execute(object? parameter) => execute(parameter);

    public event EventHandler? CanExecuteChanged
    {
        add => CommandRequery.Suggested += value;
        remove => CommandRequery.Suggested -= value;
    }
}
