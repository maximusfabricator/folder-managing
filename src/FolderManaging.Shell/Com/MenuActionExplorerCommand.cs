using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using FolderManaging.Core.Diagnostics;
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

    internal MenuActionExplorerCommand(
        string verbId,
        string title,
        string kind,
        string? token,
        string? menuIconReference = null,
        Shell32.EXPCMDFLAGS flags = Shell32.EXPCMDFLAGS.ECF_DEFAULT,
        bool disabled = false,
        bool omitIcon = false)
    {
        _verbId = verbId;
        _title = title;
        _kind = kind;
        _token = token;
        _menuIconReference = menuIconReference;
        _flags = flags;
        _disabled = disabled;
        _omitIcon = omitIcon;
    }

    public HRESULT GetTitle(Shell32.IShellItemArray? psiItemArray, out string? ppszName)
    {
        ppszName = _title;
        return HRESULT.S_OK;
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

            // Dynamic Pin/Unpin visibility (fast path; never blocks Explorer UI).
            var kind = _kind.ToLowerInvariant();
            if (kind is "pin" or "unpin")
            {
                var hr = ShellItemPaths.TryGetFirstPath(psiItemArray, out var path);
                if (!hr.Succeeded || string.IsNullOrWhiteSpace(path))
                {
                    // If we can't resolve a real folder path, hide the action (better UX than showing broken items).
                    pCmdState = Shell32.EXPCMDSTATE.ECS_HIDDEN;
                    return HRESULT.S_OK;
                }

                var isPinned = PinnedStateCache.IsPinned(path);
                if (kind == "pin" && isPinned)
                    pCmdState = Shell32.EXPCMDSTATE.ECS_HIDDEN;
                else if (kind == "unpin" && !isPinned)
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
            IdentityWriteProbe.WritePublicIdentityTest();

            click = ClickTrace.Begin(
                source: "context-menu",
                action: _kind,
                detail: string.IsNullOrWhiteSpace(_token) ? _verbId : $"{_verbId}/{_token}",
                path: null);

            ExtremeLogger.LogEnvironmentSnapshot($"Invoke entry verb={_verbId} kind={_kind} token={_token ?? "(none)"} click={ClickTrace.Current}");
            ExtremeLogger.Log($"[COM] [START] Invoke verb={_verbId} kind={_kind} token={_token ?? "(none)"} click={ClickTrace.Current}");
            ShellActionLog.Write($"[COM] [START] Invoke verb={_verbId} kind={_kind} token={_token ?? "(none)"} click={ClickTrace.Current}");

            var kindInvoke = _kind.ToLowerInvariant();

            string? path = null;
            if (kindInvoke != "settings")
            {
                HRESULT hr;
                try
                {
                    hr = ShellItemPaths.TryGetFirstPath(psiItemArray, out path);
                }
                catch (Exception ex)
                {
                    ExtremeLogger.LogException(ex, "ShellItemPaths.TryGetFirstPath threw");
                    ClickTrace.Fail(ex);
                    click.MarkFinished();
                    return HRESULT.E_FAIL;
                }

                if (!hr.Succeeded || string.IsNullOrWhiteSpace(path))
                {
                    ExtremeLogger.Log($"[COM] [FAIL] path unresolved verb={_verbId} kind={_kind} hr={hr}");
                    ShellActionLog.Write($"[COM] [FAIL] path unresolved verb={_verbId} kind={_kind} hr={hr}");
                    ClickTrace.Fail($"path unresolved hr={hr}");
                    click.MarkFinished();
                    return hr.Succeeded ? HRESULT.E_INVALIDARG : hr;
                }

                ClickTrace.Step($"resolved path='{path}'");
            }

            ExtremeLogger.Log($"[COM] [PROCESS] resolved path='{path ?? "(none)"}'");
            ShellActionLog.Write($"[COM] [PROCESS] resolved path={path ?? "(none)"}");

            try
            {
                switch (kindInvoke)
                {
                    case "color":
                        if (string.IsNullOrWhiteSpace(_token))
                            throw new ArgumentException("Color token is required.");
                        if (string.IsNullOrWhiteSpace(path))
                            throw new InvalidOperationException("color requires a folder path.");
                        ExtremeLogger.Log($"[COM] [PROCESS] dispatch -> ComVerbExecutor.ApplyColor token={_token}");
                        ComVerbExecutor.ApplyColor(path, _token);
                        ExtremeLogger.Log($"[COM] [SUCCESS] verb={_verbId} kind=color token={_token}");
                        ShellActionLog.Write($"[COM] [SUCCESS] verb={_verbId} kind=color token={_token}");
                        ClickTrace.Ok($"color={_token} path='{path}'");
                        click.MarkFinished();
                        return HRESULT.S_OK;

                    case "reset":
                        if (string.IsNullOrWhiteSpace(path))
                            throw new InvalidOperationException("reset requires a folder path.");
                        ExtremeLogger.Log($"[COM] [PROCESS] dispatch -> ComVerbExecutor.ResetColor");
                        ComVerbExecutor.ResetColor(path);
                        ExtremeLogger.Log($"[COM] [SUCCESS] verb={_verbId} kind=reset");
                        ShellActionLog.Write($"[COM] [SUCCESS] verb={_verbId} kind=reset");
                        ClickTrace.Ok($"reset path='{path}'");
                        click.MarkFinished();
                        return HRESULT.S_OK;

                    case "pin":
                        if (string.IsNullOrWhiteSpace(path))
                            throw new InvalidOperationException("pin requires a folder path.");
                        ExtremeLogger.Log($"[COM] [PROCESS] dispatch -> ComVerbExecutor.Pin");
                        ComVerbExecutor.Pin(path);
                        ExtremeLogger.Log($"[COM] [SUCCESS] verb={_verbId} kind=pin");
                        ShellActionLog.Write($"[COM] [SUCCESS] verb={_verbId} kind=pin");
                        ClickTrace.Ok($"pin path='{path}'");
                        click.MarkFinished();
                        return HRESULT.S_OK;

                    case "unpin":
                        if (string.IsNullOrWhiteSpace(path))
                            throw new InvalidOperationException("unpin requires a folder path.");
                        ExtremeLogger.Log($"[COM] [PROCESS] dispatch -> ComVerbExecutor.Unpin");
                        ComVerbExecutor.Unpin(path);
                        ExtremeLogger.Log($"[COM] [SUCCESS] verb={_verbId} kind=unpin");
                        ShellActionLog.Write($"[COM] [SUCCESS] verb={_verbId} kind=unpin");
                        ClickTrace.Ok($"unpin path='{path}'");
                        click.MarkFinished();
                        return HRESULT.S_OK;

                    case "tag":
                        if (string.IsNullOrWhiteSpace(_token))
                            throw new ArgumentException("Tag id is required.");
                        if (string.IsNullOrWhiteSpace(path))
                            throw new InvalidOperationException("tag requires a folder path.");
                        ExtremeLogger.Log($"[COM] [PROCESS] dispatch -> ComVerbExecutor.Tag token={_token}");
                        ComVerbExecutor.Tag(path, _token);
                        ExtremeLogger.Log($"[COM] [SUCCESS] verb={_verbId} kind=tag token={_token}");
                        ShellActionLog.Write($"[COM] [SUCCESS] verb={_verbId} kind=tag token={_token}");
                        ClickTrace.Ok($"tag={_token} path='{path}'");
                        click.MarkFinished();
                        return HRESULT.S_OK;

                    case "noop":
                        ClickTrace.Ok("noop separator");
                        click.MarkFinished();
                        return HRESULT.S_OK;

                    case "settings":
                        ExtremeLogger.Log($"[COM] [PROCESS] dispatch -> ComVerbExecutor.OpenSettings");
                        ComVerbExecutor.OpenSettings();
                        ExtremeLogger.Log($"[COM] [SUCCESS] verb={_verbId} kind=settings");
                        ShellActionLog.Write($"[COM] [SUCCESS] verb={_verbId} kind=settings");
                        ClickTrace.Ok("settings launched");
                        click.MarkFinished();
                        return HRESULT.S_OK;

                    case "quick-color":
                        if (string.IsNullOrWhiteSpace(path))
                            throw new InvalidOperationException("quick-color requires a folder path.");
                        ExtremeLogger.Log($"[COM] [PROCESS] dispatch -> ComVerbExecutor.OpenQuickColor");
                        ComVerbExecutor.OpenQuickColor(path);
                        ExtremeLogger.Log($"[COM] [SUCCESS] verb={_verbId} kind=quick-color");
                        ShellActionLog.Write($"[COM] [SUCCESS] verb={_verbId} kind=quick-color");
                        ClickTrace.Ok($"quick-color path='{path}'");
                        click.MarkFinished();
                        return HRESULT.S_OK;

                    case "rgb":
                        if (string.IsNullOrWhiteSpace(_token))
                            throw new ArgumentException("RGB hex is required.");
                        if (string.IsNullOrWhiteSpace(path))
                            throw new InvalidOperationException("rgb requires a folder path.");
                        ExtremeLogger.Log($"[COM] [PROCESS] dispatch -> ComVerbExecutor.ApplyRgb hex={_token}");
                        ComVerbExecutor.ApplyRgb(path, _token);
                        ExtremeLogger.Log($"[COM] [SUCCESS] verb={_verbId} kind=rgb hex={_token}");
                        ShellActionLog.Write($"[COM] [SUCCESS] verb={_verbId} kind=rgb hex={_token}");
                        ClickTrace.Ok($"rgb=#{_token} path='{path}'");
                        click.MarkFinished();
                        return HRESULT.S_OK;

                    default:
                        ExtremeLogger.Log($"[COM] [PROCESS] dispatch -> ShellCliLauncher.TryLaunchShellPreview kind={_kind}");
                        ShellCliLauncher.TryLaunchShellPreview(_kind, path ?? string.Empty, _token);
                        ClickTrace.Ok($"shell-preview kind={_kind}");
                        click.MarkFinished();
                        return HRESULT.S_OK;
                }
            }
            catch (Exception ex)
            {
                ExtremeLogger.LogException(
                    ex,
                    $"verb-execution failure verb={_verbId} kind={_kind} token={_token ?? "(none)"} path={path}");
                ShellActionLog.Write($"[COM] [FAIL] verb-execution verb={_verbId} kind={_kind} ex={ex}");
                if (kindInvoke is "color" or "rgb" or "reset" && !string.IsNullOrWhiteSpace(path) &&
                    UserFeedback.ReportFolderActionFailure(kindInvoke, path, kindInvoke is "color" or "rgb" ? _token : null, ex))
                {
                    ClickTrace.Ok($"{kindInvoke} done via elevated CLI path='{path}'");
                    click?.MarkFinished();
                    return HRESULT.S_OK;
                }

                ClickTrace.Fail(ex);
                click?.MarkFinished();
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
