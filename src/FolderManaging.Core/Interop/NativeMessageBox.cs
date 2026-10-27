// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 maximusfabricator

using System.Runtime.InteropServices;
using FolderManaging.Core.Localization;

namespace FolderManaging.Core.Interop;

/// <summary>Win32 message box for hosts without WPF (the Explorer menu host) and one-time tips.</summary>
public static class NativeMessageBox
{
    public const uint Ok = 0x0000_0000;
    public const uint YesNo = 0x0000_0004;
    public const uint IconError = 0x0000_0010;
    public const uint IconWarning = 0x0000_0030;
    public const uint IconInformation = 0x0000_0040;
    public const uint SetForeground = 0x0001_0000;
    public const uint Topmost = 0x0004_0000;
    public const int IdYes = 6;

    const uint RightAlign = 0x0008_0000;
    const uint RtlReading = 0x0010_0000;

    /// <summary>Mirrors the text for right-to-left UI languages. Windows labels the buttons in its own language.</summary>
    public static int Show(string text, string caption, uint type) =>
        MessageBoxW(0, text, caption, UiText.IsRightToLeft ? type | RightAlign | RtlReading : type);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern int MessageBoxW(nint hWnd, string text, string caption, uint type);
}
