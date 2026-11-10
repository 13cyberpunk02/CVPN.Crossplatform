using System.Text.Json.Nodes;
using CVPN.Services;

namespace CVPN.Tests;

/// <summary>
/// Kill switch для Linux. Ошибка здесь - это либо утечка мимо туннеля,
/// либо пропавший интернет, поэтому каждое правило проверяется явно.
/// </summary>
public class NftKillSwitchTests
{
    [Fact]
    public void По_умолчанию_исходящий_трафик_запрещён()
    {
        var rules = NftKillSwitch.Enable(allowLocalNetwork: false);

        Assert.Contains("type filter hook output priority 0; policy drop;", rules);
    }

    [Fact]
    public void Трафик_ядра_пропускается_по_метке()
    {
        var rules = NftKillSwitch.Enable(allowLocalNetwork: false);

        Assert.Contains($"meta mark {NftKillSwitch.Mark} accept", rules);
    }

    [Fact]
    public void Петля_и_туннель_пропускаются()
    {
        var rules = NftKillSwitch.Enable(allowLocalNetwork: false);

        Assert.Contains("oifname \"lo\" accept", rules);
        Assert.Contains("\"tun*\"", rules);
    }

    [Fact]
    public void Локальная_сеть_и_dhcp_только_по_запросу()
    {
        var closed = NftKillSwitch.Enable(allowLocalNetwork: false);
        var open = NftKillSwitch.Enable(allowLocalNetwork: true);

        Assert.DoesNotContain("192.168.0.0/16", closed);
        Assert.DoesNotContain("dport 67", closed);

        Assert.Contains("192.168.0.0/16", open);
        Assert.Contains("udp sport 68 udp dport 67 accept", open);
    }

    /// <summary>Повторное включение не должно падать и дублировать правила.</summary>
    [Fact]
    public void Включение_начинается_с_пересоздания_таблицы()
    {
        var lines = NftKillSwitch.Enable(allowLocalNetwork: true).Split('\n');

        Assert.Equal($"table inet {NftKillSwitch.Table}", lines[0]);
        Assert.Equal($"delete table inet {NftKillSwitch.Table}", lines[1]);
    }

    /// <summary>Скобки сбалансированы - иначе nft отвергнет файл целиком.</summary>
    [Fact]
    public void Скобки_сбалансированы()
    {
        var rules = NftKillSwitch.Enable(allowLocalNetwork: true);

        Assert.Equal(rules.Count(c => c == '{'), rules.Count(c => c == '}'));
    }

    [Fact]
    public void Снятие_удаляет_только_свою_таблицу()
    {
        var rules = NftKillSwitch.Disable();

        Assert.Contains($"delete table inet {NftKillSwitch.Table}", rules);
        Assert.DoesNotContain("flush ruleset", rules);
    }

    [Fact]
    public void Метка_ставится_только_на_Linux_при_включённом_kill_switch()
    {
        var on = new AppSettings { KillSwitch = true };
        var off = new AppSettings { KillSwitch = false };

        var linuxOn = new JsonObject();
        var windowsOn = new JsonObject();
        var linuxOff = new JsonObject();

        ConfigBuilder.ApplyKillSwitchMark(linuxOn, on, linux: true);
        ConfigBuilder.ApplyKillSwitchMark(windowsOn, on, linux: false);
        ConfigBuilder.ApplyKillSwitchMark(linuxOff, off, linux: true);

        Assert.Equal(NftKillSwitch.Mark, linuxOn["default_mark"]!.GetValue<int>());
        Assert.Null(windowsOn["default_mark"]);
        Assert.Null(linuxOff["default_mark"]);
    }
}
