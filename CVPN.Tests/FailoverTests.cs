using CVPN.Models;
using CVPN.Services;

namespace CVPN.Tests;

public class FailoverTests
{
    private static ServerProfile P(string name) => new() { Name = name, Host = name, Port = 443 };

    [Fact]
    public void Выбирается_самый_быстрый_из_ответивших()
    {
        var current = P("de");
        var nl = P("nl");
        var fi = P("fi");

        var next = Failover.PickReplacement(current, [(current, 20), (nl, 180), (fi, 90)]);

        Assert.Same(fi, next);
    }

    [Fact]
    public void Текущий_сервер_не_выбирается_даже_если_быстрее()
    {
        var current = P("de");
        var nl = P("nl");

        Assert.Same(nl, Failover.PickReplacement(current, [(current, 5), (nl, 300)]));
    }

    /// <summary>Не ответил никто - переключаться некуда: скорее всего, пропал интернет.</summary>
    [Fact]
    public void Если_никто_не_ответил_переключения_нет()
    {
        Assert.Null(Failover.PickReplacement(P("de"), [(P("nl"), -1), (P("fi"), -1)]));
        Assert.Null(Failover.PickReplacement(P("de"), []));
    }

    [Fact]
    public void Пауза_перед_перезапуском_растёт()
    {
        Assert.Equal(TimeSpan.FromSeconds(3), Failover.RestartDelay(1));
        Assert.Equal(TimeSpan.FromSeconds(9), Failover.RestartDelay(3));
        Assert.True(Failover.RestartDelay(0) > TimeSpan.Zero);
    }
}
