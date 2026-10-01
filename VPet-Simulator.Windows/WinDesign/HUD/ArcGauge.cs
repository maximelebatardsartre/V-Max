using System;
using System.Windows;
using System.Windows.Media;

namespace VPet_Simulator.Windows.HUD;

/// <summary>
/// V-Max HUD : jauge en arc (270°, ouverte en bas), écho de l'anneau orbital.
/// Trois couches : piste, réserve (ce qui va être absorbé) et valeur.
/// </summary>
public sealed class ArcGauge : FrameworkElement
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(nameof(Value), typeof(double), typeof(ArcGauge),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty ReserveProperty = DependencyProperty.Register(nameof(Reserve), typeof(double), typeof(ArcGauge),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Remplissage, de 0 à 1</summary>
    public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    /// <summary>Remplissage à venir (réserve), de 0 à 1</summary>
    public double Reserve { get => (double)GetValue(ReserveProperty); set => SetValue(ReserveProperty, value); }

    public double Thickness { get; set; } = 5;
    public Brush Track { get; set; } = Brushes.Gray;
    public Brush Fill { get; set; } = Brushes.White;
    public Brush ReserveFill { get; set; } = Brushes.Gray;

    private const double Start = 135, Sweep = 270;

    protected override void OnRender(DrawingContext dc)
    {
        double size = Math.Min(ActualWidth, ActualHeight);
        if (size <= Thickness * 2)
            return;
        var c = new Point(ActualWidth / 2, ActualHeight / 2);
        double r = size / 2 - Thickness / 2;
        Arc(dc, c, r, 1, Track);
        if (Reserve > Value)
            Arc(dc, c, r, Math.Clamp(Reserve, 0, 1), ReserveFill);
        if (Value > 0.002)
            Arc(dc, c, r, Math.Clamp(Value, 0, 1), Fill);
    }

    private void Arc(DrawingContext dc, Point c, double r, double fraction, Brush brush)
    {
        double a0 = Start * Math.PI / 180, a1 = (Start + Sweep * fraction) * Math.PI / 180;
        var p0 = new Point(c.X + r * Math.Cos(a0), c.Y + r * Math.Sin(a0));
        var p1 = new Point(c.X + r * Math.Cos(a1), c.Y + r * Math.Sin(a1));
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(p0, false, false);
            ctx.ArcTo(p1, new Size(r, r), 0, Sweep * fraction > 180, SweepDirection.Clockwise, true, false);
        }
        g.Freeze();
        var pen = new Pen(brush, Thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        dc.DrawGeometry(null, pen, g);
    }
}
