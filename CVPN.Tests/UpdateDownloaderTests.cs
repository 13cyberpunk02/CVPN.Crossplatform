using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CVPN.Services;

namespace CVPN.Tests;

[Collection("Localization")]
public class UpdateDownloaderTests
{
    [Fact]
    public void Файлы_релиза_разбираются_вместе_с_суммой_от_GitHub()
    {
        using var doc = JsonDocument.Parse("""
            {
              "assets": [
                { "name": "CVPN-1.2.0-setup.exe", "browser_download_url": "https://x/setup.exe", "size": 42,
                  "digest": "sha256:ABCDEF0123456789abcdef0123456789abcdef0123456789abcdef0123456789" },
                { "name": "CVPN-1.2.0-android-arm64.apk", "browser_download_url": "https://x/app.apk", "size": 7 },
                { "name": "", "browser_download_url": "https://x/empty" }
              ]
            }
            """);

        var assets = UpdateChecker.ParseAssets(doc.RootElement);

        Assert.Equal(2, assets.Count);
        Assert.Equal("abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789", assets[0].Sha256);
        Assert.Equal(42, assets[0].Size);
        Assert.Null(assets[1].Sha256);
    }

    [Fact]
    public void Файл_для_платформы_ищется_по_окончанию_имени()
    {
        var release = Release(
            new ReleaseAsset("CVPN-1.2.0-setup.exe", "u1", 1, null),
            new ReleaseAsset("CVPN-1.2.0-x86_64.AppImage", "u2", 1, null),
            new ReleaseAsset("cvpn_1.2.0_amd64.deb", "u3", 1, null));

        Assert.Equal("u1", release.FindAsset("-setup.exe")?.Url);
        Assert.Equal("u2", release.FindAsset("-x86_64.appimage")?.Url);
        Assert.Equal("u3", release.FindAsset("_amd64.deb")?.Url);
        Assert.Null(release.FindAsset("-android-arm64.apk"));
    }

    [Fact]
    public void Сумма_находится_в_формате_sha256sum()
    {
        var hash = new string('a', 64);
        var list = $"{new string('b', 64)}  other.zip\n{hash} *CVPN-1.2.0-setup.exe\r\n";

        Assert.Equal(hash, UpdateDownloader.FindChecksum(list, "CVPN-1.2.0-setup.exe"));
        Assert.Null(UpdateDownloader.FindChecksum(list, "missing.apk"));
        Assert.Null(UpdateDownloader.FindChecksum("short  CVPN-1.2.0-setup.exe", "CVPN-1.2.0-setup.exe"));
    }

    [Fact]
    public async Task Скачанный_файл_с_верной_суммой_сохраняется()
    {
        var payload = Encoding.UTF8.GetBytes("new version payload");
        await using var server = await TinyServer.StartAsync(payload);
        var dir = TempDir();

        var asset = new ReleaseAsset("CVPN-9.9.9-setup.exe", server.Url, payload.Length, Sha(payload));
        var path = await UpdateDownloader.DownloadAsync(Release(asset), asset, dir, null, CancellationToken.None);

        Assert.Equal(payload, await File.ReadAllBytesAsync(path));
        Assert.False(File.Exists(path + ".part"));
    }

    /// <summary>Подменённый файл не должен остаться на диске - его могли бы запустить.</summary>
    [Fact]
    public async Task Файл_с_чужой_суммой_отвергается_и_удаляется()
    {
        var payload = Encoding.UTF8.GetBytes("tampered");
        await using var server = await TinyServer.StartAsync(payload);
        var dir = TempDir();

        var asset = new ReleaseAsset("CVPN-9.9.9-setup.exe", server.Url, payload.Length, new string('0', 64));

        await Assert.ThrowsAsync<UpdateException>(() =>
            UpdateDownloader.DownloadAsync(Release(asset), asset, dir, null, CancellationToken.None));

        Assert.Empty(Directory.GetFiles(dir));
    }

    [Fact]
    public async Task Без_суммы_файл_не_скачивается()
    {
        var asset = new ReleaseAsset("CVPN-9.9.9-setup.exe", "http://127.0.0.1:1/never", 1, null);

        await Assert.ThrowsAsync<UpdateException>(() =>
            UpdateDownloader.DownloadAsync(Release(asset), asset, TempDir(), null, CancellationToken.None));
    }

    private static ReleaseInfo Release(params ReleaseAsset[] assets) => new("9.9.9", "https://x", "", assets);

    private static string Sha(byte[] data) => Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

    private static string TempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "cvpn-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>Минимальный HTTP-сервер на один ответ: без HttpListener, которому на Windows нужны права.</summary>
    private sealed class TinyServer : IAsyncDisposable
    {
        private readonly TcpListener _listener;
        private readonly Task _loop;

        private TinyServer(TcpListener listener, byte[] body)
        {
            _listener = listener;
            _loop = ServeAsync(body);
        }

        public string Url => $"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/file";

        public static Task<TinyServer> StartAsync(byte[] body)
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            return Task.FromResult(new TinyServer(listener, body));
        }

        private async Task ServeAsync(byte[] body)
        {
            try
            {
                using var client = await _listener.AcceptTcpClientAsync();
                await using var stream = client.GetStream();

                // Заголовки запроса дочитываем до пустой строки
                var buffer = new byte[4096];
                var request = new StringBuilder();
                while (!request.ToString().Contains("\r\n\r\n"))
                {
                    var read = await stream.ReadAsync(buffer);
                    if (read == 0) return;
                    request.Append(Encoding.ASCII.GetString(buffer, 0, read));
                }

                var head = Encoding.ASCII.GetBytes(
                    $"HTTP/1.1 200 OK\r\nContent-Length: {body.Length}\r\nContent-Type: application/octet-stream\r\nConnection: close\r\n\r\n");
                await stream.WriteAsync(head);
                await stream.WriteAsync(body);
            }
            catch (Exception)
            {
                // Тест сам проверит, что получил
            }
        }

        public async ValueTask DisposeAsync()
        {
            _listener.Stop();
            await _loop;
        }
    }
}
