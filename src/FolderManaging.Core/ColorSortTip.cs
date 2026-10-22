using System.Runtime.InteropServices;
using FolderManaging.Core.Localization;
using FolderManaging.Core.Storage;

namespace FolderManaging.Core;

/// <summary>
/// One-time explanation after the first successful color apply (preset or quick color).
/// </summary>
public static class ColorSortTip
{
    const uint MbOk = 0x0000_0000;
    const uint MbIconInformation = 0x0000_0040;
    const uint MbSetForeground = 0x0001_0000;
    const uint MbTopmost = 0x0004_0000;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern int MessageBoxW(nint hWnd, string text, string caption, uint type);

    public static void TryShowOnce()
    {
        try
        {
            var doc = JsonFolderStateStore.LoadSynchronously();
            if (doc.ColorSortTipShown)
                return;

            MessageBoxW(
                0,
                UiText.Get("ColorSortTip"),
                "Folder Managing",
                MbOk | MbIconInformation | MbSetForeground | MbTopmost);

            doc.ColorSortTipShown = true;
            JsonFolderStateStore.SaveSynchronously(doc);
        }
        catch
        {
            // a tip must not fail the color apply
        }
    }
}
