namespace CVPN.ViewModels;

/// <summary>Пункт меню: ключ, подпись и страница. Разметку по типу страницы подбирает UI.</summary>
public sealed record NavItem(string Key, string Title, object Page);
