using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Forms = System.Windows.Forms;

namespace VPet_Simulator.Windows.HUD;

/// <summary>
/// V-Max HUD : surcouche flottante ancrée au compagnon (bulle, anneau, panneaux…).
/// Fenêtre transparente, sans bordure, hors de la barre des tâches et d'Alt+Tab, qui suit le compagnon
/// et se place selon l'espace libre de l'écran.
/// </summary>
public abstract class HudOverlay : Window
{
    protected readonly MainWindow Pet;
    private readonly bool activates;
    private bool closingAnimated;

    protected HudOverlay(MainWindow pet, bool activates)
    {
        Pet = pet;
        this.activates = activates;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        ShowActivated = activates;
        SizeToContent = SizeToContent.WidthAndHeight;
        UseLayoutRounding = true;
        FontFamily = (FontFamily)FindResource("HudBody");
        Foreground = (Brush)FindResource("HudText");
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Ideal);
        SourceInitialized += (_, _) => ApplyExStyle();
        SizeChanged += (_, _) => Reposition();
        pet.LocationChanged += PetMoved;
        Closed += (_, _) => pet.LocationChanged -= PetMoved;
    }

    private void PetMoved(object? sender, EventArgs e)
    {
        if (IsVisible)
            Reposition();
    }

    /// <summary>
    /// Rectangle du compagnon à l'écran (unités WPF)
    /// </summary>
    protected Rect PetRect => new(Pet.Left, Pet.Top, Pet.ActualWidth, Pet.ActualHeight);

    /// <summary>
    /// Zone de travail (sans la barre des tâches) de l'écran où se trouve le compagnon, en unités WPF
    /// </summary>
    protected Rect WorkArea
    {
        get
        {
            var src = PresentationSource.FromVisual(Pet);
            var center = PetRect;
            var toDevice = src?.CompositionTarget?.TransformToDevice ?? Matrix.Identity;
            var toDip = src?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
            var p = toDevice.Transform(new Point(center.X + center.Width / 2, center.Y + center.Height / 2));
            var wa = Forms.Screen.FromPoint(new System.Drawing.Point((int)p.X, (int)p.Y)).WorkingArea;
            var tl = toDip.Transform(new Point(wa.Left, wa.Top));
            var br = toDip.Transform(new Point(wa.Right, wa.Bottom));
            return new Rect(tl, br);
        }
    }

    /// <summary>
    /// Place la surcouche (appelé à l'affichage, au redimensionnement et quand le compagnon bouge)
    /// </summary>
    protected abstract void Reposition();

    /// <summary>
    /// Garde un rectangle dans la zone de travail
    /// </summary>
    protected static Point Clamp(Point topLeft, Size size, Rect area, double margin = 8)
    {
        double x = Math.Max(area.Left + margin, Math.Min(topLeft.X, area.Right - size.Width - margin));
        double y = Math.Max(area.Top + margin, Math.Min(topLeft.Y, area.Bottom - size.Height - margin));
        return new Point(x, y);
    }

    /// <summary>
    /// Affiche avec une apparition douce (fondu + légère échelle depuis <paramref name="origin"/>)
    /// </summary>
    public void ShowAnimated(Point origin)
    {
        closingAnimated = false;
        if (!IsVisible)
            Show();
        Reposition();
        if (activates)
            Activate();
        if (Content is FrameworkElement root)
            VPet_Simulator.Core.UiMotion.PopIn(root, origin, 0.94, 220);
    }

    /// <summary>
    /// Masque avec un fondu (la fenêtre est conservée pour être réaffichée)
    /// </summary>
    public void HideAnimated(Action? after = null)
    {
        if (!IsVisible || closingAnimated)
        {
            after?.Invoke();
            return;
        }
        closingAnimated = true;
        if (Content is not UIElement root || !VPet_Simulator.Core.UiMotion.Enabled)
        {
            Hide();
            closingAnimated = false;
            after?.Invoke();
            return;
        }
        var a = new DoubleAnimation(0, TimeSpan.FromMilliseconds(140));
        a.Completed += (_, _) =>
        {
            if (closingAnimated)
            {
                Hide();
                root.BeginAnimation(OpacityProperty, null);
                root.Opacity = 1;
                closingAnimated = false;
            }
            after?.Invoke();
        };
        root.BeginAnimation(OpacityProperty, a);
    }

    private void ApplyExStyle()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        long ex = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
        ex |= WS_EX_TOOLWINDOW;
        if (!activates)
            ex |= WS_EX_NOACTIVATE;
        SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(ex));
    }

    private const int GWL_EXSTYLE = -20;
    private const long WS_EX_TOOLWINDOW = 0x80;
    private const long WS_EX_NOACTIVATE = 0x08000000;
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr value);
}
