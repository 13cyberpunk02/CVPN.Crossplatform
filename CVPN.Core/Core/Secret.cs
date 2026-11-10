using System.Security.Cryptography;
using System.Text;

namespace CVPN.Core;

/// <summary>
/// Шифрование учётных данных в файлах профилей.
///
/// На Windows - DPAPI: ключ принадлежит учётной записи, мастер-пароль не нужен,
/// и файлы старого WPF-клиента читаются как есть (метка "dpapi:").
/// На Linux и Android DPAPI нет, поэтому там AES-GCM с ключом из
/// <see cref="KeyProvider"/>: по умолчанию - файл с правами только для владельца,
/// на Android голова приложения подменяет его ключом из Android Keystore.
/// </summary>
public static class Secret
{
    private const string DpapiPrefix = "dpapi:";
    private const string AesPrefix = "aes:";
    private const int NonceSize = 12;
    private const int TagSize = 16;

    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("CVPN.profiles.v1");

    private static byte[]? _aesKey;

    /// <summary>
    /// Источник 256-битного ключа для AES-GCM. Задаётся платформой при запуске;
    /// по умолчанию ключ хранится в файле secret.key в каталоге данных.
    /// </summary>
    public static Func<byte[]> KeyProvider { get; set; } = FileKey.LoadOrCreate;

    /// <summary>Сколько значений не удалось расшифровать с момента запуска.</summary>
    public static int FailureCount { get; private set; }

    public static string Protect(string? plain)
    {
        if (string.IsNullOrEmpty(plain)) return "";

        try
        {
            if (OperatingSystem.IsWindows())
            {
                var bytes = ProtectedData.Protect(
                    Encoding.UTF8.GetBytes(plain), Entropy, DataProtectionScope.CurrentUser);
                return DpapiPrefix + Convert.ToBase64String(bytes);
            }

            return AesPrefix + AesEncrypt(Encoding.UTF8.GetBytes(plain));
        }
        catch (Exception)
        {
            // Шифрование недоступно - лучше сохранить как есть, чем потерять профиль
            return plain;
        }
    }

    /// <summary>
    /// Расшифровывает значение. Строку без метки возвращает как есть - так
    /// читаются файлы, созданные до появления шифрования.
    /// </summary>
    public static string Unprotect(string? stored)
    {
        if (string.IsNullOrEmpty(stored)) return "";

        try
        {
            if (stored.StartsWith(DpapiPrefix, StringComparison.Ordinal))
            {
                if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();

                var bytes = ProtectedData.Unprotect(
                    Convert.FromBase64String(stored[DpapiPrefix.Length..]), Entropy, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(bytes);
            }

            if (stored.StartsWith(AesPrefix, StringComparison.Ordinal))
                return Encoding.UTF8.GetString(AesDecrypt(stored[AesPrefix.Length..]));

            return stored;
        }
        catch (Exception)
        {
            // Файл перенесли с другой машины, из другой учётной записи или с другой ОС.
            // Пустое значение честнее исключения: профиль останется в списке,
            // а пользователь увидит, что данные нужно ввести заново.
            FailureCount++;
            return "";
        }
    }

    /// <summary>Уже зашифровано - чтобы не шифровать дважды.</summary>
    public static bool IsProtected(string? value) =>
        value is not null &&
        (value.StartsWith(DpapiPrefix, StringComparison.Ordinal) ||
         value.StartsWith(AesPrefix, StringComparison.Ordinal));

    public static void ResetFailures() => FailureCount = 0;

    // Формат: base64(nonce[12] | tag[16] | ciphertext)
    private static string AesEncrypt(byte[] plain)
    {
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var tag = new byte[TagSize];
        var cipher = new byte[plain.Length];

        using (var aes = new AesGcm(Key, TagSize))
            aes.Encrypt(nonce, plain, cipher, tag, Entropy);

        return Convert.ToBase64String([.. nonce, .. tag, .. cipher]);
    }

    private static byte[] AesDecrypt(string encoded)
    {
        var data = Convert.FromBase64String(encoded);

        var nonce = data.AsSpan(0, NonceSize);
        var tag = data.AsSpan(NonceSize, TagSize);
        var cipher = data.AsSpan(NonceSize + TagSize);
        var plain = new byte[cipher.Length];

        using (var aes = new AesGcm(Key, TagSize))
            aes.Decrypt(nonce, cipher, tag, plain, Entropy);

        return plain;
    }

    private static byte[] Key => _aesKey ??= KeyProvider();

    /// <summary>Ключ в файле, доступном только владельцу. Вариант по умолчанию для Linux.</summary>
    private static class FileKey
    {
        public static byte[] LoadOrCreate()
        {
            var path = Path.Combine(Services.AppPaths.DataDir, "secret.key");

            if (File.Exists(path))
            {
                var existing = File.ReadAllBytes(path);
                if (existing.Length == 32) return existing;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var key = RandomNumberGenerator.GetBytes(32);

            var options = new FileStreamOptions { Mode = FileMode.Create, Access = FileAccess.Write };
            if (!OperatingSystem.IsWindows())
                options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;

            using (var stream = new FileStream(path, options))
                stream.Write(key);

            return key;
        }
    }
}
