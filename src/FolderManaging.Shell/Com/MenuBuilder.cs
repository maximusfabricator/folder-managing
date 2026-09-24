using System.Drawing;
using FolderManaging.Core;
using FolderManaging.Core.Diagnostics;
using FolderManaging.Core.Models;
using FolderManaging.Core.Storage;
using Vanara.PInvoke;

namespace FolderManaging.Shell.Com;

internal static class MenuBuilder
{
    static readonly (string Name, string Hex)[] SystemPalette =
    {
        ("Red", "E53935"),
        ("Orange", "FB8C00"),
        ("Yellow", "FDD835"),
        ("Lime", "7CB342"),
        ("Green", "43A047"),
        ("Teal", "00897B"),
        ("Cyan", "00ACC1"),
        ("Blue", "1E88E5"),
        ("Indigo", "3949AB"),
        ("Purple", "8E24AA"),
        ("Pink", "D81B60"),
        ("Brown", "6D4C41"),
        ("Gray", "757575"),
    };

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

    internal static IReadOnlyList<Shell32.IExplorerCommand> Build()
    {
        ClickTrace.Step("MenuBuilder.Build start");
        var doc = MenuStateReader.GetMerged();
        var layout = doc.GetContextMenuLayout();
        var list = new List<Shell32.IExplorerCommand>();

        foreach (var entry in layout.Entries)
        {
            if (!entry.Visible)
                continue;

            // Win11 modern nested menus crash or ignore real separators; skip layout separators in the shell menu.
            if (entry.Kind == ContextMenuItemKinds.Separator)
                continue;

            var label = string.IsNullOrWhiteSpace(entry.Label)
                ? ContextMenuItemKinds.DefaultLabel(entry.Kind)
                : entry.Label;

            switch (entry.Kind)
            {
                case ContextMenuItemKinds.Pin:
                    list.Add(new MenuActionExplorerCommand("pin", "📌 Pin folder", "pin", token: null));
                    list.Add(new MenuActionExplorerCommand("unpin", "📌 Unpin folder", "unpin", token: null));
                    break;

                case ContextMenuItemKinds.QuickColor:
                    list.Add(new MenuActionExplorerCommand("quick-color", label, "quick-color", token: null));
                    break;

                case ContextMenuItemKinds.ColorPresets:
                    AppendPresetItems(list, doc);
                    break;

                case ContextMenuItemKinds.SystemPalette:
                    AppendSystemPalette(list);
                    break;

                case ContextMenuItemKinds.Reset:
                    list.Add(new MenuActionExplorerCommand("reset-color", label, "reset", token: null));
                    break;

                case ContextMenuItemKinds.Tags:
                    AppendTagItems(list, doc);
                    break;

                case ContextMenuItemKinds.Settings:
                    list.Add(new MenuActionExplorerCommand("settings", label, "settings", token: null));
                    break;
            }
        }

        ClickTrace.Step($"MenuBuilder.Build done items={list.Count} layout={(doc.ContextMenu is null ? "default" : "custom")}");
        return list;
    }

    static void AppendPresetItems(List<Shell32.IExplorerCommand> list, AppStateDocument doc)
    {
        foreach (var row in doc.MenuColors.Where(r => r is not null && !string.IsNullOrWhiteSpace(r.Id)))
        {
            var title = ComposeColorTitle(row);
            var colorIcon = FolderIconManager.TryGetPresetIconMenuReference(row.Id);
            ClickTrace.Step($"MenuBuilder color id={row.Id} hex={row.HexRgb} title='{title}' icon={(colorIcon ?? "(none)")}");
            list.Add(new MenuActionExplorerCommand($"color-{row.Id}", title, "color", row.Id, colorIcon));
        }
    }

    static void AppendSystemPalette(List<Shell32.IExplorerCommand> list)
    {
        foreach (var (name, hex) in SystemPalette)
        {
            var icon = FolderIconManager.TryGetRgbIconMenuReference(ColorTranslator.FromHtml("#" + hex));
            list.Add(new MenuActionExplorerCommand($"palette-{hex}", $"🎨 {name}", "rgb", hex, icon));
        }
    }

    static void AppendTagItems(List<Shell32.IExplorerCommand> list, AppStateDocument doc)
    {
        foreach (var t in doc.MenuTags)
        {
            if (string.IsNullOrWhiteSpace(t.Id))
                continue;
            list.Add(new MenuActionExplorerCommand($"tag-{t.Id}", $"🏷️ Tag — {t.Label}", "tag", t.Id));
        }
    }

    static string ComposeColorTitle(MenuColorPreset row)
    {
        var hexUpper = NormalizeHexForDisplay(row.HexRgb);
        if (!string.IsNullOrWhiteSpace(row.Label))
            return $"🎨 Color — {row.Label}";
        if (!string.IsNullOrWhiteSpace(hexUpper))
            return $"🎨 Color #{hexUpper}";
        return "🎨 Color";
    }

    static string NormalizeHexForDisplay(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return string.Empty;
        var s = raw.Trim().TrimStart('#');
        return s.Length == 6 ? s.ToUpperInvariant() : string.Empty;
    }
}
