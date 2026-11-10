using CVPN.Localization;
using CVPN.Services;

namespace CVPN.Tunnel;

/// <summary>
/// Ядро дочерним процессом приложения. Режим системного прокси на любой ОС
/// и TUN, когда у приложения есть права.
/// </summary>
/// <param name="corePath">Путь к sing-box.</param>
/// <param name="title">Подпись режима.</param>
/// <param name="needsTunPrivileges">TUN поднимает само приложение.</param>
/// <param name="beforeStart">
/// Подготовка перед запуском - на Windows это удаление зависшего адаптера.
/// Возвращает строку для лога или пустую.
/// </param>
public sealed class LocalCoreBackend(
    string corePath,
    string title,
    bool needsTunPrivileges,
    Func<Task<string>>? beforeStart = null) : ITunnelBackend
{
    private SingBoxService? _core;

    public string Title => title;
    public bool NeedsTunPrivileges => needsTunPrivileges;

    public event Action<string>? LineReceived;
    public event Action<CoreExit>? Exited;

    public async Task<(bool Ok, string Message)> StartAsync(string configPath)
    {
        if (beforeStart is not null)
        {
            var note = await beforeStart();
            if (note.Length > 0) LineReceived?.Invoke($"[cvpn] {note}");
        }

        var core = new SingBoxService(corePath);
        core.LineReceived += Forward;
        core.Exited += OnExited;

        var (ok, message) = await core.CheckConfigAsync(configPath);
        if (!ok)
        {
            core.LineReceived -= Forward;
            core.Exited -= OnExited;
            return (false, Loc.T("Config_Rejected", message));
        }

        _core = core;
        core.Start(configPath);
        return (true, "");
    }

    public async Task StopAsync()
    {
        var core = _core;
        if (core is null) return;

        _core = null;
        core.LineReceived -= Forward;
        core.Exited -= OnExited;
        await core.StopAsync();
    }

    private void Forward(string line) => LineReceived?.Invoke(line);

    private void OnExited(int code, bool graceful) => Exited?.Invoke(new CoreExit(code, graceful));
}
