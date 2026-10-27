// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 maximusfabricator

using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Markup;
using FolderManaging.Core.Localization;
using FlowDirection = System.Windows.FlowDirection;

namespace FolderManaging.Companion.Localization;

internal static class UiDirection
{
    /// <summary>
    /// Mirrors <paramref name="root"/> for right-to-left languages and tags it with the UI language,
    /// so WPF picks the right font for CJK, Indic, Thai and Arabic-script text.
    /// Call it on every window and every page: a navigation frame does not pass either property on to its content.
    /// </summary>
    internal static void Apply(FrameworkElement root)
    {
        root.FlowDirection = UiText.IsRightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        root.Language = XmlLanguage.GetLanguage(UiText.Current.Code);
    }

    /// <summary>
    /// Shows <paramref name="text"/> in <paramref name="block"/>. WPF ignores the U+202A…U+202C marks that
    /// <see cref="UiText.LeftToRight"/> puts around paths, so those parts become left-to-right runs instead.
    /// </summary>
    internal static void SetText(TextBlock block, string text)
    {
        block.Inlines.Clear();
        var pieces = text.Split('\u202A');
        block.Inlines.Add(new Run(pieces[0]));
        foreach (var piece in pieces.Skip(1))
        {
            var end = piece.IndexOf('\u202C');
            block.Inlines.Add(new Run(end < 0 ? piece : piece[..end]) { FlowDirection = FlowDirection.LeftToRight });
            if (end >= 0)
                block.Inlines.Add(new Run(piece[(end + 1)..]));
        }
    }
}
