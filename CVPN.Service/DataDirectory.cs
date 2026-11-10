using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;

namespace CVPN.Service;

/// <summary>
/// Каталог данных службы закрыт от обычных пользователей: в config.json
/// учётные данные лежат открытым текстом.
/// Windows - ACL только SYSTEM и администраторы; Linux - права 0700 у root.
/// </summary>
public static class DataDirectory
{
    public static void Prepare(string path)
    {
        Directory.CreateDirectory(path);
        Directory.CreateDirectory(Path.Combine(path, "rules"));

        try
        {
            if (OperatingSystem.IsWindows()) RestrictWindows(path);
            else File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        catch (Exception)
        {
            // Не удалось выставить права - служба всё равно должна работать.
        }
    }

    [SupportedOSPlatform("windows")]
    private static void RestrictWindows(string path)
    {
        var info = new DirectoryInfo(path);
        var security = info.GetAccessControl();

        // Рвём наследование и выбрасываем унаследованные разрешения:
        // именно среди них лежит чтение для группы «Пользователи»
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);

        foreach (FileSystemAccessRule rule in security.GetAccessRules(true, true, typeof(SecurityIdentifier)))
            security.RemoveAccessRule(rule);

        Allow(security, WellKnownSidType.LocalSystemSid);
        Allow(security, WellKnownSidType.BuiltinAdministratorsSid);

        info.SetAccessControl(security);
    }

    [SupportedOSPlatform("windows")]
    private static void Allow(DirectorySecurity security, WellKnownSidType sid)
    {
        security.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(sid, null),
            FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None,
            AccessControlType.Allow));
    }
}
