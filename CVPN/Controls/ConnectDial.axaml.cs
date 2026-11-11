using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CVPN.ViewModels;

namespace CVPN.Controls;

public partial class ConnectDial : UserControl
{
    /// <summary>Наибольший наклон кнопки, градусы: заметно, но без «укачивания».</summary>
    private const double MaxTilt = 12;

    /// <summary>Доля пути к цели за кадр: плавное следование вместо рывков за мышью.</summary>
    private const double Follow = 0.18;

    private readonly DispatcherTimer _tiltTimer;
    private double _targetX;
    private double _targetY;

    public ConnectDial()
    {
        InitializeComponent();

        _tiltTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Render, (_, _) => StepTilt());
    }

    /// <summary>
    /// Команда вызывается здесь, а не привязкой: заодно пишем отметку
    /// о нажатии в лог - она помогала разбирать «круг не реагирует».
    /// </summary>
    private void OnCoreClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;

        vm.NoteDialClick();

        if (vm.ToggleConnection.CanExecute(null)) vm.ToggleConnection.Execute(null);
    }

    /// <summary>
    /// Кнопка наклоняется к курсору, как объёмная линза под пальцем: край под
    /// мышью уходит вглубь. Только мышь и перо - на сенсорном экране наклон
    /// после касания просто остался бы висеть.
    /// </summary>
    private void OnCorePointerMoved(object? sender, PointerEventArgs e)
    {
        if (e.Pointer.Type == PointerType.Touch) return;

        var size = Core.Bounds.Size;
        if (size.Width <= 0 || size.Height <= 0) return;

        var point = e.GetPosition(Core);
        var dx = Math.Clamp(point.X / size.Width * 2 - 1, -1, 1);
        var dy = Math.Clamp(point.Y / size.Height * 2 - 1, -1, 1);

        _targetY = dx * MaxTilt;
        _targetX = -dy * MaxTilt;

        if (!_tiltTimer.IsEnabled) _tiltTimer.Start();
    }

    private void OnCorePointerExited(object? sender, PointerEventArgs e)
    {
        _targetX = 0;
        _targetY = 0;

        if (!_tiltTimer.IsEnabled) _tiltTimer.Start();
    }

    /// <summary>Поворот задан в разметке; у трансформаций нет сгенерированных полей.</summary>
    private Avalonia.Media.Rotate3DTransform? TiltTransform => Tilt.RenderTransform as Avalonia.Media.Rotate3DTransform;

    /// <summary>Шаг сглаживания. Дошли до цели - таймер останавливается и не тратит кадры.</summary>
    private void StepTilt()
    {
        if (TiltTransform is not { } tilt)
        {
            _tiltTimer.Stop();
            return;
        }

        var x = tilt.AngleX + (_targetX - tilt.AngleX) * Follow;
        var y = tilt.AngleY + (_targetY - tilt.AngleY) * Follow;

        var settled = Math.Abs(_targetX - x) < 0.05 && Math.Abs(_targetY - y) < 0.05;
        if (settled)
        {
            x = _targetX;
            y = _targetY;
        }

        tilt.AngleX = x;
        tilt.AngleY = y;

        if (settled) _tiltTimer.Stop();
    }

    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _tiltTimer.Stop();
    }
}
