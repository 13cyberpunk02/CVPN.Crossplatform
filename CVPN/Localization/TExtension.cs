using Avalonia.Markup.Xaml;

namespace CVPN.Localization;

/// <summary>
/// Разметочное расширение: <c>Text="{loc:T Logs_Title}"</c>.
///
/// Язык фиксируется при запуске, поэтому значение вычисляется один раз -
/// без подписок и уведомлений. Смена языка на лету потребовала бы пересборки
/// всего визуального дерева и того не стоит.
/// </summary>
public sealed class TExtension : MarkupExtension
{
    public TExtension()
    {
    }

    public TExtension(string key) => Key = key;

    public string Key { get; set; } = "";

    public override object ProvideValue(IServiceProvider serviceProvider) => Loc.T(Key);
}
