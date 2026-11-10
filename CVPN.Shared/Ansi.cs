using System.Text.RegularExpressions;

namespace CVPN.Shared;

/// <summary>
/// sing-box красит вывод ANSI-кодами, если считает, что пишет в терминал
/// (на Android - всегда). Отключить это в конфиге 1.14 нельзя, поэтому коды
/// вырезаются при чтении: в логе приложения и в файле они - мусор вида «[36m».
/// </summary>
public static partial class Ansi
{
    [GeneratedRegex(@"\x1B\[[0-9;]*[A-Za-z]")]
    private static partial Regex Escape();

    public static string Strip(string line) => line.IndexOf('\x1B') < 0 ? line : Escape().Replace(line, "");
}
