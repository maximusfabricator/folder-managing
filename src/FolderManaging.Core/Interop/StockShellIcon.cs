using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace FolderManaging.Core.Interop;

/// <summary>Loads the system stock folder icon via <c>SHGetStockIconInfo</c> (SIID_FOLDER).</summary>
internal static class StockShellIcon
{
    /// <summary><see cref="SHGetStockIconInfo"/> stock icon id for a directory.</summary>
    internal const uint SiidFolder = 3;

    internal const uint ShgsiIcon = 0x0000_0100;
    internal const uint ShgsiSmallicon = 0x0000_0001;
    internal const uint ShgsiLargeicon = 0x0000_0000;
    internal const uint ShgsiShelliconsize = 0x0000_0004;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct SHSTOCKICONINFO
    {
        internal uint cbSize;
        internal nint hIcon;
        internal int iSysImageIndex;
        internal int iIcon;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        internal string szPath;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    internal static extern int SHGetStockIconInfo(uint siid, uint uFlags, ref SHSTOCKICONINFO psii);

    /// <summary>Creates a square ARGB bitmap at <paramref name="targetSize"/> from the shell folder stock icon.</summary>
    internal static Bitmap CaptureFolderStockBitmap(int targetSize)
    {
        // Prefer shell-sized stock icon, then default large, then small (scaled up).
        var flagsToTry = new[]
        {
            ShgsiIcon | ShgsiShelliconsize,
            ShgsiIcon | ShgsiLargeicon,
            ShgsiIcon | ShgsiSmallicon,
        };

        Exception? last = null;
        foreach (var flags in flagsToTry)
        {
            var sii = new SHSTOCKICONINFO { cbSize = (uint)Marshal.SizeOf<SHSTOCKICONINFO>() };
            var hr = SHGetStockIconInfo(SiidFolder, flags, ref sii);
            if (hr != 0 || sii.hIcon == 0)
                continue;

            try
            {
                using var ico = Icon.FromHandle(sii.hIcon);
                using var src = ico.ToBitmap();
                var bmp = new Bitmap(targetSize, targetSize, PixelFormat.Format32bppArgb);
                using (var g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.Transparent);
                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
                    g.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighQuality;
                    g.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceOver;
                    g.DrawImage(src, new Rectangle(0, 0, targetSize, targetSize));
                }

                return bmp;
            }
            catch (Exception ex)
            {
                last = ex;
            }
            finally
            {
                Win32Shell.DestroyIcon(sii.hIcon);
            }
        }

        throw last ?? new InvalidOperationException(
            "SHGetStockIconInfo could not provide SIID_FOLDER (shell32 / icon cache unavailable).");
    }
}
