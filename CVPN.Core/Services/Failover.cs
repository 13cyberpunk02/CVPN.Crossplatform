using CVPN.Models;

namespace CVPN.Services;

/// <summary>
/// Правила «сторожа» подключения - отдельно от таймеров и ядра, чтобы их
/// можно было проверить тестами.
/// </summary>
public static class Failover
{
    /// <summary>Как часто проверять, ходит ли трафик через текущий сервер.</summary>
    public static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(15);

    /// <summary>
    /// Сколько неудачных проверок подряд считать отказом. Одна - не повод:
    /// проверка могла попасть на секундный провал сети.
    /// </summary>
    public const int FailuresBeforeSwitch = 3;

    /// <summary>Сколько раз подряд перезапускать упавшее ядро, прежде чем сдаться.</summary>
    public const int MaxRestarts = 3;

    /// <summary>Пауза перед перезапуском: растёт с каждой попыткой - 3, 6, 9 секунд.</summary>
    public static TimeSpan RestartDelay(int attempt) => TimeSpan.FromSeconds(3 * Math.Max(1, attempt));

    /// <summary>
    /// Запасной сервер: самый быстрый из ответивших, кроме текущего.
    /// null - не ответил никто, и переключаться некуда (скорее всего, пропал
    /// интернет целиком - тогда метаться между серверами бессмысленно).
    /// </summary>
    public static ServerProfile? PickReplacement(
        ServerProfile? current, IEnumerable<(ServerProfile Profile, int DelayMs)> measured) =>
        measured
            .Where(m => m.DelayMs >= 0 && !ReferenceEquals(m.Profile, current))
            .OrderBy(m => m.DelayMs)
            .Select(m => m.Profile)
            .FirstOrDefault();
}
