using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using CVPN.Localization;
using CVPN.Platform;
using QRCoder;

namespace CVPN.Views;

/// <summary>Экспорт профиля или подписки: QR-код и ссылка.</summary>
public partial class ExportView : UserControl
{
    private readonly string _payload = "";
    private readonly IClipboardService? _clipboard;
    private readonly Action? _close;

    // Для дизайнера
    public ExportView() => InitializeComponent();

    public ExportView(string payload, string title, string subtitle, IClipboardService clipboard, Action close)
        : this()
    {
        _payload = payload;
        _clipboard = clipboard;
        _close = close;

        Header.Text = title;
        Subtitle.Text = subtitle;
        LinkBox.Text = payload;

        var qr = Render(payload);
        QrImage.Source = qr;
        QrFrame.IsVisible = qr is not null;
    }

    private static Bitmap? Render(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload)) return null;

        try
        {
            using var generator = new QRCodeGenerator();
            // Уровень Q переживает до 25% повреждений - запас на переснятое фото экрана
            using var data = generator.CreateQrCode(payload, QRCodeGenerator.ECCLevel.Q);
            using var png = new PngByteQRCode(data);

            return new Bitmap(new MemoryStream(png.GetGraphic(10)));
        }
        catch (Exception)
        {
            // Слишком длинная подписка может не поместиться в QR - тогда остаётся текст
            return null;
        }
    }

    private async void OnCopy(object? sender, RoutedEventArgs e)
    {
        if (_clipboard is null) return;

        try
        {
            await _clipboard.SetTextAsync(_payload);
            CopyButton.Content = Loc.T("Export_Copied");
        }
        catch (Exception)
        {
            CopyButton.Content = Loc.T("Export_CopyFailed");
        }
    }

    private void OnClose(object? sender, RoutedEventArgs e) => _close?.Invoke();
}
