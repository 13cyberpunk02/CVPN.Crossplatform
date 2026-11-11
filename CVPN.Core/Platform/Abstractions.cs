using CVPN.Models;

namespace CVPN.Platform;

// Всё, чем ViewModels касаются окружения. Реализации живут в головах приложения
// (CVPN.Desktop, CVPN.Android) и в общем UI-проекте, сюда попадают только контракты.

// ===================== интерфейс =====================

/// <summary>Поток UI. Вывод ядра и ответы сети приходят из фоновых потоков.</summary>
public interface IUiDispatcher
{
    bool CheckAccess();

    /// <summary>Поставить действие в очередь UI. Не ждёт выполнения.</summary>
    void Post(Action action);

    /// <summary>Таймер, тикающий на потоке UI.</summary>
    IUiTimer CreateTimer(TimeSpan interval, Action tick);
}

public interface IUiTimer
{
    void Start();
    void Stop();
}

/// <summary>Диалоги. На десктопе - окна, на телефоне - страницы поверх основной.</summary>
public interface IDialogService
{
    /// <summary>Вопрос «да / нет».</summary>
    Task<bool> ConfirmAsync(string message);

    /// <summary>Редактор профиля. null - пользователь отменил.</summary>
    Task<ServerProfile?> EditProfileAsync(ServerProfile? existing);

    /// <summary>Экспорт: ссылка и QR-код.</summary>
    Task ShowExportAsync(string payload, string title, string subtitle);
}

/// <summary>Фильтр выбора файлов: «JSON» → ["*.json"].</summary>
public sealed record FileFilter(string Name, IReadOnlyList<string> Patterns)
{
    /// <summary>
    /// Разбирает фильтр в формате WPF «Имя|*.a;*.b|Имя2|*.*». В таком виде
    /// фильтры лежат в Strings.resx - переводы не пришлось переписывать.
    /// </summary>
    public static IReadOnlyList<FileFilter> ParseWpf(string filter)
    {
        var parts = filter.Split('|');
        var result = new List<FileFilter>();

        for (var i = 0; i + 1 < parts.Length; i += 2)
        {
            var patterns = parts[i + 1].Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            result.Add(new FileFilter(parts[i].Trim(), patterns));
        }

        return result;
    }
}

/// <summary>
/// Выбранный файл. На Android у него нет пути - только поток через content://,
/// поэтому LocalPath может быть null, а читать надо через OpenReadAsync.
/// </summary>
public sealed record PickedFile(string Name, string? LocalPath, Func<Task<Stream>> OpenReadAsync)
{
    public async Task<string> ReadAllTextAsync()
    {
        await using var stream = await OpenReadAsync();
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync();
    }
}

public interface IFilePicker
{
    /// <summary>Выбор одного файла. null - пользователь отменил.</summary>
    Task<PickedFile?> OpenAsync(string title, IReadOnlyList<FileFilter> filters, string? startDirectory = null);
}

public interface IClipboardService
{
    Task SetTextAsync(string text);
    Task<string?> GetTextAsync();
}

/// <summary>Открыть файл, папку или ссылку внешней программой.</summary>
public interface ILauncher
{
    /// <summary>Есть ли смысл показывать кнопки «открыть файл / папку» (на Android - нет).</summary>
    bool CanOpenFiles { get; }

    Task<bool> OpenFileAsync(string path);
    Task<bool> OpenFolderAsync(string path);
    Task<bool> OpenUrlAsync(string url);
}

/// <summary>
/// Сканер QR-кода камерой - для ссылок на серверы, которые показывают картинкой
/// (на сайте провайдера, на экране другого устройства). Есть только на телефоне.
/// </summary>
public interface IQrScanner
{
    bool IsSupported { get; }

    /// <summary>
    /// Текст из QR-кода. null - пользователь закрыл сканер. Ошибка (нет сервисов
    /// Google, нет камеры) - исключение с понятным текстом.
    /// </summary>
    Task<string?> ScanAsync();
}

/// <summary>
/// Самообновление: какой файл релиза нужен именно этой установке и как его
/// поставить. Установщик для Windows, AppImage или .deb для Linux, APK для Android.
/// </summary>
public interface IAppUpdater
{
    /// <summary>
    /// Файл релиза для этой установки. null - сама обновиться не может
    /// (например, распакованный tar.gz): тогда ведём на страницу релиза.
    /// </summary>
    CVPN.Services.ReleaseAsset? PickAsset(CVPN.Services.ReleaseInfo release);

    /// <summary>Куда скачивать файлы обновления.</summary>
    string DownloadDirectory { get; }

    /// <summary>
    /// Ставит скачанный файл. Пустая строка - установка пошла, приложение
    /// закроется или перезапустится само. Иначе - текст ошибки для пользователя.
    /// </summary>
    Task<string> InstallAsync(string file);
}

/// <summary>Жизненный цикл приложения.</summary>
public interface IAppLifetime
{
    void Shutdown();

    /// <summary>Перезапуск процесса. false - платформа не умеет или не вышло.</summary>
    bool Restart();
}

// ===================== система =====================

/// <summary>Системный прокси - режим без TUN.</summary>
public interface ISystemProxy
{
    bool IsSupported { get; }
    void Enable(int port);

    /// <summary>Вернуть то, что было до нас. Вызов без Enable ничего не делает.</summary>
    void Restore();
}

/// <summary>Блокировка трафика мимо туннеля.</summary>
public interface IKillSwitch
{
    bool IsSupported { get; }
    bool IsActive { get; }

    /// <summary>Пустая строка - успех, иначе текст проблемы.</summary>
    Task<string> EnableAsync(string corePath, string? byeDpiPath);

    Task<string> DisableAsync();

    /// <summary>Снять правила, оставшиеся после аварийного завершения.</summary>
    Task RestoreAfterCrashAsync();
}

public interface IAutoStart
{
    bool IsSupported { get; }
    void Apply(bool enabled);

    /// <summary>Привести систему в соответствие с настройкой - путь к программе мог смениться.</summary>
    void Sync(bool enabled);
}

/// <summary>Служба туннеля: TUN без запроса прав при каждом запуске.</summary>
public interface ITunnelServiceManager
{
    bool IsSupported { get; }

    /// <summary>
    /// Служба зарегистрирована в системе - независимо от того, запущена ли она.
    /// По этому признаку страница решает, предлагать установку или удаление.
    /// </summary>
    bool IsInstalled { get; }

    /// <summary>Готова ли служба принять команду прямо сейчас.</summary>
    Task<bool> IsAvailableAsync();

    /// <summary>Текст состояния для страницы настроек.</summary>
    Task<string> DescribeAsync();

    bool Install(out string error);
    bool Uninstall(out string error);
}

/// <summary>
/// Запуск с повышенными правами без окна UAC (задача планировщика Windows).
/// На других платформах не поддерживается.
/// </summary>
public interface IElevatedLauncher
{
    bool IsSupported { get; }
    bool Exists { get; }
    bool PathMatchesCurrent();
    string? RegisteredPath();
    bool Install(bool runAtLogon, out string error);
    bool Uninstall(out string error);
}

/// <summary>Чем закончилась попытка получить права на TUN.</summary>
public enum PrivilegeOutcome
{
    /// <summary>Права есть, можно запускать.</summary>
    Ready,

    /// <summary>Пользователь отказался.</summary>
    Cancelled,

    /// <summary>Приложение перезапускается с правами - текущий процесс сейчас завершится.</summary>
    Restarting,

    /// <summary>Получить права не удалось.</summary>
    Failed
}

/// <summary>Права на создание TUN: UAC на Windows, root/capability на Linux, VpnService на Android.</summary>
public interface ITunPrivileges
{
    Task<(PrivilegeOutcome Outcome, string Message)> EnsureAsync();
}
