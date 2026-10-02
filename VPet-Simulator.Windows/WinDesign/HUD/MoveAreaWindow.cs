using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Shell;

namespace VPet_Simulator.Windows.HUD;

/// <summary>
/// V-Max : cadre à placer et redimensionner pour délimiter la zone où le compagnon se déplace
/// (remplace la fenêtre « 桌宠移动范围 » de VPet).
/// </summary>
public sealed class MoveAreaWindow : Window
{
    private readonly MainWindow mw;
    private readonly Action? saved;

    public MoveAreaWindow(MainWindow mw, Action? onSaved = null)
    {
        this.mw = mw;
        saved = onSaved;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        ResizeMode = ResizeMode.CanResize;
        ShowInTaskbar = false;
        Topmost = true;
        Title = "Zone de déplacement";
        Background = Brushes.Transparent;
        FontFamily = (FontFamily)FindResource("HudBody");
        WindowChrome.SetWindowChrome(this, new WindowChrome { CaptionHeight = 0, ResizeBorderThickness = new Thickness(10), GlassFrameThickness = new Thickness(0) });

        // départ : la zone actuelle, sinon les deux tiers de l'écran principal
        var wa = SystemParameters.WorkArea;
        var current = (mw.Core.Controller as MWController) is { IsPrimaryScreen: false } c ? c.ScreenBorder : System.Drawing.Rectangle.Empty;
        if (current.Width > 100 && current.Height > 100)
        {
            Left = current.X; Top = current.Y; Width = current.Width; Height = current.Height;
        }
        else
        {
            Width = wa.Width * 0.66; Height = wa.Height * 0.66;
            Left = wa.Left + (wa.Width - Width) / 2; Top = wa.Top + (wa.Height - Height) / 2;
        }

        var accent = ((SolidColorBrush)FindResource("HudAccent")).Color;
        var frame = new Rectangle
        {
            Stroke = (Brush)FindResource("HudAccent"), StrokeThickness = 3, StrokeDashArray = new DoubleCollection { 4, 3 },
            RadiusX = 18, RadiusY = 18, Fill = new SolidColorBrush(Color.FromArgb(0x26, accent.R, accent.G, accent.B)),
        };
        var card = new Border
        {
            Style = (Style)FindResource("HudPanel"), CornerRadius = new CornerRadius(20), Padding = new Thickness(20, 16, 20, 16),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, MaxWidth = 380,
        };
        var sp = new StackPanel();
        sp.Children.Add(new TextBlock { Text = "ZONE DE DÉPLACEMENT", Style = (Style)FindResource("HudEyebrow") });
        sp.Children.Add(new TextBlock { Text = "Place et redimensionne ce cadre", Style = (Style)FindResource("HudTitle"), FontSize = 18, TextWrapping = TextWrapping.Wrap });
        sp.Children.Add(new TextBlock { Text = "Ton compagnon restera à l'intérieur. Fais glisser le cadre, tire sur ses bords.", Style = (Style)FindResource("HudBodyText"), FontSize = 13, Foreground = (Brush)FindResource("HudTextMuted"), Margin = new Thickness(0, 6, 0, 14) });
        var size = new TextBlock { FontFamily = (FontFamily)FindResource("HudMono"), FontSize = 11.5, Foreground = (Brush)FindResource("HudTextMuted"), Margin = new Thickness(0, 0, 0, 12) };
        SizeChanged += (_, _) => size.Text = $"{ActualWidth:0} × {ActualHeight:0}";
        sp.Children.Add(size);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Style = (Style)FindResource("HudGhostButton"), Content = "Annuler", Margin = new Thickness(0, 0, 8, 0) };
        cancel.Click += (_, _) => Close();
        var ok = new Button { Style = (Style)FindResource("HudPrimaryButton"), Content = "Enregistrer la zone" };
        ok.Click += (_, _) => Save();
        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);
        sp.Children.Add(buttons);
        card.Child = sp;

        var root = new Grid();
        root.Children.Add(frame);
        root.Children.Add(card);
        root.MouseLeftButtonDown += (_, e) =>
        {
            if (e.OriginalSource is DependencyObject d && IsButton(d))
                return;
            if (e.ButtonState == MouseButtonState.Pressed)
                DragMove();
        };
        Content = root;
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); else if (e.Key == Key.Enter) Save(); };
        Loaded += (_, _) => VPet_Simulator.Core.UiMotion.PopIn(root, new Point(0.5, 0.5), 0.97, 200);
    }

    private static bool IsButton(DependencyObject d)
    {
        for (var x = d; x != null; x = x is Visual ? VisualTreeHelper.GetParent(x) : null)
            if (x is ButtonBase)
                return true;
        return false;
    }

    private void Save()
    {
        if (mw.Core.Controller is MWController ctrl)
            ctrl.ScreenBorder = new System.Drawing.Rectangle((int)Left, (int)Top, (int)ActualWidth, (int)ActualHeight);
        saved?.Invoke();
        mw.Toast("Zone de déplacement enregistrée.", HudToast.Kind.Success);
        Close();
    }
}
