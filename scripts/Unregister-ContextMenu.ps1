<#!
Removes the per-user context menu registration. Pass the same CLI binary used for install
so its embedded uninstall logic runs (or use any built copy of FolderManaging.Cli.exe).

Example:
  .\Unregister-ContextMenu.ps1 -ExePath "D:\Important\FolderManaging\artifacts\FolderManaging.Cli.exe"
#>
param(
    [Parameter(Mandatory = $true)]
    [string] $ExePath
)

$resolved = (Resolve-Path -LiteralPath $ExePath).Path
& $resolved uninstall-context-menu
