using Android.Runtime;
using Avalonia;
using Avalonia.Android;
using CVPN.Services;

namespace CVPN.Android;

[global::Android.App.Application]
public class Application : AvaloniaAndroidApplication<App>
{
    protected Application(nint javaReference, JniHandleOwnership transfer) : base(javaReference, transfer)
    {
    }

    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
    {
        // Данные - во внутреннем хранилище приложения, бинарники - в nativeLibraryDir:
        // только оттуда Android разрешает запускать исполняемые файлы
        AppPaths.Configure(
            dataDir: FilesDir?.AbsolutePath,
            binariesDir: ApplicationInfo?.NativeLibraryDir);

        App.ConfigurePlatform = AndroidPlatform.Configure;

        return base.CustomizeAppBuilder(builder).WithInterFont();
    }
}
