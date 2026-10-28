// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 maximusfabricator

using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using FolderManaging.Core;
using FolderManaging.Core.Diagnostics;
using FolderManaging.Core.Storage;
using FolderManaging.Shell.Diagnostics;
using Vanara.PInvoke;

namespace FolderManaging.Shell.Com;

[ComVisible(true)]
internal sealed class MenuActionExplorerCommand : Shell32.IExplorerCommand
{
    private readonly string _verbId;
    private readonly string _title;
    private readonly string _kind;
    private readonly string? _token;
    private readonly string? _menuIconReference;
    private readonly Shell32.EXPCMDFLAGS _flags;
    private readonly bool _disabled;
    private readonly bool _omitIcon;
    private readonly string? _checkedTitle;

    internal MenuActionExplorerCommand(
        string verbId,
        string title,
        string kind,
        string? token,
        string? menuIconReference = null,
        Shell32.EXPCMDFLAGS flags = Shell32.EXPCMDFLAGS.ECF_DEFAULT,
        bool disabled = false,
        bool omitIcon = false,
        string? checkedTitle = null)
    {
        _verbId = verbId;
        _title = title;
        _kind = kind;
        _token = token;
        _menuIconReference = menuIconReference;
        _flags = flags;
        _disabled = disabled;
        _omitIcon = omitIcon;
        _checkedTitle = checkedTitle;
    }

    public HRESULT GetTitle(Shell32.IShellItemArray? psiItemArray, out string? ppszName)
    {
        ppszName = _checkedTitle is not null && IsTagOnSelection(psiItemArray) ? _checkedTitle : _title;
        return HRESULT.S_OK;
    }

    /// <summary>Tag rows show ✅ when every selected folder already has the tag (a click then removes it).</summary>
    bool IsTagOnSelection(Shell32.IShellItemArray? items)
    {
        try
        {
            var paths = ShellItemPaths.GetAllPaths(items);
            if (paths.Count == 0 || string.IsNullOrWhiteSpace(_token))
                return false;
            var doc = MenuStateReader.GetMerged();
            return paths.All(p => FolderStateTagSync.FolderHasTag(doc, p, _token));
        }
        catch (Exception ex)
        {
            ShellActionLog.Write($"GetTitle tag state failed (verb={_verbId}): {ex.Message}");
            return false;
        }
    }

    public HRESULT GetIcon(Shell32.IShellItemArray? psiItemArray, out string? ppszIcon)
    {
        if (_omitIcon)
        {
            // Documented "no icon": null + E_NOTIMPL. (S_OK/S_FALSE with null crashes Win11 FileExplorer.)
            ppszIcon = null;
            return HRESULT.E_NOTIMPL;
        }

        if (!string.IsNullOrEmpty(_menuIconReference))
        {
            ppszIcon = _menuIconReference;
            return HRESULT.S_OK;
        }

        ppszIcon = "imageres.dll,-3";
        return HRESULT.S_OK;
    }

    public HRESULT GetToolTip(Shell32.IShellItemArray? psiItemArray, out string? ppszInfotip)
    {
        ppszInfotip = _title;
        return HRESULT.S_OK;
    }

    public HRESULT GetCanonicalName(out Guid pguidCommandName)
    {
        pguidCommandName = CommandGuids.ForVerb(_verbId);
        return HRESULT.S_OK;
    }

    public HRESULT GetState(Shell32.IShellItemArray? psiItemArray, bool fOkToBeSlow, out Shell32.EXPCMDSTATE pCmdState)
    {
        try
        {
            if (_disabled || string.Equals(_kind, "noop", StringComparison.OrdinalIgnoreCase))
            {
                pCmdState = Shell32.EXPCMDSTATE.ECS_DISABLED;
                return HRESULT.S_OK;
            }

            pCmdState = Shell32.EXPCMDSTATE.ECS_ENABLED;

            // Pin shows when a selected folder is not pinned yet, Unpin when one is (both for a mixed selection).
            var kind = _kind.ToLowerInvariant();
            if (kind is "pin" or "unpin")
            {
                var paths = ShellItemPaths.GetAllPaths(psiItemArray);
                if (paths.Count == 0)
                {
                    // If we can't resolve a real folder path, hide the action (better UX than showing broken items).
                    pCmdState = Shell32.EXPCMDSTATE.ECS_HIDDEN;
                    return HRESULT.S_OK;
                }

                var show = kind == "pin"
                    ? paths.Any(p => !PinnedStateCache.IsPinned(p))
                    : paths.Any(PinnedStateCache.IsPinned);
                if (!show)
                    pCmdState = Shell32.EXPCMDSTATE.ECS_HIDDEN;
            }

            return HRESULT.S_OK;
        }
        catch (Exception ex)
        {
            ShellActionLog.Write($"GetState crash (verb={_verbId}, kind={_kind}): {ex}");
            pCmdState = Shell32.EXPCMDSTATE.ECS_HIDDEN;
            return HRESULT.S_OK;
        }
    }

    public HRESULT Invoke(Shell32.IShellItemArray? psiItemArray, IBindCtx? pbc)
    {
        // Outermost guard: any throw escaping this method crashes Explorer's surrogate silently. Catch absolutely everything
        // and dump it to the public log so we still see the failure even when AppData / managed logger is broken.
        ClickTrace.Scope? click = null;
        try
        {
            click = ClickTrace.Begin(
                source: "context-menu",
                action: _kind,
                detail: string.IsNullOrWhiteSpace(_token) ? _verbId : $"{_verbId}/{_token}",
                path: null);

            ExtremeLogger.LogEnvironmentSnapshot($"Invoke entry verb={_verbId} kind={_kind} token={_token ?? "(none)"} click={ClickTrace.Current}");
            ExtremeLogger.Log($"[COM] [START] Invoke verb={_verbId} kind={_kind} token={_token ?? "(none)"} click={ClickTrace.Current}");
            ShellActionLog.Write($"[COM] [START] Invoke verb={_verbId} kind={_kind} token={_token ?? "(none)"} click={ClickTrace.Current}");

            var kindInvoke = _kind.ToLowerInvariant();
            if (kindInvoke == "noop")
            {
                ClickTrace.Ok("noop separator");
                click.MarkFinished();
                return HRESULT.S_OK;
            }

            if (kindInvoke == "settings")
            {
                ComVerbExecutor.OpenSettings();
                ShellActionLog.Write($"[COM] [SUCCESS] verb={_verbId} kind=settings");
                ClickTrace.Ok("settings launched");
                click.MarkFinished();
                return HRESULT.S_OK;
            }

            IReadOnlyList<string> paths;
            try
            {
                paths = ShellItemPaths.GetAllPaths(psiItemArray);
            }
            catch (Exception ex)
            {
                ExtremeLogger.LogException(ex, "ShellItemPaths.GetAllPaths threw");
                ClickTrace.Fail(ex);
                click.MarkFinished();
                return HRESULT.E_FAIL;
            }

            if (paths.Count == 0)
            {
                ShellActionLog.Write($"[COM] [FAIL] path unresolved verb={_verbId} kind={_kind}");
                ClickTrace.Fail("path unresolved");
                click.MarkFinished();
                return HRESULT.E_INVALIDARG;
            }

            ClickTrace.Step($"resolved {paths.Count} path(s), first='{paths[0]}'");
            ShellActionLog.Write($"[COM] [PROCESS] resolved {paths.Count} path(s), first={paths[0]}");

            try
            {
                switch (kindInvoke)
                {
                    case "color" or "rgb" or "recent":
                    {
                        if (string.IsNullOrWhiteSpace(_token))
                            throw new ArgumentException("Color token is required.");
                        var isRgb = kindInvoke == "rgb" ||
                                    (kindInvoke == "recent" && _token.StartsWith("rgb-", StringComparison.OrdinalIgnoreCase));
                        var value = isRgb && _token.StartsWith("rgb-", StringComparison.OrdinalIgnoreCase) ? _token[4..] : _token;
                        var actionKind = isRgb ? FolderActionKind.Rgb : FolderActionKind.Color;
                        ComVerbExecutor.RunBatch(paths.Select(p => new FolderAction(actionKind, p, value)).ToList());
                        break;
                    }

                    case "reset":
                        ComVerbExecutor.RunBatch(paths.Select(p => new FolderAction(FolderActionKind.Reset, p)).ToList());
                        break;

                    case "tag":
                    {
                        if (string.IsNullOrWhiteSpace(_token))
                            throw new ArgumentException("Tag id is required.");
                        var add = ComVerbExecutor.TagShouldAdd(paths, _token);
                        var kind = add ? FolderActionKind.Tag : FolderActionKind.Untag;
                        ComVerbExecutor.RunBatch(paths.Select(p => new FolderAction(kind, p, _token)).ToList());
                        break;
                    }

                    case "pin":
                        ComVerbExecutor.Pin(paths.Where(p => !PinnedStateCache.IsPinned(p)).ToList());
                        break;

                    case "unpin":
                        ComVerbExecutor.Unpin(paths.Where(PinnedStateCache.IsPinned).ToList());
                        break;

                    case "quick-color":
                        ComVerbExecutor.OpenQuickColor(paths);
                        break;

                    default:
                        ClickTrace.Fail($"unknown menu kind={_kind}");
                        click.MarkFinished();
                        return HRESULT.E_NOTIMPL;
                }

                ShellActionLog.Write($"[COM] [SUCCESS] verb={_verbId} kind={kindInvoke} token={_token ?? "(none)"}");
                ClickTrace.Ok($"{kindInvoke} x{paths.Count}");
                click.MarkFinished();
                return HRESULT.S_OK;
            }
            catch (Exception ex)
            {
                ExtremeLogger.LogException(
                    ex,
                    $"verb-execution failure verb={_verbId} kind={_kind} token={_token ?? "(none)"} first={paths[0]}");
                ShellActionLog.Write($"[COM] [FAIL] verb-execution verb={_verbId} kind={_kind} ex={ex}");
                UserFeedback.ReportFailure(paths[0], ex);
                ClickTrace.Fail(ex);
                click.MarkFinished();
                return HRESULT.E_FAIL;
            }
        }
        catch (Exception ex)
        {
            ExtremeLogger.LogException(ex, $"Invoke catastrophic failure verb={_verbId} kind={_kind}");
            try
            {
                ShellActionLog.Write($"[COM] [FAIL] Invoke catastrophic verb={_verbId} kind={_kind} ex={ex}");
                ClickTrace.Fail(ex);
                click?.MarkFinished();
            }
            catch
            {
                // Swallow: tracing channels can fail in the surrogate too.
            }

            return HRESULT.E_FAIL;
        }
        finally
        {
            click?.Dispose();
        }
    }

    public HRESULT GetFlags(out Shell32.EXPCMDFLAGS pFlags)
    {
        pFlags = _flags;
        return HRESULT.S_OK;
    }

    public HRESULT EnumSubCommands(out Shell32.IEnumExplorerCommand? ppEnum)
    {
        ppEnum = null;
        return HRESULT.E_NOTIMPL;
    }
}
