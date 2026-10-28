// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 maximusfabricator

using FolderManaging.Core;
using FolderManaging.Core.Diagnostics;
using FolderManaging.Core.Models;
using FolderManaging.Core.Storage;
using Vanara.PInvoke;

namespace FolderManaging.Shell.Com;

internal static class MenuBuilder
{
    internal static string GetRootTitle()
    {
        try
        {
            return MenuStateReader.GetMerged().GetContextMenuLayout().RootTitle;
        }
        catch
        {
            return ContextMenuLayout.DefaultRootTitle;
        }
    }

    /// <summary>Icon must always be a non-null path, or no icon at all — a null GetIcon with S_OK crashed Explorer.</summary>
    internal static IReadOnlyList<Shell32.IExplorerCommand> Build()
    {
        ClickTrace.Step("MenuBuilder.Build start");
        var doc = MenuStateReader.GetMerged();
        var list = new List<Shell32.IExplorerCommand>();
        foreach (var line in ContextMenuPlan.Build(doc))
        {
            var icon = IconFor(line);
            if (line.Icon == MenuLineIcon.Preset)
                ClickTrace.Step($"MenuBuilder color id={line.Token} title='{line.Title}' icon={icon ?? "(none)"}");
            list.Add(new MenuActionExplorerCommand(
                line.VerbId,
                line.Title,
                line.Kind,
                line.Token,
                icon,
                disabled: line.Disabled,
                omitIcon: line.Icon == MenuLineIcon.None,
                checkedTitle: line.CheckedTitle));
        }

        ClickTrace.Step($"MenuBuilder.Build done items={list.Count} layout={(doc.ContextMenu is null ? "default" : "custom")}");
        return list;
    }

    static string? IconFor(MenuLine line) => line.Icon switch
    {
        MenuLineIcon.Preset when line.Token is not null => FolderIconManager.TryGetPresetIconMenuReference(line.Token),
        MenuLineIcon.Rgb when line.IconColor is { } c => FolderIconManager.TryGetRgbIconMenuReference(c),
        MenuLineIcon.Tag when line.Token is not null => FolderIconManager.TryGetTagIconMenuReference(
            line.Token, line.IconColor is { } c ? MenuColorHex.ToHexRgb(c) : null),
        _ => null,
    };
}
