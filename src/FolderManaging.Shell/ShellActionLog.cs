// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 maximusfabricator

using FolderManaging.Core.Diagnostics;

namespace FolderManaging.Shell;

internal static class ShellActionLog
{
    internal static void Write(string message) =>
        TraceLog.Append("shell-menu.log", message);
}
