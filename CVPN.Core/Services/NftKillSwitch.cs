namespace CVPN.Services;

/// <summary>
/// Kill switch для Linux на nftables: отдельная таблица с политикой drop
/// на исходящий трафик. Чистая функция - только текст правил; применяет
/// его платформенный код (nft -f), а тесты проверяют содержимое.
///
/// Как отличить трафик туннеля. В Windows правило ставится на программу
/// (sing-box.exe). В Linux такого фильтра нет, поэтому ядро помечает свои
/// соединения меткой <see cref="Mark"/> (route.default_mark в config.json,
/// см. ConfigBuilder), и пропускается всё с этой меткой.
/// </summary>
public static class NftKillSwitch
{
    /// <summary>Таблица целиком наша: снятие - удаление таблицы, чужие правила не трогаются.</summary>
    public const string Table = "cvpn_killswitch";

    /// <summary>Метка соединений ядра (SO_MARK). Не пересекается с метками auto_redirect sing-box.</summary>
    public const int Mark = 0x1C9E;

    /// <summary>Интерфейсы TUN: имя выбирает sing-box (tun0, tun1…).</summary>
    private const string TunInterfaces = "{ \"tun*\", \"utun*\", \"sing*\" }";

    /// <summary>Правила включения. Применяются атомарно: «nft -f» либо всё, либо ничего.</summary>
    public static string Enable(bool allowLocalNetwork)
    {
        var lines = new List<string>
        {
            // Создать и тут же удалить: повторное включение не падает на
            // «уже существует» и не дублирует правила
            $"table inet {Table}",
            $"delete table inet {Table}",
            $"table inet {Table} {{",
            "    chain output {",
            "        type filter hook output priority 0; policy drop;",
            "        oifname \"lo\" accept",
            $"        meta mark {Mark} accept",
            $"        oifname {TunInterfaces} accept"
        };

        if (allowLocalNetwork)
        {
            lines.Add("        ip daddr { 10.0.0.0/8, 172.16.0.0/12, 192.168.0.0/16, 169.254.0.0/16 } accept");
            lines.Add("        ip6 daddr { fe80::/10, fc00::/7 } accept");

            // DHCP: без него адрес не продлится, и сеть пропадёт уже по-настоящему
            lines.Add("        udp sport 68 udp dport 67 accept");
            lines.Add("        udp sport 546 udp dport 547 accept");
        }

        lines.Add("    }");
        lines.Add("}");

        return string.Join('\n', lines) + "\n";
    }

    /// <summary>Снятие: вся таблица разом.</summary>
    public static string Disable() =>
        $"table inet {Table}\ndelete table inet {Table}\n";
}
