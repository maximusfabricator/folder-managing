// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 maximusfabricator

using System.Runtime.InteropServices;

namespace FolderManaging.Core.Interop;

[StructLayout(LayoutKind.Sequential)]
internal struct PropertyKey(Guid fmtid, uint pid)
{
    public Guid FormatId = fmtid;
    public uint PropertyId = pid;
}

[ComImport, Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IShellItem
{
    [PreserveSig] int BindToHandler(nint pbc, in Guid bhid, in Guid riid, [MarshalAs(UnmanagedType.Interface)] out object ppv);
    [PreserveSig] int GetParent(out IShellItem ppsi);
    [PreserveSig] int GetDisplayName(uint sigdnName, out nint ppszName);
    [PreserveSig] int GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);
    [PreserveSig] int Compare(IShellItem psi, uint hint, out int piOrder);
}

[ComImport, Guid("7e9fb0d3-919f-4307-ab2e-9b1860310c93"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IShellItem2
{
    [PreserveSig] int BindToHandler(nint pbc, in Guid bhid, in Guid riid, [MarshalAs(UnmanagedType.Interface)] out object ppv);
    [PreserveSig] int GetParent(out IShellItem ppsi);
    [PreserveSig] int GetDisplayName(uint sigdnName, out nint ppszName);
    [PreserveSig] int GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);
    [PreserveSig] int Compare(IShellItem psi, uint hint, out int piOrder);
    [PreserveSig] int GetPropertyStore(int flags, in Guid riid, out nint ppv);
    [PreserveSig] int GetPropertyStoreWithCreateObject(int flags, nint punkCreateObject, in Guid riid, out nint ppv);
    [PreserveSig] int GetPropertyStoreForKeys(nint rgKeys, uint cKeys, int flags, in Guid riid, out nint ppv);
    [PreserveSig] int GetPropertyDescriptionList(in PropertyKey keyType, in Guid riid, out nint ppv);
    [PreserveSig] int Update(nint pbc);
    [PreserveSig] int GetProperty(in PropertyKey key, nint ppropvar);
    [PreserveSig] int GetCLSID(in PropertyKey key, out Guid pclsid);
    [PreserveSig] int GetFileTime(in PropertyKey key, out long pft);
    [PreserveSig] int GetInt32(in PropertyKey key, out int pi);
    [PreserveSig] int GetString(in PropertyKey key, out nint ppsz);
    [PreserveSig] int GetUInt32(in PropertyKey key, out uint pui);
    [PreserveSig] int GetUInt64(in PropertyKey key, out ulong pull);
    [PreserveSig] int GetBool(in PropertyKey key, [MarshalAs(UnmanagedType.Bool)] out bool pf);
}

[ComImport, Guid("70629033-e363-4a28-a567-0db78006e6d7"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IEnumShellItems
{
    [PreserveSig] int Next(uint celt, [MarshalAs(UnmanagedType.Interface)] out object? rgelt, out uint pceltFetched);
    [PreserveSig] int Skip(uint celt);
    [PreserveSig] int Reset();
    [PreserveSig] int Clone(out IEnumShellItems ppenum);
}

internal static class ShellItemInterop
{
    public const uint SIGDN_DESKTOPABSOLUTEPARSING = 0x8002_8000;

    public static readonly Guid BHID_EnumItems = new("94f60519-2850-4924-aa5a-d15e84868039");
    public static readonly Guid IID_IShellItem = new("43826d1e-e718-42ee-bc55-a1e261c37bfe");
    public static readonly Guid IID_IEnumShellItems = new("70629033-e363-4a28-a567-0db78006e6d7");

    /// <summary>System.FileAttributes. On a Quick access item it is the snapshot taken when the folder was pinned.</summary>
    public static readonly PropertyKey PKEY_FileAttributes = new(new Guid("B725F130-47EF-101A-A5F1-02608C9EEBAC"), 13);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    public static extern int SHCreateItemFromParsingName(string pszPath, nint pbc, in Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out object ppv);

    [DllImport("propsys.dll", CharSet = CharSet.Unicode)]
    public static extern int PSGetPropertyKeyFromName(string pszName, out PropertyKey ppropkey);

    public static string? GetDisplayName(IShellItem2 item, uint sigdn)
    {
        if (item.GetDisplayName(sigdn, out var buffer) != 0 || buffer == 0)
            return null;
        try
        {
            return Marshal.PtrToStringUni(buffer);
        }
        finally
        {
            Marshal.FreeCoTaskMem(buffer);
        }
    }
}
