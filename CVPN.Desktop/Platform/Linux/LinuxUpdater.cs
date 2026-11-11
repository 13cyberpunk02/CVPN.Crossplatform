using System.Diagnostics;
using System.Runtime.Versioning;
using CVPN.Localization;
using CVPN.Platform;
using CVPN.Services;

namespace CVPN.Desktop.Platform.Linux;

/// <summary>
/// Самообновление на Linux:
///  • AppImage - новый файл встаёт на место старого (запущенный образ
///    смонтирован, его подмена работающему процессу не мешает);
///  • .deb - пакет ставится через pkexec dpkg -i, polkit спросит пароль;
///  • распакованный tar.gz сам не обновляется - ведём на страницу релиза.
/// </summary>
[SupportedOSPlatform("linux")]
public sealed class LinuxUpdater(IAppLifetime lifetime) : IAppUpdater
{
    /// <summary>Путь к файлу AppImage - его выставляет сама среда AppImage.</summary>
    private static string? AppImage =>
        Environment.GetEnvironmentVariable("APPIMAGE") is { Length: > 0 } path && File.Exists(path) ? path : null;

    /// <summary>Установлено пакетом: файлы в /opt/cvpn и dpkg о нём знает.</summary>
    private static bool IsDeb =>
        AppContext.BaseDirectory.StartsWith("/opt/cvpn/", StringComparison.Ordinal)
        && File.Exists("/var/lib/dpkg/info/cvpn.list");

    // Во временной папке: dpkg запускается от root, и ему не нужен доступ к домашней папке
    public string DownloadDirectory => Path.Combine(Path.GetTempPath(), $"cvpn-update-{Environment.UserName}");

    public ReleaseAsset? PickAsset(ReleaseInfo release)
    {
        if (AppImage is not null) return release.FindAsset("-x86_64.AppImage");
        if (IsDeb) return release.FindAsset("_amd64.deb");

        return null;
    }

    public async Task<string> InstallAsync(string file)
    {
        if (AppImage is { } image) return ReplaceAppImage(image, file);
        if (IsDeb) return await InstallDebAsync(file);

        return Loc.T("Update_InstallFailed", "unsupported installation");
    }

    private string ReplaceAppImage(string image, string file)
    {
        var fresh = image + ".new";

        try
        {
            File.Copy(file, fresh, overwrite: true);
            File.SetUnixFileMode(fresh,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                UnixFileMode.OtherRead | UnixFileMode.OtherExecute);

            // Переименование атомарно: файл либо старый, либо новый, но не половина
            File.Move(fresh, image, overwrite: true);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            TryDelete(fresh);
            return Loc.T("Update_NotWritable", Path.GetDirectoryName(image) ?? image);
        }

        try
        {
            Process.Start(new ProcessStartInfo(image) { UseShellExecute = false });
        }
        catch (Exception ex)
        {
            return Loc.T("Update_InstallFailed", ex.Message);
        }

        lifetime.Shutdown();
        return "";
    }

    private async Task<string> InstallDebAsync(string file)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("pkexec")
            {
                ArgumentList = { "dpkg", "-i", file },
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            });

            if (process is null) return Loc.T("Update_InstallFailed", "pkexec");

            var stderr = process.StandardError.ReadToEndAsync();
            await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();

            switch (process.ExitCode)
            {
                case 0:
                    break;

                // 126 - окно пароля закрыли, 127 - polkit не дал прав
                case 126 or 127:
                    return Loc.T("Update_Declined");

                default:
                    var reason = (await stderr).Trim();
                    return Loc.T("Update_PackageFailed", reason.Length > 0 ? reason : $"code {process.ExitCode}", file);
            }
        }
        catch (Exception ex)
        {
            return Loc.T("Update_InstallFailed", ex.Message);
        }

        // Пакет стоит - перезапускаемся уже новой версией
        if (!lifetime.Restart()) return Loc.T("Update_RestartManually");

        return "";
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception)
        {
            // Нечего удалять
        }
    }
}
