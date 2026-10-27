// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 maximusfabricator

using System.Reflection;
using System.Runtime.CompilerServices;
using FolderManaging.Core;

namespace FolderManaging.Shell.Diagnostics;

/// <summary>
/// Runs the FIRST time any code in <c>FolderManaging.Shell.dll</c> is touched by <c>dllhost.exe</c>.
///
/// Two critical jobs:
///   1. Subscribe to <see cref="AppDomain.AssemblyResolve"/> so dependencies (e.g. <c>FolderManaging.Core.dll</c>,
///      <c>Vanara.PInvoke.*</c>) are loaded from the package directory — comhost picks them up by probing
///      <c>AppContext.BaseDirectory</c>, but in surrogate hosts that directory often resolves to <c>System32</c>.
///   2. Drop a "session opened" line into <see cref="ExtremeLogger"/> so we always see *something* in the log
///      even when the very first managed call later crashes.
/// </summary>
internal static class ShellModuleInitializer
{
    static int _hooked;

    [ModuleInitializer]
#pragma warning disable CA2255 // ModuleInitializer in a class library: we *intentionally* run before any COM Activator call.
    internal static void Init()
#pragma warning restore CA2255
    {
        try
        {
            // Touch disk before subscribing to anything — if this fails, later logging still tries other roots.
            ExtremeLogger.BootTouch();

            if (Interlocked.CompareExchange(ref _hooked, 1, 0) == 0)
            {
                AppDomain.CurrentDomain.AssemblyResolve += OnAssemblyResolve;
            }

            ExtremeLogger.LogEnvironmentSnapshot("ShellModuleInitializer.Init: shell DLL loaded into surrogate");

            try
            {
                AppBootstrapper.Initialize();
            }
            catch (Exception ex)
            {
                ExtremeLogger.LogException(ex, "ShellModuleInitializer: AppBootstrapper.Initialize failed");
            }
        }
        catch (Exception ex)
        {
            ExtremeLogger.LogException(ex, "ShellModuleInitializer.Init failed");
        }
    }

    static Assembly? OnAssemblyResolve(object? sender, ResolveEventArgs args)
    {
        try
        {
            var name = new AssemblyName(args.Name).Name;
            if (string.IsNullOrEmpty(name))
                return null;

            var dir = ExtremeLogger.ShellAssemblyDirectory;
            if (string.IsNullOrEmpty(dir))
            {
                ExtremeLogger.Log($"AssemblyResolve: empty shell directory for '{args.Name}'");
                return null;
            }

            foreach (var ext in new[] { ".dll", ".exe" })
            {
                var candidate = Path.Combine(dir, name + ext);
                if (File.Exists(candidate))
                {
                    ExtremeLogger.Log($"AssemblyResolve: '{args.Name}' -> {candidate}");
                    return Assembly.LoadFrom(candidate);
                }
            }

            ExtremeLogger.Log($"AssemblyResolve: NOT FOUND '{args.Name}' under {dir}");
            return null;
        }
        catch (Exception ex)
        {
            ExtremeLogger.LogException(ex, $"AssemblyResolve crashed for '{args.Name}'");
            return null;
        }
    }
}
