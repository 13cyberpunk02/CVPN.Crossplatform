using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using CVPN.Models;
using CVPN.ViewModels;
using CVPN.Views;

namespace CVPN.Platform;

/// <summary>
/// Диалоги слоем поверх главного экрана. Отдельные окна на телефоне невозможны,
/// а на десктопе слой выглядит не хуже модального окна - поэтому способ один
/// для всех платформ. Слой предоставляет MainView через <see cref="Attach"/>.
/// </summary>
public sealed class OverlayDialogService(IClipboardService clipboard) : IDialogService
{
    private Panel? _layer;

    /// <summary>Слой, в который кладутся диалоги. Вызывает MainView при появлении.</summary>
    public void Attach(Panel layer) => _layer = layer;

    public Task<bool> ConfirmAsync(string message) =>
        ShowAsync<bool>(close => new ConfirmView(message, close), cancelResult: false);

    public async Task<ServerProfile?> EditProfileAsync(ServerProfile? existing)
    {
        var vm = new ProfileEditorViewModel(existing);
        var saved = await ShowAsync<bool>(close => new ProfileEditorView(vm, close), cancelResult: false);
        return saved ? vm.Draft : null;
    }

    public Task ShowExportAsync(string payload, string title, string subtitle) =>
        ShowAsync<bool>(close => new ExportView(payload, title, subtitle, clipboard, () => close(true)),
            cancelResult: true);

    /// <summary>
    /// Кладёт содержимое на слой и ждёт, пока оно само себя закроет. Клик по
    /// затемнению и Escape - это отмена с <paramref name="cancelResult"/>.
    /// </summary>
    private Task<T> ShowAsync<T>(Func<Action<T>, Control> build, T cancelResult)
    {
        var layer = _layer;
        if (layer is null) return Task.FromResult(cancelResult);

        var done = new TaskCompletionSource<T>();
        Control? host = null;

        void Close(T result)
        {
            if (host is not null) layer.Children.Remove(host);
            layer.IsVisible = layer.Children.Count > 0;
            done.TrySetResult(result);
        }

        var content = build(Close);

        // Цвета и анимация появления - в стилях Border.scrim и Border.dialog (Controls.axaml)
        var scrim = new Border { Classes = { "scrim" } };
        scrim.PointerPressed += (_, _) => Close(cancelResult);

        var card = new Border
        {
            Classes = { "card", "dialog" },
            RenderTransform = new Avalonia.Media.ScaleTransform(1, 1),
            MaxWidth = 540,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(16),
            Padding = new Thickness(24, 20),
            Child = new ScrollViewer
            {
                HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
                Content = content
            }
        };

        var panel = new Panel { Children = { scrim, card } };
        panel.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            e.Handled = true;
            Close(cancelResult);
        };

        host = panel;
        layer.Children.Add(panel);
        layer.IsVisible = true;

        // Фокус внутрь диалога - иначе Escape и Tab уходят странице под ним
        content.AttachedToVisualTree += (_, _) => content.Focus();

        return done.Task;
    }
}
