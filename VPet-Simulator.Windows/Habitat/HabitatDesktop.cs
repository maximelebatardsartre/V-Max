using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace VPet_Simulator.Windows.Habitat;

/// <summary>
/// V-Max habitat : bascule sur le vrai bureau. Quand l'utilisateur affiche le bureau (Win+D) ou que le PC est
/// inactif, le compagnon quitte la fenêtre habitat et vit sur le fond d'écran, avec la carte de cette image
/// (projetée selon le mode d'ajustement de Windows). Il revient dans l'habitat dès qu'une application reprend la main.
/// </summary>
internal sealed class HabitatDesktop
{
    private readonly MainWindow mw;
    private readonly HabitatMode mode;
    private readonly DispatcherTimer timer;
    private bool forIdle;
    private string? cachedPath;
    private DateTime cachedStamp;
    private HabitatMap? cachedMap;
    private string? cachedSha;

    public HabitatDesktop(MainWindow mw, HabitatMode mode)
    {
        this.mw = mw;
        this.mode = mode;
        timer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => Tick(), mw.Dispatcher);
    }

    public void Start() => timer.Start();
    public void Stop()
    {
        timer.Stop();
        if (mode.OnDesktop)
            mode.ExitDesktop();
    }

    private void Tick()
    {
        if (!mode.IsActive || mode.Window == null || mode.Window.IsEditing || !mode.DesktopEnabled)
        {
            if (mode.OnDesktop)
                mode.ExitDesktop();
            return;
        }
        int idleMinutes = mode.IdleMinutes;
        bool idle = idleMinutes > 0 && IdleTime() >= TimeSpan.FromMinutes(idleMinutes);
        var fg = Foreground();
        if (!mode.OnDesktop)
        {
            if (fg == Fg.Desktop || idle)
            {
                var map = WallpaperMap();
                if (map == null)
                    return; // pas de carte pour ce fond d'écran : on reste dans l'habitat
                forIdle = fg != Fg.Desktop;
                mode.EnterDesktop(map, MonitorBounds(), WallpaperInfo.CurrentFit(), PixelToDip(), topmost: forIdle);
            }
            return;
        }
        // retour dans l'habitat : activité reprise (inactivité) ou une application au premier plan (Win+D)
        if (forIdle ? IdleTime() < TimeSpan.FromSeconds(2) : fg == Fg.OtherApp)
            mode.ExitDesktop();
        else if (forIdle && fg == Fg.Desktop)
            forIdle = false; // l'utilisateur est revenu… sur le bureau : on y reste
    }

    /// <summary>Carte du fond d'écran actuel (empreinte recalculée seulement si le fichier change)</summary>
    private HabitatMap? WallpaperMap()
    {
        var path = WallpaperInfo.CurrentPath();
        if (path == null)
            return null;
        var stamp = File.GetLastWriteTimeUtc(path);
        if (path != cachedPath || stamp != cachedStamp)
        {
            cachedPath = path;
            cachedStamp = stamp;
            try
            {
                cachedSha = HabitatMap.Sha256Of(path);
                cachedMap = HabitatMap.TryLoad(cachedSha);
            }
            catch
            {
                cachedSha = null;
                cachedMap = null;
            }
        }
        // même image que l'habitat : la carte de l'habitat (à jour, et le compagnon garde sa place)
        var map = cachedSha != null && cachedSha == mode.WindowMap.Image.Sha256 ? mode.WindowMap : cachedMap;
        return map is { Floors.Count: > 0 } ? map : null;
    }

    /// <summary>Écran de la fenêtre habitat (bureau entier, pas seulement la zone de travail : le fond d'écran le couvre)</summary>
    private Rect MonitorBounds()
    {
        var hwnd = new WindowInteropHelper(mode.Window!).Handle;
        var b = Forms.Screen.FromHandle(hwnd).Bounds;
        var m = PresentationSource.FromVisual(mw)?.CompositionTarget?.TransformFromDevice ?? System.Windows.Media.Matrix.Identity;
        var tl = m.Transform(new Point(b.Left, b.Top));
        var br = m.Transform(new Point(b.Right, b.Bottom));
        return new Rect(tl, br);
    }

    private double PixelToDip() => PresentationSource.FromVisual(mw)?.CompositionTarget?.TransformFromDevice.M11 ?? 1;

    #region Windows
    private enum Fg { Desktop, OtherApp, Neutral }

    /// <summary>Que montre le premier plan : le bureau, une autre application, ou rien de décisif (barre des tâches, V-Max)</summary>
    private static Fg Foreground()
    {
        var h = GetForegroundWindow();
        if (h == IntPtr.Zero)
            return Fg.Neutral;
        GetWindowThreadProcessId(h, out uint pid);
        if (pid == Environment.ProcessId)
            return Fg.Neutral;
        var sb = new StringBuilder(128);
        GetClassName(h, sb, sb.Capacity);
        return sb.ToString() switch
        {
            "Progman" or "WorkerW" => Fg.Desktop,
            "Shell_TrayWnd" or "Shell_SecondaryTrayWnd" or "NotifyIconOverflowWindow" or "TopLevelWindowForOverflowXamlIsland"
                or "Windows.UI.Core.CoreWindow" or "XamlExplorerHostIslandWindow" or "ForegroundStaging" or "MultitaskingViewFrame" => Fg.Neutral,
            _ => IsWindowVisible(h) && !IsIconic(h) ? Fg.OtherApp : Fg.Neutral,
        };
    }

    private static TimeSpan IdleTime()
    {
        var info = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
        if (!GetLastInputInfo(ref info))
            return TimeSpan.Zero;
        return TimeSpan.FromMilliseconds(unchecked((uint)Environment.TickCount - info.dwTime));
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LASTINPUTINFO { public uint cbSize; public uint dwTime; }
    [DllImport("user32.dll")] private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hWnd, StringBuilder name, int max);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hWnd);
    #endregion
}
