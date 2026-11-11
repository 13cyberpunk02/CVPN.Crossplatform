using Android.Content;
using AndroidX.Core.Content;
using CVPN.Localization;
using CVPN.Platform;
using CVPN.Services;

namespace CVPN.Android;

/// <summary>
/// Самообновление на Android: APK скачивается в кэш приложения и отдаётся
/// системному установщику. Ставить молча Android не даёт - пользователь
/// подтверждает установку в системном окне. APK подписан тем же ключом,
/// поэтому встаёт поверх с сохранением профилей.
/// </summary>
internal sealed class AndroidUpdater : IAppUpdater
{
    /// <summary>Совпадает с android:authorities провайдера в AndroidManifest.xml.</summary>
    private const string Authority = "com.cyberpunk.cvpn.updates";

    private static Context Context => global::Android.App.Application.Context;

    // Путь совпадает с cache-path в Resources/xml/update_paths.xml
    public string DownloadDirectory => Path.Combine(Context.CacheDir!.AbsolutePath, "updates");

    public ReleaseAsset? PickAsset(ReleaseInfo release) => release.FindAsset("-android-arm64.apk");

    public Task<string> InstallAsync(string file)
    {
        try
        {
            // С Android 8 ставить APK можно только с разрешения «Установка неизвестных приложений»
            if (OperatingSystem.IsAndroidVersionAtLeast(26) && Context.PackageManager?.CanRequestPackageInstalls() != true)
            {
                var settings = new Intent(
                    global::Android.Provider.Settings.ActionManageUnknownAppSources,
                    global::Android.Net.Uri.Parse($"package:{Context.PackageName}"));
                settings.AddFlags(ActivityFlags.NewTask);
                Context.StartActivity(settings);

                return Task.FromResult(Loc.T("Update_AllowInstall"));
            }

            var uri = FileProvider.GetUriForFile(Context, Authority, new Java.IO.File(file));

            var intent = new Intent(Intent.ActionView);
            intent.SetDataAndType(uri, "application/vnd.android.package-archive");
            intent.AddFlags(ActivityFlags.GrantReadUriPermission | ActivityFlags.NewTask);
            Context.StartActivity(intent);

            return Task.FromResult("");
        }
        catch (Exception ex)
        {
            return Task.FromResult(Loc.T("Update_InstallFailed", ex.Message));
        }
    }
}
