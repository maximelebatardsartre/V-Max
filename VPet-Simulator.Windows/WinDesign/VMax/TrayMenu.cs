using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shell;
using Forms = System.Windows.Forms;

namespace VPet_Simulator.Windows;

/// <summary>
/// V-Max : menu de la zone de notification façon 2026 (surcouche acrylique, coins arrondis, interrupteurs).
/// Le <see cref="Forms.ContextMenuStrip"/> d'origine reste le modèle (entrées des plugins incluses) :
/// son affichage classique est annulé et ses éléments sont rendus ici, puis déclenchés via PerformClick.
/// </summary>
public class TrayMenu : Window
{
    private static TrayMenu? open;
    /// <summary>Menu actuellement ouvert (QA)</summary>
    public static TrayMenu? Current => open;
    private readonly MainWindow mw;
    private readonly Forms.ContextMenuStrip model;
    private bool closing;

    public static void Show(MainWindow mw, Forms.ContextMenuStrip model)
    {
        if (open != null)
        {
            open.CloseAnimated();
            return;
        }
        open = new TrayMenu(mw, model);
        open.Show();
        open.Activate();
    }

    private TrayMenu(MainWindow mw, Forms.ContextMenuStrip model)
    {
        this.mw = mw;
        this.model = model;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        SizeToContent = SizeToContent.WidthAndHeight;
        Background = (Brush)FindResource("VMaxWindowFallback");
        Foreground = (Brush)FindResource("PrimaryText");
        FontFamily = (FontFamily)FindResource("MainFont");
        FontSize = 14;
        WindowChrome.SetWindowChrome(this, new WindowChrome { CaptionHeight = 0, GlassFrameThickness = new Thickness(-1), ResizeBorderThickness = new Thickness(0), UseAeroCaptionButtons = false });
        Content = BuildContent();
        Opacity = 0;
        SourceInitialized += (_, _) =>
        {
            if (WindowEffects.Apply(this, WindowEffects.Backdrop.Acrylic, mw.IsDarkTheme))
                Background = Brushes.Transparent;
        };
        Loaded += (_, _) => { PlaceNearCursor(); AnimateIn(); };
        Deactivated += (_, _) => CloseAnimated();
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) CloseAnimated(); };
        Closed += (_, _) => open = null;
    }

    private FrameworkElement BuildContent()
    {
        var root = new StackPanel { Margin = new Thickness(6), MinWidth = 260 };
        root.RenderTransform = new TranslateTransform();

        // En-tête
        var header = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(10, 8, 10, 10) };
        header.Children.Add(new Border
        {
            Width = 28, Height = 28, CornerRadius = new CornerRadius(8), Background = (Brush)FindResource("DARKPrimary"),
            Child = new TextBlock { Text = "V", FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Foreground = (Brush)FindResource("DARKPrimaryText") }
        });
        var names = new StackPanel { Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        names.Children.Add(new TextBlock { Text = "V-Max", FontWeight = FontWeights.SemiBold });
        names.Children.Add(new TextBlock { Text = mw.Core?.Save?.Name ?? "", FontSize = 12, Foreground = (Brush)FindResource("VMaxSubtleText") });
        header.Children.Add(names);
        root.Children.Add(header);
        root.Children.Add(Divider());

        foreach (Forms.ToolStripItem item in model.Items)
        {
            if (item is Forms.ToolStripSeparator)
            {
                root.Children.Add(Divider());
                continue;
            }
            if (item is not Forms.ToolStripMenuItem mi || !mi.Available)
                continue;
            bool isToggle = mi.CheckOnClick || mi.Name == "NotifyIcon_HitThrough";
            if (mi.Name == "NotifyIcon_Exit")
                root.Children.Add(Divider());
            root.Children.Add(Row(mi, isToggle));
        }
        return new Border { Child = root };
    }

    private Border Divider() => new()
    {
        Height = 1,
        Margin = new Thickness(8, 4, 8, 4),
        Background = (Brush)FindResource("VMaxStroke"),
    };

    private static string IconFor(string name) => name switch
    {
        "NotifyIcon_HitThrough" => "",
        "NotifyIcon_TopMost" => "",
        "NotifyIcon_Tutorial" => "",
        "NotifyIcon_Reset" => "",
        "NotifyIcon_Report" => "",
        "NotifyIcon_Console" => "",
        "NotifyIcon_Settings" => "",
        "NotifyIcon_Restart" => "",
        "NotifyIcon_Exit" => "",
        _ => "",
    };

    private FrameworkElement Row(Forms.ToolStripMenuItem mi, bool isToggle)
    {
        var grid = new Grid { Height = 36 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var icon = new TextBlock { Text = IconFor(mi.Name ?? ""), FontFamily = (FontFamily)FindResource("VMaxIconFont"), FontSize = 15, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        var text = new TextBlock { Text = (mi.Text ?? "").Replace("&", ""), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 12, 0) };
        grid.Children.Add(icon);
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
        CheckBox? toggle = null;
        if (isToggle)
        {
            toggle = new CheckBox { Style = (Style)FindResource("VMaxToggle"), IsChecked = mi.Checked, IsHitTestVisible = false, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(toggle, 2);
            grid.Children.Add(toggle);
        }
        if (mi.Name == "NotifyIcon_Exit")
            text.Foreground = icon.Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0x5C, 0x4A));

        var hover = new Border { CornerRadius = new CornerRadius(6), Background = (Brush)FindResource("VMaxCardHover"), Opacity = 0 };
        var cell = new Grid { Cursor = Cursors.Hand, Background = Brushes.Transparent };
        cell.Children.Add(hover);
        cell.Children.Add(grid);
        cell.MouseEnter += (_, _) => hover.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(100)));
        cell.MouseLeave += (_, _) => hover.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(150)));
        cell.MouseLeftButtonUp += (_, _) =>
        {
            if (toggle != null)
            {
                mi.PerformClick();
                toggle.IsChecked = mi.Checked;
                return; // les interrupteurs laissent le menu ouvert
            }
            CloseAnimated();
            Dispatcher.BeginInvoke(new Action(mi.PerformClick));
        };
        return cell;
    }

    private void PlaceNearCursor()
    {
        var src = PresentationSource.FromVisual(this);
        if (src?.CompositionTarget == null)
            return;
        var toDip = src.CompositionTarget.TransformFromDevice;
        var cursor = Forms.Cursor.Position;
        var work = Forms.Screen.FromPoint(cursor).WorkingArea;
        var p = toDip.Transform(new Point(cursor.X, cursor.Y));
        var tl = toDip.Transform(new Point(work.Left, work.Top));
        var br = toDip.Transform(new Point(work.Right, work.Bottom));
        double left = p.X - ActualWidth + 12;
        double top = p.Y - ActualHeight - 8;
        Left = Math.Max(tl.X + 8, Math.Min(left, br.X - ActualWidth - 8));
        Top = Math.Max(tl.Y + 8, Math.Min(top, br.Y - ActualHeight - 8));
    }

    private void AnimateIn()
    {
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(140)) { EasingFunction = ease });
        if (((Border)Content).Child is FrameworkElement inner && inner.RenderTransform is TranslateTransform tt)
            tt.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(10, 0, TimeSpan.FromMilliseconds(200)) { EasingFunction = ease });
    }

    private void CloseAnimated()
    {
        if (closing)
            return;
        closing = true;
        var anim = new DoubleAnimation(0, TimeSpan.FromMilliseconds(110));
        anim.Completed += (_, _) => Close();
        BeginAnimation(OpacityProperty, anim);
    }
}
