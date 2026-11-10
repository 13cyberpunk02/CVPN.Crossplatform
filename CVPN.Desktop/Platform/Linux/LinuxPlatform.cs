using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using CVPN.Localization;
using CVPN.Platform;

namespace CVPN.Desktop.Platform.Linux;

/// <summary>Короткий запуск утилиты с ожиданием вывода.</summary>
internal static class Tool
{
    public static (int Code, string Output) Run(string file, params string[] args)
    {
        try
        {
            var info = new ProcessStartInfo(file)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            foreach (var a in args) info.ArgumentList.Add(a);

            using var process = Process.Start(info);
            if (process is null) return (-1, "");

            var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
            if (!process.WaitForExit(5000))
            {
                process.Kill();
                return (-1, output);
            }

            return (process.ExitCode, output.Trim());
        }
        catch (Exception ex)
        {
            return (-1, ex.Message);
        }
    }

    public static bool Exists(string name) =>
        (Environment.GetEnvironmentVariable("PATH") ?? "")
        .Split(':', StringSplitOptions.RemoveEmptyEntries)
        .Any(dir => File.Exists(Path.Combine(dir, name)));
}

/// <summary>
/// Права на TUN под Linux. Нужен root или capability CAP_NET_ADMIN у самого
/// sing-box - второе удобнее: выдаётся один раз через setcap, и приложение
/// дальше работает от обычного пользователя.
/// </summary>
[SupportedOSPlatform("linux")]
public sealed class LinuxTunPrivileges(Func<string> corePath) : ITunPrivileges
{
    [DllImport("libc")]
    private static extern uint geteuid();

    public Task<(PrivilegeOutcome Outcome, string Message)> EnsureAsync()
    {
        if (geteuid() == 0) return Task.FromResult((PrivilegeOutcome.Ready, ""));

        var path = corePath();
        var (_, caps) = Tool.Run("getcap", path);

        if (caps.Contains("cap_net_admin", StringComparison.OrdinalIgnoreCase))
            return Task.FromResult((PrivilegeOutcome.Ready, ""));

        return Task.FromResult((PrivilegeOutcome.Failed, Loc.T("Linux_TunNeedsCap", path)));
    }
}

/// <summary>Автозапуск по стандарту freedesktop: ярлык в ~/.config/autostart.</summary>
[SupportedOSPlatform("linux")]
public sealed class LinuxAutoStart : IAutoStart
{
    private static string EntryPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "autostart", "cvpn.desktop");

    public bool IsSupported => true;

    public void Apply(bool enabled)
    {
        if (!enabled)
        {
            if (File.Exists(EntryPath)) File.Delete(EntryPath);
            return;
        }

        var exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe)) return;

        Directory.CreateDirectory(Path.GetDirectoryName(EntryPath)!);
        File.WriteAllText(EntryPath,
            $"""
             [Desktop Entry]
             Type=Application
             Name=CVPN
             Exec="{exe}" --minimized
             X-GNOME-Autostart-enabled=true
             """);
    }

    public void Sync(bool enabled)
    {
        if (enabled || File.Exists(EntryPath)) Apply(enabled);
    }
}

/// <summary>
/// Системный прокси через gsettings (GNOME и производные: Cinnamon, Budgie, Unity).
/// Браузеры и большинство программ GTK читают именно его.
/// </summary>
[SupportedOSPlatform("linux")]
public sealed class GnomeSystemProxy : ISystemProxy
{
    private const string Schema = "org.gnome.system.proxy";
    private string? _previousMode;

    public bool IsSupported { get; } = Tool.Exists("gsettings");

    public void Enable(int port)
    {
        if (_previousMode is null)
        {
            var (code, mode) = Tool.Run("gsettings", "get", Schema, "mode");
            _previousMode = code == 0 ? mode.Trim('\'') : "none";
        }

        foreach (var kind in new[] { "http", "https", "socks" })
        {
            Tool.Run("gsettings", "set", $"{Schema}.{kind}", "host", "127.0.0.1");
            Tool.Run("gsettings", "set", $"{Schema}.{kind}", "port", port.ToString());
        }

        Tool.Run("gsettings", "set", Schema, "ignore-hosts",
            "['localhost', '127.0.0.0/8', '::1', '10.0.0.0/8', '172.16.0.0/12', '192.168.0.0/16', '*.local', '*.lan']");
        Tool.Run("gsettings", "set", Schema, "mode", "manual");
    }

    public void Restore()
    {
        if (_previousMode is null) return;

        Tool.Run("gsettings", "set", Schema, "mode", _previousMode);
        _previousMode = null;
    }
}
