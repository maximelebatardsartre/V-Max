using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Forms = System.Windows.Forms;

namespace VPet_Simulator.Windows;

/// <summary>
/// V-Max : détection des écrans (nom convivial, résolution native, mise à l'échelle, écran principal). Sert à
/// guider la préparation de l'habitat et du fond d'écran — l'utilisateur voit exactement sur quel écran il travaille.
/// </summary>
public static class Screens
{
    public sealed record ScreenDetail(
        string DeviceName,   // \\.\DISPLAY1
        string FriendlyName, // « Dell U2720Q », « Écran de portable »…
        int Width, int Height,
        int X, int Y,
        bool Primary,
        int ScalePercent)    // 100, 125, 150…
    {
        public string Resolution => $"{Width} × {Height}";
        public string ScaleText => ScalePercent == 100 ? "100 %" : $"{ScalePercent} %";
    }

    /// <summary>Liste des écrans, l'écran principal en premier</summary>
    public static List<ScreenDetail> All()
    {
        var list = new List<ScreenDetail>();
        foreach (var s in Forms.Screen.AllScreens)
        {
            var b = s.Bounds;
            list.Add(new ScreenDetail(
                s.DeviceName,
                FriendlyName(s.DeviceName),
                b.Width, b.Height, b.X, b.Y,
                s.Primary,
                ScalePercent(b)));
        }
        list.Sort((a, c) => c.Primary.CompareTo(a.Primary));
        return list;
    }

    // --- nom convivial via le pilote du moniteur (EnumDisplayDevices sur l'adaptateur)
    private static string FriendlyName(string deviceName)
    {
        try
        {
            var monitor = new DISPLAY_DEVICE { cb = Marshal.SizeOf<DISPLAY_DEVICE>() };
            if (EnumDisplayDevices(deviceName, 0, ref monitor, EDD_GET_DEVICE_INTERFACE_NAME))
            {
                var name = monitor.DeviceString?.Trim();
                if (!string.IsNullOrWhiteSpace(name) && name != "Generic PnP Monitor" && name != "Écran PnP générique")
                    return name!;
                if (!string.IsNullOrWhiteSpace(name))
                    return "Écran " + name;
            }
        }
        catch { }
        return deviceName.Replace(@"\\.\", "");
    }

    // --- échelle : DPI effectif du moniteur
    private static int ScalePercent(System.Drawing.Rectangle bounds)
    {
        try
        {
            var pt = new POINT { X = bounds.X + bounds.Width / 2, Y = bounds.Y + bounds.Height / 2 };
            var hmon = MonitorFromPoint(pt, MONITOR_DEFAULTTONEAREST);
            if (GetDpiForMonitor(hmon, MDT_EFFECTIVE_DPI, out uint dpiX, out _) == 0 && dpiX > 0)
                return (int)Math.Round(dpiX / 96.0 * 100);
        }
        catch { }
        return 100;
    }

    #region Win32
    private const int EDD_GET_DEVICE_INTERFACE_NAME = 0x1;
    private const uint MONITOR_DEFAULTTONEAREST = 2;
    private const int MDT_EFFECTIVE_DPI = 0;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DISPLAY_DEVICE
    {
        public int cb;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
        public int StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceID;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
    }

    private struct POINT { public int X; public int Y; }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool EnumDisplayDevices(string? lpDevice, uint iDevNum, ref DISPLAY_DEVICE lpDisplayDevice, uint dwFlags);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

    [DllImport("Shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);
    #endregion
}
