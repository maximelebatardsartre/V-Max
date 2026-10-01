using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace VPet_Simulator.Windows.HUD;

/// <summary>
/// V-Max HUD : notification courte (remplace les boîtes de dialogue non critiques).
/// Pastille posée sous le compagnon, empilée si plusieurs arrivent, disparaît seule.
/// </summary>
public sealed class HudToast : HudOverlay
{
    public enum Kind { Info, Success, Warning }

    private static readonly List<HudToast> active = new();
    private readonly DispatcherTimer life = new();

    private HudToast(MainWindow pet, string text, Kind kind, TimeSpan duration) : base(pet, activates: false)
    {
        string glyph = kind switch { Kind.Success => "", Kind.Warning => "", _ => "" };
        var brush = (Brush)FindResource(kind switch { Kind.Success => "HudSuccess", Kind.Warning => "HudAmber", _ => "HudSilver" });
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(new TextBlock { Text = glyph, FontFamily = (FontFamily)FindResource("HudIcons"), FontSize = 14, Foreground = brush, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) });
        row.Children.Add(new TextBlock { Text = text, Style = (Style)FindResource("HudBodyText"), FontSize = 13.5, MaxWidth = 300, VerticalAlignment = VerticalAlignment.Center });
        Content = new Border
        {
            Style = (Style)FindResource("HudPanel"),
            CornerRadius = new CornerRadius(18),
            Padding = new Thickness(14, 9, 16, 10),
            Margin = new Thickness(12),
            Child = row,
        };
        life.Interval = duration;
        life.Tick += (_, _) => { life.Stop(); HideAnimated(Close); };
        Closed += (_, _) => { active.Remove(this); RestackAll(); };
        MouseLeftButtonUp += (_, _) => { life.Stop(); HideAnimated(Close); };
    }

    /// <summary>
    /// Affiche une notification près du compagnon
    /// </summary>
    public static void Show(MainWindow pet, string text, Kind kind = Kind.Info, double seconds = 4)
    {
        pet.Dispatcher.BeginInvoke(() =>
        {
            if (active.Count >= 3)
                active[0].Close();
            var t = new HudToast(pet, text, kind, TimeSpan.FromSeconds(Math.Max(2, seconds)));
            active.Add(t);
            t.ShowAnimated(new Point(0.5, 0));
            t.life.Start();
        });
    }

    private static void RestackAll()
    {
        foreach (var t in active)
            t.Reposition();
    }

    protected override void Reposition()
    {
        if (ActualWidth < 1)
            return;
        var pet = PetRect;
        var area = WorkArea;
        // empilées vers le bas sous le compagnon (vers le haut s'il est au bas de l'écran)
        int index = active.IndexOf(this);
        double offset = 0;
        for (int i = 0; i < index; i++)
            offset += active[i].ActualHeight - 16;
        bool below = pet.Bottom + 120 < area.Bottom;
        double y = below ? pet.Bottom - pet.Height * 0.06 + offset : pet.Top - ActualHeight - offset;
        var pos = Clamp(new Point(pet.Left + pet.Width / 2 - ActualWidth / 2, y), new Size(ActualWidth, ActualHeight), area, 4);
        Left = pos.X;
        Top = pos.Y;
    }
}
