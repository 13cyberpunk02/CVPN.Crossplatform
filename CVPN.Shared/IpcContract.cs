using System.Text.Json;
using System.Text.Json.Serialization;

namespace CVPN.Shared;

/// <summary>
/// Общий контракт приложения и службы. Файл включён в оба проекта ссылкой,
/// чтобы протокол нельзя было поменять с одной стороны и забыть про другую.
/// </summary>
public static class IpcContract
{
    /// <summary>Имя канала. Служба одна на машину, поэтому имя фиксированное.</summary>
    public const string PipeName = "cvpn-tunnel";

    public const string ServiceName = "CVPNTunnel";

    /// <summary>
    /// Сокет службы на Linux. Каталог /run/cvpn создаёт systemd (RuntimeDirectory),
    /// сам сокет доступен только группе cvpn - в неё установщик добавляет пользователя.
    /// </summary>
    public const string UnixSocketPath = "/run/cvpn/tunnel.sock";

    /// <summary>Группа, которой разрешено управлять службой на Linux.</summary>
    public const string UnixGroup = "cvpn";

    /// <summary>
    /// Адрес канала для NamedPipe*Stream: на Windows - имя канала, на Unix -
    /// полный путь к сокету (.NET берёт его как есть, без /tmp/CoreFxPipe_).
    /// </summary>
    public static string PipeAddress => OperatingSystem.IsWindows() ? PipeName : UnixSocketPath;

    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };
}

public enum IpcCommand
{
    /// <summary>Проверка, что служба жива.</summary>
    Ping,

    /// <summary>Запустить ядро с переданной конфигурацией.</summary>
    Start,

    Stop,

    /// <summary>Текущее состояние и накопленные строки лога.</summary>
    Status
}

public sealed class IpcRequest
{
    public IpcCommand Command { get; set; }

    /// <summary>Содержимое config.json. Служба сохраняет его сама - путь не принимается.</summary>
    public string? Config { get; set; }

    /// <summary>
    /// Локальные наборы правил, на которые ссылается конфиг. Служба работает
    /// под SYSTEM и не видит каталог пользователя, поэтому файлы приходится
    /// передавать вместе с конфигом.
    /// </summary>
    public List<RuleSetFile> RuleSets { get; set; } = [];
}

/// <summary>Файл набора правил, переданный вместе с конфигом.</summary>
public sealed class RuleSetFile
{
    /// <summary>Только имя файла. Путь служба назначает сама.</summary>
    public required string Name { get; set; }

    /// <summary>Содержимое .srs в base64.</summary>
    public required string Content { get; set; }
}

public sealed class IpcResponse
{
    public bool Ok { get; set; }
    public bool Running { get; set; }
    public string Message { get; set; } = "";

    /// <summary>Строки вывода ядра, накопленные с прошлого запроса.</summary>
    public List<string> Log { get; set; } = [];
}