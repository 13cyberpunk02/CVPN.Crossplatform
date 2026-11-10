using System.Diagnostics;
using System.Runtime.InteropServices;

namespace CVPN.Shared;

/// <summary>
/// Штатная остановка дочернего консольного процесса.
/// Нужна потому, что sing-box снимает TUN-интерфейс только при корректном
/// завершении. Если процесс убить, адаптер остаётся в системе, и следующий
/// запуск падает с «create adapter: file already exists».
///
/// На Windows - CTRL_C через общую консоль, на Linux и Android - SIGTERM.
/// </summary>
public static class ConsoleSignal
{
    private const uint CtrlCEvent = 0;
    private const int SigTerm = 15;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(uint processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool FreeConsole();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetConsoleCtrlHandler(IntPtr handler, bool add);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GenerateConsoleCtrlEvent(uint ctrlEvent, uint processGroupId);

    [DllImport("libc", EntryPoint = "kill", SetLastError = true)]
    private static extern int UnixKill(int pid, int signal);

    /// <summary>
    /// Консоль у процесса одна: два одновременных вызова (например, с фоновых
    /// потоков) перецепили бы её друг у друга. Сериализуем.
    /// </summary>
    private static readonly object Gate = new();

    /// <summary>
    /// Просит процесс завершиться и ждёт указанное время. Возвращает false,
    /// если он не отреагировал - тогда вызывающему коду остаётся Kill.
    /// </summary>
    public static bool TryGracefulStop(Process process, TimeSpan timeout)
    {
        if (!OperatingSystem.IsWindows()) return TryUnixStop(process, timeout);

        lock (Gate) return TryWindowsStop(process, timeout);
    }

    private static bool TryUnixStop(Process process, TimeSpan timeout)
    {
        try
        {
            if (UnixKill(process.Id, SigTerm) != 0) return false;
            return process.WaitForExit((int)timeout.TotalMilliseconds);
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static bool TryWindowsStop(Process process, TimeSpan timeout)
    {
        if (!AttachConsole((uint)process.Id)) return false;

        var handlerDisabled = false;

        try
        {
            handlerDisabled = SetConsoleCtrlHandler(IntPtr.Zero, true);
            if (!handlerDisabled) return false;

            if (!GenerateConsoleCtrlEvent(CtrlCEvent, 0)) return false;

            return process.WaitForExit((int)timeout.TotalMilliseconds);
        }
        catch (Exception)
        {
            return false;
        }
        finally
        {
            if (handlerDisabled) SetConsoleCtrlHandler(IntPtr.Zero, false);
            FreeConsole();
        }
    }
}
