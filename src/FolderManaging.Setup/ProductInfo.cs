using System.Reflection;

namespace FolderManaging.Setup;

internal static class ProductInfo
{
    internal const string DisplayName = "Folder Managing";
    internal const string Publisher = "Folder Managing";

    /// <summary>Must match Identity/@Name in sparse\AppxManifest.User.xml.</summary>
    internal const string PackageName = "FolderManaging";

    /// <summary>Developer package from scripts\sparse — registers the same COM class, so it is removed on install.</summary>
    internal const string DevPackageName = "FolderManagingSparseDev";

    /// <summary>Subject of the release signing certificate (Identity/@Publisher).</summary>
    internal const string CertificateSubject = "CN=Folder Managing";

    internal const string UninstallKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\FolderManaging";
    internal const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    internal const string RunValueName = "FolderManaging";

    internal const string MaintenanceExeName = "FolderManaging.Setup.exe";
    internal const string IconFileName = "FolderManaging.ico";
    internal const string ShortcutFileName = "Folder Managing.lnk";

    internal static readonly string[] AppProcessNames = ["FolderManaging.Companion", "FolderManaging.Cli"];

    internal static string Version =>
        typeof(ProductInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
        ?? "1.0.0";

    internal static string InstallDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "FolderManaging");

    internal static string MaintenanceExePath => Path.Combine(InstallDirectory, MaintenanceExeName);

    /// <summary>App execution alias declared in the package manifest; the only way to start package exes by path.</summary>
    internal static string CompanionAliasPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WindowsApps", "foldermanaging.exe");

    internal static string StartMenuShortcutPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), ShortcutFileName);

    internal static string DesktopShortcutPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), ShortcutFileName);

    internal static string ProgramDataDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "FolderManaging");

    internal static string LocalDataDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FolderManaging");

    internal static string ProfileDataDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".FolderManaging");
}
