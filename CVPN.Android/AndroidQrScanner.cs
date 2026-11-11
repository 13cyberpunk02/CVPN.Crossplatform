using Android.Gms.Common;
using Android.Gms.Tasks;
using CVPN.Localization;
using CVPN.Platform;
using Xamarin.Google.MLKit.Vision.Barcode.Common;
using Xamarin.Google.MLKit.Vision.CodeScanner;

namespace CVPN.Android;

/// <summary>
/// Сканер QR-кода из сервисов Google Play (Google Code Scanner). Окно камеры
/// рисуют сами сервисы: приложению не нужно разрешение на камеру, и кадры
/// с камеры к нему не попадают - приходит только распознанный текст.
/// </summary>
internal sealed class AndroidQrScanner : IQrScanner
{
    public bool IsSupported => true;

    public Task<string?> ScanAsync()
    {
        var activity = MainActivity.Current
                       ?? throw new InvalidOperationException("no foreground activity");

        // Без сервисов Google сканер не откроется - скажем, что делать вместо него
        if (GoogleApiAvailability.Instance.IsGooglePlayServicesAvailable(activity) != ConnectionResult.Success)
            throw new InvalidOperationException(Loc.T("Profiles_QrNoServices"));

        var options = new GmsBarcodeScannerOptions.Builder()
            .SetBarcodeFormats(Barcode.FormatQrCode)
            .EnableAutoZoom()
            .Build();

        var scanner = GmsBarcodeScanning.GetClient(activity, options);
        var result = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);

        scanner.StartScan()
            .AddOnSuccessListener(new Success(value => result.TrySetResult((value as Barcode)?.RawValue)))
            .AddOnCanceledListener(new Canceled(() => result.TrySetResult(null)))
            .AddOnFailureListener(new Failure(error =>
                result.TrySetException(new InvalidOperationException(error.Message ?? error.ToString()))));

        return result.Task;
    }

    private sealed class Success(Action<Java.Lang.Object?> action) : Java.Lang.Object, IOnSuccessListener
    {
        public void OnSuccess(Java.Lang.Object? value) => action(value);
    }

    private sealed class Canceled(Action action) : Java.Lang.Object, IOnCanceledListener
    {
        public void OnCanceled() => action();
    }

    private sealed class Failure(Action<Java.Lang.Exception> action) : Java.Lang.Object, IOnFailureListener
    {
        public void OnFailure(Java.Lang.Exception error) => action(error);
    }
}
