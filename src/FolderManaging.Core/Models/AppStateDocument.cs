namespace FolderManaging.Core.Models;

public sealed class AppStateDocument
{
    public int Version { get; set; } = 1;

    public List<FolderStateEntry> Folders { get; set; } = new();

    public List<MenuColorPreset> MenuColors { get; set; } = new();

    public List<MenuTagPreset> MenuTags { get; set; } = new();

    /// <summary>
    /// When true, color/reset on protected folders (Program Files, etc.) immediately retries via elevated CLI
    /// without asking each time. Windows may still show one UAC prompt per elevated launch. Default: on.
    /// </summary>
    public bool AutoElevateProtectedFolders { get; set; } = true;

    /// <summary>Companion light/dark theme. Default: follow Windows.</summary>
    public AppThemePreference Theme { get; set; } = AppThemePreference.System;

    /// <summary>User layout of the "Folder Manager" context menu; <c>null</c> means the built-in default.</summary>
    public ContextMenuLayout? ContextMenu { get; set; }

    /// <summary>
    /// When true, we already showed the one-time tip that Explorer sorts colored folders via Tags/Title.
    /// </summary>
    public bool ColorSortTipShown { get; set; }

    public ContextMenuLayout GetContextMenuLayout() => ContextMenuLayout.Normalize(ContextMenu);

    /// <summary>Restores app settings to defaults. Folder data, color presets and tags are kept.</summary>
    public void ResetSettingsToDefaults()
    {
        AutoElevateProtectedFolders = true;
        Theme = AppThemePreference.System;
        ContextMenu = null;
    }
}
