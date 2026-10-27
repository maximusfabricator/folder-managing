// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 maximusfabricator

using System.Drawing;
using System.Globalization;
using System.Text;

namespace FolderManaging.Core.ShellProperties;

/// <summary>
/// Persists folder color hue (0–360) only on colored folders. Uncolored siblings are left alone
/// (no 999 markers). Explorer sorts sparse numeric <c>FolderManaging.ColorHue</c> when that column is selected.
/// Title/Tags keep a human-readable fallback label for Sort → More without the custom column.
/// </summary>
public static class FolderColorHueProperty
{
    public static readonly Guid FormatId = new("A7C4E8F1-2B3D-4E5F-9A0B-1C2D3E4F5A6B");
    public const uint PropId = 2;
    public const string CanonicalName = "FolderManaging.ColorHue";

    public static readonly Guid SummaryFormatId = new("F29F85E0-4FF9-1068-AB91-08002B27B3D9");
    public const uint TitlePropId = 2;
    public const uint KeywordsPropId = 5;
    public const uint CommentPropId = 6;

    public static readonly Guid RatingFormatId = new("64440492-4C8B-11D1-8B70-080036B11A03");
    public const uint RatingPropId = 9;

    public const string IniSectionName = "FolderManaging";
    public const string IniHueKey = "ColorHue";
    public static string IniSectionHeader => "[" + IniSectionName + "]";

    /// <summary>Legacy marker from builds that wrote hue 999 onto uncolored siblings — cleanup only.</summary>
    public const string SortTailMarker = "FolderManaging-sort-tail=1";

    public const ushort VarTypeUInt32 = 19;
    public const ushort VarTypeLpstr = 31;
    public const ushort VarTypeVectorLpstr = 4127;

    /// <summary>Always English: Explorer shows InfoTip as stored, with no language switch later.</summary>
    public const string SortHintInfoTip =
        "Folder Managing color. Sort: Sort → \"Folder color\" (or Tags), ascending";

    public static int FromColor(Color color)
    {
        var hue = color.GetHue();
        if (float.IsNaN(hue) || float.IsInfinity(hue))
            return 0;
        var rounded = (int)Math.Round(hue, MidpointRounding.AwayFromZero);
        if (rounded < 0) return 0;
        if (rounded > 360) return 360;
        return rounded;
    }

    public static string ToHueToken(int hueDegrees) =>
        Math.Clamp(hueDegrees, 0, 360).ToString("D3", CultureInfo.InvariantCulture);

    public static string ToSortLabel(int hueDegrees, string? colorIdOrHex)
    {
        var hue = ToHueToken(hueDegrees);
        var name = string.IsNullOrWhiteSpace(colorIdOrHex)
            ? "color"
            : colorIdOrHex.Trim();
        if (name.Length > 24)
            name = name[..24];
        return hue + " · " + name;
    }

    public static string InfoTipLine() => "InfoTip=" + SortHintInfoTip;

    public static void AppendToDesktopIni(StringBuilder body, int colorHueDegrees, string? colorIdOrHex, IReadOnlyList<string>? tagLabels = null)
    {
        var hue = Math.Clamp(colorHueDegrees, 0, 360);
        var label = ToSortLabel(hue, colorIdOrHex);
        var hueText = hue.ToString(CultureInfo.InvariantCulture);
        var nl = Environment.NewLine;
        var summary = SummaryFormatId.ToString("B").ToUpperInvariant();
        var colorFmt = FormatId.ToString("B").ToUpperInvariant();
        var tags = tagLabels ?? [];

        body.Append(IniSectionHeader).Append(nl)
            .Append(IniHueKey).Append('=').Append(hueText).Append(nl);
        if (tags.Count > 0)
            body.Append(FolderTagProperty.IniKey).Append('=').Append(FolderTagProperty.Join(tags)).Append(nl);

        // Native property-bag form for FolderManaging.ColorHue (UInt32) — used by Sort → «Цвет папки».
        body.Append('[').Append(colorFmt).Append(']').Append(nl)
            .Append("Prop").Append(PropId.ToString(CultureInfo.InvariantCulture))
            .Append('=').Append(VarTypeUInt32.ToString(CultureInfo.InvariantCulture))
            .Append(',').Append(hueText).Append(nl);

        // Human-readable fallbacks in Sort → More (Tags / Title).
        body.Append('[').Append(summary).Append(']').Append(nl)
            .Append("Prop").Append(TitlePropId.ToString(CultureInfo.InvariantCulture))
            .Append('=').Append(VarTypeLpstr.ToString(CultureInfo.InvariantCulture))
            .Append(',').Append(label).Append(nl)
            .Append("Prop").Append(KeywordsPropId.ToString(CultureInfo.InvariantCulture))
            .Append('=').Append(VarTypeVectorLpstr.ToString(CultureInfo.InvariantCulture))
            .Append(',').Append(label).Append(nl)
            .Append("Prop").Append(CommentPropId.ToString(CultureInfo.InvariantCulture))
            .Append('=').Append(VarTypeLpstr.ToString(CultureInfo.InvariantCulture))
            .Append(',').Append(label).Append(nl);

        body.Append('[').Append(RatingFormatId.ToString("B").ToUpperInvariant()).Append(']').Append(nl)
            .Append("Prop").Append(RatingPropId.ToString(CultureInfo.InvariantCulture))
            .Append('=').Append(VarTypeUInt32.ToString(CultureInfo.InvariantCulture))
            .Append(',').Append(hueText).Append(nl);

        FolderTagProperty.AppendPropertyBag(body, tags);
    }

    public static bool IsOwnedSectionHeader(string trimmedSectionLine)
    {
        if (trimmedSectionLine.Equals(IniSectionHeader, StringComparison.OrdinalIgnoreCase))
            return true;
        if (trimmedSectionLine.Equals("[" + FormatId.ToString("B") + "]", StringComparison.OrdinalIgnoreCase))
            return true;
        if (trimmedSectionLine.Equals("[" + SummaryFormatId.ToString("B") + "]", StringComparison.OrdinalIgnoreCase))
            return true;
        if (trimmedSectionLine.Equals("[" + RatingFormatId.ToString("B") + "]", StringComparison.OrdinalIgnoreCase))
            return true;
        if (FolderTagProperty.IsOwnedSectionHeader(trimmedSectionLine))
            return true;
        return false;
    }
}
