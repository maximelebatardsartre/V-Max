using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using VPet_Simulator.Windows.HUD;

namespace VPet_Simulator.Windows.Voice;

/// <summary>
/// V-Max voix : indicateur d'écoute au-dessus du compagnon. Anneau qui pulse (écho de l'anneau orbital),
/// vumètre en direct et état (« Je t'écoute… », « Je réfléchis… »). Ne prend jamais le focus.
/// </summary>
public sealed class ListeningOverlay : HudOverlay
{
    private readonly Rectangle[] bars = new Rectangle[5];
    private readonly TextBlock label;
    private readonly Ellipse pulse;
    private readonly TextBlock glyph;

    public ListeningOverlay(MainWindow pet) : base(pet, activates: false)
    {
        var accent = (Brush)FindResource("HudAccent");
        pulse = new Ellipse { Width = 34, Height = 34, Stroke = accent, StrokeThickness = 2, RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new ScaleTransform(1, 1) };
        glyph = new TextBlock { Text = "", FontFamily = (FontFamily)FindResource("HudIcons"), FontSize = 15, Foreground = accent, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var ring = new Grid { Width = 34, Height = 34, Margin = new Thickness(0, 0, 12, 0) };
        ring.Children.Add(pulse);
        ring.Children.Add(glyph);

        var meter = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
        for (int i = 0; i < bars.Length; i++)
        {
            bars[i] = new Rectangle { Width = 4, Height = 4, RadiusX = 2, RadiusY = 2, Fill = (Brush)FindResource("HudSilver"), Margin = new Thickness(0, 0, 3, 0), VerticalAlignment = VerticalAlignment.Center };
            meter.Children.Add(bars[i]);
        }
        label = new TextBlock { Text = "Je t'écoute…", FontFamily = (FontFamily)FindResource("HudDisplay"), FontWeight = FontWeights.SemiBold, FontSize = 14, Foreground = (Brush)FindResource("HudText"), VerticalAlignment = VerticalAlignment.Center, MaxWidth = 300, TextTrimming = TextTrimming.CharacterEllipsis };
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(ring);
        row.Children.Add(meter);
        row.Children.Add(label);
        Content = new Border
        {
            Style = (Style)FindResource("HudPanel"),
            CornerRadius = new CornerRadius(26),
            Padding = new Thickness(10, 8, 18, 8),
            Margin = new Thickness(12),
            Child = row,
        };
    }

    /// <summary>Écoute en cours : vumètre actif, anneau qui respire</summary>
    public void ShowListening()
    {
        label.Text = "Je t'écoute…";
        glyph.Text = "";
        if (!IsVisible)
            ShowAnimated(new Point(0.5, 1));
        if (VPet_Simulator.Core.UiMotion.Enabled)
        {
            var st = (ScaleTransform)pulse.RenderTransform;
            var a = new DoubleAnimation(1, 1.18, TimeSpan.FromMilliseconds(700)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase() };
            st.BeginAnimation(ScaleTransform.ScaleXProperty, a);
            st.BeginAnimation(ScaleTransform.ScaleYProperty, a);
        }
    }

    /// <summary>Transcription / réflexion : le vumètre se fige, l'anneau tourne doucement</summary>
    public void ShowThinking(string text)
    {
        label.Text = text;
        glyph.Text = "";
        SetLevel(0);
        if (!IsVisible)
            ShowAnimated(new Point(0.5, 1));
    }

    public void SetLevel(double level)
    {
        // barres en cloche : le centre réagit le plus
        double[] weight = [0.55, 0.8, 1, 0.8, 0.55];
        for (int i = 0; i < bars.Length; i++)
            bars[i].Height = 4 + Math.Clamp(level * 1.6, 0, 1) * 18 * weight[i];
    }

    protected override void Reposition()
    {
        if (ActualWidth < 1)
            return;
        var pet = PetRect;
        var area = WorkArea;
        double y = pet.Top + pet.Height * 0.12 - ActualHeight;
        if (y < area.Top)
            y = pet.Bottom - pet.Height * 0.05;
        var pos = Clamp(new Point(pet.Left + pet.Width / 2 - ActualWidth / 2, y), new Size(ActualWidth, ActualHeight), area, 4);
        Left = pos.X;
        Top = pos.Y;
    }
}
