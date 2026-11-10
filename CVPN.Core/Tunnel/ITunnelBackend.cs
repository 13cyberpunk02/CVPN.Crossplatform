namespace CVPN.Tunnel;

/// <summary>Ядро остановилось.</summary>
/// <param name="Code">Код возврата; -1, если неизвестен.</param>
/// <param name="Graceful">Остановили мы сами - это не ошибка.</param>
/// <param name="Message">Готовое объяснение для строки состояния, если бэкенд его знает.</param>
public sealed record CoreExit(int Code, bool Graceful, string? Message = null);

/// <summary>
/// Как запускается ядро: дочерним процессом, через службу или через VpnService.
/// MainViewModel не знает, какой путь выбран, - он просит бэкенд у
/// <see cref="ITunnelBackendProvider"/> и дальше работает с ним одинаково.
/// </summary>
public interface ITunnelBackend
{
    /// <summary>Подпись режима на главной странице: «TUN», «TUN · служба», «VPN».</summary>
    string Title { get; }

    /// <summary>Нужно ли приложению самому иметь права на TUN (служба и VpnService - нет).</summary>
    bool NeedsTunPrivileges { get; }

    /// <summary>Строка лога. Приходит из фонового потока.</summary>
    event Action<string>? LineReceived;

    /// <summary>Ядро остановилось. Приходит из фонового потока.</summary>
    event Action<CoreExit>? Exited;

    /// <summary>Запуск. Ok=false - запуска не было, Message объясняет почему.</summary>
    Task<(bool Ok, string Message)> StartAsync(string configPath);

    Task StopAsync();
}

/// <summary>Выбор бэкенда под текущие настройки и платформу.</summary>
public interface ITunnelBackendProvider
{
    Task<ITunnelBackend> SelectAsync(Services.AppSettings settings);
}
