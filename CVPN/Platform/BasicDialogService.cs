using Avalonia.Controls;
using Avalonia.Layout;
using CVPN.Localization;
using CVPN.Models;

namespace CVPN.Platform;

/// <summary>
/// Временная реализация диалогов до переноса окон (этап 2): вопрос «да / нет»
/// окном на десктопе, редактор профиля пока недоступен, экспорт копирует
/// ссылку в буфер обмена.
/// </summary>
public sealed class BasicDialogService(Func<TopLevel?> topLevel, IClipboardService clipboard) : IDialogService
{
    public async Task<bool> ConfirmAsync(string message)
    {
        if (topLevel() is not Window owner) return false;

        var answer = false;
        var dialog = new Window
        {
            Title = "CVPN",
            SizeToContent = SizeToContent.WidthAndHeight,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false
        };

        var yes = new Button { Content = Loc.T("Common_Yes"), MinWidth = 80 };
        var no = new Button { Content = Loc.T("Common_No"), MinWidth = 80 };
        yes.Click += (_, _) =>
        {
            answer = true;
            dialog.Close();
        };
        no.Click += (_, _) => dialog.Close();

        dialog.Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(20),
            Spacing = 16,
            MaxWidth = 420,
            Children =
            {
                new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8,
                    Children = { yes, no }
                }
            }
        };

        await dialog.ShowDialog(owner);
        return answer;
    }

    public Task<ServerProfile?> EditProfileAsync(ServerProfile? existing) =>
        Task.FromResult<ServerProfile?>(null);

    public async Task ShowExportAsync(string payload, string title, string subtitle) =>
        await clipboard.SetTextAsync(payload);
}
