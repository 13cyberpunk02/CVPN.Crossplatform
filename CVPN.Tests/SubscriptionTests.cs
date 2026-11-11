using CVPN.Models;
using CVPN.Services;

namespace CVPN.Tests;

[Collection("Localization")]
public class SubscriptionTests
{
    private const long Gb = 1024L * 1024 * 1024;

    [Fact]
    public void Заголовок_тарифа_разбирается()
    {
        var info = SubscriptionInfo.Parse("upload=1073741824; download=2147483648; total=10737418240; expire=1767225600", null);

        Assert.Equal(Gb, info.Upload);
        Assert.Equal(2 * Gb, info.Download);
        Assert.Equal(10 * Gb, info.Total);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1767225600), info.Expire);
    }

    /// <summary>Нулевой срок - бессрочно, нулевой лимит - без лимита.</summary>
    [Fact]
    public void Нули_значат_без_срока_и_без_лимита()
    {
        var info = SubscriptionInfo.Parse("upload=0; download=5; total=0; expire=0", null);
        var sub = new Subscription();
        sub.Apply(info);

        Assert.Null(info.Expire);
        Assert.True(sub.IsUnlimited);
        Assert.False(sub.HasQuota);
        Assert.False(sub.HasExpire);
    }

    [Fact]
    public void Без_заголовка_ничего_не_известно()
    {
        var info = SubscriptionInfo.Parse(null, null);

        Assert.Equal(SubscriptionInfo.Empty, info);
    }

    [Fact]
    public void Мусор_в_заголовке_не_ломает_разбор()
    {
        var info = SubscriptionInfo.Parse("upload=abc; =5; download=7;; total", null);

        Assert.Equal(-1, info.Upload);
        Assert.Equal(7, info.Download);
        Assert.Equal(-1, info.Total);
    }

    [Theory]
    [InlineData("Example VPN", "Example VPN")]
    [InlineData("base64:0J/RgNC10LzQuNGD0Lw=", "Премиум")]
    [InlineData("base64:%%%", "")]
    [InlineData(null, "")]
    public void Название_подписки_бывает_в_base64(string? raw, string expected)
    {
        Assert.Equal(expected, SubscriptionInfo.DecodeTitle(raw));
    }

    [Fact]
    public void Трафик_на_исходе_требует_внимания()
    {
        var sub = new Subscription();
        sub.Apply(new SubscriptionInfo("", 0, 95 * Gb, 100 * Gb, null));

        Assert.True(sub.IsQuotaLow);
        Assert.True(sub.NeedsAttention);
        Assert.Equal(0.95, sub.UsedShare, 3);
    }

    [Fact]
    public void Срок_на_исходе_и_истёкший()
    {
        var soon = new Subscription();
        soon.Apply(new SubscriptionInfo("", 0, 0, 100 * Gb, DateTimeOffset.Now.AddDays(1.5)));
        var gone = new Subscription();
        gone.Apply(new SubscriptionInfo("", 0, 0, 100 * Gb, DateTimeOffset.Now.AddDays(-1)));
        var fine = new Subscription();
        fine.Apply(new SubscriptionInfo("", 0, 0, 100 * Gb, DateTimeOffset.Now.AddDays(30)));

        Assert.True(soon.IsExpiringSoon);
        Assert.True(gone.IsExpired);
        Assert.False(gone.IsExpiringSoon);
        Assert.False(fine.NeedsAttention);
    }

    [Fact]
    public void Название_берётся_из_панели_иначе_адрес()
    {
        var sub = new Subscription { Url = "https://panel.example.net/sub/abc" };
        Assert.Equal("panel.example.net", sub.Title);

        sub.Apply(new SubscriptionInfo("Мой VPN", -1, -1, -1, null));
        Assert.Equal("Мой VPN", sub.Title);
    }

    /// <summary>Старая единственная подписка становится первой в списке - один раз.</summary>
    [Fact]
    public void Старая_подписка_переносится_в_список()
    {
        var updated = DateTimeOffset.Now.AddHours(-3);
        var state = new StoredState();
        state.Settings.SubscriptionUrl = "https://old.example/sub";
        state.Settings.SubscriptionUpdated = updated;

        state.Migrate();
        state.Migrate();

        var sub = Assert.Single(state.Settings.Subscriptions);
        Assert.Equal("https://old.example/sub", sub.Url);
        Assert.Equal(updated, sub.Updated);
        Assert.Equal("", state.Settings.SubscriptionUrl);
    }

    [Theory]
    [InlineData("sing")]
    [InlineData("box")]
    [InlineData("clash")]
    [InlineData("hiddify")]
    [InlineData("v2ray")]
    [InlineData("SFA")]
    public void Панель_не_принимает_приложение_за_другой_клиент(string client)
    {
        // Иначе панель отдаст конфиг этого клиента вместо списка ссылок
        Assert.DoesNotContain(client, SubscriptionService.UserAgent, StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith("CVPN/", SubscriptionService.UserAgent);
    }
}
