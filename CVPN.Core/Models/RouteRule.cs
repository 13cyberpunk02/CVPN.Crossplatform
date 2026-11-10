using System.Text.Json.Serialization;
using CVPN.Core;
using CVPN.Localization;
using CVPN.Models.Enums;

namespace CVPN.Models;

public sealed class RouteRule : ObservableObject
{
    private MatchKind _match = MatchKind.Geosite;
    private string _value = "";
    private RouteAction _action = RouteAction.Proxy;
    private bool _enabled = true;

    public MatchKind Match { get => _match; set { Set(ref _match, value); Raise(nameof(MatchLabel)); Raise(nameof(DisplayValue)); Raise(nameof(KindTitle)); Raise(nameof(KindIcon)); Raise(nameof(FlagCode)); } }
    public string Value { get => _value; set { Set(ref _value, value); Raise(nameof(DisplayValue)); Raise(nameof(FlagCode)); } }
    public RouteAction Action { get => _action; set { Set(ref _action, value); Raise(nameof(ActionLabel)); } }
    public bool Enabled { get => _enabled; set => Set(ref _enabled, value); }

    [JsonIgnore]
    public string MatchLabel => Match switch
    {
        MatchKind.Geosite => "geosite",
        MatchKind.Geoip => "geoip",
        MatchKind.Domain => "domain",
        MatchKind.DomainSuffix => "domain_suffix",
        MatchKind.DomainKeyword => "domain_keyword",
        MatchKind.Process => "process_name",
        MatchKind.RuleSetRemote => Loc.T("Routing_SetRemote"),
        MatchKind.RuleSetLocal => Loc.T("Routing_SetLocal"),
        _ => "-"
    };

    /// <summary>Для .srs в списке показываем имя набора: путь и ссылка не влезают в строку.</summary>
    [JsonIgnore]
    public string DisplayValue => Match switch
    {
        MatchKind.RuleSetRemote or MatchKind.RuleSetLocal => ShortName(Value),
        _ => Value
    };

    private static string ShortName(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";

        var name = value.Split('/', '\\').LastOrDefault() ?? value;
        return name.EndsWith(".srs", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;
    }

    /// <summary>Понятное название вида правила - то, что видит пользователь вместо «domain_suffix».</summary>
    [JsonIgnore]
    public string KindTitle => KindTitleOf(Match);

    public static string KindTitleOf(MatchKind kind) => kind switch
    {
        MatchKind.Geosite => Loc.T("Match_Geosite"),
        MatchKind.Geoip => Loc.T("Match_Geoip"),
        MatchKind.Domain => Loc.T("Match_Domain"),
        MatchKind.DomainSuffix => Loc.T("Match_Suffix"),
        MatchKind.DomainKeyword => Loc.T("Match_Keyword"),
        MatchKind.Process => Loc.T("Match_Process"),
        MatchKind.RuleSetRemote => Loc.T("Match_SetRemote"),
        MatchKind.RuleSetLocal => Loc.T("Match_SetLocal"),
        _ => "-"
    };

    /// <summary>Иконка вида правила - ключ из Theme/Icons.axaml.</summary>
    [JsonIgnore]
    public string KindIcon => Match switch
    {
        MatchKind.Geosite => "Grid",
        MatchKind.Geoip => "Pin",
        MatchKind.Process => "App",
        MatchKind.RuleSetRemote => "Link",
        MatchKind.RuleSetLocal => "Folder",
        _ => "Globe"
    };

    /// <summary>Код страны для флажка у geoip-правила.</summary>
    [JsonIgnore]
    public string? FlagCode => Match == MatchKind.Geoip && Value.Trim().Length == 2 ? Value.Trim() : null;

    [JsonIgnore]
    public string ActionLabel => Action switch
    {
        RouteAction.Proxy => Loc.T("Routing_ToProxy"),
        RouteAction.Direct => Loc.T("Routing_ToDirect"),
        RouteAction.Block => Loc.T("Routing_ToBlock"),
        RouteAction.ByeDpi => Loc.T("Routing_ToByeDpi"),
        _ => "-"
    };
}