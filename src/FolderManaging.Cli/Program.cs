// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 maximusfabricator

using FolderManaging.Cli.Cli;
using FolderManaging.Cli.Shell;
using FolderManaging.Core;
using FolderManaging.Core.Diagnostics;
using FolderManaging.Core.Localization;
using FolderManaging.Core.QuickAccess;
using FolderManaging.Core.ShellProperties;
using FolderManaging.Core.Storage;

namespace FolderManaging.Cli;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        UiText.ApplyToProcess();

        if (args.Length > 0 &&
            string.Equals(args[0].Trim(), "--elevated-worker", StringComparison.OrdinalIgnoreCase))
        {
            return ElevatedCliInvoker.RunWorkerServer();
        }

        if (args.Length > 0 &&
            string.Equals(args[0].Trim(), "repair-pins", StringComparison.OrdinalIgnoreCase))
        {
            // --auto: started after a color change; small batches only and never waits for another run.
            var automatic = args.Skip(1).Any(a => string.Equals(a.Trim(), "--auto", StringComparison.OrdinalIgnoreCase));
            var result = QuickAccessService.RefreshStalePins(automatic);
            Console.WriteLine($"{result.Outcome}: {string.Join(" | ", result.Paths)} {result.Reason}".TrimEnd());
            return result.Outcome is PinRefreshOutcome.Failed ? 14 : 0;
        }

        if (args.Length > 0 &&
            string.Equals(args[0].Trim(), "regenerate-folder-icons", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                FolderTintedPresetIconWriter.WriteAllPresets(forceRegenerate: true);
                Console.WriteLine($"Preset icons written to: {AppPaths.GeneratedIconsDirectory}");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex);
                return 9;
            }
        }

        if (args.Length > 0 &&
            string.Equals(args[0].Trim(), "force-refresh-icons", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                return ExplorerIconCacheRefresher.Run();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex);
                return 11;
            }
        }

        if (args.Length > 0 &&
            string.Equals(args[0].Trim(), "register-property-schema", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                string? propdesc = null;
                for (var i = 1; i < args.Length; i++)
                {
                    var a = args[i].Trim();
                    if (string.Equals(a, "--propdesc", StringComparison.OrdinalIgnoreCase))
                    {
                        if (i + 1 >= args.Length)
                        {
                            Console.Error.WriteLine("Missing value after --propdesc.");
                            return 12;
                        }

                        propdesc = args[++i].Trim().Trim('"');
                        continue;
                    }

                    Console.Error.WriteLine($"Unknown argument: {a}");
                    return 12;
                }

                // Rating←ColorHue map is required; .propdesc is optional (custom name rarely shows for folders).
                var source = PropertySchemaRegistration.TryFindBundledPropdesc(propdesc);
                PropertySchemaRegistration.Register(source);
                Console.WriteLine(source is null
                    ? "Registered DesktopIniPropertyMap for ColorHue (and Rating fallback)."
                    : $"Registered ColorHue schema + maps from '{source}'.");
                Console.WriteLine("Explorer: Sort → More… → \"" + UiText.Get("ColumnName") + "\" → ascending.");
                Console.WriteLine("Fallback columns: Tags / Title.");
                return 0;
            }
            catch (UnauthorizedAccessException ex)
            {
                Console.Error.WriteLine(ex.Message);
                Console.Error.WriteLine("Run this command elevated (Administrator).");
                return 12;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex);
                return 12;
            }
        }

        if (args.Length > 0 &&
            string.Equals(args[0].Trim(), "unregister-property-schema", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                PropertySchemaRegistration.Unregister();
                Console.WriteLine("Unregistered FolderManaging property schema (best-effort).");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex);
                return 13;
            }
        }

        if (args.Length > 0 &&
            string.Equals(args[0].Trim(), "cleanup-managed-inis", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                string? root = null;
                var maxDepth = 12;
                var recursive = true;
                for (var i = 1; i < args.Length; i++)
                {
                    var a = args[i].Trim();
                    if (string.Equals(a, "--root", StringComparison.OrdinalIgnoreCase))
                    {
                        if (i + 1 >= args.Length)
                        {
                            Console.Error.WriteLine("Missing value after --root.");
                            return 8;
                        }

                        root = args[++i].Trim().Trim('"');
                        continue;
                    }

                    if (string.Equals(a, "--max-depth", StringComparison.OrdinalIgnoreCase))
                    {
                        if (i + 1 >= args.Length || !int.TryParse(args[++i].Trim(), out maxDepth))
                        {
                            Console.Error.WriteLine("Invalid or missing --max-depth.");
                            return 8;
                        }

                        continue;
                    }

                    if (string.Equals(a, "--no-recursive", StringComparison.OrdinalIgnoreCase))
                    {
                        recursive = false;
                        continue;
                    }

                    Console.Error.WriteLine($"Unknown argument: {a}");
                    return 8;
                }

                root ??= Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                var removed = FolderIconManager.CleanupManagedDesktopInis(root, maxDepth, recursive);
                Console.WriteLine($"Removed {removed} managed desktop.ini file(s) under '{Path.GetFullPath(root)}'.");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex);
                return 8;
            }
        }

        var parsed = CliOptionsParser.Parse(args);
        switch (parsed)
        {
            case CliHelpRequested help:
                Console.Out.WriteLine(help.Text);
                return 0;

            case CliParseError err:
                Console.Error.WriteLine(err.Message);
                if (!string.Equals(err.Message, CliOptionsParser.UsageText, StringComparison.Ordinal))
                {
                    Console.Error.WriteLine();
                    Console.Error.WriteLine(CliOptionsParser.UsageText);
                }

                return 1;

            case InstallContextMenuCommand install:
                try
                {
                    var exe = string.IsNullOrWhiteSpace(install.CustomExecutablePath)
                        ? Environment.ProcessPath
                        : install.CustomExecutablePath;
                    ContextMenuRegistry.Install(exe!);
                    Console.WriteLine("Context menu registered for the current user (HKCU\\Software\\Classes).");
                    Console.WriteLine("Restart Explorer or sign out if menus do not appear immediately.");
                    return 0;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine(ex.Message);
                    return 2;
                }

            case UninstallContextMenuCommand:
                ContextMenuRegistry.Uninstall();
                Console.WriteLine("Context menu entries removed (if present).");
                return 0;

            case InvokeVerbCommand verb:
                return await HandleVerbAsync(verb).ConfigureAwait(false);

            case ShellPreviewCommand preview:
                Console.WriteLine(
                    $"[shell-preview] kind={preview.Kind} path={preview.Path} token={preview.Token ?? "(none)"}");
                return 0;

            default:
                Console.Error.WriteLine("Unknown command.");
                return 3;
        }
    }

    private static async Task<int> HandleVerbAsync(InvokeVerbCommand verb)
    {
        using var scope = ClickTrace.Begin(
            "cli",
            verb.Action,
            detail: verb.Value ?? verb.Tag,
            path: verb.Path);
        TraceLog.Append("shell-menu.log", $"[CLI] [START] action={verb.Action} path={verb.Path} value={verb.Value ?? "(none)"} tag={verb.Tag ?? "(none)"}");

        IFolderStateStore store = new JsonFolderStateStore();
        var editor = new FolderStateEditor(store);

        try
        {
            switch (verb.Action.ToLowerInvariant())
            {
                case "reset":
                {
                    TraceLog.Append("shell-menu.log", $"[CLI] [PROCESS] FolderIconManager.RemoveCustomization");
                    FolderIconManager.RemoveCustomizationIfOurs(verb.Path);
                    await editor.ResetFolderAsync(verb.Path, CancellationToken.None).ConfigureAwait(false);
                    Console.WriteLine($"Restored default folder icon for '{verb.Path}'.");
                    TraceLog.Append("shell-menu.log", "[CLI] [SUCCESS] reset");
                    ClickTrace.Ok();
                    scope.MarkFinished();
                    return 0;
                }

                case "color":
                {
                    var colorId = string.IsNullOrWhiteSpace(verb.Value) ? FolderColors.Red : verb.Value!;
                    TraceLog.Append("shell-menu.log", $"[CLI] [PROCESS] FolderIconManager.Apply colorId={colorId}");
                    FolderIconManager.Apply(verb.Path, colorId);
                    await editor.SetColorTokenAsync(verb.Path, colorId, CancellationToken.None).ConfigureAwait(false);
                    Console.WriteLine($"Applied folder color '{colorId}' for '{verb.Path}'.");
                    TraceLog.Append("shell-menu.log", "[CLI] [SUCCESS] color");
                    ClickTrace.Ok($"color={colorId}");
                    ColorSortTip.TryShowOnce();
                    scope.MarkFinished();
                    return 0;
                }

                case "rgb":
                {
                    if (string.IsNullOrWhiteSpace(verb.Value) || !MenuColorHex.TryParse(verb.Value, out var tint))
                    {
                        Console.Error.WriteLine("rgb requires --value RRGGBB.");
                        ClickTrace.Fail("missing or bad --value for rgb");
                        scope.MarkFinished();
                        return 4;
                    }

                    var hex = MenuColorHex.ToHexRgb(tint);
                    TraceLog.Append("shell-menu.log", $"[CLI] [PROCESS] FolderIconManager.ApplyRgb #{hex}");
                    FolderIconManager.ApplyRgb(verb.Path, tint);
                    await editor.SetColorTokenAsync(verb.Path, $"rgb-{hex.ToLowerInvariant()}", CancellationToken.None).ConfigureAwait(false);
                    Console.WriteLine($"Applied RGB #{hex} for '{verb.Path}'.");
                    TraceLog.Append("shell-menu.log", "[CLI] [SUCCESS] rgb");
                    ClickTrace.Ok($"rgb=#{hex}");
                    ColorSortTip.TryShowOnce();
                    scope.MarkFinished();
                    return 0;
                }

                case "tag":
                    if (string.IsNullOrWhiteSpace(verb.Tag))
                    {
                        Console.Error.WriteLine("Tagging requires --tag (interactive picker comes in Phase 5).");
                        ClickTrace.Fail("missing --tag");
                        scope.MarkFinished();
                        return 4;
                    }

                    TraceLog.Append("shell-menu.log", $"[CLI] [PROCESS] UpsertTag tag={verb.Tag}");
                    FolderIconManager.EnsureFolderWritable(verb.Path);
                    await editor.UpsertTagAsync(verb.Path, verb.Tag, CancellationToken.None).ConfigureAwait(false);
                    FolderIconManager.SyncFolderTags(verb.Path);
                    Console.WriteLine($"Added tag '{verb.Tag}' for '{verb.Path}'.");
                    TraceLog.Append("shell-menu.log", "[CLI] [SUCCESS] tag");
                    ClickTrace.Ok($"tag={verb.Tag}");
                    scope.MarkFinished();
                    return 0;

                case "pin":
                    TraceLog.Append("shell-menu.log", "[CLI] [PROCESS] PinManager.PinToQuickAccess");
                    PinManager.PinToQuickAccess(verb.Path);
                    await editor.SetPinnedAsync(verb.Path, pinned: true, CancellationToken.None).ConfigureAwait(false);
                    Console.WriteLine($"Pinned '{verb.Path}' to Quick Access.");
                    TraceLog.Append("shell-menu.log", "[CLI] [SUCCESS] pin");
                    ClickTrace.Ok();
                    scope.MarkFinished();
                    return 0;

                case "unpin":
                    TraceLog.Append("shell-menu.log", "[CLI] [PROCESS] PinManager.UnpinFromQuickAccess");
                    PinManager.UnpinFromQuickAccess(verb.Path);
                    await editor.SetPinnedAsync(verb.Path, pinned: false, CancellationToken.None).ConfigureAwait(false);
                    Console.WriteLine($"Unpinned '{verb.Path}' from Quick Access.");
                    TraceLog.Append("shell-menu.log", "[CLI] [SUCCESS] unpin");
                    ClickTrace.Ok();
                    scope.MarkFinished();
                    return 0;

                default:
                    Console.Error.WriteLine($"Unsupported action '{verb.Action}'.");
                    ClickTrace.Fail($"unsupported action {verb.Action}");
                    scope.MarkFinished();
                    return 5;
            }
        }
        catch (Exception ex)
        {
            TraceLog.Append("shell-menu.log", $"[CLI] [FAIL] {ex}");
            ClickTrace.Fail(ex);
            scope.MarkFinished();
            Console.Error.WriteLine(ex.Message);
            return ex is IOException or UnauthorizedAccessException ? 6 : 7;
        }
    }
}
