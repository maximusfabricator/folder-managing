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

        var s = hex.Trim().TrimStart('#');
        if (s.Length == 6 &&
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
