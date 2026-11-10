using System.Diagnostics;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using CVPN.Platform;

namespace CVPN.Desktop.Platform;

public sealed class DesktopLifetime : IAppLifetime
{
    private static IClassicDesktopStyleApplicationLifetime? Desktop =>
        Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;

    /// <summary>
    /// Через TryShutdown, а не Shutdown: так срабатывает ShutdownRequested,
    /// и App успевает вернуть прокси и снять kill switch.
    /// </summary>
    public void Shutdown() => Dispatcher.UIThread.Post(() => Desktop?.TryShutdown());

    public bool Restart()
    {
        var exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe)) return false;

        try
        {
            Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true });
        }
        catch (Exception)
        {
            return false;
        }

        Shutdown();
        return true;
    }
}
