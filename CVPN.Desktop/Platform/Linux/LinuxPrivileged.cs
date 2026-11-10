using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using CVPN.Ipc;
using CVPN.Localization;
using CVPN.Platform;
using CVPN.Services;
using CVPN.Shared;

namespace CVPN.Desktop.Platform.Linux;

/// <summary>
/// Запуск от root: напрямую, если приложение уже root, иначе через pkexec -
/// polkit покажет системное окно пароля, как UAC на Windows.
/// </summary>
[SupportedOSPlatform("linux")]
internal static class Root
{
    [DllImport("libc")]
    private static extern uint geteuid();

    public static bool IsRoot => geteuid() == 0;

    /// <summary>Код возврата pkexec, когда пользователь закрыл окно пароля.</summary>
    private const int PkexecDismissed = 126;

    /// <summary>Пустая строка - успех, иначе текст ошибки.</summary>
    public static async Task<string> RunAsync(string file, IEnumerable<string> args, string? stdin = null)
    {
        string runner;
        var argv = new List<string>();

        if (IsRoot)
        {
            runner = file;
        }
        else
        {
            if (!Tool.Exists("pkexec")) return Loc.T("Linux_NoPkexec");
            runner = "pkexec";
            argv.Add(file);
        }

        argv.AddRange(args);

        try
        {
            var info = new ProcessStartInfo(runner)
            {
                UseShellExecute = false,
                RedirectStandardInput = stdin is not null,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            foreach (var a in argv) info.ArgumentList.Add(a);

            using var process = Process.Start(info);
            if (process is null) return $"{runner}: start failed";

            if (stdin is not null)
            {
                await process.StandardInput.WriteAsync(stdin);
                process.StandardInput.Close();
            }

            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (process.ExitCode == 0) return "";
            if (!IsRoot && process.ExitCode == PkexecDismissed) return Loc.T("Linux_AuthCancelled");

            var text = ((await error) + " " + (await output)).Trim();
            return text.Length > 0 ? text : $"{Path.GetFileName(file)}: code {process.ExitCode}";
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }
}

/// <summary>
/// Kill switch на nftables (правила - NftKillSwitch в CVPN.Core). Правила
/// ставит root; отметка о включении - файл в каталоге данных, как на Windows:
/// по нему после аварийного выхода правила снимаются при следующем запуске.
/// </summary>
[SupportedOSPlatform("linux")]
public sealed class LinuxKillSwitch(Action<string> log) : IKillSwitch
{
    private static string MarkerFile => Path.Combine(AppPaths.DataDir, "killswitch.active");

    public bool IsSupported { get; } = Tool.Exists("nft") || File.Exists("/usr/sbin/nft");

    public bool IsActive => File.Exists(MarkerFile);

    public async Task<string> EnableAsync(string corePath, string? byeDpiPath)
    {
        // Трафик ciadpi не несёт метку ядра - честно предупреждаем
        if (byeDpiPath is not null) log($"[cvpn] {Loc.T("Linux_KillSwitchByeDpi")}");

        var problem = await ApplyAsync(NftKillSwitch.Enable(allowLocalNetwork: true));
        if (problem.Length > 0) return Loc.T("KillSwitch_EnableFailed", problem);

        Mark(true);
        return "";
    }

    public async Task<string> DisableAsync()
    {
        var problem = await ApplyAsync(NftKillSwitch.Disable());
        if (problem.Length == 0) Mark(false);
        return problem;
    }

    public async Task RestoreAfterCrashAsync()
    {
        if (IsActive) await DisableAsync();
    }

    /// <summary>Правила подаются через stdin: «nft -f -» применяет их одной транзакцией.</summary>
    private static Task<string> ApplyAsync(string rules)
    {
        var nft = File.Exists("/usr/sbin/nft") ? "/usr/sbin/nft" : "nft";
        return Root.RunAsync(nft, ["-f", "-"], rules);
    }

    private static void Mark(bool active)
    {
        try
        {
            if (active)
            {
                AppPaths.EnsureCreated();
                File.WriteAllText(MarkerFile, DateTime.Now.ToString("O"));
            }
            else if (File.Exists(MarkerFile))
            {
                File.Delete(MarkerFile);
            }
        }
        catch (Exception)
        {
            // Без отметки работаем без страховки - правила всё равно сняты или стоят
        }
    }
}

/// <summary>
/// Служба туннеля как юнит systemd. Установка - скрипт linux/cvpn-service.sh
/// от root: копирует службу и ядро в /opt/cvpn, заводит группу cvpn и
/// включает юнит.
/// </summary>
[SupportedOSPlatform("linux")]
public sealed class LinuxServiceManager(Func<string> corePath) : ITunnelServiceManager
{
    private const string UnitFile = "/etc/systemd/system/cvpn-tunnel.service";

    private static string Script => Path.Combine(AppContext.BaseDirectory, "linux", "cvpn-service.sh");
    private static string ServiceDir => Path.Combine(AppContext.BaseDirectory, "service");

    public bool IsSupported { get; } = Directory.Exists("/run/systemd/system");

    public bool IsInstalled => File.Exists(UnitFile);

    public Task<bool> IsAvailableAsync() => ServiceClient.IsAvailableAsync();

    public async Task<string> DescribeAsync()
    {
        if (await ServiceClient.IsAvailableAsync()) return Loc.T("Settings_ServiceOk");

        if (IsInstalled)
        {
            // Самая частая причина «не отвечает» сразу после установки - группа
            // ещё не действует в текущем сеансе
            return InGroup() ? Loc.T("Settings_ServiceNotRunning") : Loc.T("Linux_ServiceRelogin");
        }

        return File.Exists(Path.Combine(ServiceDir, "CVPN.Service"))
            ? Loc.T("Settings_ServiceMissing")
            : Loc.T("Settings_ServiceFilesMissing", ServiceDir);
    }

    // Интерфейс синхронный (так устроен и sc.exe на Windows). Ждём через Task.Run:
    // прямое ожидание асинхронного кода на потоке UI привело бы к взаимоблокировке
    public bool Install(out string error)
    {
        var core = corePath();
        error = Task.Run(() => Root.RunAsync("/bin/sh", [Script, "install", ServiceDir, core, Environment.UserName]))
            .GetAwaiter().GetResult();
        return error.Length == 0;
    }

    public bool Uninstall(out string error)
    {
        error = Task.Run(() => Root.RunAsync("/bin/sh", [Script, "uninstall"])).GetAwaiter().GetResult();
        return error.Length == 0;
    }

    /// <summary>Группа cvpn есть среди групп текущего процесса (а не только в /etc/group).</summary>
    private static bool InGroup()
    {
        var (code, groups) = Tool.Run("id", "-nG");
        return code == 0 && groups.Split(' ').Contains(IpcContract.UnixGroup);
    }
}
