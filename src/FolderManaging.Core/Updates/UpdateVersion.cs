// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 maximusfabricator

using System.Globalization;

namespace FolderManaging.Core.Updates;

/// <summary>A release number <c>major.minor.patch</c>, as in the tags (<c>v2.2.0</c>) and <c>update.json</c>.</summary>
public readonly record struct UpdateVersion(int Major, int Minor, int Patch) : IComparable<UpdateVersion>
{
    /// <summary>Accepts <c>2.2.0</c>, <c>v2.2.0</c>, <c>2.2</c> and the four-part package form <c>2.2.0.0</c>.</summary>
    public static bool TryParse(string? text, out UpdateVersion version)
    {
        version = default;
        if (string.IsNullOrWhiteSpace(text))
            return false;
        var s = text.Trim();
        if (s.StartsWith('v') || s.StartsWith('V'))
            s = s[1..];
        var plus = s.IndexOf('+');
        if (plus >= 0)
            s = s[..plus];

        var parts = s.Split('.');
        if (parts.Length is < 2 or > 4)
            return false;
        var numbers = new int[4];
        for (var i = 0; i < parts.Length; i++)
        {
            if (parts[i].Length is 0 or > 9 || !parts[i].All(char.IsAsciiDigit) ||
                !int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out numbers[i]))
                return false;
        }

        if (parts.Length == 4 && numbers[3] != 0)
            return false;
        version = new UpdateVersion(numbers[0], numbers[1], numbers[2]);
        return true;
    }

    public static UpdateVersion Parse(string text) =>
        TryParse(text, out var v) ? v : throw new FormatException($"Not a version: '{text}'.");

    /// <summary>The version of this build (<see cref="AppInfo.Version"/>).</summary>
    public static UpdateVersion Current
    {
        get
        {
#if FM_DEV
            // Developer builds: FM_FAKE_VERSION=2.1.1 pretends to be an older version against a test server.
            if (TryParse(Environment.GetEnvironmentVariable("FM_FAKE_VERSION"), out var fake))
                return fake;
#endif
            return TryParse(AppInfo.Version, out var v) ? v : default;
        }
    }

    public int CompareTo(UpdateVersion other)
    {
        var c = Major.CompareTo(other.Major);
        if (c != 0)
            return c;
        c = Minor.CompareTo(other.Minor);
        return c != 0 ? c : Patch.CompareTo(other.Patch);
    }

    public static bool operator >(UpdateVersion a, UpdateVersion b) => a.CompareTo(b) > 0;

    public static bool operator <(UpdateVersion a, UpdateVersion b) => a.CompareTo(b) < 0;

    public static bool operator >=(UpdateVersion a, UpdateVersion b) => a.CompareTo(b) >= 0;

    public static bool operator <=(UpdateVersion a, UpdateVersion b) => a.CompareTo(b) <= 0;

    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{Major}.{Minor}.{Patch}");
}
