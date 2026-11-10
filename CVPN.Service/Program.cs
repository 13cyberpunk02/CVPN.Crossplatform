using CVPN.Service;
using CVPN.Shared;

// Служба туннеля: держит ядро sing-box с правами на TUN, чтобы приложению
// не нужно было повышение при каждом запуске.
//   Windows - служба CVPNTunnel под LocalSystem;
//   Linux   - юнит systemd cvpn-tunnel.service под root.

var dataDir = OperatingSystem.IsWindows()
    ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "CVPN")
    : "/var/lib/cvpn";

var exe = OperatingSystem.IsWindows() ? "sing-box.exe" : "sing-box";

// Ядро ищем рядом со службой и уровнем выше: в установке служба лежит в service/,
// а ядро - в core/ рядом с приложением
var corePath = new[]
                   {
                       Path.Combine(AppContext.BaseDirectory, "core", exe),
                       Path.Combine(AppContext.BaseDirectory, "..", "core", exe)
                   }
                   .Select(Path.GetFullPath)
                   .FirstOrDefault(File.Exists)
               ?? Path.Combine(AppContext.BaseDirectory, "core", exe);

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddWindowsService(options => options.ServiceName = IpcContract.ServiceName);
builder.Services.AddSystemd();
builder.Services.AddSingleton<CoreRunner>();
builder.Services.AddSingleton(new ServiceOptions(corePath, dataDir));
builder.Services.AddHostedService<TunnelWorker>();

await builder.Build().RunAsync();
