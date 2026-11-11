using System.Diagnostics;
using System.Runtime.Versioning;
using CVPN.Localization;
using CVPN.Platform;
using CVPN.Services;

namespace CVPN.Desktop.Platform.Windows;

/// <summary>
/// Самообновление на Windows. Пока приложение запущено, его файлы заняты,
/// поэтому ставит обновление маленький сценарий PowerShell: ждёт выхода
/// приложения, запускает установщик (или распаковывает архив портативной
/// версии) и открывает CVPN заново.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsUpdater(IAppLifetime lifetime) : IAppUpdater
{
    private static string AppDir => AppContext.BaseDirectory;

    /// <summary>Установлено установщиком: рядом лежит деинсталлятор Inno Setup.</summary>
    private static bool IsInstalled => File.Exists(Path.Combine(AppDir, "unins000.exe"));

    public string DownloadDirectory => Path.Combine(Path.GetTempPath(), "CVPN-update");

    public ReleaseAsset? PickAsset(ReleaseInfo release) =>
        IsInstalled ? release.FindAsset("-setup.exe") : release.FindAsset("-portable.zip");

    public Task<string> InstallAsync(string file)
    {
        var exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe)) return Task.FromResult(Loc.T("Update_InstallFailed", "process path is unknown"));

        try
        {
            var script = Path.Combine(DownloadDirectory, "update.ps1");
            File.WriteAllText(script, IsInstalled ? InstallerScript : PortableScript);

            var info = new ProcessStartInfo("powershell.exe")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };

            foreach (var arg in (string[])
                     [
                         "-NoProfile", "-ExecutionPolicy", "Bypass", "-WindowStyle", "Hidden",
                         "-File", script,
                         "-ProcessId", Environment.ProcessId.ToString(),
                         "-Package", file,
                         "-AppDir", AppDir.TrimEnd('\\'),
                         "-Exe", exe
                     ])
                info.ArgumentList.Add(arg);

            Process.Start(info);
        }
        catch (Exception ex)
        {
            return Task.FromResult(Loc.T("Update_InstallFailed", ex.Message));
        }

        // Выходим штатно: App вернёт прокси и снимет kill switch, сценарий дождётся
        lifetime.Shutdown();
        return Task.FromResult("");
    }

    /// <summary>
    /// Установщик в тихом режиме с окном прогресса. Выбор компонентов и задач
    /// Inno Setup берёт из прошлой установки. Права администратора - через UAC;
    /// отказались - просто запускаем прежнюю версию.
    /// </summary>
    private const string InstallerScript = """
        param([int]$ProcessId, [string]$Package, [string]$AppDir, [string]$Exe)
        Start-Transcript -Path (Join-Path $PSScriptRoot 'update.log') -Force | Out-Null
        try { Wait-Process -Id $ProcessId -Timeout 60 -ErrorAction SilentlyContinue } catch {}
        try {
            $p = Start-Process -FilePath $Package -ArgumentList '/SILENT','/SUPPRESSMSGBOXES','/NORESTART','/SP-' -Verb RunAs -Wait -PassThru
            "installer exit code: $($p.ExitCode)"
        } catch {
            "installer was not started: $_"
        }
        Start-Process -FilePath $Exe
        Stop-Transcript | Out-Null
        """;

    /// <summary>
    /// Портативная версия: архив распаковывается поверх папки приложения.
    /// Профили лежат в %AppData%, их это не касается.
    /// </summary>
    private const string PortableScript = """
        param([int]$ProcessId, [string]$Package, [string]$AppDir, [string]$Exe)
        Start-Transcript -Path (Join-Path $PSScriptRoot 'update.log') -Force | Out-Null
        try { Wait-Process -Id $ProcessId -Timeout 60 -ErrorAction SilentlyContinue } catch {}
        Start-Sleep -Seconds 1
        $tmp = Join-Path $env:TEMP ('cvpn-portable-' + [guid]::NewGuid())
        try {
            Expand-Archive -Path $Package -DestinationPath $tmp -Force
            Copy-Item -Path (Join-Path $tmp '*') -Destination $AppDir -Recurse -Force
            "portable files updated"
        } catch {
            "portable update failed: $_"
        } finally {
            Remove-Item -Path $tmp -Recurse -Force -ErrorAction SilentlyContinue
        }
        Start-Process -FilePath $Exe
        Stop-Transcript | Out-Null
        """;
}
