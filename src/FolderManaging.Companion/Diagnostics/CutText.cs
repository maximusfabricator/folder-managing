// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 maximusfabricator

#if FM_DEV
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace FolderManaging.Companion.Diagnostics;

/// <summary>
/// Developer builds only, for the UI tours: finds the texts of a window that are not shown in full, because they are
/// trimmed, overflow their place or are clipped by a parent. Scrolling does not count.
/// </summary>
internal static class CutText
{
    const double Slack = 1.5;

    /// <summary>Appends the cut texts of <paramref name="window"/> to cut-text.txt next to the screenshot.</summary>
    internal static void Report(Window window, string shotPath)
    {
        var shot = Path.GetFileName(shotPath);
        var lines = Find(window).Select(problem => $"{shot}: {problem}").ToList();
        if (lines.Count > 0)
            File.AppendAllLines(Path.Combine(Path.GetDirectoryName(shotPath)!, "cut-text.txt"), lines);
    }

    static IEnumerable<string> Find(Window window)
    {
        foreach (var block in Descendants(window).OfType<TextBlock>())
        {
            if (!block.IsVisible || block.ActualWidth < 1 || string.IsNullOrWhiteSpace(block.Text) || InScrollBar(block))
                continue;
            if (Check(block, window) is { } problem)
                yield return $"\"{Shorten(block.Text)}\" {problem}";
        }
    }

    /// <summary>Scroll bar arrows are glyphs that the theme shrinks on purpose while the pointer is away.</summary>
    static bool InScrollBar(DependencyObject node)
    {
        for (var parent = VisualTreeHelper.GetParent(node); parent is not null; parent = VisualTreeHelper.GetParent(parent))
        {
            if (parent is System.Windows.Controls.Primitives.ScrollBar)
                return true;
        }
        return false;
    }

    static string? Check(TextBlock block, Window window)
    {
        // Fallback fonts depend on the language, so measure in the language the block is shown in.
        var text = new FormattedText(block.Text, block.Language.GetSpecificCulture(), block.FlowDirection,
            new Typeface(block.FontFamily, block.FontStyle, block.FontWeight, block.FontStretch), block.FontSize,
            System.Windows.Media.Brushes.Black, null, TextOptions.GetTextFormattingMode(block), VisualTreeHelper.GetDpi(block).PixelsPerDip);
        if (!double.IsNaN(block.LineHeight))
            text.LineHeight = block.LineHeight;

        var room = block.ActualWidth - block.Padding.Left - block.Padding.Right;
        var wraps = block.TextWrapping != TextWrapping.NoWrap;
        var needed = wraps ? text.MinWidth : text.Width;
        if (needed > room + Slack)
            return $"needs {needed:0} px, has {room:0} px";

        if (wraps)
        {
            // Layout rounding can make a block that sized itself to its text a fraction narrower than the text.
            text.MaxTextWidth = Math.Max(1, room + Slack);
            var height = text.Height + block.Padding.Top + block.Padding.Bottom;
            if (height > block.ActualHeight + Slack)
                return $"needs {height:0} px of height, has {block.ActualHeight:0} px";
        }

        var width = wraps ? room : Math.Min(text.Width, room);
        var x = block.TextAlignment switch
        {
            TextAlignment.Right => block.ActualWidth - block.Padding.Right - width,
            TextAlignment.Center => (block.ActualWidth - width) / 2,
            _ => block.Padding.Left,
        };
        var area = block.TransformToAncestor(window).TransformBounds(new Rect(x, 0, width, block.ActualHeight));
        var visible = area;
        for (DependencyObject? node = block; node is UIElement element and not Window; node = VisualTreeHelper.GetParent(node))
        {
            if (ClipOf(element) is not { } clip)
                continue;
            var bounds = element.TransformToAncestor(window).TransformBounds(clip);
            if (element is ScrollContentPresenter presenter)
            {
                if (presenter.ScrollOwner?.HorizontalScrollBarVisibility != ScrollBarVisibility.Disabled)
                    continue;
                bounds = new Rect(bounds.X, visible.Y, bounds.Width, visible.Height);
            }
            visible.Intersect(bounds);
            if (visible.IsEmpty)
                break;
        }
        if (visible.IsEmpty || visible.Width < area.Width - Slack || visible.Height < area.Height - Slack)
            return $"is clipped to {(visible.IsEmpty ? 0 : visible.Width):0}x{(visible.IsEmpty ? 0 : visible.Height):0} of {area.Width:0}x{area.Height:0} px at {area.X:0},{area.Y:0}";
        return null;
    }

    static Rect? ClipOf(UIElement element)
    {
        Rect? clip = element.ClipToBounds ? new Rect(element.RenderSize) : null;
        if (element is FrameworkElement framework && LayoutInformation.GetLayoutClip(framework) is { } layout)
            clip = Intersect(clip, layout.Bounds);
        if (element.Clip is { } own)
            clip = Intersect(clip, own.Bounds);
        return clip;
    }

    static Rect Intersect(Rect? a, Rect b)
    {
        if (a is not { } rect)
            return b;
        rect.Intersect(b);
        return rect;
    }

    static string Shorten(string text)
    {
        var flat = text.ReplaceLineEndings(" | ");
        return flat.Length <= 70 ? flat : flat[..69] + "…";
    }

    static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var nested in Descendants(child))
                yield return nested;
        }
    }
}
#endif
