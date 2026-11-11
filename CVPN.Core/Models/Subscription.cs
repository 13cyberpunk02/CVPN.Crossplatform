using System.Text;
using System.Text.Json.Serialization;
using CVPN.Core;
using CVPN.Localization;

namespace CVPN.Models;

/// <summary>
/// Подписка провайдера: ссылка, по которой приходит список серверов, и то, что
/// панель сообщает о тарифе - сколько трафика потрачено, сколько всего и до
/// какого числа подписка действует (заголовок subscription-userinfo).
/// </summary>
public sealed class Subscription : ObservableObject
{
    /// <summary>За сколько дней до конца предупреждать.</summary>
    public const int WarnDays = 3;

    /// <summary>С какой доли израсходованного трафика предупреждать.</summary>
    public const double WarnShare = 0.9;

    /// <summary>
    /// Ссылка. Шифруется вместе с остальными секретами: панели выдают её
    /// с токеном доступа прямо в адресе.
    /// </summary>
    [JsonConverter(typeof(ProtectedStringConverter))]
    public string Url
    {
        get;
        set
        {
            if (Set(ref field, value)) Raise(nameof(Title));
        }
    } = string.Empty;

    /// <summary>Название от панели (заголовок profile-title). Пусто - показываем адрес.</summary>
    public string Name
    {
        get;
        set
        {
            if (Set(ref field, value ?? "")) Raise(nameof(Title));
        }
    } = string.Empty;

    public DateTimeOffset? Updated
    {
        get;
        set
        {
            if (Set(ref field, value)) Raise(nameof(UpdatedLabel));
        }
    }

    /// <summary>Байт отправлено и получено по данным панели. -1 - панель не сообщала.</summary>
    public long Upload { get; set; } = -1;

    public long Download { get; set; } = -1;

    /// <summary>Лимит трафика в байтах. 0 - без лимита, -1 - неизвестно.</summary>
    public long Total { get; set; } = -1;

    /// <summary>Когда подписка заканчивается. null - бессрочно или неизвестно.</summary>
    public DateTimeOffset? Expire { get; set; }

    // ===================== для экрана =====================

    [JsonIgnore]
    public string Title => Name.Length > 0
        ? Name
        : Uri.TryCreate(Url, UriKind.Absolute, out var uri) ? uri.Host : Url;

    [JsonIgnore]
    public string UpdatedLabel => Updated is { } at
        ? Loc.T("Profiles_SubscriptionUpdatedAt", at.ToLocalTime().ToString("g", Loc.Culture))
        : Loc.T("Profiles_SubscriptionNever");

    [JsonIgnore]
    public long Used => Math.Max(0, Upload) + Math.Max(0, Download);

    /// <summary>Панель сообщила лимит - есть что рисовать полосой.</summary>
    [JsonIgnore]
    public bool HasQuota => Total > 0;

    /// <summary>Панель сообщила трафик без лимита.</summary>
    [JsonIgnore]
    public bool IsUnlimited => Total == 0 && (Upload >= 0 || Download >= 0);

    /// <summary>Доля израсходованного, 0..1.</summary>
    [JsonIgnore]
    public double UsedShare => HasQuota ? Math.Clamp((double)Used / Total, 0, 1) : 0;

    [JsonIgnore]
    public string QuotaLabel => HasQuota
        ? Loc.T("Sub_Quota", ByteFormat.Size(Used), ByteFormat.Size(Total))
        : IsUnlimited
            ? Loc.T("Sub_QuotaUnlimited", ByteFormat.Size(Used))
            : "";

    [JsonIgnore]
    public string QuotaLeftLabel => HasQuota ? Loc.T("Sub_QuotaLeft", ByteFormat.Size(Math.Max(0, Total - Used))) : "";

    [JsonIgnore]
    public bool HasTraffic => HasQuota || IsUnlimited;

    [JsonIgnore]
    public bool HasExpire => Expire is not null;

    /// <summary>Панель сообщила хоть что-то о тарифе - есть что показать под заголовком.</summary>
    [JsonIgnore]
    public bool HasTariff => HasTraffic || HasExpire;

    /// <summary>Полных дней до конца; отрицательно - уже закончилась.</summary>
    [JsonIgnore]
    public int DaysLeft => Expire is { } end ? (int)Math.Floor((end - DateTimeOffset.Now).TotalDays) : int.MaxValue;

    [JsonIgnore]
    public bool IsExpired => Expire is { } end && end <= DateTimeOffset.Now;

    [JsonIgnore]
    public bool IsExpiringSoon => !IsExpired && DaysLeft < WarnDays;

    [JsonIgnore]
    public bool IsQuotaLow => HasQuota && UsedShare >= WarnShare;

    /// <summary>Что-то требует внимания: срок на исходе или трафик почти весь.</summary>
    [JsonIgnore]
    public bool NeedsAttention => IsExpired || IsExpiringSoon || IsQuotaLow;

    [JsonIgnore]
    public string ExpireLabel
    {
        get
        {
            if (Expire is not { } end) return "";

            var date = end.ToLocalTime().ToString("d MMMM yyyy", Loc.Culture);

            if (IsExpired) return Loc.T("Sub_Expired", date);

            return DaysLeft switch
            {
                0 => Loc.T("Sub_ExpiresToday", date),
                _ => Loc.T("Sub_ExpiresIn", date, DaysLeft)
            };
        }
    }

    /// <summary>Свежие данные из ответа панели.</summary>
    public void Apply(SubscriptionInfo info)
    {
        if (info.Title.Length > 0) Name = info.Title;

        Upload = info.Upload;
        Download = info.Download;
        Total = info.Total;
        Expire = info.Expire;
        Updated = DateTimeOffset.Now;

        RaiseAll();
    }

    public void RaiseAll()
    {
        foreach (var name in (string[])
                 [
                     nameof(Used), nameof(HasQuota), nameof(IsUnlimited), nameof(UsedShare), nameof(QuotaLabel),
                     nameof(QuotaLeftLabel), nameof(HasTraffic), nameof(HasExpire), nameof(HasTariff), nameof(DaysLeft), nameof(IsExpired),
                     nameof(IsExpiringSoon), nameof(IsQuotaLow), nameof(NeedsAttention), nameof(ExpireLabel),
                     nameof(Title), nameof(UpdatedLabel)
                 ])
            Raise(name);
    }
}

/// <summary>Что панель рассказала о подписке в заголовках ответа.</summary>
public sealed record SubscriptionInfo(string Title, long Upload, long Download, long Total, DateTimeOffset? Expire)
{
    public static SubscriptionInfo Empty { get; } = new("", -1, -1, -1, null);

    /// <summary>
    /// «upload=123; download=456; total=789; expire=1735689600» - формат,
    /// который отдают 3x-ui, Marzban, Remnawave и другие панели. Нулевой
    /// expire - бессрочно, нулевой total - без лимита.
    /// </summary>
    public static SubscriptionInfo Parse(string? userInfo, string? title)
    {
        long upload = -1, download = -1, total = -1;
        DateTimeOffset? expire = null;

        foreach (var part in (userInfo ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var pair = part.Split('=', 2, StringSplitOptions.TrimEntries);
            if (pair.Length != 2 || !double.TryParse(pair[1], System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var number))
                continue;

            var value = (long)number;

            switch (pair[0].ToLowerInvariant())
            {
                case "upload": upload = value; break;
                case "download": download = value; break;
                case "total": total = value; break;
                case "expire" when value > 0: expire = DateTimeOffset.FromUnixTimeSeconds(value); break;
            }
        }

        return new SubscriptionInfo(DecodeTitle(title), upload, download, total, expire);
    }

    /// <summary>profile-title бывает открытым текстом или «base64:...».</summary>
    internal static string DecodeTitle(string? title)
    {
        var text = (title ?? "").Trim();
        if (!text.StartsWith("base64:", StringComparison.OrdinalIgnoreCase)) return text;

        try
        {
            return Encoding.UTF8.GetString(Convert.FromBase64String(text[7..].Trim())).Trim();
        }
        catch (FormatException)
        {
            return "";
        }
    }
}
