using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace VPet_Simulator.Windows.HUD;

/// <summary>
/// Action affichée sur l'anneau orbital
/// </summary>
public sealed record OrbitAction(string Label, string Icon, Action Run, bool Accent = false);

/// <summary>
/// V-Max HUD : anneau orbital (élément signature). Au clic droit sur le compagnon, un anneau se trace autour de lui
/// et les actions viennent se placer en arc, du côté où l'écran a de la place.
/// </summary>
public sealed class OrbitDock : HudOverlay
{
    private const double SatelliteSize = 52;
    private readonly Canvas canvas = new();
    private readonly Path ring;
    private readonly Border label;
    private readonly TextBlock labelText;
    private List<OrbitAction> actions = new();
    private readonly List<FrameworkElement> satellites = new();
    private double radius;

    public OrbitDock(MainWindow pet) : base(pet, activates: true)
    {
        SizeToContent = SizeToContent.Manual;
        ring = new Path
        {
            Stroke = (Brush)FindResource("HudSilver"),
            StrokeThickness = 1.2,
            Opacity = 0.55,
            IsHitTestVisible = false,
        };
        labelText = new TextBlock { FontFamily = (FontFamily)FindResource("HudDisplay"), FontWeight = FontWeights.SemiBold, FontSize = 13, Foreground = (Brush)FindResource("HudText") };
        label = new Border
        {
            Background = (Brush)FindResource("HudSurfaceRaised"),
            BorderBrush = (Brush)FindResource("HudStroke"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(10, 4, 10, 5),
            Child = labelText,
            Opacity = 0,
            IsHitTestVisible = false,
        };
        // fond cliquable quasi transparent : un clic hors des satellites ferme l'anneau
        canvas.Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0));
        canvas.MouseLeftButtonDown += (_, e) => { if (e.OriginalSource == canvas) HideAnimated(); };
        canvas.MouseRightButtonDown += (_, e) => { HideAnimated(); e.Handled = true; };
        Content = canvas;
        Deactivated += (_, _) => HideAnimated();
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) HideAnimated(); };
    }

    /// <summary>
    /// Ouvre l'anneau avec ces actions (ou le referme s'il est déjà ouvert)
    /// </summary>
    public void Toggle(IEnumerable<OrbitAction> items)
    {
        if (IsVisible)
        {
            HideAnimated();
            return;
        }
        actions = items.ToList();
        Build();
        Show();
        Reposition();
        Activate();
        AnimateIn();
    }

    private Point Center => new(Width / 2, Height / 2);

    private void Build()
    {
        canvas.Children.Clear();
        satellites.Clear();
        var pet = PetRect;
        radius = Math.Max(110, Math.Max(pet.Width, pet.Height) * 0.58);
        double size = (radius + SatelliteSize) * 2 + 160;
        Width = Height = size;
        canvas.Width = canvas.Height = size;
        var c = Center;
        ring.Data = new EllipseGeometry(c, radius, radius);
        canvas.Children.Add(ring);

        foreach (var a in actions)
        {
            var sat = Satellite(a);
            satellites.Add(sat);
            canvas.Children.Add(sat);
        }
        canvas.Children.Add(label);
    }

    private FrameworkElement Satellite(OrbitAction a)
    {
        var icon = new TextBlock
        {
            Text = a.Icon,
            FontFamily = (FontFamily)FindResource("HudIcons"),
            FontSize = 19,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = (Brush)FindResource(a.Accent ? "HudOnAccent" : "HudText"),
        };
        var halo = new Ellipse { Stroke = (Brush)FindResource("HudAccent"), StrokeThickness = 2, Opacity = 0, Margin = new Thickness(-4) };
        var disc = new Ellipse
        {
            Fill = (Brush)FindResource(a.Accent ? "HudAccent" : "HudSurface"),
            Stroke = (Brush)FindResource("HudStroke"),
            StrokeThickness = 1,
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 14, ShadowDepth = 3, Direction = 270, Opacity = 0.35, Color = Colors.Black },
        };
        var grid = new Grid
        {
            Width = SatelliteSize,
            Height = SatelliteSize,
            Cursor = Cursors.Hand,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = new ScaleTransform(1, 1),
            Tag = a,
            Focusable = true,
            ToolTip = null,
        };
        grid.Children.Add(halo);
        grid.Children.Add(disc);
        grid.Children.Add(icon);
        AutomationPropertiesHelper(grid, a.Label);

        var ease = (IEasingFunction)FindResource("HudEase");
        grid.MouseEnter += (_, _) =>
        {
            Scale(grid, 1.14, 140, ease);
            halo.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(140)));
            ShowLabel(grid, a.Label);
        };
        grid.MouseLeave += (_, _) =>
        {
            Scale(grid, 1, 180, ease);
            halo.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(180)));
            label.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(120)));
        };
        grid.MouseLeftButtonDown += (_, e) => { Scale(grid, 0.92, 80, ease); e.Handled = true; };
        grid.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            HideAnimated(a.Run);
        };
        grid.KeyDown += (_, e) => { if (e.Key is Key.Enter or Key.Space) HideAnimated(a.Run); };
        return grid;
    }

    private static void AutomationPropertiesHelper(FrameworkElement el, string name) =>
        System.Windows.Automation.AutomationProperties.SetName(el, name);

    private static void Scale(FrameworkElement el, double to, int ms, IEasingFunction ease)
    {
        var st = (ScaleTransform)el.RenderTransform;
        st.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(to, TimeSpan.FromMilliseconds(ms)) { EasingFunction = ease });
        st.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(to, TimeSpan.FromMilliseconds(ms)) { EasingFunction = ease });
    }

    /// <summary>
    /// Étiquette placée à l'extérieur de l'anneau, dans la direction du satellite
    /// </summary>
    private void ShowLabel(FrameworkElement sat, string text)
    {
        labelText.Text = text;
        label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var c = Center;
        double sx = Canvas.GetLeft(sat) + SatelliteSize / 2, sy = Canvas.GetTop(sat) + SatelliteSize / 2;
        double dx = sx - c.X, dy = sy - c.Y, len = Math.Max(1, Math.Sqrt(dx * dx + dy * dy));
        double lx = sx + dx / len * (SatelliteSize / 2 + 14), ly = sy + dy / len * (SatelliteSize / 2 + 14);
        var w = label.DesiredSize.Width;
        var h = label.DesiredSize.Height;
        Canvas.SetLeft(label, dx >= 0 ? lx : lx - w);
        Canvas.SetTop(label, ly - h / 2);
        label.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(120)));
    }

    /// <summary>
    /// Arc de 210° tourné vers le côté de l'écran qui a le plus de place
    /// </summary>
    private void LayoutSatellites()
    {
        var area = WorkArea;
        var pet = PetRect;
        double cx = pet.Left + pet.Width / 2, cy = pet.Top + pet.Height / 2;
        double roomRight = area.Right - cx, roomLeft = cx - area.Left;
        double centerAngle = roomRight >= roomLeft ? 0 : 180;
        if (cy - area.Top < radius + SatelliteSize)
            centerAngle += roomRight >= roomLeft ? 35 : -35;          // trop près du haut : on descend l'arc
        else if (area.Bottom - cy < radius + SatelliteSize)
            centerAngle += roomRight >= roomLeft ? -35 : 35;          // trop près du bas : on le remonte
        int n = satellites.Count;
        double span = Math.Min(210, 32 * (n - 1));
        var c = Center;
        for (int i = 0; i < n; i++)
        {
            double t = n == 1 ? 0 : i / (double)(n - 1) - 0.5;
            double ang = (centerAngle + (roomRight >= roomLeft ? 1 : -1) * t * span) * Math.PI / 180;
            Canvas.SetLeft(satellites[i], c.X + radius * Math.Cos(ang) - SatelliteSize / 2);
            Canvas.SetTop(satellites[i], c.Y + radius * Math.Sin(ang) - SatelliteSize / 2);
        }
    }

    protected override void Reposition()
    {
        if (Width is double.NaN || Width < 1)
            return;
        var pet = PetRect;
        Left = pet.Left + pet.Width / 2 - Width / 2;
        Top = pet.Top + pet.Height / 2 - Height / 2;
        LayoutSatellites();
    }

    private void AnimateIn()
    {
        var ease = (IEasingFunction)FindResource("HudEase");
        if (!VPet_Simulator.Core.UiMotion.Enabled)
            return;
        // l'anneau se trace, puis les satellites arrivent en décalé depuis le centre
        double circumference = 2 * Math.PI * radius;
        ring.StrokeDashArray = new DoubleCollection { circumference / ring.StrokeThickness, 1000 };
        ring.BeginAnimation(Shape.StrokeDashOffsetProperty,
            new DoubleAnimation(circumference / ring.StrokeThickness, 0, TimeSpan.FromMilliseconds(380)) { EasingFunction = ease });
        var c = Center;
        for (int i = 0; i < satellites.Count; i++)
        {
            var s = satellites[i];
            double tx = Canvas.GetLeft(s), ty = Canvas.GetTop(s);
            var delay = TimeSpan.FromMilliseconds(60 + 35 * i);
            s.Opacity = 0;
            s.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)) { BeginTime = delay });
            var from = new Point(c.X - SatelliteSize / 2 + (tx - (c.X - SatelliteSize / 2)) * 0.6, c.Y - SatelliteSize / 2 + (ty - (c.Y - SatelliteSize / 2)) * 0.6);
            s.BeginAnimation(Canvas.LeftProperty, new DoubleAnimation(from.X, tx, TimeSpan.FromMilliseconds(260)) { BeginTime = delay, EasingFunction = ease, FillBehavior = FillBehavior.Stop });
            s.BeginAnimation(Canvas.TopProperty, new DoubleAnimation(from.Y, ty, TimeSpan.FromMilliseconds(260)) { BeginTime = delay, EasingFunction = ease, FillBehavior = FillBehavior.Stop });
        }
    }
}
