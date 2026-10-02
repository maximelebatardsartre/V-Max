using Microsoft.Win32;
using System;
using System.Linq;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace VPet_Simulator.Windows;

/// <summary>
/// V-Max : garde le compagnon sur un écran visible.
/// Quand un écran est débranché ou que la disposition change (et au démarrage), un compagnon qui se retrouve
/// hors de toute zone de travail est ramené sur l'écran principal, au-dessus de la barre des tâches.
/// </summary>
public static class ScreenGuard
{

    private static bool watching;

    public static void Watch(Dispatcher dispatcher)
    {
        if (watching)
            return;
        watching = true;
        SystemEvents.DisplaySettingsChanged += (_, _) =>
            dispatcher.BeginInvoke(() =>
            {
                foreach (var mw in App.MainWindows.ToList())
                    EnsureOnScreen(mw);
            }, DispatcherPriority.Background);
    }

    /// <summary>
    /// Ramène la fenêtre sur l'écran principal si moins de 30 % de sa surface est visible sur une zone de travail
    /// </summary>
    public static bool EnsureOnScreen(Window w)
    {
        var hwnd = new WindowInteropHelper(w).Handle;
        // fenêtre accrochée à la couche du fond d'écran : sa position est relative à l'hôte, le mode habitat s'en charge
        if (Habitat.WallpaperLayer.IsAttached(hwnd))
            return false;
        var src = PresentationSource.FromVisual(w);
        if (hwnd == IntPtr.Zero || src?.CompositionTarget == null || w.ActualWidth < 1)
            return false;
        var toDevice = src.CompositionTarget.TransformToDevice;
        var tl = toDevice.Transform(new Point(w.Left, w.Top));
        var br = toDevice.Transform(new Point(w.Left + w.ActualWidth, w.Top + w.ActualHeight));
        var rect = new System.Drawing.Rectangle((int)tl.X, (int)tl.Y, (int)(br.X - tl.X), (int)(br.Y - tl.Y));
        long area = (long)rect.Width * rect.Height;
        if (area <= 0)
            return false;
        long visible = 0;
        foreach (var s in Forms.Screen.AllScreens)
        {
            var i = System.Drawing.Rectangle.Intersect(rect, s.WorkingArea);
            if (!i.IsEmpty)
                visible += (long)i.Width * i.Height;
        }
        if (visible >= area * 0.3)
            return false;

        var work = Forms.Screen.PrimaryScreen!.WorkingArea;
        var toDip = src.CompositionTarget.TransformFromDevice;
        var wtl = toDip.Transform(new Point(work.Left, work.Top));
        var wbr = toDip.Transform(new Point(work.Right, work.Bottom));
        w.Left = wtl.X + (wbr.X - wtl.X - w.ActualWidth) / 2;
        w.Top = wbr.Y - w.ActualHeight;
        return true;
    }
}
