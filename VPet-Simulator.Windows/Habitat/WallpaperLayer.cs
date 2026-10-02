using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;

namespace VPet_Simulator.Windows.Habitat;

/// <summary>
/// V-Max habitat (expérimental) : fait vivre le compagnon DANS la couche du fond d'écran, derrière les fenêtres
/// et les icônes, à la manière de Wallpaper Engine. La fenêtre du compagnon devient enfant de la fenêtre qui
/// dessine le fond d'écran (« WorkerW »).
/// Deux structures de bureau existent :
/// - Windows 10 / 11 avant 24H2 : un WorkerW de premier niveau, juste derrière celui qui porte les icônes ;
/// - Windows 11 24H2 et suivants : un WorkerW enfant de Progman, sous la vue des icônes (SHELLDLL_DefView).
/// Limite connue : la couche des icônes reçoit les clics, le compagnon n'est donc plus cliquable (raccourcis et anneau
/// restent accessibles via Ctrl+Alt+Espace et la zone de notification).
/// </summary>
public static class WallpaperLayer
{
    /// <summary>Dernier diagnostic (affiché dans les paramètres en cas d'échec)</summary>
    public static string Diagnostic { get; private set; } = "";

    /// <summary>Origine de la fenêtre hôte à l'écran (pixels) : les positions d'une fenêtre enfant sont relatives à elle</summary>
    public static POINT HostOrigin { get; private set; }

    /// <summary>
    /// Parent réel d'une fenêtre (GetParent renverrait le propriétaire d'une fenêtre WPF) ; le bureau pour une fenêtre normale
    /// </summary>
    public static bool IsAttached(IntPtr hwnd) => hwnd != IntPtr.Zero && GetAncestor(hwnd, 1 /* GA_PARENT */) != GetDesktopWindow();

    public static bool IsAttached(Window w) => IsAttached(new WindowInteropHelper(w).Handle);

    internal static void Note(string text) => Diagnostic += " ; " + text;

    /// <summary>Position réelle de la fenêtre à l'écran, en pixels (diagnostic)</summary>
    public static (int left, int top) ScreenPosition(Window w)
    {
        GetWindowRect(new WindowInteropHelper(w).Handle, out var r);
        return (r.Left, r.Top);
    }

    /// <summary>Cherche (et fait créer au besoin) la fenêtre qui dessine le fond d'écran</summary>
    public static IntPtr FindHost()
    {
        var log = new StringBuilder();
        var progman = FindWindow("Progman", null);
        if (progman == IntPtr.Zero)
        {
            Diagnostic = "Progman introuvable (Explorateur Windows arrêté ?).";
            return IntPtr.Zero;
        }
        // message non documenté utilisé par Windows pour l'animation de changement de fond d'écran : crée le WorkerW
        SendMessageTimeout(progman, 0x052C, new IntPtr(0xD), new IntPtr(0x1), 0x0002 /* SMTO_ABORTIFHUNG */, 1000, out _);

        // 24H2 et suivants : WorkerW enfant de Progman
        var host = FindWindowEx(progman, IntPtr.Zero, "WorkerW", null);
        if (host != IntPtr.Zero)
            log.Append("structure 24H2 (WorkerW enfant de Progman)");
        else
        {
            // avant 24H2 : le WorkerW de premier niveau qui suit celui contenant les icônes
            IntPtr found = IntPtr.Zero;
            EnumWindows((top, _) =>
            {
                if (FindWindowEx(top, IntPtr.Zero, "SHELLDLL_DefView", null) != IntPtr.Zero)
                    found = FindWindowEx(IntPtr.Zero, top, "WorkerW", null);
                return true;
            }, IntPtr.Zero);
            host = found;
            log.Append(host != IntPtr.Zero ? "structure classique (WorkerW de premier niveau)" : "aucun WorkerW de fond d'écran");
        }
        if (host != IntPtr.Zero)
        {
            GetWindowRect(host, out var r);
            HostOrigin = new POINT { X = r.Left, Y = r.Top };
            log.Append($", hôte {r.Right - r.Left}×{r.Bottom - r.Top} en ({r.Left}, {r.Top})");
        }
        Diagnostic = log.ToString();
        return host;
    }

    /// <summary>Accroche la fenêtre à la couche du fond d'écran. Retourne faux (et un diagnostic) en cas d'échec.</summary>
    public static bool Attach(Window w)
    {
        var hwnd = new WindowInteropHelper(w).Handle;
        if (hwnd == IntPtr.Zero)
            return false;
        var host = FindHost();
        if (host == IntPtr.Zero)
            return false;
        SetLastError(0);
        if (SetParent(hwnd, host) == IntPtr.Zero && Marshal.GetLastWin32Error() != 0)
        {
            Diagnostic += $" ; SetParent a échoué (erreur {Marshal.GetLastWin32Error()})";
            return false;
        }
        bool parented = GetAncestor(hwnd, 1 /* GA_PARENT */) == host;
        bool visible = IsWindowVisible(hwnd);
        if (parented && visible)
        {
            InstallPositionFix(w, hwnd);
            Diagnostic += " ; compagnon accroché";
            return true;
        }
        // échec : on ne laisse jamais le compagnon à moitié accroché
        SetParent(hwnd, IntPtr.Zero);
        Diagnostic += parented ? " ; accroché mais invisible (annulé)" : " ; parent non appliqué (annulé)";
        return false;
    }

    /// <summary>Rend la fenêtre indépendante (retour au bureau normal)</summary>
    public static void Detach(Window w)
    {
        var hwnd = new WindowInteropHelper(w).Handle;
        if (IsAttached(hwnd))
            SetParent(hwnd, IntPtr.Zero);
        if (hook != null)
            HwndSource.FromHwnd(hwnd)?.RemoveHook(hook);
        hook = null;
        HostOrigin = default;
    }

    private static HwndSourceHook? hook;

    /// <summary>
    /// WPF positionne une fenêtre enfant sans tenir compte correctement de l'origine de l'hôte (non nulle avec un écran
    /// à gauche de l'écran principal). On corrige au passage du message de positionnement : la position demandée
    /// (Left/Top, en coordonnées écran) est convertie en coordonnées relatives à l'hôte. Left/Top restent donc de vraies
    /// coordonnées écran pour tout le reste de V-Max.
    /// </summary>
    private static void InstallPositionFix(Window w, IntPtr hwnd)
    {
        var src = HwndSource.FromHwnd(hwnd);
        if (src == null)
            return;
        hook = (IntPtr h, int msg, IntPtr wParam, IntPtr lParam, ref bool handled) =>
        {
            if (msg != 0x0046 /* WM_WINDOWPOSCHANGING */ || !IsAttached(h))
                return IntPtr.Zero;
            var pos = Marshal.PtrToStructure<WINDOWPOS>(lParam);
            if ((pos.flags & 0x0002 /* SWP_NOMOVE */) != 0)
                return IntPtr.Zero;
            var toDevice = PresentationSource.FromVisual(w)?.CompositionTarget?.TransformToDevice ?? System.Windows.Media.Matrix.Identity;
            var px = toDevice.Transform(new Point(w.Left, w.Top));
            pos.x = (int)Math.Round(px.X) - HostOrigin.X;
            pos.y = (int)Math.Round(px.Y) - HostOrigin.Y;
            Marshal.StructureToPtr(pos, lParam, false);
            return IntPtr.Zero;
        };
        src.AddHook(hook);
        // repositionne une première fois avec la correction
        SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0, 0x0001 /* SWP_NOSIZE */ | 0x0004 /* SWP_NOZORDER */ | 0x0010 /* SWP_NOACTIVATE */);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WINDOWPOS { public IntPtr hwnd, hwndInsertAfter; public int x, y, cx, cy; public uint flags; }
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindow(string cls, string? title);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string cls, string? title);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lParam);
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, uint flags, uint timeout, out IntPtr result);
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetParent(IntPtr child, IntPtr parent);
    [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr hWnd, uint flags);
    [DllImport("user32.dll")] private static extern IntPtr GetDesktopWindow();
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    [DllImport("kernel32.dll")] private static extern void SetLastError(uint code);
}
