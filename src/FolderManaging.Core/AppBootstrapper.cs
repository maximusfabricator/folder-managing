using FolderManaging.Core.Diagnostics;
using FolderManaging.Core.Localization;
using FolderManaging.Core.Storage;

namespace FolderManaging.Core;

/// <summary>
/// First-run / startup initialization entry point. Must be safe to call multiple times — every host
/// (Companion, Shell extension surrogate, CLI) calls it on entry so that the user always has up-to-date
/// preset icons under <see cref="AppPaths.GeneratedIconsDirectory"/> without us shipping ANY pre-tinted
/// folder bitmaps in the installer (we extract the system folder glyph at runtime instead).
/// </summary>
public static class AppBootstrapper
{
    /// <summary>Generates the menu icons for every preset in <c>state.json</c> using the system folder icon.</summary>
    public static void Initialize()
    {
        try
        {
            UiText.ApplyToProcess();
            FolderIconManager.EnsureMenuIconsForAllPresets();
        }
        catch (Exception ex)
        {
            TraceLog.Append("shell-menu.log", $"[BOOT] AppBootstrapper.Initialize failed: {ex}");
        }
    }
}
