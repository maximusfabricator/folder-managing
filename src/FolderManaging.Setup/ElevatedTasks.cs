// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 maximusfabricator

using System.Diagnostics;
using System.Security.AccessControl;
using System.Security.Cryptography.X509Certificates;
using System.Security.Principal;
using FolderManaging.Core;
using FolderManaging.Core.Localization;
using FolderManaging.Core.ShellProperties;

namespace FolderManaging.Setup;

/// <summary>
/// Steps that need Administrator, run in a child process started with <c>runas</c> (one UAC prompt per install/uninstall).
/// Usage: <c>--elevated install|uninstall &lt;workDir&gt;</c>. Errors go to <c>workDir\elevated-error.txt</c>.
/// </summary>
internal static class ElevatedTasks
{
    internal const string Switch = "--elevated";
    internal const string ErrorFileName = "elevated-error.txt";
    internal const string WarningsFileName = "elevated-warnings.txt";

    /// <summary>Paths (one per line) whose color reset failed without admin rights during uninstall.</summary>
    internal const string ProtectedFoldersFileName = "protected-folders.txt";

    internal static int Run(string[] args)
    {
        if (args.Length < 3)
            return 64;

        var task = args[1];
        var workDir = args[2];
        var warnings = new List<string>();
        try
        {
            SetupLog.Write($"[elevated] {task} workDir={workDir}");
            switch (task)
            {
                case "install":
                    Install(workDir, warnings);
                    break;
                case "uninstall":
                    Uninstall(workDir, warnings);
                    break;
                default:
                    return 64;
            }

            if (warnings.Count > 0)
                File.WriteAllLines(Path.Combine(workDir, WarningsFileName), warnings);
            return 0;
        }
        catch (Exception ex)
        {
            SetupLog.Write($"[elevated] FAILED: {ex}");
            try
            {
                File.WriteAllText(Path.Combine(workDir, ErrorFileName), ex.Message);
            }
            catch
            {
                // parent reports a generic error
            }

            return 1;
        }
    }

    static void Install(string workDir, List<string> warnings)
    {
        // The elevated admin worker (FolderManaging.Cli --elevated-worker) cannot be stopped from the user process.
        SystemTools.StopProcesses(ProductInfo.AppProcessNames);

        if (!SystemTools.IsDesktopRuntimeInstalled())
            InstallDesktopRuntime(Path.Combine(workDir, Payload.RuntimeInstallerFileName));

        TrustCertificate(Path.Combine(workDir, Payload.CertificateFileName));

        try
        {
            PropertySchemaRegistration.Register(Path.Combine(workDir, Payload.PropdescFileName));
        }
        catch (Exception ex)
        {
            SetupLog.Write($"[elevated] property schema: {ex}");
            warnings.Add(UiText.Format("ColumnRegisterFail", ex.Message));
        }

        try
        {
            CreateSharedIconStore();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            // folder icons stay per user, as in 2.0
            SetupLog.Write($"[elevated] shared icon store: {ex}");
        }
    }

    /// <summary>
    /// <c>%ProgramData%\FolderManaging\Icons</c>: every account reads the folder icons and adds its own,
    /// but may change or delete only the files it created (the same rule as <c>%ProgramData%</c> itself).
    /// </summary>
    static void CreateSharedIconStore()
    {
        var dir = Path.Combine(ProductInfo.ProgramDataDirectory, "Icons");
        Directory.CreateDirectory(Path.Combine(dir, "Folders"));

        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.SetOwner(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null));
        const InheritanceFlags all = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
        security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            FileSystemRights.FullControl, all, PropagationFlags.None, AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
            FileSystemRights.FullControl, all, PropagationFlags.None, AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.CreatorOwnerSid, null),
            FileSystemRights.FullControl, all, PropagationFlags.InheritOnly, AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null),
            FileSystemRights.ReadAndExecute, all, PropagationFlags.None, AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null),
            FileSystemRights.CreateFiles | FileSystemRights.CreateDirectories, InheritanceFlags.ContainerInherit,
            PropagationFlags.None, AccessControlType.Allow));
        new DirectoryInfo(dir).SetAccessControl(security);
        foreach (var sub in Directory.EnumerateDirectories(dir, "*", SearchOption.AllDirectories))
            ResetToInherited(sub);
        SetupLog.Write($"[elevated] shared icon store ready: {dir}");
    }

    /// <summary>Keeps the shared icons while folders of other accounts on this PC still show them.</summary>
    static void DeleteProgramData()
    {
        var root = ProductInfo.ProgramDataDirectory;
        var icons = Path.Combine(root, "Icons");
        bool iconsInUse;
        try
        {
            iconsInUse = Directory.Exists(icons) && Directory.EnumerateFiles(icons, "*.ico", SearchOption.AllDirectories).Any();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            iconsInUse = true;
        }

        if (!iconsInUse)
        {
            SystemTools.TryDeleteDirectory(root);
            return;
        }

        SetupLog.Write("[elevated] keeping shared icons used by other accounts");
        try
        {
            foreach (var dir in Directory.EnumerateDirectories(root))
            {
                if (!string.Equals(Path.GetFileName(dir), "Icons", StringComparison.OrdinalIgnoreCase))
                    SystemTools.TryDeleteDirectory(dir);
            }

            foreach (var file in Directory.EnumerateFiles(root))
                File.Delete(file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SetupLog.Write($"[elevated] ProgramData cleanup: {ex.Message}");
        }
    }

    static void ResetToInherited(string path)
    {
        var info = new DirectoryInfo(path);
        var security = info.GetAccessControl();
        security.SetAccessRuleProtection(isProtected: false, preserveInheritance: false);
        foreach (FileSystemAccessRule rule in security.GetAccessRules(includeExplicit: true, includeInherited: false, typeof(SecurityIdentifier)))
            security.RemoveAccessRuleSpecific(rule);
        info.SetAccessControl(security);
    }

    static void Uninstall(string workDir, List<string> warnings)
    {
        SystemTools.StopProcesses(ProductInfo.AppProcessNames);

        var protectedList = Path.Combine(workDir, ProtectedFoldersFileName);
        if (File.Exists(protectedList))
        {
            foreach (var folder in File.ReadAllLines(protectedList).Where(l => l.Length > 0))
            {
                if (!FolderIconManager.TryRemoveCustomization(folder, out var message))
                    warnings.Add($"{folder}: {message}");
            }
        }

        PropertySchemaRegistration.Unregister();
        DeleteProgramData();
        RemoveTrustedCertificates();
    }

    static void InstallDesktopRuntime(string installerPath)
    {
        if (!File.Exists(installerPath))
            throw new FileNotFoundException(UiText.Get("MissingDotnet"), installerPath);

        SetupLog.Write("[elevated] installing .NET Desktop Runtime 10");
        using var p = Process.Start(new ProcessStartInfo(installerPath, "/install /quiet /norestart")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        }) ?? throw new InvalidOperationException(UiText.Get("DotnetStartFail"));
        p.WaitForExit();
        SetupLog.Write($"[elevated] runtime installer exit {p.ExitCode}");

        // 3010/1641: installed, reboot requested; 1638: a newer version is already present.
        if (p.ExitCode is not (0 or 3010 or 1641 or 1638))
            throw new InvalidOperationException(UiText.Format("DotnetExitCode", p.ExitCode));
    }

    /// <summary>Sideloaded MSIX must chain to a certificate in LocalMachine\TrustedPeople.</summary>
    static void TrustCertificate(string cerPath)
    {
        using var cert = X509CertificateLoader.LoadCertificateFromFile(cerPath);
        using var store = new X509Store(StoreName.TrustedPeople, StoreLocation.LocalMachine);
        store.Open(OpenFlags.ReadWrite);
        var existing = store.Certificates.Find(X509FindType.FindByThumbprint, cert.Thumbprint, validOnly: false);
        if (existing.Count == 0)
        {
            store.Add(cert);
            SetupLog.Write($"[elevated] trusted certificate {cert.Thumbprint}");
        }
    }

    static void RemoveTrustedCertificates()
    {
        using var store = new X509Store(StoreName.TrustedPeople, StoreLocation.LocalMachine);
        store.Open(OpenFlags.ReadWrite);
        foreach (var cert in store.Certificates.Find(X509FindType.FindBySubjectDistinguishedName, ProductInfo.CertificateSubject, validOnly: false))
        {
            store.Remove(cert);
            SetupLog.Write($"[elevated] removed certificate {cert.Thumbprint}");
            cert.Dispose();
        }
    }
}
