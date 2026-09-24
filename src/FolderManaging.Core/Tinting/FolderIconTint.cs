using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace FolderManaging.Core.Tinting;

/// <summary>Applies luminance grayscale then an RGB tint via one <see cref="ColorMatrix"/> (alpha preserved).</summary>
internal static class FolderIconTint
{
    // BT.601 luma — desaturates the stock yellow folder before tint so blues/greens stay clean.
    const float Lr = 0.299f;
    const float Lg = 0.587f;
    const float Lb = 0.114f;

    /// <param name="strength">0 = unchanged, 1 = full multiply toward <paramref name="tint"/>.</param>
    internal static Bitmap ApplyRgbTint(Bitmap source, Color tint, float strength = 0.92f)
    {
        strength = Math.Clamp(strength, 0f, 1f);
        var w = source.Width;
        var h = source.Height;
        var dest = new Bitmap(w, h, PixelFormat.Format32bppArgb);

        var tr = tint.R / 255f;
        var tg = tint.G / 255f;
        var tb = tint.B / 255f;

        // Per-channel on grayscale luma: out = L * (1 + (t - 1) * k).
        // High strength keeps the stock folder shape but makes the hue match the chosen HEX closely
        // (0.52 left oranges/pinks looking almost like the default yellow folder).
        var m00 = 1f + (tr - 1f) * strength;
        var m11 = 1f + (tg - 1f) * strength;
        var m22 = 1f + (tb - 1f) * strength;

        // ColorMatrix rows = input channel, cols = output channel:
        //   out[c] = sum_k src[k] * m[k][c]
        // Per output channel we want: out[c] = (Lr*src.R + Lg*src.G + Lb*src.B) * m_cc
        // so column c is filled with (L_k * m_cc) for input rows k = R/G/B.
        var matrix = new ColorMatrix(new[]
        {
            new[] { Lr * m00, Lr * m11, Lr * m22, 0f, 0f },
            new[] { Lg * m00, Lg * m11, Lg * m22, 0f, 0f },
            new[] { Lb * m00, Lb * m11, Lb * m22, 0f, 0f },
            new[] { 0f, 0f, 0f, 1f, 0f },
            new[] { 0f, 0f, 0f, 0f, 1f },
        });

        using var g = Graphics.FromImage(dest);
        g.Clear(Color.Transparent);
        using var attrs = new ImageAttributes();
        attrs.SetColorMatrix(matrix, ColorMatrixFlag.Default, ColorAdjustType.Bitmap);
        g.DrawImage(source, new Rectangle(0, 0, w, h), 0, 0, w, h, GraphicsUnit.Pixel, attrs);
        return dest;
    }

    internal static Bitmap ResizeSquare(Bitmap source, int size)
    {
        var dst = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(dst);
        g.Clear(Color.Transparent);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.SmoothingMode = SmoothingMode.HighQuality;
        g.CompositingQuality = CompositingQuality.HighQuality;
        g.CompositingMode = CompositingMode.SourceOver;
        g.DrawImage(source, new Rectangle(0, 0, size, size));
        return dst;
    }
}
