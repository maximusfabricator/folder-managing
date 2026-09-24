using System.Diagnostics;

namespace FolderManaging.Cli;

/// <summary>
/// Clears Explorer icon caches and restarts explorer.exe so <c>desktop.ini</c> / folder icon changes become visible.
/// </summary>
internal static class ExplorerIconCacheRefresher
{
    internal static int Run()
    {
        Console.WriteLine("Stopping explorer.exe (all Explorer windows will close)...");
        try
        {
            foreach (var p in Process.GetProcessesByName("explorer"))
            {
                try
                {
                    p.Kill(entireProcessTree: false);
                    p.WaitForExit(5000);
                }
                catch
                {
                    // ignore per-process failures
                }
                finally
                {
                    p.Dispose();
                }
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Warning while stopping Explorer: {ex.Message}");
        }

        Thread.Sleep(1500);

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var iconDb = Path.Combine(localAppData, "IconCache.db");
        if (File.Exists(iconDb))
        {
            try
            {
                File.Delete(iconDb);
                Console.WriteLine($"Deleted: {iconDb}");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Could not delete IconCache.db: {ex.Message}");
            }
        }
        else
        {
            Console.WriteLine($"No file (skip): {iconDb}");
        }

        var explorerDir = Path.Combine(localAppData, "Microsoft", "Windows", "Explorer");
        if (Directory.Exists(explorerDir))
        {
            foreach (var path in Directory.EnumerateFiles(explorerDir, "iconcache*", SearchOption.TopDirectoryOnly))
            {
                try
                {
                    File.Delete(path);
                    Console.WriteLine($"Deleted: {path}");
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Could not delete '{path}': {ex.Message}");
                }
            }
        }
        else
        {
            Console.WriteLine($"No directory (skip): {explorerDir}");
        }

        Console.WriteLine("Starting explorer.exe...");
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to start explorer.exe: {ex.Message}");
            Console.Error.WriteLine("Start it manually from Win+R: explorer");
            return 10;
        }

        Console.WriteLine("Done.");
        return 0;
    }
}
