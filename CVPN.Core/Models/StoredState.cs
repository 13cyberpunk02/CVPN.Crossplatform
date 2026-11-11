using CVPN.Services;

namespace CVPN.Models;

public sealed class StoredState
{
    public List<ServerProfile> Profiles { get; set; } = [];
    public AppSettings Settings { get; set; } = new();
    public string? ActiveProfileName { get; set; }

    public List<RoutingProfile> RoutingProfiles { get; set; } = [];
    public string? ActiveRoutingProfile { get; set; }

    /// <summary>
    /// Старый формат: правила лежали одним списком. Читается ради совместимости
    /// и при первой же загрузке превращается в набор «Основной».
    /// </summary>
    public List<RouteRule>? Rules { get; set; }

    public void Migrate()
    {
        if (RoutingProfiles.Count == 0)
        {
            RoutingProfiles.Add(new RoutingProfile
            {
                Name = Localization.Loc.T("Routing_DefaultSet"),
                ProxyByDefault = Settings.ProxyByDefault,
                Rules = [.. Rules ?? []]
            });
        }

        Rules = null;
        ActiveRoutingProfile ??= RoutingProfiles[0].Name;

        MigrateSubscription();
    }

    /// <summary>
    /// Была одна ссылка подписки в настройках - становится первой в списке.
    /// Старые поля очищаются, чтобы перенос не повторялся.
    /// </summary>
    private void MigrateSubscription()
    {
        var url = Settings.SubscriptionUrl.Trim();
        if (url.Length == 0) return;

        if (Settings.Subscriptions.All(s => s.Url != url))
            Settings.Subscriptions.Add(new Subscription { Url = url, Updated = Settings.SubscriptionUpdated });

        Settings.SubscriptionUrl = "";
        Settings.SubscriptionUpdated = null;
    }
}