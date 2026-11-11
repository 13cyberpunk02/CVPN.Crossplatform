using System.Collections.ObjectModel;
using System.Windows.Input;
using CVPN.Core;
using CVPN.Localization;
using CVPN.Models;
using CVPN.Platform;
using CVPN.Services;

namespace CVPN.ViewModels;

/// <summary>
/// Страница профилей: импорт, создание, экспорт, проверка серверов и подписка.
///
/// Сам список остаётся в оболочке - из него строится конфигурация. Здесь всё,
/// что с ним делают руками.
/// </summary>
public sealed class ProfilesViewModel : PageViewModel
{
    public ProfilesViewModel(MainViewModel shell) : base(shell)
    {
        ImportLink = new RelayCommand(Import);
        PasteLink = new RelayCommand(async () => await PasteAsync(), () => !IsBusy);
        ImportFile = new RelayCommand(async () => await ImportFromFileAsync());
        Create = new RelayCommand(async () => await EditAsync(null));

        EditProfileCommand = new RelayCommand(p =>
        {
            if (p is ServerProfile sp) _ = EditAsync(sp);
        });
        RemoveProfile = new RelayCommand(p =>
        {
            if (p is ServerProfile sp) Delete(sp);
        });
        SelectProfile = new RelayCommand(p =>
        {
            if (p is ServerProfile sp) _ = shell.SelectServerAsync(sp);
        });
        ExportProfile = new RelayCommand(p =>
        {
            if (p is ServerProfile sp) _ = ShowExportAsync(sp);
        });

        ExportAll = new RelayCommand(async () => await ShowExportAllAsync(), () => Profiles.Count > 0);
        PingAll = new RelayCommand(async () => await PingAsync(), () => !IsBusy);
        // Параметр - конкретная подписка; без параметра обновляются все
        UpdateSubscription = new RelayCommand(
            async p => await UpdateSubscriptionsAsync(p is Subscription one ? [one] : [.. Subscriptions]),
            _ => !IsBusy);
        RemoveSubscription = new RelayCommand(p =>
        {
            if (p is Subscription sub) _ = RemoveSubscriptionAsync(sub);
        });
        AddFromText = new RelayCommand(async () => await AddFromFieldAsync(), () => !IsBusy);
        ScanQr = new RelayCommand(async () => await ScanQrAsync(), () => !IsBusy && CanScanQr);
        ToggleSort = new RelayCommand(() => SortFastest = !SortFastest);

        Profiles.CollectionChanged += (_, _) => Refresh();
        Subscriptions.CollectionChanged += (_, _) => RaiseSubscriptions();
        shell.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.Active)) Raise(nameof(Selected));
        };

        Refresh();
    }

    public ObservableCollection<ServerProfile> Profiles => Shell.Profiles;

    // ===================== список на экране =====================

    /// <summary>Профили после поиска и сортировки - то, что видно в списке.</summary>
    public ObservableCollection<ServerProfile> Visible { get; } = [];

    /// <summary>Поиск по имени, адресу и протоколу.</summary>
    public string Search
    {
        get;
        set
        {
            if (Set(ref field, value ?? "")) Refresh();
        }
    } = "";

    /// <summary>Сначала быстрые: по задержке, не ответившие - в конце.</summary>
    public bool SortFastest
    {
        get;
        set
        {
            if (Set(ref field, value)) Refresh();
        }
    }

    public ICommand ToggleSort { get; }

    public int ProfileCount => Profiles.Count;
    public bool HasProfiles => Profiles.Count > 0;

    /// <summary>Профили есть, но поиск их все отсеял.</summary>
    public bool NothingFound => Profiles.Count > 0 && Visible.Count == 0;

    /// <summary>
    /// Выбор сервера щелчком по карточке - так же, как кнопка «Выбрать».
    /// Пустой выбор (список перестроился) игнорируем: сервер от этого не меняется.
    /// </summary>
    public ServerProfile? Selected
    {
        get => Shell.Active;
        set
        {
            if (value is null || ReferenceEquals(value, Shell.Active)) return;
            _ = Shell.SelectServerAsync(value);
        }
    }

    private void Refresh()
    {
        var query = Search.Trim();

        IEnumerable<ServerProfile> items = Profiles.Where(p => Matches(p, query));

        if (SortFastest)
            items = items.OrderBy(p => p.LatencyMs < 0 ? int.MaxValue : p.LatencyMs);

        Visible.Clear();
        foreach (var profile in items) Visible.Add(profile);

        Raise(nameof(ProfileCount));
        Raise(nameof(HasProfiles));
        Raise(nameof(NothingFound));
        Raise(nameof(Selected));
    }

    internal static bool Matches(ServerProfile profile, string query) =>
        query.Length == 0
        || profile.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
        || profile.Endpoint.Contains(query, StringComparison.OrdinalIgnoreCase)
        || profile.ProtocolLabel.Contains(query, StringComparison.OrdinalIgnoreCase);

    // ===================== подписка =====================

    /// <summary>Подписки провайдеров - основная, запасная и сколько угодно ещё.</summary>
    public System.Collections.ObjectModel.ObservableCollection<Subscription> Subscriptions => Settings.Subscriptions;

    public bool HasSubscriptions => Subscriptions.Count > 0;

    /// <summary>Подписок больше одной - есть смысл в кнопке «Обновить все».</summary>
    public bool HasManySubscriptions => Subscriptions.Count > 1;

    private void RaiseSubscriptions()
    {
        Raise(nameof(HasSubscriptions));
        Raise(nameof(HasManySubscriptions));
    }

    public ICommand RemoveSubscription { get; }

    public AppSettings Settings => Shell.Settings;

    /// <summary>Текст в поле импорта по ссылке.</summary>
    public string LinkText
    {
        get;
        set => Set(ref field, value);
    } = "";

    /// <summary>Идёт долгая операция: проверка серверов или загрузка подписки.</summary>
    public bool IsBusy
    {
        get;
        private set => Set(ref field, value);
    }

    public ICommand ImportLink { get; }
    public ICommand PasteLink { get; }
    public ICommand ImportFile { get; }
    public ICommand Create { get; }
    public ICommand EditProfileCommand { get; }
    public ICommand RemoveProfile { get; }
    public ICommand SelectProfile { get; }
    public ICommand ExportProfile { get; }
    public ICommand ExportAll { get; }
    public ICommand PingAll { get; }
    public ICommand UpdateSubscription { get; }

    /// <summary>Единое поле «ссылка на сервер или подписку» - кнопка «Добавить».</summary>
    public ICommand AddFromText { get; }

    /// <summary>Сканировать QR-код камерой - на телефоне.</summary>
    public ICommand ScanQr { get; }

    public bool CanScanQr => Shell.Platform.QrScanner.IsSupported;

    /// <summary>
    /// Обновление подписки при запуске - не чаще раза в сутки. Вызывается
    /// оболочкой: страница может не открыться ни разу за сеанс, а список
    /// серверов должен быть свежим.
    /// </summary>
    public async Task StartupRefreshAsync()
    {
        if (Subscriptions.Count == 0) return;

        // Пауза, чтобы не соперничать со стартом за сеть
        await Task.Delay(TimeSpan.FromSeconds(8));

        if (Settings.AutoUpdateSubscription)
        {
            var stale = Subscriptions
                .Where(s => s.Updated is null || DateTimeOffset.Now - s.Updated.Value >= TimeSpan.FromDays(1))
                .ToList();

            if (stale.Count > 0) await UpdateSubscriptionsAsync(stale);
        }

        // Срок и трафик проверяем при каждом запуске - даже без обновления,
        // по последним известным данным: кончиться подписка может и без сети
        WarnAboutSubscriptions();
    }

    /// <summary>
    /// Предупреждение о подписке, которой осталось меньше трёх дней или 10%
    /// трафика: лучше узнать заранее, чем когда VPN молча перестанет работать.
    /// </summary>
    private void WarnAboutSubscriptions()
    {
        var problem = Subscriptions.FirstOrDefault(s => s.NeedsAttention);
        if (problem is null) return;

        Shell.Notify(problem.IsExpired
            ? Loc.T("Sub_WarnExpired", problem.Title)
            : problem.IsExpiringSoon
                ? Loc.T("Sub_WarnExpiring", problem.Title, Math.Max(0, problem.DaysLeft))
                : Loc.T("Sub_WarnQuota", problem.Title, problem.QuotaLeftLabel));
    }

    /// <summary>
    /// При открытии списка меряем непроверенные профили: прочерки вместо чисел
    /// читаются как поломка, а не как «данных пока нет».
    /// </summary>
    public override void Activate() => _ = EnsureLatencyAsync();

    // ===================== импорт =====================

    private void Import()
    {
        if (!LinkParser.TryParse(LinkText, out var profile, out var error))
        {
            Shell.Notify(error);
            return;
        }

        Profiles.Add(profile);
        Shell.Active ??= profile;
        LinkText = "";

        Shell.Notify(Loc.T("Profiles_Added", profile.Name));
        Shell.Persist();
    }

    /// <summary>
    /// Импорт из буфера обмена одним нажатием - главное на телефоне, где вставлять
    /// в поле неудобно. Ссылки по одной в строке добавляются все разом; одиночный
    /// http(s)-адрес считается ссылкой подписки.
    /// </summary>
    private async Task PasteAsync()
    {
        string? text;
        try
        {
            text = await Shell.Platform.Clipboard.GetTextAsync();
        }
        catch (Exception)
        {
            text = null;
        }

        await AddTextAsync(text ?? "", Loc.T("Profiles_ClipboardEmpty"));
    }

    /// <summary>Кнопка «Добавить» у поля: поле очищается, только если что-то добавилось.</summary>
    /// <summary>
    /// В QR-коде то же, что вставляют текстом: ссылка на сервер, несколько ссылок
    /// или ссылка подписки - разбор общий с буфером обмена.
    /// </summary>
    private async Task ScanQrAsync()
    {
        string? text;
        try
        {
            text = await Shell.Platform.QrScanner.ScanAsync();
        }
        catch (Exception ex)
        {
            Shell.Notify(Loc.T("Profiles_QrFailed", ex.Message));
            return;
        }

        // Сканер закрыли - ничего не добавляем и не ругаемся
        if (text is null) return;

        await AddTextAsync(text, Loc.T("Profiles_QrEmpty"));
    }

    private async Task AddFromFieldAsync()
    {
        if (await AddTextAsync(LinkText, Loc.T("Profiles_EnterLink"))) LinkText = "";
    }

    /// <summary>
    /// Общий разбор вставленного текста - для буфера и для поля. Одиночный
    /// http(s)-адрес - ссылка подписки, иначе ссылки на серверы по одной в строке.
    /// Возвращает true, если что-то добавилось.
    /// </summary>
    private async Task<bool> AddTextAsync(string text, string emptyMessage)
    {
        var lines = text.Split(['\r', '\n'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (lines.Length == 0)
        {
            Shell.Notify(emptyMessage);
            return false;
        }

        if (lines.Length == 1 && IsSubscriptionUrl(lines[0]))
        {
            // Уже есть такая - просто обновляем, вторую копию не заводим
            var url = lines[0];
            var subscription = Subscriptions.FirstOrDefault(s => s.Url == url);
            if (subscription is null)
            {
                subscription = new Subscription { Url = url };
                Subscriptions.Add(subscription);
            }

            await UpdateSubscriptionsAsync([subscription]);
            return true;
        }

        var added = new List<ServerProfile>();
        var lastError = "";

        foreach (var line in lines)
        {
            if (LinkParser.TryParse(line, out var profile, out var error)) added.Add(profile);
            else lastError = error;
        }

        if (added.Count == 0)
        {
            Shell.Notify(lastError.Length > 0 ? lastError : emptyMessage);
            return false;
        }

        foreach (var profile in added) Profiles.Add(profile);
        Shell.Active ??= added[0];
        Shell.Persist();

        Shell.Notify(added.Count == 1
            ? Loc.T("Profiles_Added", added[0].Name)
            : Loc.T("Profiles_AddedMany", added.Count));

        // Сразу меряем задержку новых: прочерк вместо числа выглядит как поломка
        _ = EnsureLatencyAsync();
        return true;
    }

    internal static bool IsSubscriptionUrl(string text) =>
        text.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
        || text.StartsWith("http://", StringComparison.OrdinalIgnoreCase);

    /// <summary>Принимает конфиг sing-box, массив outbound'ов или одиночный объект.</summary>
    private async Task ImportFromFileAsync()
    {
        var file = await Shell.Platform.Files.OpenAsync(
            Loc.T("Profiles_PickConfig"),
            FileFilter.ParseWpf(Loc.T("Profiles_ConfigFilter")));

        if (file is null) return;

        // Через поток, а не по пути: на Android у выбранного файла пути нет
        string json;
        try
        {
            json = await file.ReadAllTextAsync();
        }
        catch (Exception ex)
        {
            Shell.Notify(Loc.T("Import_ReadFailed", ex.Message));
            return;
        }

        if (!ConfigImporter.TryImport(json, out var imported, out var error))
        {
            Shell.Notify(error);
            return;
        }

        foreach (var profile in imported) Profiles.Add(profile);

        Shell.Active ??= Profiles.FirstOrDefault();

        Shell.Notify(imported.Count == 1
            ? Loc.T("Profiles_Added", imported[0].Name)
            : Loc.T("Profiles_AddedMany", imported.Count));

        Shell.Persist();
    }

    // ===================== создание и правка =====================

    private async Task EditAsync(ServerProfile? existing)
    {
        var result = await Shell.Platform.Dialogs.EditProfileAsync(existing);
        if (result is null) return;

        if (existing is null)
        {
            Profiles.Add(result);
            Shell.Active ??= result;
            Shell.Notify(Loc.T("Profiles_Created", result.Name));
        }
        else
        {
            // Заменяем на месте, чтобы не терять позицию в списке
            var index = Profiles.IndexOf(existing);
            if (index >= 0) Profiles[index] = result;

            if (ReferenceEquals(Shell.Active, existing)) Shell.Active = result;
            Shell.Notify(Loc.T("Profiles_Updated", result.Name));
        }

        Shell.Persist();
    }

    private void Delete(ServerProfile profile)
    {
        Profiles.Remove(profile);

        if (ReferenceEquals(Shell.Active, profile)) Shell.Active = Profiles.FirstOrDefault();

        Shell.Persist();
    }

    // ===================== экспорт =====================

    private async Task ShowExportAsync(ServerProfile profile)
    {
        var link = ProfileLink.Build(profile);

        if (link.Length == 0)
        {
            Shell.Notify(Loc.T("Profiles_NoLinkForProtocol"));
            return;
        }

        await Shell.Platform.Dialogs.ShowExportAsync(
            link, profile.Name, $"{profile.ProtocolLabel} · {profile.Endpoint}");
    }

    /// <summary>Весь список одной строкой подписки - её можно скормить другому клиенту.</summary>
    private async Task ShowExportAllAsync()
    {
        var payload = ProfileLink.BuildSubscription(Profiles);

        await Shell.Platform.Dialogs.ShowExportAsync(
            payload, Loc.T("Profiles_AllTitle"), Loc.T("Profiles_AllSubtitle", Profiles.Count));
    }

    // ===================== проверка и подписка =====================

    private async Task EnsureLatencyAsync()
    {
        if (IsBusy) return;
        if (Profiles.All(p => p.LatencyMs >= 0)) return;

        await PingAsync(onlyUnknown: true);
    }

    /// <summary>
    /// Замер идёт напрямую по TCP, а не через ядро: так это работает и без
    /// подключения, и сразу для всего списка.
    /// </summary>
    private async Task PingAsync(bool onlyUnknown = false)
    {
        var targets = onlyUnknown
            ? Profiles.Where(p => p.LatencyMs < 0).ToList()
            : Profiles.ToList();

        if (targets.Count == 0) return;

        IsBusy = true;
        Shell.Notify(Loc.T("Profiles_Checking"));

        try
        {
            var probes = targets.Select(async profile =>
            {
                var ms = await LatencyProbe.MeasureAsync(profile.Host, profile.Port);
                Shell.Dispatch(() => profile.LatencyMs = ms);
            });

            await Task.WhenAll(probes);

            var alive = targets.Count(p => p.LatencyMs >= 0);
            Shell.Notify(Loc.T("Profiles_Responded", alive, targets.Count));
        }
        finally
        {
            IsBusy = false;

            // Новые замеры меняют порядок «сначала быстрые»
            if (SortFastest) Shell.Dispatch(Refresh);
        }
    }

    /// <summary>
    /// Обновляет профили из подписок. У каждой заменяются только её серверы:
    /// созданные вручную и пришедшие из других подписок остаются на месте.
    /// </summary>
    private async Task UpdateSubscriptionsAsync(IReadOnlyList<Subscription> targets)
    {
        if (targets.Count == 0) return;

        IsBusy = true;
        Shell.Notify(Loc.T("Profiles_LoadingSubscription"));

        try
        {
            var activeName = Shell.Active?.Name;
            var loaded = 0;
            var lastError = "";

            foreach (var subscription in targets)
            {
                var (fetched, error, info) = await SubscriptionService.FetchAsync(subscription.Url);

                // Тариф показываем, даже если серверов нет: у закончившейся подписки
                // панель часто отдаёт пустой список - и как раз тогда важно видеть срок
                if (info != SubscriptionInfo.Empty) subscription.Apply(info);

                if (error.Length > 0)
                {
                    lastError = $"{subscription.Title}: {error}";
                    continue;
                }

                if (info == SubscriptionInfo.Empty) subscription.Apply(info);

                foreach (var stale in Profiles.Where(p => p.Subscription == subscription.Url).ToList())
                    Profiles.Remove(stale);

                foreach (var profile in fetched) Profiles.Add(profile);
                loaded += fetched.Count;
            }

            // Возвращаем выбор на сервер с тем же именем, если он ещё есть
            Shell.Active = Profiles.FirstOrDefault(p => p.Name == activeName) ?? Profiles.FirstOrDefault();
            Shell.Persist();

            Shell.Notify(lastError.Length > 0 ? lastError : Loc.T("Profiles_SubscriptionLoaded", loaded));
            if (lastError.Length == 0) WarnAboutSubscriptions();
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Подписка удаляется вместе со своими серверами - после вопроса.</summary>
    private async Task RemoveSubscriptionAsync(Subscription subscription)
    {
        var count = Profiles.Count(p => p.Subscription == subscription.Url);

        if (!await Shell.Platform.Dialogs.ConfirmAsync(Loc.T("Sub_RemoveConfirm", subscription.Title, count)))
            return;

        foreach (var profile in Profiles.Where(p => p.Subscription == subscription.Url).ToList())
            Profiles.Remove(profile);

        Subscriptions.Remove(subscription);

        if (Shell.Active is null || !Profiles.Contains(Shell.Active)) Shell.Active = Profiles.FirstOrDefault();
        Shell.Persist();

        Shell.Notify(Loc.T("Sub_Removed", subscription.Title));
    }
}