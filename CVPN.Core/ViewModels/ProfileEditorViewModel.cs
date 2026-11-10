using CVPN.Core;
using CVPN.Localization;
using CVPN.Models;
using CVPN.Models.Enums;
using CVPN.Services;

namespace CVPN.ViewModels;

/// <summary>
/// Редактор профиля. Работает с черновиком-копией: отмена ничего не портит,
/// а сохранение отдаёт новый объект, который страница ставит на место старого.
/// Раньше эта логика жила в code-behind окна WPF - теперь её используют и окно
/// на десктопе, и страница поверх приложения на телефоне.
/// </summary>
public sealed class ProfileEditorViewModel : ObservableObject
{
    private static readonly string[] Modes = ["auto", "packet-up", "stream-up", "stream-one"];
    private static readonly string[] Fingerprints = ["", .. TlsFingerprint.Known];

    private string _error = "";

    public ProfileEditorViewModel(ServerProfile? existing)
    {
        Draft = Clone(existing);
        Header = existing is null ? Loc.T("Editor_New") : Loc.T("Editor_Edit");
    }

    public ServerProfile Draft { get; }

    public string Header { get; }

    // Порядок совпадает с ProtocolKind
    public IReadOnlyList<string> ProtocolOptions { get; } =
        ["VLESS · Reality", "VLESS · WebSocket + TLS", "VLESS · XHTTP", "AnyTLS", "NaiveProxy"];

    public int ProtocolIndex
    {
        get => (int)Draft.Protocol;
        set
        {
            if (value < 0 || value == (int)Draft.Protocol) return;

            var fingerprint = FingerprintIndex;
            Draft.Protocol = (ProtocolKind)value;

            Raise();
            RaiseGroups();

            // Подпись первого пункта отпечатка зависит от протокола
            Raise(nameof(FingerprintOptions));
            FingerprintIndex = fingerprint;
        }
    }

    public IReadOnlyList<string> ModeOptions => Modes;

    public int ModeIndex
    {
        get => Math.Max(0, Array.IndexOf(Modes, Draft.Mode));
        set
        {
            if (value >= 0) Draft.Mode = Modes[value];
            Raise();
        }
    }

    /// <summary>Первый пункт - «по умолчанию»: для VLESS это chrome, для AnyTLS uTLS не включается.</summary>
    public IReadOnlyList<string> FingerprintOptions =>
    [
        Draft.Protocol == ProtocolKind.AnyTls
            ? Loc.T("Editor_FingerprintNone")
            : Loc.T("Editor_FingerprintDefault", TlsFingerprint.VlessDefault),
        .. Fingerprints.Skip(1)
    ];

    public int FingerprintIndex
    {
        get => Math.Max(0, Array.IndexOf(Fingerprints, TlsFingerprint.Normalize(Draft.Fingerprint)));
        set
        {
            if (value >= 0) Draft.Fingerprint = Fingerprints[value];
            Raise();
        }
    }

    // ===================== какие поля показывать =====================

    private ProtocolKind Kind => Draft.Protocol;

    public bool ShowVless => Kind is ProtocolKind.VlessReality or ProtocolKind.VlessWs or ProtocolKind.VlessXhttp;

    /// <summary>
    /// Reality необязателен для XHTTP: он работает и поверх обычного TLS,
    /// поэтому поля ключа показываем, но пустыми их оставить можно.
    /// </summary>
    public bool ShowReality => Kind is ProtocolKind.VlessReality or ProtocolKind.VlessXhttp;

    /// <summary>Naive идёт через Cronet: отпечаток там задаёт сам Chromium, uTLS не применяется.</summary>
    public bool ShowFingerprint => Kind is not ProtocolKind.Naive;

    public bool ShowWs => Kind is ProtocolKind.VlessWs;
    public bool ShowXhttp => Kind is ProtocolKind.VlessXhttp;
    public bool ShowUser => Kind is ProtocolKind.Naive;
    public bool ShowPassword => Kind is ProtocolKind.AnyTls or ProtocolKind.Naive;

    private void RaiseGroups()
    {
        Raise(nameof(ShowVless));
        Raise(nameof(ShowReality));
        Raise(nameof(ShowFingerprint));
        Raise(nameof(ShowWs));
        Raise(nameof(ShowXhttp));
        Raise(nameof(ShowUser));
        Raise(nameof(ShowPassword));
    }

    // ===================== сохранение =====================

    public string Error
    {
        get => _error;
        private set
        {
            Set(ref _error, value);
            Raise(nameof(HasError));
        }
    }

    public bool HasError => Error.Length > 0;

    /// <summary>Проверяет черновик и дозаполняет очевидное. false - ошибка уже в <see cref="Error"/>.</summary>
    public bool TrySave()
    {
        if (!Validate(out var error))
        {
            Error = error;
            return false;
        }

        if (string.IsNullOrWhiteSpace(Draft.Sni)) Draft.Sni = Draft.Host;
        if (string.IsNullOrWhiteSpace(Draft.Name)) Draft.Name = Draft.Host;

        Error = "";
        return true;
    }

    private bool Validate(out string error)
    {
        error = "";

        if (string.IsNullOrWhiteSpace(Draft.Host))
            error = Loc.T("Editor_NeedHost");
        else if (Draft.Port is < 1 or > 65535)
            error = Loc.T("Editor_BadPort");
        else if (ShowVless && !Guid.TryParse(Draft.Uuid, out _))
            error = Loc.T("Editor_BadUuid");
        else if (Kind is ProtocolKind.VlessReality && string.IsNullOrWhiteSpace(Draft.PublicKey))
            error = Loc.T("Editor_NeedPublicKey");
        else if (Kind is ProtocolKind.AnyTls && string.IsNullOrWhiteSpace(Draft.Password))
            error = Loc.T("Editor_NeedPassword");
        else if (Kind is ProtocolKind.Naive
                 && (string.IsNullOrWhiteSpace(Draft.Username) || string.IsNullOrWhiteSpace(Draft.Password)))
            error = Loc.T("Editor_NeedCredentials");

        return error.Length == 0;
    }

    private static ServerProfile Clone(ServerProfile? p) => p is null
        ? new ServerProfile { Port = 443, Flow = "xtls-rprx-vision", Path = "/" }
        : new ServerProfile
        {
            Name = p.Name,
            Host = p.Host,
            Port = p.Port,
            Protocol = p.Protocol,
            Uuid = p.Uuid,
            Password = p.Password,
            Sni = p.Sni,
            PublicKey = p.PublicKey,
            ShortId = p.ShortId,
            Flow = p.Flow,
            Path = p.Path,
            HttpHost = p.HttpHost,
            Mode = p.Mode,
            Fingerprint = p.Fingerprint,
            Username = p.Username,
            CountryCode = p.CountryCode
        };
}
