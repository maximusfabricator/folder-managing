# Folder Managing

Folder colors, tags and a **Folder Manager** item in the Windows 11 File Explorer right-click menu.

Created by **maximusfabricator** · Free software under the [GNU General Public License, version 2](LICENSE)

## Features

- **Folder colors.** Right-click a folder → *Folder Manager* → pick one of your color presets, the 13-color system
  palette, or any color from the color wheel. The folder icon changes right away; no restart, no Explorer restart.
- **Tags.** Give folders text tags such as *Work* or *Archive*. Each tag has a color and is drawn as a small badge on the
  folder icon.
- **Sort and group in Explorer.** The *Folder color* and *Folder tags* columns let you sort colored folders in rainbow
  order (Sort → More… → *Folder color*) or group them by tag.
- **Several folders at once.** Select folders in Explorer and color, reset or tag all of them; folders that need
  administrator rights share one Windows prompt.
- **Recent colors and color codes.** The menu shows your last three colors; the color window takes a code such as
  `#3A86FF` and saves any color to *My colors*. Ready-made color schemes are on the Colors page.
- **Quick access.** Pin or unpin a folder from the same menu; the menu always shows the one that applies. A pinned
  folder shows its color in the navigation pane too (see the FAQ).
- **Your own menu.** Show, hide, rename and reorder the items of the right-click menu, add separators.
- **Manage folders.** One list of every folder with a color, tags or a pin, with buttons to clear each of them, for
  several rows at once, or for all. Renamed and moved folders stay in the list.
- **Check and repair.** Settings → *Check and repair folders* explains why a colored folder looks plain and fixes it.
- **Export and import** colors, tags, the menu and the folder list, for a backup or another PC.
- **Tray icon.** Double-click it to color any folder quickly. Optional start with Windows.
- **System folders.** Folders such as *Program Files* need administrator rights; confirm the Windows prompt once and
  the app can change them without asking again while the setting is on.
- **24 languages**, light and dark theme in the Windows 11 style.

A colored folder gets a hidden `desktop.ini` that points to a generated icon — the same mechanism Windows uses for
custom folder icons. Apart from the optional tray icon and the optional administrator service, nothing runs in the
background.

## Install

1. Download `FolderManaging-Setup-<version>.exe` from the [Releases](https://github.com/maximusfabricator/folder-managing/releases) page.
2. Run it and click **Install**. Windows asks for administrator rights once: to trust the Folder Managing signature
   and, if needed, to install the .NET 10 Desktop Runtime (included in the installer, no download).
3. Right-click any folder → **Folder Manager**.

Requirements: Windows 11, x64. On Windows 10 the app installs, but the menu item may not appear.

**Update:** run the newer installer; settings, colors and tags are kept.
**Uninstall:** Settings → Apps → Installed apps → Folder Managing → Uninstall. You choose whether colored folders get
their default icon back and whether your settings are deleted.

## Languages

English, 中文（简体）, 中文（繁體）, हिन्दी, Español, Français, العربية, বাংলা, Português (Brasil), Русский, اردو,
Bahasa Indonesia, Deutsch, 日本語, Türkçe, 한국어, Tiếng Việt, Italiano, فارسی, Polski, Українська, ไทย, Nederlands,
Norsk bokmål.

The app follows the Windows display language; change it in **Settings → Language**. The choice also applies to the
installer and to the Explorer menu. Arabic, Persian and Urdu use a right-to-left layout.

The English, Russian and Norwegian texts were written by hand; the other languages were translated with AI assistance.
Corrections from native speakers are very welcome — the texts are plain JSON files in
[`src/FolderManaging.Core/Localization/Strings`](src/FolderManaging.Core/Localization/Strings) (`en.json` is the source).

## Your data

| What | Where |
| --- | --- |
| Settings, colors, tags, folder list | `%USERPROFILE%\.FolderManaging\state.json` |
| Folder icons (readable by every account on the PC) | `%ProgramData%\FolderManaging\Icons` |
| Menu icons; folder icons of version 2.0 | `%LOCALAPPDATA%\FolderManaging\Icons` |
| Error log (*Error log…* in the app) | `%LOCALAPPDATA%\FolderManaging\errors.log` |

Nothing is sent anywhere; the app has no network access of its own.

## FAQ

**The previews of the files inside a colored folder are gone.**
Windows draws a folder either with a custom icon or with small previews of the pictures and documents inside it, never
both. A colored folder has a custom icon, so the previews go away. *Reset* brings them back.

**A colored folder on a network drive or NAS looks plain.**
Windows ignores `desktop.ini` on network locations it does not trust. Add the server to the Local intranet zone:
Control Panel → Internet Options → Security → Local intranet → Sites → Advanced. *Check and repair folders* tells you
when this is the reason.

**I copied a colored folder to another PC (or a USB stick) and it is plain there.**
The color is a `desktop.ini` in the folder plus an icon file on the PC that colored it; the icon does not travel with
the folder. Install Folder Managing on the other PC, use Settings → *Export* here and *Import* there, or color the
folder again.

**A folder that came from a downloaded ZIP file keeps the standard icon.**
Files from the internet carry a “downloaded from the internet” mark (Mark of the Web), and Windows ignores a marked
`desktop.ini`. *Check and repair folders* removes the mark from the files Folder Managing wrote.

**Other people who use this PC see my colored folders as plain.**
Since 2.1.0 new icons go to a shared folder every account can read. Older folders move over the next time they change;
*Check and repair folders* moves the rest.

**My Quick access pins flicker or move after I color a pinned folder.**
Windows saves a copy of the folder's attributes at the moment you pin it. A folder pinned before it was colored keeps
the old copy, and the navigation pane shows it plain. The only way to update the copy is to unpin and pin again, and a
new pin always goes to the end of the list. So Folder Managing pins that folder and every pin after it again, in the
same order — open Explorer windows redraw once. It does this after you color or reset a pinned folder, once when the
app starts, and from *Check and repair folders*; automatically for at most 8 pins. Windows' own items (Desktop,
Downloads, Documents…), libraries and network locations are never touched. If the PC turns off in the middle, the app
finishes the job on its next start.

**Can I still use “Pin to Quick access” from Windows or in the Open and Save dialogs of other apps?**
Yes. Folder Managing's *Pin folder* uses the same Windows list. A folder you pinned through Windows before coloring it
is refreshed the same way as above.

## Build from source

Requirements: Windows 11, [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0), and for the installer the
Windows SDK (`makeappx.exe`, `signtool.exe`).

```powershell
dotnet build FolderManaging.sln -c Release
dotnet run --project tests\FolderManaging.SelfTests -c Release
```

Installer for users (signs the package with a self-signed certificate created on the first run under
`artifacts\certs`, which is never committed):

```powershell
powershell -ExecutionPolicy Bypass -File scripts\release\Build-UserRelease.ps1
```

The result is `artifacts\release\FolderManaging-Setup-<version>.exe`. The version comes from `Directory.Build.props`.

Project layout:

| Project | Role |
| --- | --- |
| `FolderManaging.Core` | Folder icons, tags, state, localization — shared by everything else |
| `FolderManaging.Shell` | The Explorer context menu (COM `IExplorerCommand`, hosted by Windows in `dllhost`) |
| `FolderManaging.Cli` | Command-line worker used by the menu, the installer and the administrator service |
| `FolderManaging.Companion` | The settings app and tray icon (WPF) |
| `FolderManaging.Setup` | Installer and uninstaller |
| `tests/FolderManaging.SelfTests` | Self-tests: `dotnet run --project tests\FolderManaging.SelfTests -c Release` |

Builds default to the developer edition (`FmEdition=Dev`, extra diagnostics). The release script builds the user
edition (`-p:FmEdition=User`), which has no developer tooling.

## License

Folder Managing is free software, licensed under the **GNU General Public License, version 2 only** (GPL-2.0-only) —
the same license as the Linux kernel. The full text is in [LICENSE](LICENSE).

In short (the license text is what counts):

- **You may** use the app for any purpose, read and study the code, change it, and share copies — modified or not,
  for free or for money.
- **If you share a copy or a changed version**, you must keep it under the same GPL-2.0 license, include or offer its
  complete source code, keep the copyright notices of the original author intact, and mark clearly what you changed.
- **The copyright stays with the author.** Every copy and every derived version keeps the notice
  `Copyright (C) 2026 maximusfabricator`, so the original source is always credited.
- There is **no warranty**.

Original project: <https://github.com/maximusfabricator/folder-managing>

### Name and logo

The name **Folder Managing** and the Folder Managing logo belong to the author, maximusfabricator. They mark the
original app. The license covers the code; it gives no right to present another product as Folder Managing, just as
the license of the Linux kernel does not cover the name “Linux”.

- You may say that your project is based on Folder Managing.
- A changed version that you publish needs its own name and logo, so users can tell it apart from the original and
  know who made it.

### Third-party components

WPF UI, Vanara and the .NET libraries, all under the MIT License, are listed with their license texts in
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

## Contributing

Bug reports, translations and pull requests are welcome. By contributing, you agree that your contribution is licensed
under GPL-2.0-only, like the rest of the project.
