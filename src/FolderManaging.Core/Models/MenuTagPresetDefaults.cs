using System.Drawing;

namespace FolderManaging.Core.Models;

/// <summary>Fallback badge color when a tag has no <see cref="MenuTagPreset.HexRgb"/>.</summary>
public static class MenuTagPresetDefaults
{
    public const string HexRgb = "0078D4";

    public static Color Color { get; } = Color.FromArgb(0x00, 0x78, 0xD4);
}
