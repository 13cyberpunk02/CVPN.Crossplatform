using System.Collections.ObjectModel;
using System.Windows.Input;
using CVPN.Platform;
using CVPN.Core;
using CVPN.Localization;
using CVPN.Models;
using CVPN.Models.Enums;

namespace CVPN.ViewModels;

/// <summary>
/// Страница живых соединений: что куда идёт и по какому правилу.
///
/// Первая страница, вынесенная из MainViewModel. Оболочка передаётся целиком -
/// это временный мостик, пока состояние туннеля не переехало в отдельный
/// сервис. Зато сама страница уже автономна: своя коллекция, свой таймер,
/// свои команды.
/// </summary>
public sealed class ConnectionsViewModel : PageViewModel
{
    private IUiTimer? _timer;

    public ConnectionsViewModel(MainViewModel shell) : base(shell)
    {
        RuleDirect = new RelayCommand(p =>
        {
            if (p is ConnectionInfo c) AddRule(c, RouteAction.Direct);
        });
        RuleProxy = new RelayCommand(p =>
        {
            if (p is ConnectionInfo c) AddRule(c, RouteAction.Proxy);
        });
        RuleBlock = new RelayCommand(p =>
        {
            if (p is ConnectionInfo c) AddRule(c, RouteAction.Block);
        });
        CloseConnection = new RelayCommand(p =>
        {
            if (p is ConnectionInfo c) _ = CloseAsync(c);
        });
    }

    public ObservableCollection<ConnectionInfo> Connections { get; } = [];

    public ICommand RuleDirect { get; }
    public ICommand RuleProxy { get; }
    public ICommand RuleBlock { get; }
    public ICommand CloseConnection { get; }

    /// <summary>
    /// Опрос идёт, только пока страница открыта: держать его постоянно незачем,
    /// а секунда - предел, за которым список перестаёт выглядеть живым.
    /// </summary>
    public override void Activate()
    {
        if (_timer is not null) return;

        _timer = Shell.Platform.Ui.CreateTimer(TimeSpan.FromSeconds(1), () => _ = RefreshAsync());
        _timer.Start();

        _ = RefreshAsync();
    }

    public override void Deactivate()
    {
        _timer?.Stop();
        _timer = null;
    }

    private async Task RefreshAsync()
    {
        if (_frozen) return;

        if (Shell.Api is null || !Shell.IsConnected)
        {
            Show([]);
            return;
        }

        Show(await Shell.Api.GetConnectionsAsync());
    }

    private IReadOnlyList<ConnectionInfo> _all = [];
    private bool _frozen;

    /// <summary>Поиск по домену, процессу, правилу и выходу. Сводка сверху его не учитывает.</summary>
    public string Filter
    {
        get;
        set
        {
            if (Set(ref field, value)) ApplyFilter();
        }
    } = string.Empty;

    public int TotalCount => _all.Count;
    public bool HasAny => _all.Count > 0;
    public int ProxyCount => _all.Count(c => c.IsProxy);
    public int DirectCount => _all.Count(c => c.IsDirect);
    public int ByeDpiCount => _all.Count(c => c.IsByeDpi);

    public string TotalDownloadLabel => ByteFormat.Size(_all.Sum(c => c.Download));
    public string TotalUploadLabel => ByteFormat.Size(_all.Sum(c => c.Upload));

    /// <summary>Есть соединения, но поиск их все отсеял.</summary>
    public bool NothingFound => _all.Count > 0 && Connections.Count == 0;

    private void Show(IReadOnlyList<ConnectionInfo> fresh)
    {
        // Полоска под строкой - доля от самого тяжёлого соединения, а не от суммы:
        // иначе при сотне соединений все полоски были бы пустыми
        var heaviest = fresh.Count == 0 ? 0 : fresh.Max(c => c.Download + c.Upload);
        foreach (var item in fresh)
            item.Share = heaviest > 0 ? (double)(item.Download + item.Upload) / heaviest : 0;

        _all = [.. fresh.OrderByDescending(c => c.Download + c.Upload)];
        ApplyFilter();

        Raise(nameof(TotalCount));
        Raise(nameof(HasAny));
        Raise(nameof(ProxyCount));
        Raise(nameof(DirectCount));
        Raise(nameof(ByeDpiCount));
        Raise(nameof(TotalDownloadLabel));
        Raise(nameof(TotalUploadLabel));
    }

    private void ApplyFilter()
    {
        var query = Filter.Trim();

        Connections.Clear();

        foreach (var item in _all.Where(c => Matches(c, query)))
            Connections.Add(item);

        Raise(nameof(NothingFound));
    }

    internal static bool Matches(ConnectionInfo c, string query) =>
        query.Length == 0
        || c.Endpoint.Contains(query, StringComparison.OrdinalIgnoreCase)
        || c.Process.Contains(query, StringComparison.OrdinalIgnoreCase)
        || c.Rule.Contains(query, StringComparison.OrdinalIgnoreCase)
        || c.Outbound.Contains(query, StringComparison.OrdinalIgnoreCase);

#if DEBUG
    /// <summary>Отладочный снимок экрана: показать готовый список и не опрашивать ядро.</summary>
    public void ShowDemo(IReadOnlyList<ConnectionInfo> items)
    {
        _frozen = true;
        Show(items);
    }
#endif

    /// <summary>
    /// Правило прямо из списка - ради этого страница и нужна: увидел домен
    /// не в том выходе, тут же его и починил.
    /// </summary>
    private void AddRule(ConnectionInfo connection, RouteAction action)
    {
        var domain = connection.RuleCandidate;

        var existing = Shell.Rules.FirstOrDefault(r => r.Match == MatchKind.DomainSuffix && r.Value == domain);

        if (existing is not null)
        {
            if (existing.Action == action)
            {
                Shell.Notify(Loc.T("Connections_RuleExists", domain));
                return;
            }

            existing.Action = action;
            Shell.Persist();

            Shell.Notify(Loc.T("Connections_RuleChanged", domain, Describe(action)));
            return;
        }

        Shell.AddRule(MatchKind.DomainSuffix, domain, action);

        Shell.Notify(Loc.T("Connections_RuleAdded", domain, Describe(action)));
    }

    private static string Describe(RouteAction action) => action switch
    {
        RouteAction.Direct => Loc.T("Common_Direct"),
        RouteAction.Block => Loc.T("Common_Block"),
        RouteAction.ByeDpi => Loc.T("Common_ByeDpi"),
        _ => Loc.T("Common_ViaProxy")
    };

    private async Task CloseAsync(ConnectionInfo connection)
    {
        if (Shell.Api is null) return;

        await Shell.Api.CloseConnectionAsync(connection.Id);
        await RefreshAsync();
    }
}