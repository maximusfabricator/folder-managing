# Changelog

## 2.0.0 — 2026-10-27

First public release.

### License

- Folder Managing is now free software under the GNU General Public License, version 2 only (GPL-2.0-only), the same
  license as the Linux kernel. Copyright (C) 2026 maximusfabricator.
- Every C#, XAML and PowerShell file carries the license identifier and the copyright notice, and the app's own
  program files name the author in their file properties; the license text and the third-party notices ship with the
  app.
- The name “Folder Managing” and the logo stay with the author; a changed version needs its own name and logo (see
  the README).

### New

- **24 languages:** English, Chinese (Simplified and Traditional), Hindi, Spanish, French, Arabic, Bengali, Portuguese
  (Brazil), Russian, Urdu, Indonesian, German, Japanese, Turkish, Korean, Vietnamese, Italian, Persian, Polish,
  Ukrainian, Thai, Dutch and Norwegian Bokmål.
- **Settings → Language.** By default the app follows the Windows display language. A change applies right away to the
  app and the tray menu, and to the Explorer menu on the next right-click. The *Folder color* and *Folder tags* column
  titles follow when the administrator service runs (otherwise the next time it starts).
- Right-to-left layout for Arabic, Persian and Urdu.
- **About page:** version, author, license summary, the full license text, the source code link and third-party
  notices.
- Message boxes now use the app language for their buttons too, follow the light or dark theme, and can be copied
  with Ctrl+C.
- The installer shows the license and the copyright; Settings → Apps lists the author as publisher and links to the
  project page.

### Fixed

- Removing tags from folders that need administrator rights now takes effect immediately.
- Error messages pointed to a “Settings → Open logs” item that does not exist; they now point to *Error log…*.
- The package description in Windows was garbled (wrong text encoding in the release build).
- On Windows 10 the installer no longer hides the “will be updated” line behind the Windows 10 note.
- Context menu page: item names and the default names in the rename boxes were cut off (“Color wheel (v…”,
  “Restore default fol”), in a small window the item list was empty, and the preview hid its last rows and the row
  count. The rename box now sits under the item, and the preview scrolls.
- Manage folders page: the table no longer floats in the middle of the page. *Unpin*, *Clear tags* and *Clear color*
  are only active when there is something to remove; *Clear tags* on a folder without tags used to rewrite its
  desktop.ini and could ask for administrator rights. Colors show as their name or as #RRGGBB instead of “(red)” or
  “rgb-5cccb2”. In a narrow window the path column shrank to “C:…”; now, when the paths would not fit, the row buttons
  show only their icons, with the name as a tooltip. The tags and pinned cells showed the folder path as their tooltip.
- The administrator service status says how to start the service.

### Changed

- Russian texts were reviewed: consistent formal address and the Windows 11 names for Explorer commands.
- “Reset settings” also resets the language to “Match Windows”.
