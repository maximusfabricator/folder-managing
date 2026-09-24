using Microsoft.Win32;

namespace FolderManaging.Cli.Shell;

/// <summary>Registers static Explorer context menu verbs under HKCU\Software\Classes (per-user, no elevation).</summary>
public static class ContextMenuRegistry
{
    private const string ParentVerbName = "FolderManaging";

    public static void Install(string cliExecutablePath)
    {
        if (string.IsNullOrWhiteSpace(cliExecutablePath))
        {
            throw new ArgumentException("CLI path is required.", nameof(cliExecutablePath));
        }

        if (!File.Exists(cliExecutablePath))
        {
            throw new FileNotFoundException("CLI executable not found.", cliExecutablePath);
        }

        var trimmed = cliExecutablePath.Trim('"');
        var quotedExe = $"\"{trimmed}\"";

        Uninstall();

        RegisterDirectoryTree(@"Directory\shell", quotedExe, pathToken: "%1");
        RegisterDirectoryTree(@"Directory\Background\shell", quotedExe, pathToken: "%V");
    }

    public static void Uninstall()
    {
        TryDeleteVerbTree(@"Directory\shell", ParentVerbName);
        TryDeleteVerbTree(@"Directory\Background\shell", ParentVerbName);
    }

    private static void RegisterDirectoryTree(string shellRelativePath, string quotedExe, string pathToken)
    {
        var exePath = trimmedFromQuoted(quotedExe);
        using var shell = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{shellRelativePath}", writable: true)
                          ?? throw new InvalidOperationException($"Cannot open or create: HKCU\\Software\\Classes\\{shellRelativePath}");

        using var parent = shell.CreateSubKey(ParentVerbName, writable: true)
                           ?? throw new InvalidOperationException("Cannot create parent verb.");

        parent.SetValue("MUIVerb", "Folder Manager");
        parent.SetValue("Icon", $"{exePath},0");

        using var subShell = parent.CreateSubKey("shell", writable: true)
                             ?? throw new InvalidOperationException("Cannot create shell subkey.");

        var pathArg = $"\"{pathToken}\"";
        CreateChildVerb(subShell, "ColorCoral", "Color — Coral", $"{quotedExe} --action color --value coral --path {pathArg}");
        CreateChildVerb(subShell, "ColorOcean", "Color — Ocean", $"{quotedExe} --action color --value ocean --path {pathArg}");
        CreateChildVerb(subShell, "ColorMint", "Color — Mint", $"{quotedExe} --action color --value mint --path {pathArg}");
        CreateChildVerb(subShell, "ColorPlum", "Color — Plum", $"{quotedExe} --action color --value plum --path {pathArg}");
        CreateChildVerb(subShell, "ResetColor", "Restore default icon", $"{quotedExe} --action reset --path {pathArg}");
        CreateChildVerb(subShell, "AddTag", "Add tag…", $"{quotedExe} --action tag --tag QuickTag --path {pathArg}");
        CreateChildVerb(subShell, "Pin", "Pin folder", $"{quotedExe} --action pin --path {pathArg}");
        CreateChildVerb(subShell, "Unpin", "Unpin folder", $"{quotedExe} --action unpin --path {pathArg}");
    }

    private static string trimmedFromQuoted(string quotedExe) => quotedExe.Trim('"');

    private static void CreateChildVerb(RegistryKey subShell, string keyName, string label, string commandLine)
    {
        using var verb = subShell.CreateSubKey(keyName, writable: true)
                         ?? throw new InvalidOperationException($"Cannot create verb {keyName}.");
        verb.SetValue("MUIVerb", label);

        using var command = verb.CreateSubKey("command", writable: true)
                            ?? throw new InvalidOperationException($"Cannot create command for {keyName}.");
        command.SetValue(null, commandLine);
    }

    private static void TryDeleteVerbTree(string shellRelativePath, string verbName)
    {
        try
        {
            using var shell = Registry.CurrentUser.OpenSubKey($@"Software\Classes\{shellRelativePath}", writable: true);
            shell?.DeleteSubKeyTree(verbName, throwOnMissingSubKey: false);
        }
        catch (IOException)
        {
            // Explorer may briefly lock keys; best-effort uninstall.
        }
    }
}
