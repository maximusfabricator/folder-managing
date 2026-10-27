# Explorer sort by color (rainbow / hue)

Colored folders store a numeric HSV **hue** (`0`–`360`) in their managed `desktop.ini`. Uncolored folders are **not** modified — no `999` placeholders on siblings.

## What gets written (colored folder only)

```ini
[FolderManaging]
ColorHue=282

[{A7C4E8F1-2B3D-4E5F-9A0B-1C2D3E4F5A6B}]
Prop2=19,282
```

Plus human-readable Title/Tags (`282 · purple`) as a Sort → More fallback.

## How to sort

1. Register the schema once (Administrator): `.\scripts\sparse\Register-PropertySchema.ps1`
2. Color folders (re-apply if they were colored before this build).
3. Parent folder → **Sort → More… → “Folder color”** (the column title follows the app language) → ascending.

Among colored folders, order follows the rainbow. Folders with no `ColorHue` have an empty value for that column — Explorer decides where empties go (often first or last depending on view); we no longer force them with `999`.

## Why we dropped sibling `999`

Writing `desktop.ini` onto every neighbor caused I/O, ReadOnly flags, and clutter. The column is sparse (`isColumnSparse`): only colored folders carry a value.

## Legacy cleanup

Older builds may have left `FolderManaging-sort-tail=1` files with hue `999`. The next color apply or reset in that parent **deletes** those tails only; it never recreates them.

## Propdesc notes

Valid attrs used: `type="UInt32"`, `isColumnSparse="true"`, `labelInfo sortDescription="LowestHighest"`.  
`sortDescendingType="Minimum"` is **not** part of the documented propdesc schema and is not used.
