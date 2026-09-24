using System.Security.Principal;
using System.Text;

namespace FolderManaging.Core.Diagnostics;

/// <summary>Temporary COM/surrogate identity probe (remove after debugging).</summary>
public static class IdentityWriteProbe
{
    /// <summary>Writes <c>C:\Users\Public\identity_test.txt</c> with <see cref="WindowsIdentity.GetCurrent"/>.</summary>
    public static void WritePublicIdentityTest()
    {
        try
        {
            var pub = Environment.GetEnvironmentVariable("PUBLIC") ?? @"C:\Users\Public";
            var path = Path.Combine(pub, "identity_test.txt");
            var name = WindowsIdentity.GetCurrent().Name;
            var body =
                "WindowsIdentity.GetCurrent().Name=" + name + Environment.NewLine +
                "DateTimeOffset=" + DateTimeOffset.Now.ToString("O", System.Globalization.CultureInfo.InvariantCulture) + Environment.NewLine +
                "ProcessId=" + Environment.ProcessId + Environment.NewLine;
            File.WriteAllText(path, body, Encoding.UTF8);
        }
        catch (Exception ex)
        {
            try
            {
                var fb = Path.Combine(Path.GetTempPath(), "identity_test_write_failed.txt");
                File.WriteAllText(fb, ex.ToString(), Encoding.UTF8);
            }
            catch
            {
                // ignore
            }
        }
    }
}
