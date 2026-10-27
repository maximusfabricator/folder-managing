// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 maximusfabricator

namespace FolderManaging.Cli.Cli;

internal abstract record CliParseResult;

internal sealed record InstallContextMenuCommand(string? CustomExecutablePath) : CliParseResult;

internal sealed record UninstallContextMenuCommand : CliParseResult;

internal sealed record InvokeVerbCommand(string Action, string Path, string? Value, string? Tag) : CliParseResult;

/// <summary>Explorer shell extension preview: prints only; does not touch JSON state (Phase 2 stub).</summary>
internal sealed record ShellPreviewCommand(string Kind, string Path, string? Token) : CliParseResult;

internal sealed record CliHelpRequested(string Text) : CliParseResult;

internal sealed record CliParseError(string Message) : CliParseResult;
