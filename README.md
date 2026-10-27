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
- **Quick access.** Pin or unpin a folder from the same menu; the menu always shows the one that applies.
- **Your own menu.** Show, hide, rename and reorder the items of the right-click menu, add separators.
- **Manage folders.** One list of every folder with a color, tags or a pin, with buttons to clear each of them.
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
   and, if needed, to install the .NET 8 Desktop Runtime (included in the installer, no download).
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
| Generated folder icons | `%LOCALAPPDATA%\FolderManaging\Icons` |
| Error log (*Error log…* in the app) | `%LOCALAPPDATA%\FolderManaging\errors.log` |

Nothing is sent anywhere; the app has no network access of its own.

## Build from source

Requirements: Windows 11, [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0), and for the installer the
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
