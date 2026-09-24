using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using Vanara.PInvoke;

namespace FolderManaging.Shell.Com;

/// <summary>Top-level "Folder Manager" node: one cascade level (Explorer does not support nested cascades).</summary>
[ComVisible(true)]
[Guid(ComConstants.FolderManagerCommandClsid)]
[ClassInterface(ClassInterfaceType.None)]
public sealed class FolderManagerRootExplorerCommand : Shell32.IExplorerCommand
{
    public HRESULT GetTitle(Shell32.IShellItemArray? psiItemArray, out string? ppszName)
    {
        ppszName = MenuBuilder.GetRootTitle();
        return HRESULT.S_OK;
    }

    public HRESULT GetIcon(Shell32.IShellItemArray? psiItemArray, out string? ppszIcon)
    {
        ppszIcon = "imageres.dll,-3";
        return HRESULT.S_OK;
    }

    public HRESULT GetToolTip(Shell32.IShellItemArray? psiItemArray, out string? ppszInfotip)
    {
        ppszInfotip = "Folder Managing tools";
        return HRESULT.S_OK;
    }

    public HRESULT GetCanonicalName(out Guid pguidCommandName)
    {
        pguidCommandName = new Guid(ComConstants.FolderManagerCommandClsid);
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
        pFlags = Shell32.EXPCMDFLAGS.ECF_HASSUBCOMMANDS | Shell32.EXPCMDFLAGS.ECF_ISDROPDOWN;
        return HRESULT.S_OK;
    }

    public HRESULT EnumSubCommands(out Shell32.IEnumExplorerCommand? ppEnum)
    {
        ppEnum = new ExplorerCommandsEnumerator(MenuBuilder.Build());
        return HRESULT.S_OK;
    }
}
