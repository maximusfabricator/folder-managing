// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 maximusfabricator

using System.Buffers.Binary;
using System.Drawing;
using System.Drawing.Imaging;

namespace FolderManaging.Core.Tinting;

/// <summary>Writes a Windows Vista+ style .ico containing embedded PNG images (sharp at multiple DPI).</summary>
internal static class PngIcoEncoder
{
    /// <summary>Saves <paramref name="images"/> as PNG payloads inside an ICO (largest first).</summary>
    internal static void Save(string path, IReadOnlyList<(int LogicalSize, Bitmap Bitmap)> images)
    {
        using var fs = File.Create(path);
        using var ms = new MemoryStream();
        var writer = new BinaryWriter(ms);

        var count = (ushort)images.Count;
        writer.Write((ushort)0);
        writer.Write((ushort)1);
        writer.Write(count);

        var pngChunks = new List<byte[]>(images.Count);
        foreach (var (_, bmp) in images)
        {
            using var pms = new MemoryStream();
            bmp.Save(pms, ImageFormat.Png);
            pngChunks.Add(pms.ToArray());
        }

        var headerSize = 6 + 16 * count;
        var offset = headerSize;
        for (var i = 0; i < count; i++)
        {
            var logical = images[i].LogicalSize;
            var png = pngChunks[i];

            writer.Write(EncodeIcoDimension(logical));
            writer.Write(EncodeIcoDimension(logical));
            writer.Write((byte)0);
            writer.Write((byte)0);
            writer.Write((ushort)1);
            writer.Write((ushort)0);
            writer.Write(png.Length);
            writer.Write(offset);
            offset += png.Length;
        }

        foreach (var png in pngChunks)
            writer.Write(png);

        ms.Position = 0;
        ms.CopyTo(fs);
    }

    /// <summary>ICO width/height byte: 0 means 256.</summary>
    static byte EncodeIcoDimension(int px) =>
        px >= 256 ? (byte)0 : (byte)px;
}
