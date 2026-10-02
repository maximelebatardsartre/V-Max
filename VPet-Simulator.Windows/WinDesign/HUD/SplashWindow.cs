using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace VPet_Simulator.Windows.HUD;

/// <summary>
/// V-Max HUD : écran d'accueil pendant le chargement. Carte de verre fumé, anneau orbital qui tourne
/// (élément signature), nom et étape en cours ; disparaît en fondu quand le compagnon apparaît.
/// Remplace l'étiquette « Loading » colorée de VPet.
/// </summary>
public sealed class SplashWindow : Window
{
    private readonly TextBlock status;
    private readonly Border card;
    private bool closing;

    public SplashWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        ShowActivated = false;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        FontFamily = (FontFamily)FindResource("HudBody");
        Title = "V-Max";

        // anneau orbital : piste fine + arc ruban qui tourne, satellite qui orbite
        var ring = new Grid { Width = 64, Height = 64, Margin = new Thickness(0, 0, 18, 0) };
        ring.Children.Add(new Ellipse { Stroke = (Brush)FindResource("HudStroke"), StrokeThickness = 2 });
        var arc = new Path
        {
            Data = Geometry.Parse("M 32,2 A 30,30 0 0 1 62,32"),
            Stroke = (Brush)FindResource("HudAccent"),
            StrokeThickness = 2.5,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = new RotateTransform(),
        };
        ring.Children.Add(arc);
        var dot = new Ellipse { Width = 8, Height = 8, Fill = (Brush)FindResource("HudAmber"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, -3, 0, 0) };
        var orbit = new Grid { RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new RotateTransform() };
        orbit.Children.Add(dot);
        ring.Children.Add(orbit);
        ring.Children.Add(new TextBlock { Text = "V", FontFamily = (FontFamily)FindResource("HudDisplay"), FontWeight = FontWeights.Bold, FontSize = 24, Foreground = (Brush)FindResource("HudText"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
        if (VPet_Simulator.Core.UiMotion.Enabled)
        {
            ((RotateTransform)arc.RenderTransform).BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0, 360, TimeSpan.FromSeconds(1.1)) { RepeatBehavior = RepeatBehavior.Forever });
            ((RotateTransform)orbit.RenderTransform).BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(360, 0, TimeSpan.FromSeconds(3.2)) { RepeatBehavior = RepeatBehavior.Forever });
        }

        var name = new TextBlock { Text = "V-Max", FontFamily = (FontFamily)FindResource("HudDisplay"), FontWeight = FontWeights.SemiBold, FontSize = 26, Foreground = (Brush)FindResource("HudText") };
        status = new TextBlock { Text = "Démarrage…", FontSize = 13, Foreground = (Brush)FindResource("HudTextMuted"), Margin = new Thickness(0, 4, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 260 };
        var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        texts.Children.Add(name);
        texts.Children.Add(status);
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(ring);
        row.Children.Add(texts);
        card = new Border
        {
            Style = (Style)FindResource("HudPanel"),
            CornerRadius = new CornerRadius(26),
            Padding = new Thickness(26, 22, 34, 22),
            Margin = new Thickness(30),
            Width = 380,
            Child = row,
        };
        Content = card;
        Loaded += (_, _) => VPet_Simulator.Core.UiMotion.PopIn(card, new Point(0.5, 0.5), 0.92, 260);
    }

    /// <summary>Étape de chargement en cours (une ligne, sans les détails techniques)</summary>
    public void SetStatus(string text)
    {
        if (closing)
            return;
        var line = text.Split('\n')[0].Trim();
        status.Text = line.Length == 0 ? status.Text : line;
    }

    /// <summary>Disparaît en fondu (légère montée) puis se ferme</summary>
    public void FadeOut()
    {
        if (closing)
            return;
        closing = true;
        if (!VPet_Simulator.Core.UiMotion.Enabled)
        {
            Close();
            return;
        }
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(320)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
        fade.Completed += (_, _) => Close();
        card.RenderTransform = new TranslateTransform();
        ((TranslateTransform)card.RenderTransform).BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, -10, TimeSpan.FromMilliseconds(320)));
        card.BeginAnimation(OpacityProperty, fade);
    }
}
