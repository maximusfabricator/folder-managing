// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 maximusfabricator

using System.Text;

namespace FolderManaging.Core.Updates;

public enum UpdateStage
{
    /// <summary>The setup runs; the app may let go of the downloaded file.</summary>
    Started,

    /// <summary>The user said No to the Windows (UAC) prompt; nothing changed.</summary>
    Cancelled,

    /// <summary>The setup stopped before it changed the app; the message says why.</summary>
    Failed,

    /// <summary>The app is about to be replaced: it should close now.</summary>
    Installing,

    Done,
}

/// <summary>
/// How the tray app and <c>FolderManaging-Setup.exe --update --status FILE</c> talk: the setup appends one
/// <c>stage⇥message</c> line per step to FILE (in the update folder), the app polls it.
/// </summary>
public static class UpdateHandoff
{
    public const string UpdateSwitch = "--update";
    public const string StatusSwitch = "--status";

    /// <summary>Arguments the setup gives the tray app it starts after an update.</summary>
    public const string RelaunchArguments = "--tray --updated";
    public const string UpdatedSwitch = "--updated";

    public static string NewStatusPath(string directory) =>
        Path.Combine(directory, $"update-status-{Guid.NewGuid():N}.txt");

    public static void Write(string? statusPath, UpdateStage stage, string message = "")
    {
        if (string.IsNullOrEmpty(statusPath))
            return;
        try
        {
            var line = stage + "\t" + message.Replace('\r', ' ').Replace('\n', ' ') + Environment.NewLine;
            File.AppendAllText(statusPath, line, new UTF8Encoding(false));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // the app then only sees the setup process end
        }
    }

    /// <summary>The last stage written, or <c>null</c> when the file is not there yet.</summary>
    public static (UpdateStage Stage, string Message)? ReadLast(string statusPath)
    {
        try
        {
            if (!File.Exists(statusPath))
                return null;
            using var stream = new FileStream(statusPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            (UpdateStage, string)? last = null;
            while (reader.ReadLine() is { } line)
            {
                var tab = line.IndexOf('\t');
                var name = tab < 0 ? line : line[..tab];
                if (Enum.TryParse<UpdateStage>(name, ignoreCase: false, out var stage))
                    last = (stage, tab < 0 ? "" : line[(tab + 1)..]);
            }

            return last;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
