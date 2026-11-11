using System.Net.Http;
using System.Security.Cryptography;
using CVPN.Localization;

namespace CVPN.Services;

/// <summary>Обновление не скачалось или не прошло проверку. Текст - для пользователя.</summary>
public sealed class UpdateException(string message) : Exception(message);

/// <summary>
/// Скачивание файла обновления с проверкой SHA-256. Без совпавшей суммы файл
/// не ставится никогда: подменённый установщик получил бы права администратора.
/// </summary>
public static class UpdateDownloader
{
    /// <summary>Список сумм, который выкладывает сборка релиза: «хеш  имя» на строку.</summary>
    public const string ChecksumsName = "SHA256SUMS.txt";

    // Таймаут не общий, а на чтение: установщик весит десятки мегабайт, медленный канал - не ошибка
    private static readonly HttpClient Http = new() { Timeout = Timeout.InfiniteTimeSpan };

    private static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Скачивает файл в directory и возвращает путь к нему. Прогресс - байты
    /// скачанного и всего. Ошибки - UpdateException с понятным текстом.
    /// </summary>
    public static async Task<string> DownloadAsync(
        ReleaseInfo release,
        ReleaseAsset asset,
        string directory,
        IProgress<(long Done, long Total)>? progress,
        CancellationToken ct)
    {
        var expected = asset.Sha256 ?? await ChecksumFromListAsync(release, asset.Name, ct);
        if (string.IsNullOrEmpty(expected)) throw new UpdateException(Loc.T("Update_NoChecksum"));

        Directory.CreateDirectory(directory);
        CleanOld(directory, asset.Name);

        var target = Path.Combine(directory, asset.Name);
        var partial = target + ".part";

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, asset.Url);
            request.Headers.TryAddWithoutValidation("User-Agent", "CVPN");

            using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode)
                throw new UpdateException(Loc.T("Update_DownloadFailed", $"HTTP {(int)response.StatusCode}"));

            var total = response.Content.Headers.ContentLength ?? asset.Size;

            await using var source = await response.Content.ReadAsStreamAsync(ct);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

            await using (var file = File.Create(partial))
            {
                var buffer = new byte[81920];
                long done = 0;
                var reported = -1L;

                while (true)
                {
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    timeout.CancelAfter(ReadTimeout);

                    int read;
                    try
                    {
                        read = await source.ReadAsync(buffer, timeout.Token);
                    }
                    catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                    {
                        throw new UpdateException(Loc.T("Update_DownloadFailed", Loc.T("Update_Stalled")));
                    }

                    if (read == 0) break;

                    await file.WriteAsync(buffer.AsMemory(0, read), ct);
                    hash.AppendData(buffer, 0, read);
                    done += read;

                    // Не чаще раза на процент: иначе интерфейс захлебнётся событиями
                    var step = total > 0 ? done * 100 / total : done / (512 * 1024);
                    if (step == reported) continue;

                    reported = step;
                    progress?.Report((done, total));
                }
            }

            var actual = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
            if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
                throw new UpdateException(Loc.T("Update_ChecksumMismatch"));

            File.Move(partial, target, overwrite: true);
            return target;
        }
        catch (UpdateException)
        {
            TryDelete(partial);
            throw;
        }
        catch (OperationCanceledException)
        {
            TryDelete(partial);
            throw;
        }
        catch (Exception ex)
        {
            TryDelete(partial);
            throw new UpdateException(Loc.T("Update_DownloadFailed", ex.Message));
        }
    }

    /// <summary>Сумма из SHA256SUMS.txt релиза - для файлов, у которых GitHub её не посчитал.</summary>
    private static async Task<string?> ChecksumFromListAsync(ReleaseInfo release, string name, CancellationToken ct)
    {
        var list = release.Assets.FirstOrDefault(a => a.Name.Equals(ChecksumsName, StringComparison.OrdinalIgnoreCase));
        if (list is null) return null;

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, list.Url);
            request.Headers.TryAddWithoutValidation("User-Agent", "CVPN");

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(ReadTimeout);

            using var response = await Http.SendAsync(request, cts.Token);
            if (!response.IsSuccessStatusCode) return null;

            return FindChecksum(await response.Content.ReadAsStringAsync(cts.Token), name);
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            return null;
        }
    }

    /// <summary>Строка вида «хеш  имя» или «хеш *имя» (формат sha256sum).</summary>
    internal static string? FindChecksum(string list, string name)
    {
        foreach (var raw in list.Split('\n'))
        {
            var line = raw.Trim();
            var space = line.IndexOf(' ');
            if (space <= 0) continue;

            var hash = line[..space];
            var file = line[space..].Trim().TrimStart('*');

            if (hash.Length == 64 && file.Equals(name, StringComparison.OrdinalIgnoreCase))
                return hash.ToLowerInvariant();
        }

        return null;
    }

    /// <summary>Прошлые скачанные версии больше не нужны - место на телефоне не бесконечное.</summary>
    private static void CleanOld(string directory, string keep)
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(directory))
            {
                if (!Path.GetFileName(file).Equals(keep, StringComparison.OrdinalIgnoreCase)) TryDelete(file);
            }
        }
        catch (Exception)
        {
            // Не убрали старое - не беда, обновлению это не мешает
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception)
        {
            // Файл занят или уже удалён
        }
    }
}
