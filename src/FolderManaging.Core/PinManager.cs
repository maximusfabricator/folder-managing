using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using FolderManaging.Core.Diagnostics;

namespace FolderManaging.Core;

/// <summary>Pin / unpin a folder in Quick Access via Shell.Application verbs.</summary>
public static class PinManager
{
    const string PinVerb = "pintohome";
    const string UnpinVerb = "unpinfromhome";

    public static void PinToQuickAccess(string folderPath) =>
        RunSta(() => InvokePinUnpin(Path.GetFullPath(folderPath), pin: true));

    public static void UnpinFromQuickAccess(string folderPath) =>
        RunSta(() => InvokePinUnpin(Path.GetFullPath(folderPath), pin: false));

    static void RunSta(Action action)
    {
        Exception? caught = null;
        var t = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                caught = ex;
            }
        });
        t.SetApartmentState(ApartmentState.STA);
        t.IsBackground = true;
        t.Start();
        t.Join();
        if (caught != null)
            ExceptionDispatchInfo.Capture(caught).Throw();
    }

    /// <summary>Shell.Application does not accept extended-length <c>\\?\</c> prefixes; strip them for UNC/drive paths.</summary>
    static string NormalizeFolderPathForShell(string fullFolderPath)
    {
        var p = Path.GetFullPath(fullFolderPath);
        if (p.StartsWith(@"\\?\", StringComparison.Ordinal))
        {
            if (p.StartsWith(@"\\?\UNC\", StringComparison.Ordinal))
                return @"\\" + p.Substring(8);
            return p.Length > 4 ? p.Substring(4) : p;
        }

        return p.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    static void InvokePinUnpin(string fullFolderPath, bool pin)
    {
        if (string.IsNullOrWhiteSpace(fullFolderPath))
            throw new ArgumentException("Folder path is required.", nameof(fullFolderPath));
        if (!Directory.Exists(fullFolderPath))
            throw new DirectoryNotFoundException(fullFolderPath);

        var pathShell = NormalizeFolderPathForShell(fullFolderPath);
        var verb = pin ? PinVerb : UnpinVerb;

        try
        {
            InvokeShellFolderVerbDynamic(pathShell, verb);
            TraceLog.Append("shell-menu.log", $"[CORE] PinManager OK dynamic verb={verb} path={pathShell}");
            return;
        }
        catch (Exception ex)
        {
            TraceLog.Append("shell-menu.log", $"[CORE] PinManager dynamic failed verb={verb} path={pathShell}: {ex}");
        }

        try
        {
            InvokeFolderVerbDispatch(pathShell, verb);
            TraceLog.Append("shell-menu.log", $"[CORE] PinManager OK reflection verb={verb} path={pathShell}");
            return;
        }
        catch (Exception ex)
        {
            TraceLog.Append("shell-menu.log", $"[CORE] PinManager reflection failed verb={verb} path={pathShell}: {ex}");
        }

        if (TryInvokeShellVerbByScan(pathShell, pin))
        {
            TraceLog.Append("shell-menu.log", $"[CORE] PinManager OK verb-scan pin={pin} path={pathShell}");
            return;
        }

        throw new InvalidOperationException(
            $"Shell did not accept pin/unpin for '{pathShell}'. Tried '{verb}', reflection InvokeVerb, and verb-menu scan.");
    }

    static void InvokeShellFolderVerbDynamic(string pathShell, string verb)
    {
        var shellType = Type.GetTypeFromProgID("Shell.Application")
            ?? throw new InvalidOperationException("Shell.Application is not available.");

        var shellObj = Activator.CreateInstance(shellType)
            ?? throw new InvalidOperationException("Could not create Shell.Application.");

        try
        {
            dynamic shell = shellObj;
            dynamic folderDyn = shell.NameSpace(pathShell);
            if (folderDyn is null)
                throw new InvalidOperationException("Shell.NameSpace returned null.");

            object folderObj = folderDyn;
            try
            {
                dynamic selfDyn = folderDyn.Self;
                if (selfDyn is null)
                    throw new InvalidOperationException("Folder.Self returned null.");

                object selfObj = selfDyn;
                try
                {
                    selfDyn.InvokeVerb(verb);
                }
                finally
                {
                    Marshal.FinalReleaseComObject(selfObj);
                }
            }
            finally
            {
                Marshal.FinalReleaseComObject(folderObj);
            }
        }
        finally
        {
            Marshal.FinalReleaseComObject(shellObj);
        }
    }

    static bool TryInvokeShellVerbByScan(string pathShell, bool pin)
    {
        var shellType = Type.GetTypeFromProgID("Shell.Application")
            ?? throw new InvalidOperationException("Shell.Application is not available.");
        var shell = Activator.CreateInstance(shellType)
            ?? throw new InvalidOperationException("Could not create Shell.Application.");

        object? folder = null;
        object? self = null;
        try
        {
            folder = shellType.InvokeMember(
                "NameSpace",
                System.Reflection.BindingFlags.InvokeMethod,
                null,
                shell,
                new object[] { pathShell });
            if (folder == null)
                return false;

            var folderType = folder.GetType();
            self = folderType.InvokeMember(
                "Self",
                System.Reflection.BindingFlags.GetProperty,
                null,
                folder,
                null);
            if (self == null)
                return false;

            dynamic dSelf = self;
            dynamic verbs = dSelf.Verbs;
            var count = (int)verbs.Count;
            for (var i = 1; i <= count; i++)
            {
                dynamic verbObj = verbs.Item(i);
                var name = (string)verbObj.Name;
                if (!MatchesPinIntent(name, pin))
                    continue;
                verbObj.DoIt();
                return true;
            }

            return false;
        }
        finally
        {
            if (self != null)
                Marshal.FinalReleaseComObject(self);
            if (folder != null)
                Marshal.FinalReleaseComObject(folder);
            if (shell != null)
                Marshal.FinalReleaseComObject(shell);
        }
    }

    static bool MatchesPinIntent(string verbName, bool pin)
    {
        if (string.IsNullOrWhiteSpace(verbName))
            return false;
        var n = verbName.AsSpan();
        if (pin)
        {
            return n.Contains("Pin", StringComparison.OrdinalIgnoreCase)
                   && (n.Contains("Quick", StringComparison.OrdinalIgnoreCase)
                       || n.Contains("Home", StringComparison.OrdinalIgnoreCase)
                       || n.Contains("Быстр", StringComparison.OrdinalIgnoreCase)
                       || n.Contains("Закреп", StringComparison.OrdinalIgnoreCase));
        }

        return n.Contains("Unpin", StringComparison.OrdinalIgnoreCase)
               || n.Contains("Откреп", StringComparison.OrdinalIgnoreCase)
               || n.Contains("Изъять", StringComparison.OrdinalIgnoreCase);
    }

    static void InvokeFolderVerbDispatch(string pathShell, string verb)
    {
        var shellType = Type.GetTypeFromProgID("Shell.Application")
            ?? throw new InvalidOperationException("Shell.Application is not available.");
        var shell = Activator.CreateInstance(shellType)
            ?? throw new InvalidOperationException("Could not create Shell.Application.");

        object? folder = null;
        object? self = null;
        try
        {
            folder = shellType.InvokeMember(
                "NameSpace",
                System.Reflection.BindingFlags.InvokeMethod,
                null,
                shell,
                new object[] { pathShell });
            if (folder == null)
                throw new InvalidOperationException("Shell could not open the folder (NameSpace returned null).");

            var folderType = folder.GetType();
            self = folderType.InvokeMember(
                "Self",
                System.Reflection.BindingFlags.GetProperty,
                null,
                folder,
                null);
            if (self == null)
                throw new InvalidOperationException("FolderItem.Self returned null.");

            var selfType = self.GetType();
            selfType.InvokeMember(
                "InvokeVerb",
                System.Reflection.BindingFlags.InvokeMethod,
                null,
                self,
                new object[] { verb });
        }
        finally
        {
            if (self != null)
                Marshal.FinalReleaseComObject(self);
            if (folder != null)
                Marshal.FinalReleaseComObject(folder);
            if (shell != null)
                Marshal.FinalReleaseComObject(shell);
        }
    }
}
