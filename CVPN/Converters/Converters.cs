using System.Collections.Concurrent;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace CVPN.Converters;

/// <summary>
/// Значение равно параметру - по строковому представлению. Заменяет DataTrigger
/// из WPF: Classes.direct="{Binding Action, Converter={StaticResource Eq}, ConverterParameter=Direct}".
/// </summary>
public sealed class EqualsConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        string.Equals(value?.ToString(), parameter?.ToString(), StringComparison.OrdinalIgnoreCase);

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// Код страны → картинка флага из Assets/Flags. Флаги картинками, потому что
/// флаги-эмодзи рисует не каждая ОС (Windows - нет). Нет файла - null, и Image
/// просто ничего не показывает.
/// </summary>
public sealed class FlagConverter : IValueConverter
{
    private static readonly ConcurrentDictionary<string, Bitmap?> Cache = new(StringComparer.OrdinalIgnoreCase);

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var code = value as string;
        if (string.IsNullOrWhiteSpace(code) || code.Length != 2) return null;

        return Cache.GetOrAdd(code, Load);
    }

    private static Bitmap? Load(string code)
    {
        var uri = new Uri($"avares://CVPN/Assets/Flags/{code.ToLowerInvariant()}.png");

        try
        {
            return AssetLoader.Exists(uri) ? new Bitmap(AssetLoader.Open(uri)) : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
