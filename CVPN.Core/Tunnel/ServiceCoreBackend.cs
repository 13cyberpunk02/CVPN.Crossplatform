using CVPN.Ipc;
using CVPN.Localization;
using CVPN.Services;

namespace CVPN.Tunnel;

/// <summary>
/// Ядро в службе туннеля (служба Windows, юнит systemd). Права на TUN у службы,
/// приложение только отдаёт конфиг и раз в секунду забирает накопленный лог.
/// </summary>
public sealed class ServiceCoreBackend(string title) : ITunnelBackend
{
    private CancellationTokenSource? _pump;

    public string Title => title;
    public bool NeedsTunPrivileges => false;

    public event Action<string>? LineReceived;
    public event Action<CoreExit>? Exited;

    public async Task<(bool Ok, string Message)> StartAsync(string configPath)
    {
        LineReceived?.Invoke("[cvpn] starting through the CVPN Tunnel service");

        var configText = await File.ReadAllTextAsync(configPath);
        var ruleSets = RuleSetPayload.Collect(configText);

        if (ruleSets.Count > 0)
            LineReceived?.Invoke($"[cvpn] rule sets sent to the service: {ruleSets.Count}");

        var response = await ServiceClient.StartAsync(configText, ruleSets);

        if (response?.Ok != true)
            return (false, response?.Message ?? Loc.T("Service_NoAnswer"));

        _pump = new CancellationTokenSource();
        _ = PumpAsync(_pump.Token);

        return (true, "");
    }

    public async Task StopAsync()
    {
        if (_pump is null) return;

        await _pump.CancelAsync();
        _pump.Dispose();
        _pump = null;

        await ServiceClient.StopAsync();
    }

    /// <summary>
    /// Служба не может писать в наш лог напрямую, поэтому раз в секунду
    /// забираем накопленные строки. Очередь на стороне службы ограничена,
    /// так что при простое ничего не растёт.
    /// </summary>
    private async Task PumpAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));

        try
        {
            while (await timer.WaitForNextTickAsync(ct))
            {
                var status = await ServiceClient.StatusAsync();
                if (status is null || ct.IsCancellationRequested) continue;

                foreach (var line in status.Log) LineReceived?.Invoke(line);

                if (status.Running) continue;

                Exited?.Invoke(new CoreExit(-1, false, Loc.T("Service_CoreStopped")));
                return;
            }
        }
        catch (OperationCanceledException)
        {
            // остановили сами
        }
    }
}
