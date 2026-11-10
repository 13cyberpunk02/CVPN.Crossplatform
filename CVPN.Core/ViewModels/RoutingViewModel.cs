using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;
using CVPN.Core;
using CVPN.Localization;
using CVPN.Models;
using CVPN.Models.Enums;
using CVPN.Services;

namespace CVPN.ViewModels;

/// <summary>
/// Страница маршрутизации: наборы правил и сами правила.
///
/// Коллекции остаются в оболочке - из них строится конфигурация, и они нужны
/// независимо от того, открыта ли страница. Здесь только действия над ними
/// и то, что относится к отображению.
/// </summary>
public sealed class RoutingViewModel : PageViewModel
{
    public RoutingViewModel(MainViewModel shell) : base(shell)
    {
        TestDomain = new RelayCommand(RunTest);

        RemoveRule = new RelayCommand(p =>
        {
            if (p is RouteRule rr) Remove(rr);
        });
        MoveUp = new RelayCommand(p =>
        {
            if (p is RouteRule rr) Move(rr, -1);
        });
        MoveDown = new RelayCommand(p =>
        {
            if (p is RouteRule rr) Move(rr, +1);
        });

        AddSet = new RelayCommand(AddRoutingProfile);
        RemoveSet = new RelayCommand(RemoveRoutingProfile, () => RoutingProfiles.Count > 1);

        AddNewRule = new RelayCommand(AddFromForm);
        PickSrs = new RelayCommand(async () => await PickSrsAsync());

        shell.PropertyChanged += OnShellChanged;
    }

    // ===================== форма нового правила =====================
    // Раньше жила в code-behind WPF-разметки; здесь её используют все платформы.

    private static readonly Dictionary<MatchKind, string> Hints = new()
    {
        [MatchKind.Geosite] = "youtube · twitch · category-ads-all",
        [MatchKind.Geoip] = Loc.T("Hint_Geoip"),
        [MatchKind.Domain] = Loc.T("Hint_Domain"),
        [MatchKind.DomainSuffix] = Loc.T("Hint_Suffix"),
        [MatchKind.DomainKeyword] = Loc.T("Hint_Keyword"),
        [MatchKind.Process] = Loc.T("Hint_Process"),
        [MatchKind.RuleSetRemote] = "https://…/geosite-youtube.srs",
        [MatchKind.RuleSetLocal] = Loc.T("Hint_SetLocal")
    };

    /// <summary>Порядок совпадает с MatchKind.</summary>
    public IReadOnlyList<string> MatchOptions { get; } =
    [
        "geosite", "geoip", "domain", "domain_suffix", "domain_keyword", "process_name",
        Loc.T("Routing_SetRemote"), Loc.T("Routing_SetLocal")
    ];

    /// <summary>Порядок совпадает с RouteAction.</summary>
    public IReadOnlyList<string> ActionOptions { get; } =
        [Loc.T("Common_ViaProxy"), Loc.T("Common_Direct"), Loc.T("Common_Block"), Loc.T("Common_ByeDpi")];

    public IReadOnlyList<string> FallbackOptions { get; } = [Loc.T("Common_ViaProxy"), Loc.T("Common_Direct")];

    public int NewMatchIndex
    {
        get;
        set
        {
            Set(ref field, Math.Max(0, value));
            Raise(nameof(ValueHint));
            Raise(nameof(ShowPickSrs));
        }
    }

    private MatchKind NewMatch => (MatchKind)NewMatchIndex;

    public string NewValue
    {
        get;
        set => Set(ref field, value);
    } = "";

    public int NewActionIndex
    {
        get;
        set => Set(ref field, Math.Max(0, value));
    }

    public string ValueHint => Hints.GetValueOrDefault(NewMatch, "");

    /// <summary>Кнопка обзора нужна только для локального .srs.</summary>
    public bool ShowPickSrs => NewMatch == MatchKind.RuleSetLocal;

    /// <summary>«Всё остальное»: 0 - через прокси, 1 - напрямую.</summary>
    public int FallbackIndex
    {
        get => ActiveRouting.ProxyByDefault ? 0 : 1;
        set
        {
            if (value < 0 || value == FallbackIndex) return;
            SetFallback(value == 0);
            Raise();
        }
    }

    public ICommand AddNewRule { get; }
    public ICommand PickSrs { get; }

    private void AddFromForm()
    {
        AddRule(NewMatch, NewValue, (RouteAction)NewActionIndex);
        NewValue = "";
    }

    /// <summary>
    /// Копируем набор к себе: иначе правило сломается, стоит файлу переехать.
    /// Читаем потоком - на Android у выбранного файла нет пути.
    /// </summary>
    private async Task PickSrsAsync()
    {
        var file = await Shell.Platform.Files.OpenAsync(
            Loc.T("Routing_PickSrsTitle"),
            Platform.FileFilter.ParseWpf(Loc.T("Routing_SrsFilter")));

        if (file is null) return;

        try
        {
            AppPaths.EnsureCreated();
            var target = Path.Combine(AppPaths.RulesDir, Path.GetFileName(file.Name));

            if (!string.Equals(target, file.LocalPath, StringComparison.OrdinalIgnoreCase))
            {
                await using var source = await file.OpenReadAsync();
                await using var destination = File.Create(target);
                await source.CopyToAsync(destination);
            }

            NewValue = target;
        }
        catch (Exception ex)
        {
            // Не смогли скопировать - работаем по исходному пути, если он есть
            if (file.LocalPath is not null) NewValue = file.LocalPath;
            else Shell.Notify(Loc.T("Import_ReadFailed", ex.Message));
        }
    }

    public ObservableCollection<RouteRule> Rules => Shell.Rules;

    public ObservableCollection<RoutingProfile> RoutingProfiles => Shell.RoutingProfiles;

    /// <summary>Активный набор. Логика переключения живёт в оболочке.</summary>
    public RoutingProfile ActiveRouting
    {
        get => Shell.ActiveRouting;
        set => Shell.ActiveRouting = value;
    }

    public ICommand TestDomain { get; }
    public ICommand RemoveRule { get; }
    public ICommand MoveUp { get; }
    public ICommand MoveDown { get; }
    public ICommand AddSet { get; }
    public ICommand RemoveSet { get; }

    /// <summary>Домен, который проверяем против текущих правил.</summary>
    public string TestInput
    {
        get;
        set => Set(ref field, value);
    } = string.Empty;

    /// <summary>Человеческое объяснение результата проверки.</summary>
    public string TestResult
    {
        get;
        private set => Set(ref field, value);
    } = string.Empty;

    /// <summary>Исход проверки - по нему подсвечивается результат.</summary>
    public RouteAction? TestOutcome
    {
        get;
        private set
        {
            Set(ref field, value);
            Raise(nameof(HasTestResult));
        }
    }

    public bool HasTestResult => TestOutcome is not null;

    /// <summary>
    /// Проверяет домен по текущим правилам, не поднимая туннель. Отвечает
    /// на вопрос, из-за которого обычно и лезут в логи: какое правило сработает.
    /// </summary>
    private void RunTest()
    {
        var domain = RuleMatcher.Normalize(TestInput);

        if (domain.Length == 0)
        {
            TestOutcome = null;
            TestResult = Loc.T("Routing_EnterDomain");
            return;
        }

        var match = RuleMatcher.Evaluate(Rules, ActiveRouting.ProxyByDefault, domain);

        TestOutcome = match.Outcome;

        var outcome = match.Outcome switch
        {
            RouteAction.Direct => Loc.T("Common_Direct"),
            RouteAction.Block => Loc.T("Routing_WillBeBlocked"),
            RouteAction.ByeDpi => Loc.T("Common_ByeDpi"),
            _ => Loc.T("Common_ViaProxy")
        };

        var reason = match.Rule is null
            ? Loc.T("Routing_NoRuleMatched")
            : Loc.T("Routing_MatchedRule", match.Rule.MatchLabel, match.Rule.DisplayValue);

        TestResult = $"{domain} - {outcome}: {reason}.";

        if (!match.IsCertain)
        {
            var byIp = match.Unknown.Where(r => r.Match == MatchKind.Geoip).ToList();
            var bySet = match.Unknown.Where(r => r.Match != MatchKind.Geoip).ToList();

            if (bySet.Count > 0)
            {
                var names = bySet.Select(r => $"{r.MatchLabel} {r.DisplayValue}");
                TestResult += " " + Loc.T("Routing_UnknownSets", string.Join(", ", names));
            }

            if (byIp.Count > 0)
            {
                var names = byIp.Select(r => r.DisplayValue);
                TestResult += " " + Loc.T("Routing_UnknownGeoip", string.Join(", ", names));
            }
        }
    }

    /// <summary>Вызывается из формы добавления на странице.</summary>
    public void AddRule(MatchKind match, string value, RouteAction action) =>
        Shell.AddRule(match, value, action);

    /// <summary>Что делать с трафиком вне правил - свойство набора, а не приложения.</summary>
    public void SetFallback(bool proxyByDefault)
    {
        ActiveRouting.ProxyByDefault = proxyByDefault;
        Shell.Persist();
    }

    private void Remove(RouteRule rule)
    {
        Rules.Remove(rule);
        Shell.Persist();
    }

    /// <summary>
    /// Порядок правил определяет поведение: ядро берёт первое совпадение.
    /// Без перестановки единственным способом что-то поправить было бы
    /// удалить всё и добавить заново.
    /// </summary>
    private void Move(RouteRule rule, int offset)
    {
        var from = Rules.IndexOf(rule);
        if (from < 0) return;

        var to = from + offset;
        if (to < 0 || to >= Rules.Count) return;

        Rules.Move(from, to);
        Shell.Persist();

        if (Shell.IsConnected) Shell.Notify(Loc.T("Routing_OrderAfterReconnect"));
    }

    private void AddRoutingProfile()
    {
        var name = UniqueName(Loc.T("Routing_NewSet"));
        var profile = new RoutingProfile { Name = name, ProxyByDefault = ActiveRouting.ProxyByDefault };

        RoutingProfiles.Add(profile);
        ActiveRouting = profile;

        Shell.Notify(Loc.T("Routing_SetCreated", name));
    }

    private void RemoveRoutingProfile()
    {
        if (RoutingProfiles.Count < 2) return;

        var doomed = ActiveRouting;
        var fallback = RoutingProfiles.First(r => !ReferenceEquals(r, doomed));

        ActiveRouting = fallback;
        RoutingProfiles.Remove(doomed);
        Shell.Persist();

        Shell.Notify(Loc.T("Routing_SetDeleted", doomed.Name));
    }

    private string UniqueName(string basis)
    {
        if (RoutingProfiles.All(r => r.Name != basis)) return basis;

        for (var n = 2;; n++)
        {
            var candidate = $"{basis} {n}";
            if (RoutingProfiles.All(r => r.Name != candidate)) return candidate;
        }
    }

    private void OnShellChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.Rules) or nameof(MainViewModel.ActiveRouting))
        {
            Raise(nameof(Rules));
            Raise(nameof(ActiveRouting));

            // Смена набора меняет и значение «всё остальное»
            Raise(nameof(FallbackIndex));
        }
    }
}