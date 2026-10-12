using System.Text;

namespace FolderManaging.Setup;

/// <summary>Plain installer log in %TEMP% so a failed install can be diagnosed without dev tooling.</summary>
internal static class SetupLog
{
    static readonly object Gate = new();

    internal static string FilePath { get; } = Path.Combine(Path.GetTempPath(), "FolderManaging-Setup.log");

    internal static void Write(string message)
    {
        lock (Gate)
        {
            try
            {
                File.AppendAllText(FilePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {message}{Environment.NewLine}", Encoding.UTF8);
            }
            catch
            {
                // logging must never break setup
            }
        }
    }
}
