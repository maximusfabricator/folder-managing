// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 maximusfabricator

using System.Drawing;
using System.Globalization;

namespace FolderManaging.Core.Storage;

public static class MenuColorHex
{
    public static string ToHexRgb(Color c) =>
        $"{c.R:X2}{c.G:X2}{c.B:X2}";

    public static bool TryParse(string? hex, out Color color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(hex))
            return false;

        var s = hex.Trim();
        if (s.StartsWith('#'))
            s = s[1..].TrimStart();
        else if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            s = s[2..];
        // Short CSS form: #f80 is #ff8800.
        if (s.Length == 3 && s.All(Uri.IsHexDigit))
            s = string.Concat(s.Select(c => new string(c, 2)));
        if (s.Length == 6 && s.All(Uri.IsHexDigit) &&
            uint.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
        {
            color = Color.FromArgb(
                (int)(rgb >> 16) & 0xff,
                (int)(rgb >> 8) & 0xff,
                (int)rgb & 0xff);
            return true;
        }

        return false;
    }
}
