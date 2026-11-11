using System.Net.Http;
using System.Text;
using CVPN.Localization;
using CVPN.Models;

namespace CVPN.Services;

/// <summary>
/// Загрузка списка серверов по ссылке подписки. Формат почти везде одинаковый:
/// список ссылок построчно, целиком закодированный в base64. Встречается и без
/// кодирования, поэтому проверяются оба варианта.
/// </summary>
public static class SubscriptionService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };

    /// <summary>
    /// Как приложение представляется панели. Панели выбирают формат ответа по
    /// User-Agent: увидев «sing-box», «clash» или «hiddify», они отдают готовый
    /// конфиг этого клиента вместо списка ссылок, а разбирается только список.
    /// Поэтому имена других клиентов здесь упоминать нельзя.
    /// </summary>
    public static string UserAgent => $"CVPN/{UpdateChecker.CurrentVersion.ToString(3)}";

    public static async Task<(List<ServerProfile> Profiles, string Error, SubscriptionInfo Info)> FetchAsync(
        string url, CancellationToken ct = default)
    {
        var profiles = new List<ServerProfile>();

        if (string.IsNullOrWhiteSpace(url)) return (profiles, Loc.T("Sub_NoUrl"), SubscriptionInfo.Empty);

        string body;
        SubscriptionInfo info;

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);

            using var response = await Http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
                return (profiles, Loc.T("Sub_HttpError", (int)response.StatusCode), SubscriptionInfo.Empty);

            // Тариф панели: потрачено, всего, до какого числа, и название подписки
            info = SubscriptionInfo.Parse(Header(response, "subscription-userinfo"), Header(response, "profile-title"));

            body = await response.Content.ReadAsStringAsync(ct);
        }
        catch (Exception ex)
        {
            return (profiles, Loc.T("Sub_LoadFailed", ex.Message), SubscriptionInfo.Empty);
        }

        foreach (var line in Decode(body)
                     .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (LinkParser.TryParse(line, out var profile, out _))
            {
                profile.Subscription = url;
                profiles.Add(profile);
            }
        }

        return profiles.Count > 0
            ? (profiles, "", info)
            : (profiles, Loc.T("Sub_NoServers"), info);
    }

    /// <summary>Заголовок ответа или содержимого - панели кладут его и туда, и туда.</summary>
    private static string? Header(HttpResponseMessage response, string name)
    {
        if (response.Headers.TryGetValues(name, out var values)) return string.Join(";", values);
        if (response.Content.Headers.TryGetValues(name, out var content)) return string.Join(";", content);
        return null;
    }

    /// <summary>Если тело - base64, разворачиваем; иначе возвращаем как есть.</summary>
    private static string Decode(string body)
    {
        var trimmed = body.Trim();

        // Ссылки в открытом виде начинаются со схемы - декодировать нечего
        if (trimmed.Contains("://", StringComparison.Ordinal)) return trimmed;

        try
        {
            var padded = trimmed.Replace('-', '+').Replace('_', '/');
            padded = padded.PadRight(padded.Length + (4 - padded.Length % 4) % 4, '=');

            return Encoding.UTF8.GetString(Convert.FromBase64String(padded));
        }
        catch (FormatException)
        {
            return trimmed;
        }
    }
}