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

    /// <summary>
    /// Draws a small price-tag glyph in the bottom-right corner (in-place).
    /// Visible on both yellow stock folders and strongly tinted ones.
    /// </summary>
    internal static void DrawTagBadge(Bitmap icon, Color badge)
    {
        var size = Math.Min(icon.Width, icon.Height);
        if (size < 8)
            return;

        // Keep the overlay readable at 16px and chunky enough at 256px.
        var tagW = Math.Max(5, (int)Math.Round(size * 0.34));
        var tagH = Math.Max(4, (int)Math.Round(size * 0.22));
        var margin = Math.Max(1, (int)Math.Round(size * 0.06));
        var left = icon.Width - tagW - margin;
        var top = icon.Height - tagH - margin;
        var hole = Math.Max(1f, size * 0.035f);
        var tip = tagW * 0.28f;

        using var g = Graphics.FromImage(icon);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.CompositingMode = CompositingMode.SourceOver;

        using var path = new GraphicsPath();
        path.AddPolygon(new[]
        {
            new PointF(left + tip, top),
            new PointF(left + tagW, top),
            new PointF(left + tagW, top + tagH),
            new PointF(left + tip, top + tagH),
            new PointF(left, top + tagH * 0.5f),
        });

        // Soft dark rim so light badges do not disappear on yellow folders.
        using (var rim = new Pen(Color.FromArgb(180, 20, 20, 20), Math.Max(1f, size / 64f)))
            g.DrawPath(rim, path);

        using (var fill = new SolidBrush(Color.FromArgb(245, badge.R, badge.G, badge.B)))
            g.FillPath(fill, path);

        using (var outline = new Pen(Color.FromArgb(220, 255, 255, 255), Math.Max(1f, size / 80f)))
            g.DrawPath(outline, path);

        var cx = left + tip * 0.55f;
        var cy = top + tagH * 0.5f;
        using (var holeBrush = new SolidBrush(Color.FromArgb(230, 255, 255, 255)))
            g.FillEllipse(holeBrush, cx - hole, cy - hole, hole * 2f, hole * 2f);
        using (var holeRim = new Pen(Color.FromArgb(160, 30, 30, 30), Math.Max(0.75f, size / 128f)))
            g.DrawEllipse(holeRim, cx - hole, cy - hole, hole * 2f, hole * 2f);
    }
}
