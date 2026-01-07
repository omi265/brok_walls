using System;
using System.Runtime.InteropServices;

namespace BroKWalls
{
    public enum DesktopWallpaperPosition
    {
        Center = 0,
        Tile = 1,
        Stretch = 2,
        Fit = 3,
        Fill = 4,
        Span = 5
    }

    [ComImport]
    [Guid("B92B5679-D5A9-4596-BE30-F193910D799F")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IDesktopWallpaper
    {
        void SetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string? monitorID, [MarshalAs(UnmanagedType.LPWStr)] string wallpaper);
        void GetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string? monitorID, [MarshalAs(UnmanagedType.LPWStr)] out string wallpaper);
        void GetMonitorDevicePathAt(uint monitorIndex, [MarshalAs(UnmanagedType.LPWStr)] out string monitorID);
        void GetMonitorDevicePathCount(out uint count);
        void GetMonitorRect([MarshalAs(UnmanagedType.LPWStr)] string monitorID, out Win32Rect rect);
        void SetPosition(DesktopWallpaperPosition position);
        void GetPosition(out DesktopWallpaperPosition position);
        void SetSlideshow(IntPtr items);
        void GetSlideshow(out IntPtr items);
        void AdvanceSlideshow([MarshalAs(UnmanagedType.LPWStr)] string? monitorID, int direction);
        void GetStatus(out uint status);
        void Enable([MarshalAs(UnmanagedType.Bool)] bool enable);
    }

    [ComImport]
    [Guid("C2CF3110-460E-4fc1-B9D0-8A1C0C9CC4BD")]
    public class DesktopWallpaperClass { }

    public static class WallpaperHelper
    {
        public static void SetWallpaper(string path, DesktopWallpaperPosition position)
        {
            IDesktopWallpaper? wallpaper = null;
            try
            {
                var wallType = Type.GetTypeFromCLSID(new Guid("C2CF3110-460E-4fc1-B9D0-8A1C0C9CC4BD"));
                if (wallType == null) throw new Exception("COM Class not found.");
                
                wallpaper = (IDesktopWallpaper)Activator.CreateInstance(wallType)!;
                
                // Set the global position style
                wallpaper.SetPosition(position);

                // Get monitor count
                wallpaper.GetMonitorDevicePathCount(out uint monitorCount);

                // Explicitly set wallpaper for each monitor to ensure independent scaling/fitting
                for (uint i = 0; i < monitorCount; i++)
                {
                    wallpaper.GetMonitorDevicePathAt(i, out string monitorId);
                    wallpaper.SetWallpaper(monitorId, path);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"COM Multi-Monitor Error: {ex.Message}");
                SetWallpaperLegacy(path);
            }
            finally
            {
                if (wallpaper != null) Marshal.ReleaseComObject(wallpaper);
            }
        }

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern int SystemParametersInfo(int uAction, int uParam, string lpvParam, int fuWinIni);

        private static void SetWallpaperLegacy(string path)
        {
            const int SPI_SETDESKWALLPAPER = 20;
            const int SPIF_UPDATEINIFILE = 0x01;
            const int SPIF_SENDCHANGE = 0x02;
            SystemParametersInfo(SPI_SETDESKWALLPAPER, 0, path, SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Win32Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}
