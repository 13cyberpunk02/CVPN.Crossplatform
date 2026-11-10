namespace CVPN.Services;

/// <summary>Все пути приложения в одном месте, чтобы не плодить Path.Combine по коду.</summary>
public static class AppPaths
{
    /// <summary>
    /// Каталог данных. На Windows - %AppData%\CVPN, на Linux - ~/.config/CVPN,
    /// на Android - внутренний каталог files приложения.
    /// </summary>
    public static string DataDir { get; private set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CVPN");

    /// <summary>
    /// Каталог с исполняемыми файлами ядра и ByeDPI. На десктопе - рядом с приложением.
    /// На Android исполнять можно только то, что лежит в nativeLibraryDir,
    /// поэтому голова Android переопределяет его при запуске.
    /// </summary>
    public static string BinariesDir { get; private set; } = AppContext.BaseDirectory;

    /// <summary>Переопределение путей платформой. Вызывается один раз, до первого обращения.</summary>
    public static void Configure(string? dataDir = null, string? binariesDir = null)
    {
        if (!string.IsNullOrEmpty(dataDir)) DataDir = dataDir;
        if (!string.IsNullOrEmpty(binariesDir)) BinariesDir = binariesDir;
    }

    private static string Exe(string name) => OperatingSystem.IsWindows() ? name + ".exe" : name;

    public static string ProfilesFile => Path.Combine(DataDir, "profiles.json");
    public static string SettingsFile => Path.Combine(DataDir, "settings.json");

    /// <summary>Конфиг перегенерируется при каждом запуске - правим только в UI, не руками.</summary>
    public static string GeneratedConfig => Path.Combine(DataDir, "config.json");

    public static string CacheFile => Path.Combine(DataDir, "cache.db");

    /// <summary>Сюда копируются локальные .srs, чтобы профиль не зависел от исходного пути.</summary>
    public static string RulesDir => Path.Combine(DataDir, "rules");

    /// <summary>
    /// Ядро sing-box. На Android бинарник упакован как libsingbox.so - иначе
    /// установщик не распакует его в каталог, из которого разрешён запуск.
    /// </summary>
    public static string DefaultCorePath => OperatingSystem.IsAndroid()
        ? Path.Combine(BinariesDir, "libsingbox.so")
        : Path.Combine(BinariesDir, "core", Exe("sing-box"));

    /// <summary>ciadpi из поставки: на десктопе лежит в byedpi, на Android - libciadpi.so.</summary>
    public static string BundledByeDpiPath => OperatingSystem.IsAndroid()
        ? Path.Combine(BinariesDir, "libciadpi.so")
        : Path.Combine(BinariesDir, "byedpi", Exe("ciadpi"));

    /// <summary>Путь к поставленному с приложением ciadpi или пустая строка, если его нет.</summary>
    public static string DetectByeDpiPath() =>
        File.Exists(BundledByeDpiPath) ? BundledByeDpiPath : "";

    public static void EnsureCreated()
    {
        Directory.CreateDirectory(DataDir);
        Directory.CreateDirectory(RulesDir);
    }
}
