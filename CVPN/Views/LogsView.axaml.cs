using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using CVPN.ViewModels;

namespace CVPN.Views;

/// <summary>Лог держится внизу: новая строка прокручивает список к себе.</summary>
public partial class LogsView : UserControl
{
    private INotifyCollectionChanged? _log;

    public LogsView() => InitializeComponent();

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        if (DataContext is not LogsViewModel vm) return;

        _log = vm.Log;
        _log.CollectionChanged += OnLogChanged;
        ScrollToEnd();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);

        if (_log is null) return;

        _log.CollectionChanged -= OnLogChanged;
        _log = null;
    }

    private void OnLogChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add) ScrollToEnd();
    }

    private void ScrollToEnd() => Dispatcher.UIThread.Post(() =>
    {
        var count = LogList.ItemCount;
        if (count > 0) LogList.ScrollIntoView(count - 1);
    }, DispatcherPriority.Background);
}
