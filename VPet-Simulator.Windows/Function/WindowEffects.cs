using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace VPet_Simulator.Windows;

/// <summary>
/// V-Max : effets de fenêtre natifs de Windows 11 (fond Mica / Acrylic, coins arrondis, légende sombre).
/// Sous Windows 10 (ou si l'effet échoue), la fenêtre garde un fond opaque <c>VMaxWindowFallback</c>.
/// </summary>
public static class WindowEffects
{
    public enum Backdrop
    {
        None = 1,
        Mica = 2,
        Acrylic = 3,
        MicaAlt = 4,
    }

    /// <summary>
    /// Windows 11 22H2 (build 22621) ou plus : fonds système disponibles
    /// </summary>
    public static bool SupportsBackdrop => Environment.OSVersion.Version.Build >= 22621;

    /// <summary>
    /// Applique le fond système et les coins arrondis. Retourne true si le fond translucide est actif.
    /// À appeler dans <see cref="Window.SourceInitialized"/>.
    /// </summary>
    public static bool Apply(Window window, Backdrop backdrop, bool dark)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero)
            return false;
        try
        {
            int useDark = dark ? 1 : 0;
            DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref useDark, sizeof(int));
            int corner = DWMWCP_ROUND;
            DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, sizeof(int));

            if (!SupportsBackdrop || backdrop == Backdrop.None)
                return false;

            // Le contenu WPF doit être transparent pour laisser voir le fond système
            if (HwndSource.FromHwnd(hwnd) is HwndSource src && src.CompositionTarget != null)
                src.CompositionTarget.BackgroundColor = Colors.Transparent;
            var margins = new MARGINS { cxLeftWidth = -1, cxRightWidth = -1, cyTopHeight = -1, cyBottomHeight = -1 };
            DwmExtendFrameIntoClientArea(hwnd, ref margins);
            int type = (int)backdrop;
            return DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref type, sizeof(int)) == 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Met à jour uniquement la légende claire/sombre (changement de thème à chaud)
    /// </summary>
    public static void SetDark(Window window, bool dark)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero)
            return;
        int useDark = dark ? 1 : 0;
        try { DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref useDark, sizeof(int)); } catch { }
    }

    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWA_SYSTEMBACKDROP_TYPE = 38;
    private const int DWMWCP_ROUND = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct MARGINS
    {
        public int cxLeftWidth, cxRightWidth, cyTopHeight, cyBottomHeight;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref MARGINS margins);
}
