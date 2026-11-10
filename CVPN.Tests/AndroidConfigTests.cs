using System.Text.Json.Nodes;
using CVPN.Models;
using CVPN.Services;

namespace CVPN.Tests;

/// <summary>
/// Конфиг для Android: без этих правок ядро либо не стартует (tun без прав,
/// netlink, resolv.conf), либо ломает маршрутизацию.
/// </summary>
public class AndroidConfigTests
{
    private static JsonObject Desktop(AppSettings settings)
    {
        var profile = new ServerProfile
        {
            Name = "nl", Host = "nl.example.net", Port = 443,
            Uuid = "8f1ce66e-719d-48b8-9ee6-804b52887082", PublicKey = "key", Sni = "www.google.com"
        };
        var routing = new RoutingProfile { Name = "set", ProxyByDefault = true };

        return JsonNode.Parse(ConfigBuilder.Build([profile], profile, routing, settings))!.AsObject();
    }

    private static JsonObject Android(AppSettings settings, string dns = "192.168.1.1")
    {
        var root = Desktop(settings);
        ConfigBuilder.AdaptForAndroid(root, dns);
        return root;
    }

    [Fact]
    public void Входа_tun_нет_а_mixed_остаётся()
    {
        var root = Android(new AppSettings { TunEnabled = true });
        var types = root["inbounds"]!.AsArray().Select(i => i!["type"]!.GetValue<string>()).ToList();

        Assert.DoesNotContain("tun", types);
        Assert.Contains("mixed", types);
    }

    [Fact]
    public void Привязки_к_интерфейсу_и_поиска_процессов_нет()
    {
        var root = Android(new AppSettings { TunEnabled = true });
        var route = root["route"]!.AsObject();

        Assert.False(route.ContainsKey("auto_detect_interface"));
        Assert.False(route.ContainsKey("find_process"));
        Assert.DoesNotContain(route["rules"]!.AsArray(), r => r!.AsObject().ContainsKey("process_name"));
    }

    [Fact]
    public void Локальный_резолвер_становится_udp_на_dns_сети()
    {
        var root = Android(new AppSettings(), dns: "192.168.1.1");
        var servers = root["dns"]!["servers"]!.AsArray().Select(s => s!.AsObject()).ToList();

        Assert.DoesNotContain(servers, s => s["type"]!.GetValue<string>() == "local");

        var local = servers.Single(s => s["tag"]!.GetValue<string>() == "dns-local");
        Assert.Equal("udp", local["type"]!.GetValue<string>());
        Assert.Equal("192.168.1.1", local["server"]!.GetValue<string>());
    }

    [Fact]
    public void Без_dns_сети_берётся_запасной()
    {
        var root = Android(new AppSettings(), dns: "");
        var local = root["dns"]!["servers"]!.AsArray().Single(s => s!["tag"]!.GetValue<string>() == "dns-local");

        Assert.Equal("1.1.1.1", local!["server"]!.GetValue<string>());
    }

    /// <summary>Перехват DNS обязателен: запросы к адресу DNS внутри VPN приходят через tun2socks.</summary>
    [Fact]
    public void Перехват_dns_остаётся()
    {
        var root = Android(new AppSettings { TunEnabled = true });

        Assert.Contains(root["route"]!["rules"]!.AsArray(),
            r => r!["action"]?.GetValue<string>() == "hijack-dns");
    }
}
