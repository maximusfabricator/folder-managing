// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 maximusfabricator

using System.Runtime.InteropServices;
using FolderManaging.Core.Interop;

namespace FolderManaging.Core.QuickAccess;

/// <summary>
/// The real Quick access list through <c>Shell.Application</c>. Must run on an STA thread.
/// Only pinned items are read; recent files and frequent folders are skipped before their names are looked at.
/// </summary>
internal sealed class ShellQuickAccess : IQuickAccess
{
    const string QuickAccessFolder = "shell:::{679f85cb-0220-4080-b29b-5540cc05aab6}";
    const string PinVerb = "pintohome";
    const string UnpinVerb = "unpinfromhome";

    /// <summary>Windows applies a pin change a moment after the verb returns.</summary>
    static readonly TimeSpan SettleTimeout = TimeSpan.FromSeconds(4);

    static readonly Lazy<HashSet<string>> KnownFolders = new(ReadKnownFolders);

    static readonly Lazy<PropertyKey> IsPinnedKey = new(() =>
        ShellItemInterop.PSGetPropertyKeyFromName("System.Home.IsPinned", out var key) == 0
            ? key
            : throw new InvalidOperationException("System.Home.IsPinned is not known on this Windows."));

    /// <summary>Uses <c>IShellItem2</c>: only it returns the attribute snapshot a pin keeps.</summary>
    public IReadOnlyList<QuickAccessPin> GetPinned()
    {
        var pins = new List<QuickAccessPin>();
        Check(ShellItemInterop.SHCreateItemFromParsingName(QuickAccessFolder, 0, ShellItemInterop.IID_IShellItem, out var root));
        object? enumObject = null;
        try
        {
            Check(((IShellItem)root).BindToHandler(0, ShellItemInterop.BHID_EnumItems, ShellItemInterop.IID_IEnumShellItems,
                out enumObject));
            var items = (IEnumShellItems)enumObject;
            while (items.Next(1, out var element, out var fetched) == 0 && fetched == 1 && element is not null)
            {
                try
                {
                    if (element is not IShellItem2 item ||
                        item.GetBool(IsPinnedKey.Value, out var pinned) != 0 || !pinned)
                        continue;
                    var path = ShellItemInterop.GetDisplayName(item, ShellItemInterop.SIGDN_DESKTOPABSOLUTEPARSING);
                    if (string.IsNullOrWhiteSpace(path))
                        continue;
                    var stored = item.GetUInt32(ShellItemInterop.PKEY_FileAttributes, out var attributes) == 0 ? attributes : 0;
                    pins.Add(new QuickAccessPin(path, (FileAttributes)stored, IsPlainFolder(path)));
                }
                finally
                {
                    Marshal.FinalReleaseComObject(element);
                }
            }
        }
        finally
        {
            if (enumObject is not null)
                Marshal.FinalReleaseComObject(enumObject);
            Marshal.FinalReleaseComObject(root);
        }

        return pins;
    }

    static void Check(int hr)
    {
        if (hr < 0)
            Marshal.ThrowExceptionForHR(hr);
    }

    public void Pin(string path)
    {
        var shell = CreateShell();
        try
        {
            dynamic folder = ((dynamic)shell).NameSpace(PinRefresher.NormalizePath(path))
                             ?? throw new DirectoryNotFoundException(path);
            try
            {
                dynamic self = folder.Self;
                try
                {
                    self.InvokeVerb(PinVerb);
                }
                finally
                {
                    Marshal.FinalReleaseComObject((object)self);
                }
            }
            finally
            {
                Marshal.FinalReleaseComObject((object)folder);
            }
        }
        finally
        {
            Marshal.FinalReleaseComObject(shell);
        }

        WaitUntil(path, pinned: true);
    }

    /// <summary>
    /// Unpins through the Quick access item: <c>unpinfromhome</c> on the folder itself silently does nothing.
    /// Works for a pinned folder that no longer exists, too.
    /// </summary>
    public void Unpin(string path)
    {
        var wanted = PinRefresher.NormalizePath(path);
        WithQuickAccessItems(item =>
        {
            if (ToBool(item.ExtendedProperty("System.Home.IsPinned")) != true)
                return false;
            string? itemPath = item.Path;
            if (itemPath is null ||
                !string.Equals(PinRefresher.NormalizePath(itemPath), wanted, StringComparison.OrdinalIgnoreCase))
                return false;
            item.InvokeVerb(UnpinVerb);
            return true;
        });
        WaitUntil(path, pinned: false);
    }

    void WaitUntil(string path, bool pinned)
    {
        var deadline = DateTime.UtcNow + SettleTimeout;
        while (PinRefresher.IsPinned(this, path) != pinned && DateTime.UtcNow < deadline)
            Thread.Sleep(150);
    }

    /// <summary>Runs <paramref name="visit"/> on each Quick access item until it returns <c>true</c>.</summary>
    static void WithQuickAccessItems(Func<dynamic, bool> visit)
    {
        var shell = CreateShell();
        object? folder = null;
        object? items = null;
        try
        {
            folder = ((dynamic)shell).NameSpace(QuickAccessFolder)
                     ?? throw new InvalidOperationException("Quick access is not available.");
            items = ((dynamic)folder).Items();
            var count = (int)((dynamic)items).Count;
            for (var i = 0; i < count; i++)
            {
                object? item = ((dynamic)items).Item(i);
                if (item is null)
                    continue;
                try
                {
                    if (visit(item))
                        return;
                }
                finally
                {
                    Marshal.FinalReleaseComObject(item);
                }
            }
        }
        finally
        {
            if (items is not null)
                Marshal.FinalReleaseComObject(items);
            if (folder is not null)
                Marshal.FinalReleaseComObject(folder);
            Marshal.FinalReleaseComObject(shell);
        }
    }

    static object CreateShell()
    {
        var type = Type.GetTypeFromProgID("Shell.Application")
                   ?? throw new InvalidOperationException("Shell.Application is not available.");
        return Activator.CreateInstance(type) ?? throw new InvalidOperationException("Could not create Shell.Application.");
    }

    /// <summary>A folder on a local drive that is not a known folder (Desktop, Downloads…), library or network location.</summary>
    internal static bool IsPlainFolder(string path)
    {
        if (path.StartsWith("::", StringComparison.Ordinal) || path.StartsWith(@"\\", StringComparison.Ordinal) ||
            !Path.IsPathFullyQualified(path) || path.EndsWith(".library-ms", StringComparison.OrdinalIgnoreCase))
            return false;
        if (KnownFolders.Value.Contains(PinRefresher.NormalizePath(path)))
            return false;
        try
        {
            var root = Path.GetPathRoot(path);
            return root is not null && new DriveInfo(root).DriveType is DriveType.Fixed or DriveType.Removable;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    static HashSet<string> ReadKnownFolders()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var special in new[]
                 {
                     Environment.SpecialFolder.Desktop, Environment.SpecialFolder.MyDocuments, Environment.SpecialFolder.MyMusic,
                     Environment.SpecialFolder.MyPictures, Environment.SpecialFolder.MyVideos, Environment.SpecialFolder.UserProfile,
                 })
        {
            var p = Environment.GetFolderPath(special);
            if (!string.IsNullOrEmpty(p))
                set.Add(PinRefresher.NormalizePath(p));
        }

        if (Win32Shell.TryGetKnownFolderPath(Win32Shell.FolderIdDownloads) is { } downloads)
            set.Add(PinRefresher.NormalizePath(downloads));
        return set;
    }

    static bool? ToBool(object? value) => value switch
    {
        bool b => b,
        int i => i != 0,
        _ => null,
    };
}
