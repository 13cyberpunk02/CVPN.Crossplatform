using CVPN.Services;

namespace CVPN.Tests;

/// <summary>
/// Битый profiles.json. Раньше приложение молча начинало с пустого набора, и
/// первое же сохранение затирало файл - профили пропадали насовсем.
/// </summary>
// Последовательно с SecretTests: Load сбрасывает статический Secret.FailureCount
[Collection("SecretState")]
public class ProfileStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "cvpn-store-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly string _previousDataDir = AppPaths.DataDir;

    public ProfileStoreTests()
    {
        Directory.CreateDirectory(_dir);
        AppPaths.Configure(dataDir: _dir);
    }

    public void Dispose()
    {
        AppPaths.Configure(dataDir: _previousDataDir);
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch
        {
            /* временный каталог */
        }
    }

    [Fact]
    public void Битый_файл_откладывается_а_не_затирается()
    {
        const string broken = """{ "Profiles": [ { "Name": "Мой сервер", "Host": "C:\stale" } ] }""";
        File.WriteAllText(AppPaths.ProfilesFile, broken);

        var state = ProfileStore.Load();

        // Запуск не сорван - набор по умолчанию
        Assert.NotEmpty(state.RoutingProfiles);

        // Исходный файл цел - в отложенной копии
        var backup = Assert.Single(Directory.GetFiles(_dir, "profiles.json.broken-*"));
        Assert.Equal(broken, File.ReadAllText(backup));

        // Человек узнает, где копия
        Assert.Contains(backup, ProfileStore.LastLoadWarning);

        // Сохранение пишет новый файл, а копию не трогает
        ProfileStore.Save(state);
        Assert.Equal(broken, File.ReadAllText(backup));
    }
}
