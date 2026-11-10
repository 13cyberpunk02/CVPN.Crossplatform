using System.Text.RegularExpressions;

namespace CVPN.Models;

public enum LogLevel
{
    Trace,
    Debug,
    Info,
    Warn,
    Error
}

/// <summary>
/// Строка лога, разобранная на части - чтобы страница могла показать время,
/// уровень и источник отдельно, раскрасить их и фильтровать. Исходная строка
/// хранится целиком: в буфер обмена и в файл уходит именно она.
/// </summary>
public sealed partial class LogEntry
{
    private LogEntry(string raw, string time, LogLevel level, string source, string message)
    {
        Raw = raw;
        Time = time;
        Level = level;
        Source = source;
        Message = message;
    }

    public string Raw { get; }

    /// <summary>Время чч:мм:сс.</summary>
    public string Time { get; }

    public LogLevel Level { get; }

    /// <summary>Кто пишет: «cvpn», «router», «inbound/tun[tun-in]» и т. п. Может быть пустым.</summary>
    public string Source { get; }

    public string Message { get; }

    public bool HasSource => Source.Length > 0;

    /// <summary>Источник с пробелом после него - для строки, где он идёт перед сообщением.</summary>
    public string SourcePrefix => Source.Length > 0 ? Source + " " : "";

    public bool IsError => Level == LogLevel.Error;
    public bool IsWarn => Level == LogLevel.Warn;
    public bool IsInfo => Level == LogLevel.Info;
    public bool IsQuiet => Level is LogLevel.Debug or LogLevel.Trace;

    /// <summary>Короткая метка уровня - одинаковой ширины, столбец не прыгает.</summary>
    public string LevelLabel => Level switch
    {
        LogLevel.Error => "ERR",
        LogLevel.Warn => "WRN",
        LogLevel.Debug => "DBG",
        LogLevel.Trace => "TRC",
        _ => "INF"
    };

    // +0300 2025-01-01 12:34:56 INFO [3527064221 0ms] inbound/tun[tun-in]: inbound connection to ...
    [GeneratedRegex(
        @"^(?:[+-]\d{4}\s+\d{4}-\d{2}-\d{2}\s+(?<time>\d{2}:\d{2}:\d{2})\s+)?(?<level>TRACE|DEBUG|INFO|WARN|WARNING|ERROR|FATAL|PANIC)\s+(?:\[[^\]]*\]\s+)?(?<rest>.*)$",
        RegexOptions.CultureInvariant)]
    private static partial Regex CoreLine();

    // Источник - первое «слово» до двоеточия без пробелов: router, dns, outbound/vless[proxy]
    [GeneratedRegex(@"^(?<source>[\w\-./]+(?:\[[^\]\s]*\])?):\s+(?<message>.*)$", RegexOptions.CultureInvariant)]
    private static partial Regex Sourced();

    // Собственные строки приложения: «[cvpn] ...», «[byedpi] ...»
    [GeneratedRegex(@"^\[(?<source>[\w\-]+)\]\s*(?<message>.*)$", RegexOptions.CultureInvariant)]
    private static partial Regex Bracketed();

    public static LogEntry Parse(string raw, DateTime now)
    {
        var fallbackTime = now.ToString("HH:mm:ss");

        var core = CoreLine().Match(raw);
        if (core.Success)
        {
            var time = core.Groups["time"].Success ? core.Groups["time"].Value : fallbackTime;
            var level = ParseLevel(core.Groups["level"].Value);
            var rest = core.Groups["rest"].Value;

            var sourced = Sourced().Match(rest);
            return sourced.Success
                ? new LogEntry(raw, time, level, sourced.Groups["source"].Value, sourced.Groups["message"].Value)
                : new LogEntry(raw, time, level, "", rest);
        }

        var own = Bracketed().Match(raw);
        if (own.Success)
        {
            var message = own.Groups["message"].Value;
            return new LogEntry(raw, fallbackTime, Guess(message), own.Groups["source"].Value, message);
        }

        return new LogEntry(raw, fallbackTime, Guess(raw), "", raw);
    }

    private static LogLevel ParseLevel(string level) => level switch
    {
        "TRACE" => LogLevel.Trace,
        "DEBUG" => LogLevel.Debug,
        "WARN" or "WARNING" => LogLevel.Warn,
        "ERROR" or "FATAL" or "PANIC" => LogLevel.Error,
        _ => LogLevel.Info
    };

    /// <summary>
    /// У своих строк уровня нет - угадываем по словам. Ошибку лучше подсветить
    /// лишний раз, чем пропустить: её ищут глазами в первую очередь.
    /// </summary>
    private static LogLevel Guess(string message)
    {
        if (message.Contains("error", StringComparison.OrdinalIgnoreCase)
            || message.Contains("failed", StringComparison.OrdinalIgnoreCase)
            || message.Contains("could not", StringComparison.OrdinalIgnoreCase)
            || message.Contains("fatal", StringComparison.OrdinalIgnoreCase))
            return LogLevel.Error;

        if (message.Contains("warn", StringComparison.OrdinalIgnoreCase)
            || message.Contains("timeout", StringComparison.OrdinalIgnoreCase))
            return LogLevel.Warn;

        return LogLevel.Info;
    }

    public bool Matches(string query) =>
        query.Length == 0 || Raw.Contains(query, StringComparison.OrdinalIgnoreCase);
}
