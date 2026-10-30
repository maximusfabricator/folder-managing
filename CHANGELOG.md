# Changelog

## 2.2.3 — 2026-10-30

### New

- **Available updates stays on screen.** When a new version is out, a notice stays in the corner until you choose
  *Update* or *Later*. The tray icon keeps an orange mark until you update or skip that version. Closing the notice
  does not hide the mark.

### Fixed

- **Starting the app checks for updates.** About 15 seconds after each start, Folder Managing looks for a new version,
  even if it already checked earlier that day. The update window still scrolls, so the notes and the buttons stay
  on screen.

## 2.2.2 — 2026-10-30

### Fixed

- **The update window scrolls.** The notes and the buttons were cut off, and there was no way to scroll to them.
  The notes now scroll inside the window. *Update*, *Later* and *Skip this version* stay on screen. The window
  grows to fit, up to the size of the screen, and it can be resized.
- **A new version shows up after the app starts.** The check about a minute and a half after start was skipped
  when the app had already checked during the last day, so a release that came out later stayed quiet until
  *Check for updates* was pressed. Starting the app checks again. While it keeps running, it still checks once a
  day. The window comes to the front, and the tray names the version. Full-screen apps, games, presentations and
  Focus assist still wait.

## 2.2.1 — 2026-10-30

### Fixed

- **Built-in colors can be hidden.** On Folder colors, Red, Blue, Green and Purple had a Delete button that did
  nothing. It is now Hide. A hidden color leaves the list, the Explorer menu and the Quick color window. A folder
  that already uses it keeps that color. Bring it back with Restore, under Hidden colors.
- Each color of a color scheme has Hide too. Hiding it is kept when you turn that scheme off and on again. Turning
  a scheme on does not bring a hidden color back.
- Colors you added yourself still have Delete.

## 2.2.0 — 2026-10-29

### New

- **Update notifications.** Once a day the app checks the GitHub releases of Folder Managing. When a new version is
  out, a small window shows what is new, with *Update*, *Later* and *Skip this version*. It does not take the focus
  and waits while you are in a full-screen app, a game or a presentation; the tray menu shows the update too.
- **Update** downloads the setup, checks that it is the published one, closes the app, installs and opens it again.
  No setup pages to click through; your settings, colors, tags, start with Windows and the desktop shortcut stay as
  they were. Windows asks for administrator rights only when the update needs them.
- **Settings → Check for updates automatically** turns the daily check off. *Check for updates* is in Settings, on the
  About page and in the tray menu. The About page shows when the app last checked.
- Only two small files are downloaded for a check; nothing about you or your folders is sent. See *Updates* in the
  README.
- Command line: `foldermanaging-cli update --check` shows what the check finds, `--json` for scripts.

### Note

- Version 2.1.1 and older cannot update themselves: install 2.2.0 once with its setup. Later versions arrive by
  themselves.

## 2.1.1 — 2026-10-29

### Changed

- **Color schemes are now built in.** Vivid, Pastel, Earth, Ocean and Work status are no longer copied into *My colors*:
  on the *Folder colors* page each scheme has its own switch. The scheme list and the *Add colors* button are gone —
  you turn a scheme on or off, nothing is added. Schemes cannot be changed or deleted, and their names follow the app
  language.
- The colors of the schemes you turn on appear in the Explorer menu, each scheme under its own heading, and in the
  quick color window. In the menu they are one block that can be moved or hidden on the Context menu page.
- All schemes are off at first, also after the update. Colors you added from a scheme in 2.1.0 stay in *My colors*.
- A folder keeps its color when you turn its scheme off.
- *Export and import* now also saves which schemes are on.

## 2.1.0 — 2026-10-28

### New

- **Several folders at once.** Select folders in Explorer and pick a color, *Reset* or a tag: all of them change.
  Folders that need administrator rights are done together, with one Windows (UAC) prompt.
- **Quick color window:** type a color code (#3A86FF, 3A86FF or the short #38F), see your last five colors, and save
  a color to *My colors* with one click. The Explorer menu has a new *Recent* block with your last three colors (it can
  be moved or hidden on the Context menu page).
- **Ready-made color schemes** on the Colors page: Vivid, Pastel, Earth, Ocean and Work status. *Add colors* puts the
  scheme into *My colors*; colors you already have are skipped.
- **Settings → Check and repair folders** explains why a colored folder looks plain and fixes what can be fixed: a
  missing desktop.ini or icon, a missing Read-only attribute, the “downloaded from the internet” mark, icons only your
  account can see, and Quick access pins that still show the folder as it was before it was colored. For a missing
  folder, a folder you cannot open, or a network folder Windows does not trust, it tells you what to do.
- **Settings → Export and import:** save colors, tags, the menu layout and the list of colored folders to a file and
  load it on another PC or after a reinstall. Folders that do not exist there are skipped; nothing is deleted.
- **Manage folders:** select several rows with Ctrl or Shift and *Reset selected*, or *Reset all* (Quick access pins
  stay).
- **Renamed and moved folders keep their place in the list.** Each colored folder gets an id in its desktop.ini; when
  you rename a folder in Explorer, or move it next to where it was, the app follows it instead of forgetting it.
- **Folder icons for every account.** Icons are now stored in a shared folder (%ProgramData%\FolderManaging\Icons), so
  other people who sign in to the same PC see your colored folders too. Existing folders move over by themselves the
  next time the app updates them; folders that need administrator rights move over with *Check and repair folders*.

### Fixed

- *Unpin folder* in the Explorer menu did nothing, and *Pin folder* on a folder that was already pinned unpinned it.
  Both now do exactly what they say, and the Manage folders page shows an error instead of pretending a pin was
  removed.
- A folder that was pinned to Quick access before it was colored stayed plain in the navigation pane. The app now
  pins such a folder again, together with the pins after it, in the same order. This happens after coloring or
  resetting a pinned folder, once when the app starts, and from *Check and repair folders*. Windows' own items
  (Desktop, Downloads, Documents and so on), libraries and network locations are never touched.

### Changed

- The app now runs on .NET 10 (long-term support until November 2028). The installer brings the .NET 10 Desktop Runtime
  along if it is missing.
- The settings file gets a new format. The first time 2.1.0 saves it, the old file is kept next to it as
  state.v1.backup.json.

## 2.0.1 — 2026-10-28

### Fixed

- The taskbar showed the app icon on a square in the Windows accent color. The package now has icons in every
  taskbar size without that background, made from the app icon itself.
- The program files no longer contain the folder path of the PC the release was built on.

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
