// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 maximusfabricator

using System.Runtime.InteropServices;
using FolderManaging.Core.Diagnostics;
using FolderManaging.Core.Models;
using FolderManaging.Core.QuickAccess;
using FolderManaging.Core.Storage;

namespace FolderManaging.Core;

/// <summary>Reasons a colored or tagged folder can look plain in Explorer. The names are string-table keys.</summary>
public enum FolderIssue
{
    /// <summary>Folder deleted, renamed outside our view, or its drive is not connected.</summary>
    FolderMissing,

    /// <summary>Windows does not let this account read the folder.</summary>
    NoAccess,

    /// <summary>Our <c>desktop.ini</c> is gone or was replaced.</summary>
    DesktopIniMissing,

    /// <summary>The <c>.ico</c> named in <c>desktop.ini</c> does not exist.</summary>
    IconMissing,

    /// <summary>The folder lacks the Read-only/System attribute Explorer needs to read <c>desktop.ini</c>.</summary>
    AttributeMissing,

    /// <summary><c>desktop.ini</c> or the icon carries the "downloaded from the internet" mark, which Windows ignores.</summary>
    DownloadMark,

    /// <summary>Network folder in a zone Windows does not trust, so it ignores <c>desktop.ini</c> there.</summary>
    UntrustedNetwork,

    /// <summary>The icon is in the per-user store, so other accounts on this PC see a plain folder.</summary>
    IconNotShared,

    /// <summary>Quick access keeps an old picture of the folder from before it was colored.</summary>
    StalePin,
}

public sealed record FolderCheck(string Path, IReadOnlyList<FolderIssue> Issues)
{
    public bool IsHealthy => Issues.Count == 0;
}

/// <summary>"Why is the color not visible?" checks and the matching repairs.</summary>
public static class FolderDiagnostics
{
    const string ZoneStream = ":Zone.Identifier";

    /// <summary>Issues that only advice can solve (no button fixes a missing drive).</summary>
    public static bool IsRepairable(FolderIssue issue) =>
        issue is not (FolderIssue.FolderMissing or FolderIssue.NoAccess or FolderIssue.UntrustedNetwork);

    public static IReadOnlyList<FolderCheck> CheckAll(AppStateDocument doc)
    {
        IReadOnlyList<QuickAccessPin> pins = QuickAccessService.GetPinned();
        return doc.Folders
            .Where(StateMigration.HasCustomization)
            .Select(f => Check(f.Path, pins))
            .ToList();
    }

    public static FolderCheck Check(string folderPath, IReadOnlyList<QuickAccessPin>? pins = null)
    {
        var issues = new List<FolderIssue>();
        string full;
        try
        {
            full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folderPath));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return new FolderCheck(folderPath, [FolderIssue.FolderMissing]);
        }

        if (IsUntrustedNetworkPath(full))
            issues.Add(FolderIssue.UntrustedNetwork);

        FileAttributes attributes;
        try
        {
            attributes = File.GetAttributes(full);
            if (!attributes.HasFlag(FileAttributes.Directory))
                return new FolderCheck(full, [FolderIssue.FolderMissing]);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException or IOException)
        {
            issues.Insert(0, FolderIssue.FolderMissing);
            return new FolderCheck(full, issues);
        }
        catch (UnauthorizedAccessException)
        {
            issues.Insert(0, FolderIssue.NoAccess);
            return new FolderCheck(full, issues);
        }

        var iniPath = Path.Combine(full, "desktop.ini");
        try
        {
            if (!File.Exists(iniPath) || !FolderIconManager.IsOurDesktopIni(iniPath))
            {
                issues.Add(FolderIssue.DesktopIniMissing);
            }
            else
            {
                if ((attributes & (FileAttributes.ReadOnly | FileAttributes.System)) == 0)
                    issues.Add(FolderIssue.AttributeMissing);
                var icon = FolderIconManager.TryReadIconPath(iniPath);
                if (icon is not null && !File.Exists(icon))
                    issues.Add(FolderIssue.IconMissing);
                if (HasDownloadMark(iniPath) || (icon is not null && HasDownloadMark(icon)))
                    issues.Add(FolderIssue.DownloadMark);
                if (icon is not null && File.Exists(icon) && !PerFolderIconCache.IsInCurrentStore(icon) &&
                    IsUnder(icon, AppPaths.PerUserFolderIconsDirectory))
                    issues.Add(FolderIssue.IconNotShared);
            }
        }
        catch (UnauthorizedAccessException)
        {
            issues.Add(FolderIssue.NoAccess);
        }
        catch (IOException ex)
        {
            TraceLog.Append("shell-menu.log", $"[DIAG] '{full}': {ex.Message}");
        }

        pins ??= QuickAccessService.GetPinned();
        var pin = pins.FirstOrDefault(p =>
            string.Equals(PinRefresher.NormalizePath(p.Path), full, StringComparison.OrdinalIgnoreCase));
        if (pin is { IsPlainFolder: true } && QuickAccessService.ReadSnapshot(full) is { } live && PinRefresher.IsStale(pin, live))
            issues.Add(FolderIssue.StalePin);

        return new FolderCheck(full, issues);
    }

    /// <summary>
    /// Fixes what can be fixed. Folders that need admin rights go through <see cref="FolderBatch"/> (one UAC prompt).
    /// </summary>
    public static FolderBatchResult Repair(IReadOnlyList<FolderCheck> checks, Func<IReadOnlyList<string>, bool> confirmElevation)
    {
        var actions = new List<FolderAction>();
        foreach (var check in checks)
        {
            if (check.Issues.Contains(FolderIssue.DownloadMark))
                actions.Add(new FolderAction(FolderActionKind.Sync, check.Path, "unblock"));
            if (check.Issues.Any(i => i is FolderIssue.DesktopIniMissing or FolderIssue.IconMissing
                    or FolderIssue.AttributeMissing or FolderIssue.IconNotShared or FolderIssue.DownloadMark))
                actions.Add(new FolderAction(FolderActionKind.Sync, check.Path, "force"));
        }

        var result = FolderBatch.Run(actions, confirmElevation, RepairHost.Instance);
        if (checks.Any(c => c.Issues.Contains(FolderIssue.StalePin)))
        {
            var pins = QuickAccessService.RefreshStalePins(automatic: false);
            if (pins.Outcome is PinRefreshOutcome.Failed or PinRefreshOutcome.Skipped)
            {
                var failed = result.Failed.ToList();
                failed.AddRange(checks.Where(c => c.Issues.Contains(FolderIssue.StalePin))
                    .Select(c => new FolderActionFailure(c.Path, pins.Reason ?? "")));
                result = result with { Failed = failed };
            }
        }

        return result with { Done = result.Done.Distinct(StringComparer.OrdinalIgnoreCase).ToList() };
    }

    /// <summary>Like the normal host, with "unblock" as an extra kind of sync.</summary>
    sealed class RepairHost : FolderBatch.IHost
    {
        public static readonly RepairHost Instance = new();

        public void Execute(FolderAction action)
        {
            if (action is { Kind: FolderActionKind.Sync, Value: "unblock" })
                RemoveDownloadMarks(action.Path);
            else
                FolderBatch.Execute(action);
        }

        public bool StartElevated(out string error, out bool declined)
        {
            var ok = ElevatedCliInvoker.EnsureWorkerStarted(out error);
            declined = !ok && string.Equals(error, ElevatedCliInvoker.UacCancelledMessage, StringComparison.Ordinal);
            return ok;
        }

        public bool ExecuteElevated(FolderAction action, out string error) =>
            action is { Kind: FolderActionKind.Sync, Value: "unblock" }
                ? ElevatedCliInvoker.TryInvoke("unblock", action.Path, null, out error, allowStartWorker: false)
                : ElevatedCliInvoker.TryInvoke(action.Verb, action.Path, action.Value, out error, allowStartWorker: false);
    }

    /// <summary>Deletes the "downloaded from the internet" mark from the folder's <c>desktop.ini</c> and its icon.</summary>
    /// <exception cref="UnauthorizedAccessException">The folder needs admin rights.</exception>
    public static void RemoveDownloadMarks(string folderPath)
    {
        var iniPath = Path.Combine(Path.GetFullPath(folderPath), "desktop.ini");
        DeleteZoneStream(iniPath);
        if (File.Exists(iniPath) && FolderIconManager.TryReadIconPath(iniPath) is { } icon)
        {
            try
            {
                DeleteZoneStream(icon);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // the next Sync writes a fresh icon without the mark
            }
        }
    }

    static void DeleteZoneStream(string filePath)
    {
        if (HasDownloadMark(filePath))
            File.Delete(filePath + ZoneStream);
    }

    public static bool HasDownloadMark(string filePath)
    {
        try
        {
            return File.Exists(filePath) && File.Exists(filePath + ZoneStream);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

    static bool IsUnder(string file, string directory)
    {
        var dir = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory)) + Path.DirectorySeparatorChar;
        return Path.GetFullPath(file).StartsWith(dir, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A network folder outside the Local intranet and Trusted sites zones.</summary>
    public static bool IsUntrustedNetworkPath(string fullPath)
    {
        try
        {
            var isNetwork = fullPath.StartsWith(@"\\", StringComparison.Ordinal) ||
                            (Path.GetPathRoot(fullPath) is { Length: > 0 } root && new DriveInfo(root).DriveType == DriveType.Network);
            if (!isNetwork)
                return false;
            return MapToZone(new Uri(fullPath).AbsoluteUri) is not (UrlZone.LocalMachine or UrlZone.Intranet or UrlZone.Trusted);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UriFormatException or COMException)
        {
            return false;
        }
    }

    enum UrlZone
    {
        LocalMachine = 0,
        Intranet = 1,
        Trusted = 2,
        Internet = 3,
        Untrusted = 4,
        Unknown = -1,
    }

    static UrlZone MapToZone(string url)
    {
        if (CoInternetCreateSecurityManager(0, out var manager, 0) != 0 || manager is null)
            return UrlZone.Unknown;
        try
        {
            return manager.MapUrlToZone(url, out var zone, 0) == 0 ? (UrlZone)zone : UrlZone.Unknown;
        }
        finally
        {
            Marshal.FinalReleaseComObject(manager);
        }
    }

    [DllImport("urlmon.dll")]
    static extern int CoInternetCreateSecurityManager(nint pSP, out IInternetSecurityManager? ppSM, uint dwReserved);

    [ComImport, Guid("79eac9ee-baf9-11ce-8c82-00aa004ba90b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IInternetSecurityManager
    {
        [PreserveSig] int SetSecuritySite(nint pSite);
        [PreserveSig] int GetSecuritySite(out nint ppSite);
        [PreserveSig] int MapUrlToZone([MarshalAs(UnmanagedType.LPWStr)] string pwszUrl, out int pdwZone, uint dwFlags);
    }
}
