using CVPN.Shared;

namespace CVPN.Tests;

public class AnsiTests
{
    /// <summary>Строка из лога sing-box на Android - как она пришла с телефона.</summary>
    [Fact]
    public void Цветовые_коды_sing_box_вырезаются()
    {
        const string raw = "+0300 2026-11-10 14:06:09 \u001b[36mINFO\u001b[0m [\u001b[38;5;40m919101976\u001b[0m 0ms] inbound/mixed";

        Assert.Equal("+0300 2026-11-10 14:06:09 INFO [919101976 0ms] inbound/mixed", Ansi.Strip(raw));
    }

    [Fact]
    public void Строка_без_кодов_не_меняется()
    {
        const string plain = "[cvpn] connecting to NL (vless · reality)";

        Assert.Same(plain, Ansi.Strip(plain));
    }
}
