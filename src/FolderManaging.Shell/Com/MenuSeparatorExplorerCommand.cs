// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 maximusfabricator

using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using Vanara.PInvoke;

namespace FolderManaging.Shell.Com;

/// <summary>
/// Standalone separator command. Win11 modern menu often ignores these unless
/// <see cref="GetTitle"/> returns a null pointer (not an empty string). Prefer
/// <see cref="Shell32.EXPCMDFLAGS.ECF_SEPARATORBEFORE"/> on the next real item.
/// </summary>
[ComVisible(true)]
internal sealed class MenuSeparatorExplorerCommand : Shell32.IExplorerCommand
{
    private readonly string _verbId;

    internal MenuSeparatorExplorerCommand(string verbId) => _verbId = verbId;

    public HRESULT GetTitle(Shell32.IShellItemArray? psiItemArray, out string? ppszName)
    {
        // Never null — Win11 modern menu can AV if a subcommand title pointer is null.
        ppszName = string.Empty;
        return HRESULT.S_OK;
    }

    public HRESULT GetIcon(Shell32.IShellItemArray? psiItemArray, out string? ppszIcon)
    {
        ppszIcon = "imageres.dll,-3";
        return HRESULT.S_OK;
    }

    public HRESULT GetToolTip(Shell32.IShellItemArray? psiItemArray, out string? ppszInfotip)
    {
        ppszInfotip = string.Empty;
        return HRESULT.S_OK;
    }

    public HRESULT GetCanonicalName(out Guid pguidCommandName)
    {
        pguidCommandName = CommandGuids.ForVerb("sep:" + _verbId);
        return HRESULT.S_OK;
    }

    public HRESULT GetState(Shell32.IShellItemArray? psiItemArray, bool fOkToBeSlow, out Shell32.EXPCMDSTATE pCmdState)
    {
        pCmdState = Shell32.EXPCMDSTATE.ECS_ENABLED;
        return HRESULT.S_OK;
    }

    public HRESULT Invoke(Shell32.IShellItemArray? psiItemArray, IBindCtx? pbc) => HRESULT.S_OK;

    public HRESULT GetFlags(out Shell32.EXPCMDFLAGS pFlags)
    {
        pFlags = Shell32.EXPCMDFLAGS.ECF_ISSEPARATOR;
        return HRESULT.S_OK;
    }

    public HRESULT EnumSubCommands(out Shell32.IEnumExplorerCommand? ppEnum)
    {
        ppEnum = null;
        return HRESULT.E_NOTIMPL;
    }
}
