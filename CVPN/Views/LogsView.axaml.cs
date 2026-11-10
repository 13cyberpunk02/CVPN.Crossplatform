using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CVPN.Models;
using CVPN.ViewModels;

namespace CVPN.Views;

/// <summary>
/// Прокрутка лога. Новая строка тянет список вниз, только пока он и так
/// внизу: стоит пролистать вверх - автопрокрутка встаёт и появляется
/// кнопка «К последним». Как в терминале.
/// </summary>
public partial class LogsView : UserControl
{
    /// <summary>Насколько можно не доскроллить до низа и всё ещё считаться «внизу».</summary>
    private const double BottomSlack = 24;

    private INotifyCollectionChanged? _visible;
    private ScrollViewer? _scroll;
    private bool _stick = true;

    public LogsView() => InitializeComponent();

    private LogsViewModel? Vm => DataContext as LogsViewModel;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        if (Vm is not { } vm) return;

        _visible = vm.Visible;
        _visible.CollectionChanged += OnVisibleChanged;

        _stick = true;
        ScrollToEnd();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);

        if (_visible is not null) _visible.CollectionChanged -= OnVisibleChanged;
        _visible = null;

        if (_scroll is not null) _scroll.ScrollChanged -= OnScrollChanged;
        _scroll = null;
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);

        _scroll = LogList.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
        if (_scroll is not null) _scroll.ScrollChanged += OnScrollChanged;
    }

    private void OnVisibleChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            // Новый поиск или снятая пауза - показываем самое свежее
            _stick = true;
            ScrollToEnd();
            return;
        }

        if (e.Action == NotifyCollectionChangedAction.Add && _stick) ScrollToEnd();
    }

    /// <summary>
    /// Решаем по сдвигу, а не по размеру: новая строка увеличивает высоту
    /// списка раньше, чем мы до неё докрутим, и без этой проверки
    /// автопрокрутка выключалась бы сама собой.
    /// </summary>
    private void OnScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (_scroll is null || e.OffsetDelta.Y == 0) return;

        _stick = _scroll.Offset.Y + _scroll.Viewport.Height >= _scroll.Extent.Height - BottomSlack;
        ToBottom.IsVisible = !_stick;
    }

    private void ScrollToEnd() => Dispatcher.UIThread.Post(() =>
    {
        var count = LogList.ItemCount;
        if (count > 0) LogList.ScrollIntoView(count - 1);
        ToBottom.IsVisible = false;
    }, DispatcherPriority.Background);

    private void OnToBottom(object? sender, RoutedEventArgs e)
    {
        _stick = true;
        ScrollToEnd();
    }

    /// <summary>Ctrl+C - выделенные строки; Ctrl+A - выделить все.</summary>
    private void OnListKeyDown(object? sender, KeyEventArgs e)
    {
        var command = e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta);
        if (!command) return;

        if (e.Key == Key.C)
        {
            _ = CopySelectedAsync();
            e.Handled = true;
        }
        else if (e.Key == Key.A)
        {
            LogList.SelectAll();
            e.Handled = true;
        }
    }

    private void OnCopySelected(object? sender, RoutedEventArgs e) => _ = CopySelectedAsync();

    /// <summary>Строки копируются в порядке лога, а не в порядке щелчков.</summary>
    private async Task CopySelectedAsync()
    {
        if (Vm is not { } vm || LogList.SelectedItems is null) return;

        var selected = LogList.SelectedItems.OfType<LogEntry>().ToHashSet();
        await vm.CopyAsync(vm.Visible.Where(selected.Contains));
    }
}
