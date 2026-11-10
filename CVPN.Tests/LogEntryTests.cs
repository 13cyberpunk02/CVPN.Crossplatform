using CVPN.Models;

namespace CVPN.Tests;

public class LogEntryTests
{
    private static readonly DateTime Now = new(2026, 11, 10, 9, 5, 7);

    [Fact]
    public void Строка_ядра_разбирается_на_время_уровень_источник_и_текст()
    {
        var entry = LogEntry.Parse(
            "+0300 2026-11-10 14:02:12 INFO [3527064221 12ms] outbound/vless[proxy]: outbound connection to www.youtube.com:443",
            Now);

        Assert.Equal("14:02:12", entry.Time);
        Assert.Equal(LogLevel.Info, entry.Level);
        Assert.Equal("outbound/vless[proxy]", entry.Source);
        Assert.Equal("outbound connection to www.youtube.com:443", entry.Message);
    }

    [Theory]
    [InlineData("WARN", LogLevel.Warn)]
    [InlineData("ERROR", LogLevel.Error)]
    [InlineData("FATAL", LogLevel.Error)]
    [InlineData("DEBUG", LogLevel.Debug)]
    [InlineData("TRACE", LogLevel.Trace)]
    public void Уровень_ядра_распознаётся(string level, LogLevel expected)
    {
        var entry = LogEntry.Parse($"+0300 2026-11-10 14:02:12 {level} router: something", Now);

        Assert.Equal(expected, entry.Level);
    }

    /// <summary>Без отметки времени ядра берётся время прихода строки.</summary>
    [Fact]
    public void Без_времени_в_строке_берётся_текущее()
    {
        var entry = LogEntry.Parse("INFO sing-box started (0.41s)", Now);

        Assert.Equal("09:05:07", entry.Time);
        Assert.Equal("", entry.Source);
        Assert.Equal("", entry.SourcePrefix);
        Assert.Equal("sing-box started (0.41s)", entry.Message);
    }

    [Fact]
    public void Своя_строка_получает_источник_из_скобок()
    {
        var entry = LogEntry.Parse("[cvpn] building configuration", Now);

        Assert.Equal("cvpn", entry.Source);
        Assert.Equal("cvpn ", entry.SourcePrefix);
        Assert.Equal("building configuration", entry.Message);
        Assert.Equal(LogLevel.Info, entry.Level);
    }

    [Theory]
    [InlineData("[cvpn] could not set the system proxy: access denied", LogLevel.Error)]
    [InlineData("[cvpn] start failed", LogLevel.Error)]
    [InlineData("[cvpn] latency check timeout", LogLevel.Warn)]
    [InlineData("какая-то строка без формата", LogLevel.Info)]
    public void Уровень_своих_строк_угадывается_по_словам(string raw, LogLevel expected)
    {
        Assert.Equal(expected, LogEntry.Parse(raw, Now).Level);
    }

    /// <summary>В буфер обмена уходит исходная строка, а не разобранная.</summary>
    [Fact]
    public void Исходная_строка_сохраняется_целиком()
    {
        const string raw = "+0300 2026-11-10 14:02:12 WARN dns: lookup failed";

        Assert.Equal(raw, LogEntry.Parse(raw, Now).Raw);
    }

    [Fact]
    public void Поиск_идёт_по_всей_строке_без_учёта_регистра()
    {
        var entry = LogEntry.Parse("+0300 2026-11-10 14:02:12 INFO router: match YouTube.com", Now);

        Assert.True(entry.Matches("youtube"));
        Assert.True(entry.Matches("ROUTER"));
        Assert.True(entry.Matches(""));
        Assert.False(entry.Matches("telegram"));
    }
}
