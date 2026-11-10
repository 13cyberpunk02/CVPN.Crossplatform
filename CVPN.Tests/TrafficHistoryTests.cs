using CVPN.Models;
using CVPN.ViewModels;

namespace CVPN.Tests;

public class TrafficHistoryTests
{
    [Fact]
    public void Замеры_читаются_от_старого_к_новому()
    {
        var history = new TrafficHistory(4);

        history.Push(1, 10);
        history.Push(2, 20);

        Assert.Equal(2, history.Count);
        Assert.Equal(10, history.Down(0));
        Assert.Equal(20, history.Down(1));
        Assert.Equal(2, history.Up(1));
    }

    /// <summary>Буфер кольцевой: старые точки вытесняются, память не растёт.</summary>
    [Fact]
    public void Переполнение_вытесняет_самые_старые()
    {
        var history = new TrafficHistory(3);

        for (var i = 1; i <= 5; i++) history.Push(i, i * 10);

        Assert.Equal(3, history.Count);
        Assert.Equal(30, history.Down(0));
        Assert.Equal(50, history.Down(2));
        Assert.Equal(50, history.Max);
    }

    [Fact]
    public void Отрицательные_значения_считаются_нулём()
    {
        var history = new TrafficHistory(2);

        history.Push(-5, -1);

        Assert.Equal(0, history.Up(0));
        Assert.Equal(0, history.Max);
    }

    [Fact]
    public void Очистка_сбрасывает_точки_и_сообщает_графику()
    {
        var history = new TrafficHistory(3);
        var changes = 0;
        history.Changed += () => changes++;

        history.Push(1, 1);
        history.Clear();

        Assert.Equal(0, history.Count);
        Assert.Equal(2, changes);
        Assert.Throws<ArgumentOutOfRangeException>(() => history.Down(0));
    }

    /// <summary>Верх шкалы - круглое число, не меньше максимума и не меньше 64 КБ/с.</summary>
    [Theory]
    [InlineData(0, 64 * 1024)]
    [InlineData(10 * 1024, 64 * 1024)]
    [InlineData(70 * 1024, 80 * 1024)]
    [InlineData(100 * 1024, 100 * 1024)]
    [InlineData(1500 * 1024, 2 * 1024 * 1024)]
    [InlineData(5_300_000, 6 * 1024 * 1024)]
    [InlineData(90L * 1024 * 1024, 100L * 1024 * 1024)]
    public void Шкала_округляется_вверх_до_круглого(long max, long expected)
    {
        Assert.Equal(expected, TrafficHistory.NiceCeiling(max));
    }
}

public class ConnectionsFilterTests
{
    private static ConnectionInfo Make(string host, string process = "", string rule = "", string outbound = "proxy") => new()
    {
        Id = "1", Host = host, Port = 443, Network = "tcp", Outbound = outbound, Rule = rule,
        Process = process, Upload = 0, Download = 0, Started = DateTimeOffset.Now
    };

    [Theory]
    [InlineData("")]
    [InlineData("YOUTUBE")]
    [InlineData("chrome")]
    [InlineData("geosite")]
    [InlineData(":443")]
    public void Поиск_находит_по_домену_процессу_и_правилу(string query)
    {
        var connection = Make("www.youtube.com", "chrome.exe", "geosite-youtube");

        Assert.True(ConnectionsViewModel.Matches(connection, query));
    }

    [Fact]
    public void Поиск_отсеивает_лишнее()
    {
        Assert.False(ConnectionsViewModel.Matches(Make("github.com", "git.exe", "final"), "telegram"));
    }

    [Fact]
    public void Выход_определяет_вид_соединения()
    {
        Assert.True(Make("a", outbound: "proxy").IsProxy);
        Assert.True(Make("a", outbound: "auto").IsProxy);
        Assert.True(Make("a", outbound: "direct").IsDirect);
        Assert.True(Make("a", outbound: "byedpi").IsByeDpi);
        Assert.False(Make("a", outbound: "byedpi").IsProxy);
    }
}
