// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 maximusfabricator

#if FM_DEV
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace FolderManaging.Core.Diagnostics;

/// <summary>Developer builds only: PNG of a window as it is on screen, for the UI tours.</summary>
public static class ScreenCapture
{
    public static void SaveWindow(IntPtr hwnd, string path)
    {
        if (DwmGetWindowAttribute(hwnd, DwmwaExtendedFrameBounds, out var r, Marshal.SizeOf<Rect>()) != 0)
            GetWindowRect(hwnd, out r);

        using var bitmap = new Bitmap(Math.Max(1, r.Right - r.Left), Math.Max(1, r.Bottom - r.Top));
        using (var graphics = Graphics.FromImage(bitmap))
            graphics.CopyFromScreen(r.Left, r.Top, 0, 0, bitmap.Size);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        bitmap.Save(path, ImageFormat.Png);
    }

    const int DwmwaExtendedFrameBounds = 9;

    [StructLayout(LayoutKind.Sequential)]
    struct Rect
    {
        public int Left, Top, Right, Bottom;
    }

    [DllImport("dwmapi.dll")]
    static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out Rect value, int size);

    [DllImport("user32.dll")]
    static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
}
#endif
