// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 maximusfabricator

namespace FolderManaging.Cli.Cli;

internal static class CliOptionsParser
{
    public static CliParseResult Parse(string[] args)
    {
        if (args.Length == 0)
        {
            return new CliParseError("Missing arguments. Use --help for usage.");
        }

        if (args.Any(static a => a.Equals("--help", StringComparison.OrdinalIgnoreCase) || a.Equals("-h", StringComparison.OrdinalIgnoreCase)))
        {
            return new CliHelpRequested(UsageText);
        }

#if FM_DEV
        var head = args[0].Trim();
        if (head.Equals("install-context-menu", StringComparison.OrdinalIgnoreCase))
        {
            return ParseInstall(args);
        }

        if (head.Equals("uninstall-context-menu", StringComparison.OrdinalIgnoreCase))
        {
            return new UninstallContextMenuCommand();
        }

        if (head.Equals("shell-preview", StringComparison.OrdinalIgnoreCase))
        {
            return ParseShellPreview(args);
        }
#endif

        return ParseVerbInvocation(args);
    }

    private static CliParseResult ParseInstall(string[] args)
    {
        string? customExe = null;
        for (var i = 1; i < args.Length; i++)
        {
            if (IsSwitch(args[i], "exe", "cli-exe"))
            {
                if (!TryReadValue(args, ref i, out customExe))
                {
                    return new CliParseError("Missing value for --exe.");
                }
            }
            else
            {
                return new CliParseError($"Unknown argument for install-context-menu: {args[i]}");
            }
        }

        return new InstallContextMenuCommand(customExe);
    }

    private static CliParseResult ParseShellPreview(string[] args)
    {
        string? kind = null;
        string? path = null;
        string? token = null;

        for (var i = 1; i < args.Length; i++)
        {
            var tokenArg = args[i];
            if (!tokenArg.StartsWith('-'))
            {
                return new CliParseError($"Unexpected token in shell-preview: {tokenArg}");
            }

            if (TrySplitKeyValue(tokenArg, out var key, out var inlineValue))
            {
                if (!AssignShellPreview(key, inlineValue, ref kind, ref path, ref token))
                {
                    return new CliParseError($"Unknown option: {key}");
                }

                continue;
            }

            if (!TryReadValue(args, ref i, out var next))
            {
                return new CliParseError($"Missing value after {tokenArg}.");
            }

            if (!AssignShellPreview(NormalizeShellPreviewKey(tokenArg), next, ref kind, ref path, ref token))
            {
                return new CliParseError($"Unknown option: {tokenArg}");
            }
        }

        if (string.IsNullOrWhiteSpace(kind))
        {
            return new CliParseError("shell-preview requires --kind.");
        }

        if (string.IsNullOrWhiteSpace(path))
        {
            return new CliParseError("shell-preview requires --path.");
        }

        return new ShellPreviewCommand(kind.Trim(), path.Trim(), token?.Trim());
    }

    private static bool AssignShellPreview(string key, string? val, ref string? kind, ref string? path, ref string? token)
    {
        switch (key)
        {
            case "kind":
            case "k":
                kind = val;
                return true;
            case "path":
            case "p":
                path = val;
                return true;
            case "token":
            case "t":
                token = val;
                return true;
            default:
                return false;
        }
    }

    private static string NormalizeShellPreviewKey(string token)
    {
        var trimmed = token.TrimStart('-');
        if (trimmed.Equals("kind", StringComparison.OrdinalIgnoreCase) || trimmed.Equals("k", StringComparison.OrdinalIgnoreCase))
        {
            return "kind";
        }

        if (trimmed.Equals("path", StringComparison.OrdinalIgnoreCase) || trimmed.Equals("p", StringComparison.OrdinalIgnoreCase))
        {
            return "path";
        }

        if (trimmed.Equals("token", StringComparison.OrdinalIgnoreCase) || trimmed.Equals("t", StringComparison.OrdinalIgnoreCase))
        {
            return "token";
        }

        return trimmed.ToLowerInvariant();
    }

    private static CliParseResult ParseVerbInvocation(string[] args)
    {
        string? action = null;
        string? path = null;
        string? value = null;
        string? tag = null;

        for (var i = 0; i < args.Length; i++)
        {
            var token = args[i];
            if (token.StartsWith('-'))
            {
                if (TrySplitKeyValue(token, out var key, out var inlineValue))
                {
                    if (!Assign(key, inlineValue, ref action, ref path, ref value, ref tag))
                    {
                        return new CliParseError($"Unknown option: {key}");
                    }

                    continue;
                }

                if (!TryReadValue(args, ref i, out var next))
                {
                    return new CliParseError($"Missing value after {token}.");
                }

                if (!Assign(NormalizeKey(token), next, ref action, ref path, ref value, ref tag))
                {
                    return new CliParseError($"Unknown option: {token}");
                }
            }
            else
            {
                return new CliParseError($"Unexpected token: {token}");
            }
        }

        if (string.IsNullOrWhiteSpace(action))
        {
            return new CliParseError("Missing required --action.");
        }

        if (string.IsNullOrWhiteSpace(path))
        {
            return new CliParseError("Missing required --path.");
        }

        return new InvokeVerbCommand(action.Trim(), path.Trim(), value?.Trim(), tag?.Trim());
    }

    private static bool Assign(string key, string? val, ref string? action, ref string? path, ref string? value, ref string? tag)
    {
        switch (key)
        {
            case "action":
            case "a":
                action = val;
                return true;
            case "path":
            case "p":
                path = val;
                return true;
            case "value":
            case "v":
                value = val;
                return true;
            case "tag":
            case "t":
                tag = val;
                return true;
            default:
                return false;
        }
    }

    private static string NormalizeKey(string token)
    {
        var trimmed = token.TrimStart('-');
        if (trimmed.Equals("action", StringComparison.OrdinalIgnoreCase) || trimmed.Equals("a", StringComparison.OrdinalIgnoreCase))
        {
            return "action";
        }

        if (trimmed.Equals("path", StringComparison.OrdinalIgnoreCase) || trimmed.Equals("p", StringComparison.OrdinalIgnoreCase))
        {
            return "path";
        }

        if (trimmed.Equals("value", StringComparison.OrdinalIgnoreCase) || trimmed.Equals("v", StringComparison.OrdinalIgnoreCase))
        {
            return "value";
        }

        if (trimmed.Equals("tag", StringComparison.OrdinalIgnoreCase) || trimmed.Equals("t", StringComparison.OrdinalIgnoreCase))
        {
            return "tag";
        }

        return trimmed.ToLowerInvariant();
    }

    private static bool IsSwitch(string token, params string[] names)
    {
        if (!token.StartsWith('-'))
        {
            return false;
        }

        var body = token.TrimStart('-');
        return names.Any(n => body.Equals(n, StringComparison.OrdinalIgnoreCase));
    }

    private static bool TrySplitKeyValue(string token, out string key, out string? value)
    {
        var eq = token.IndexOf('=');
        if (eq <= 0)
        {
            key = "";
            value = null;
            return false;
        }

        key = NormalizeKey(token[..eq]);
        value = token[(eq + 1)..].Trim();
        if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
        {
            value = value[1..^1];
        }

        return true;
    }

    private static bool TryReadValue(string[] args, ref int index, out string? value)
    {
        if (index + 1 >= args.Length)
        {
            value = null;
            return false;
        }

        index++;
        value = args[index].Trim();
        if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
        {
            value = value[1..^1];
        }

        return true;
    }

#if FM_DEV
    const string DevUsageText = """

        Dev only — legacy context menu (per-user, HKCU):
          FolderManaging.Cli install-context-menu [--exe "<path-to-cli.exe>"]
          FolderManaging.Cli uninstall-context-menu

        Dev only — shell extension preview (no persistence):
          FolderManaging.Cli shell-preview --kind <pin|unpin|color|tag> --path "<folder>" [--token <id>]
        """;
#else
    const string DevUsageText = "";
#endif

    internal const string UsageText = BaseUsageText + DevUsageText;

    const string BaseUsageText = """
        FolderManaging CLI

        Explorer invokes:
          FolderManaging.Cli --action <color|reset|tag|pin|unpin> --path "<folder>" [--value <token>] [--tag <name>]

        Remove our desktop.ini customizations (optional bulk):
          FolderManaging.Cli cleanup-managed-inis [--root "<dir>"] [--max-depth 12] [--no-recursive]

        Regenerate preset icons into %LocalAppData%\FolderManaging\Icons (or FOLDERMANAGING_ICONS_DIR):
          FolderManaging.Cli regenerate-folder-icons

        Clear Explorer icon caches and restart explorer.exe (run after desktop.ini changes if icons stay stale):
          FolderManaging.Cli force-refresh-icons

        Register Explorer "Color hue" property (Administrator — enables Sort by Color hue / rainbow):
          FolderManaging.Cli register-property-schema [--propdesc "<path-to-FolderManaging.propdesc>"]
            (Admin: maps ColorHue → Rating for Sort. Comments/InfoTip sort needs no register.)
          FolderManaging.Cli unregister-property-schema

        Check for a newer version (downloads and installs nothing; exit 0 up to date, 10 newer, 2 offline, 3 not trusted):
          foldermanaging-cli update --check [--json]

        Examples:
          FolderManaging.Cli --action color --value mint --path "D:\Work\Demo"
          FolderManaging.Cli --action reset --path "D:\Work\Demo"
          FolderManaging.Cli --action tag --tag ClientA --path "D:\Work\Demo"
          FolderManaging.Cli --action pin --path "D:\Work\Demo"
        """;
}
