using System.Net.Http;
using System.Reflection;
using System.Text.Json;

namespace CVPN.Services;

/// <summary>Файл релиза. Sha256 - контрольная сумма из GitHub (поле digest), если она есть.</summary>
public sealed record ReleaseAsset(string Name, string Url, long Size, string? Sha256);

public sealed record ReleaseInfo(string Version, string Url, string Notes, IReadOnlyList<ReleaseAsset> Assets)
{
    /// <summary>Файл релиза по окончанию имени: «-setup.exe», «.AppImage», «.apk».</summary>
    public ReleaseAsset? FindAsset(string suffix) =>
        Assets.FirstOrDefault(a => a.Name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// Проверка обновлений через GitHub Releases: есть ли версия новее и какие
/// у неё файлы. Скачивает и ставит их UpdateDownloader и IAppUpdater платформы.
/// </summary>
public static class UpdateChecker
{
    /// <summary>Репозиторий кроссплатформенной версии. Прежний WPF-клиент живёт в 13CyberPunk02/CVPN, его релизы сюда не относятся.</summary>
    private const string Repository = "13cyberpunk02/CVPN.Crossplatform";

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    public static Version CurrentVersion =>
        Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);

    /// <summary>
    /// Сравнивает версию из тега с текущей. Вынесено отдельно от запроса,
    /// чтобы разбор тегов проверялся тестами: форматы у разных проектов разные.
    /// </summary>
    public static bool IsNewer(string? tag, Version current)
    {
        var parsed = ParseTag(tag);

        return parsed is not null && parsed > current;
    }

    /// <summary>Из «v1.2.3» получаем 1.2.3. Суффиксы вроде «-beta» отбрасываются.</summary>
    public static Version? ParseTag(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag)) return null;

        var text = tag.Trim().TrimStart('v', 'V');

        var dash = text.IndexOfAny(['-', '+']);
        if (dash > 0) text = text[..dash];

        // Version требует минимум две части: «2» не разберётся, «2.0» - да
        if (!text.Contains('.')) text += ".0";

        return Version.TryParse(text, out var version) ? version : null;
    }

    /// <summary>Возвращает сведения о релизе, если он новее текущей сборки.</summary>
    public static async Task<ReleaseInfo?> CheckAsync(CancellationToken ct = default)
    {
        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Get, $"https://api.github.com/repos/{Repository}/releases/latest");

            // GitHub API отклоняет запросы без User-Agent
            request.Headers.TryAddWithoutValidation("User-Agent", "CVPN");
            request.Headers.TryAddWithoutValidation("Accept", "application/vnd.github+json");

            using var response = await Http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode) return null;

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            var root = doc.RootElement;

            // Черновики и предрелизы не предлагаем
            if (root.TryGetProperty("draft", out var draft) && draft.GetBoolean()) return null;
            if (root.TryGetProperty("prerelease", out var pre) && pre.GetBoolean()) return null;

            var tag = Text(root, "tag_name");
            if (!IsNewer(tag, CurrentVersion)) return null;

            return new ReleaseInfo(
                tag.TrimStart('v', 'V'),
                Text(root, "html_url"),
                Text(root, "body"),
                ParseAssets(root));
        }
        catch (Exception)
        {
            // Нет сети, лимит запросов, репозиторий приватный - молча пропускаем
            return null;
        }
    }

    /// <summary>
    /// Файлы релиза. digest («sha256:...») GitHub считает сам при загрузке файла -
    /// по нему скачанное сверяется до установки.
    /// </summary>
    internal static IReadOnlyList<ReleaseAsset> ParseAssets(JsonElement release)
    {
        if (!release.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array) return [];

        var list = new List<ReleaseAsset>();

        foreach (var asset in assets.EnumerateArray())
        {
            var name = Text(asset, "name");
            var url = Text(asset, "browser_download_url");
            if (name.Length == 0 || url.Length == 0) continue;

            var size = asset.TryGetProperty("size", out var s) && s.TryGetInt64(out var bytes) ? bytes : 0;
            var digest = Text(asset, "digest");
            var sha = digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) ? digest[7..].ToLowerInvariant() : null;

            list.Add(new ReleaseAsset(name, url, size, sha));
        }

        return list;
    }

    private static string Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";
}