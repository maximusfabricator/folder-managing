// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 maximusfabricator

using FolderManaging.Core.Interop;
using FolderManaging.Core.Localization;
using FolderManaging.Core.Storage;

namespace FolderManaging.Core;

/// <summary>
/// One-time explanation after the first successful color apply (preset or quick color).
/// </summary>
public static class ColorSortTip
{
    public static void TryShowOnce()
    {
        try
        {
            var doc = JsonFolderStateStore.LoadSynchronously();
            if (doc.ColorSortTipShown)
                return;

            NativeMessageBox.Show(
                UiText.Get("ColorSortTip"),
                "Folder Managing",
                NativeMessageBox.Ok | NativeMessageBox.IconInformation | NativeMessageBox.SetForeground | NativeMessageBox.Topmost);

            doc.ColorSortTipShown = true;
            JsonFolderStateStore.SaveSynchronously(doc);
        }
        catch
        {
            // a tip must not fail the color apply
        }
    }
}
