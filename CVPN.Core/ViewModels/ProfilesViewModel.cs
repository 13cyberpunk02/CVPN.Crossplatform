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
        UpdateSubscription = new RelayCommand(async () => await UpdateSubscriptionAsync(), () => !IsBusy);
        AddFromText = new RelayCommand(async () => await AddFromFieldAsync(), () => !IsBusy);
        ScanQr = new RelayCommand(async () => await ScanQrAsync(), () => !IsBusy && CanScanQr);
        ToggleSort = new RelayCommand(() => SortFastest = !SortFastest);

        Profiles.CollectionChanged += (_, _) => Refresh();
        Settings.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(AppSettings.SubscriptionUrl) or nameof(AppSettings.SubscriptionUpdated))
                RaiseSubscription();
        };
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

    public bool HasSubscription => !string.IsNullOrWhiteSpace(Settings.SubscriptionUrl);

    /// <summary>Адрес подписки без схемы и хвоста - целиком он длинный и ничего не говорит.</summary>
    public string SubscriptionLabel =>
        Uri.TryCreate(Settings.SubscriptionUrl, UriKind.Absolute, out var uri) ? uri.Host : Settings.SubscriptionUrl;

    public string SubscriptionUpdatedLabel => Settings.SubscriptionUpdated is { } at
        ? Loc.T("Profiles_SubscriptionUpdatedAt", at.ToLocalTime().ToString("g", Loc.Culture))
        : Loc.T("Profiles_SubscriptionNever");

    private void RaiseSubscription()
    {
        Raise(nameof(HasSubscription));
        Raise(nameof(SubscriptionLabel));
        Raise(nameof(SubscriptionUpdatedLabel));
    }

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
        if (!Settings.AutoUpdateSubscription) return;
        if (string.IsNullOrWhiteSpace(Settings.SubscriptionUrl)) return;

        var last = Settings.SubscriptionUpdated;
        if (last is not null && DateTimeOffset.Now - last.Value < TimeSpan.FromDays(1)) return;

        // Пауза, чтобы не соперничать со стартом за сеть
        await Task.Delay(TimeSpan.FromSeconds(8));
        await UpdateSubscriptionAsync();
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
            Settings.SubscriptionUrl = lines[0];
            await UpdateSubscriptionAsync();
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
    /// Обновляет профили из подписки. Заменяются только пришедшие из неё:
    /// созданные вручную остаются на месте.
    /// </summary>
    private async Task UpdateSubscriptionAsync()
    {
        IsBusy = true;
        Shell.Notify(Loc.T("Profiles_LoadingSubscription"));

        try
        {
            var (fetched, error) = await SubscriptionService.FetchAsync(Settings.SubscriptionUrl);

            if (error.Length > 0)
            {
                Shell.Notify(error);
                return;
            }

            var activeName = Shell.Active?.Name;

            foreach (var stale in Profiles.Where(p => p.Subscription == Settings.SubscriptionUrl).ToList())
                Profiles.Remove(stale);

            foreach (var profile in fetched) Profiles.Add(profile);

            // Возвращаем выбор на сервер с тем же именем, если он ещё есть
            Shell.Active = Profiles.FirstOrDefault(p => p.Name == activeName) ?? Profiles.FirstOrDefault();

            Settings.SubscriptionUpdated = DateTimeOffset.Now;

            Shell.Notify(Loc.T("Profiles_SubscriptionLoaded", fetched.Count));
            Shell.Persist();
        }
        finally
        {
            IsBusy = false;
        }
    }
}