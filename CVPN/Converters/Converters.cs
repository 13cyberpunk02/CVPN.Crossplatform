using System.Collections.Concurrent;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
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
/// Ключ → иконка из Theme/Icons.axaml: "routing" → ресурс Icon.routing.
/// Так пункты меню получают иконки без знания о них в CVPN.Core.
/// </summary>
public sealed class IconConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string key && Application.Current?.TryFindResource($"Icon.{key}", out var icon) == true
            ? icon
            : null;

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

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Get(value as string);

    /// <summary>Флаг по коду страны - и для разметки, и для меню трея.</summary>
    public static Bitmap? Get(string? code) =>
        string.IsNullOrWhiteSpace(code) || code.Length != 2 ? null : Cache.GetOrAdd(code, Load);

    private static Bitmap? Load(string code)
    {
        var uri = new Uri($"avares://CVPN.UI/Assets/Flags/{code.ToLowerInvariant()}.png");

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

/// <summary>
/// Число → ширина колонки в звёздах: доли полоски «прокси / напрямую / ByeDPI»
/// задаются прямо количеством соединений.
/// </summary>
public sealed class StarConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        new GridLength(System.Convert.ToDouble(value ?? 0, CultureInfo.InvariantCulture), GridUnitType.Star);

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
