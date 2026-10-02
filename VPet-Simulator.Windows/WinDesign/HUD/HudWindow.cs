using LinePutScript;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shell;

namespace VPet_Simulator.Windows.HUD;

/// <summary>
/// V-Max HUD : grande fenêtre indépendante (galerie, statistiques, sauvegardes, planning…), dans le langage visuel
/// des panneaux : fond encre fumée opaque, coins arrondis de Windows 11, en-tête (surtitre, titre, boutons),
/// onglets en pastilles avec transition, taille et position mémorisées, Échap pour fermer.
/// Les classes dérivées remplissent <see cref="Body"/> ou ajoutent des onglets avec <see cref="AddTab"/>.
/// </summary>
public abstract class HudWindow : Window
{
    protected readonly MainWindow MW;
    private readonly string key;
    protected readonly TextBlock EyebrowText;
    protected readonly TextBlock TitleText;
    protected readonly StackPanel HeaderButtons;
    private readonly WrapPanel tabs = new() { Margin = new Thickness(24, 0, 24, 6) };
    private readonly ContentControl body = new();
    private readonly ContentControl footer = new();
    private readonly List<(RadioButton chip, Func<FrameworkElement> build, FrameworkElement? built)> tabList = new();

    protected HudWindow(MainWindow mw, string key, string eyebrow, string title, double width, double height)
    {
        MW = mw;
        this.key = key;
        Title = "V-Max · " + title;
        Icon = mw.Icon;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.CanResize;
        ShowInTaskbar = true;
        MinWidth = 560;
        MinHeight = 420;
        FontFamily = (FontFamily)FindResource("HudBody");
        Foreground = (Brush)FindResource("HudText");
        var surface = ((SolidColorBrush)FindResource("HudSurface")).Color;
        Background = new SolidColorBrush(Color.FromRgb(surface.R, surface.G, surface.B));
        WindowChrome.SetWindowChrome(this, new WindowChrome { CaptionHeight = 0, ResizeBorderThickness = new Thickness(6), GlassFrameThickness = new Thickness(0), UseAeroCaptionButtons = false });
        RestoreBounds_(width, height);

        EyebrowText = new TextBlock { Text = eyebrow, Style = (Style)FindResource("HudEyebrow") };
        TitleText = new TextBlock { Text = title, Style = (Style)FindResource("HudTitle"), FontSize = 24, TextTrimming = TextTrimming.CharacterEllipsis };
        var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        titles.Children.Add(EyebrowText);
        titles.Children.Add(TitleText);
        HeaderButtons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top };
        var close = new Button { Style = (Style)FindResource("HudIconButton"), Content = "", ToolTip = "Fermer (Échap)" };
        close.Click += (_, _) => FadeClose();
        HeaderButtons.Children.Add(close);
        var header = new Grid { Margin = new Thickness(24, 18, 14, 10), Background = Brushes.Transparent };
        header.ColumnDefinitions.Add(new ColumnDefinition());
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(titles);
        Grid.SetColumn(HeaderButtons, 1);
        header.Children.Add(HeaderButtons);
        header.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ButtonState != MouseButtonState.Pressed || e.OriginalSource is not (Grid or TextBlock or StackPanel))
                return;
            if (e.ClickCount == 2)
                WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            else
                DragMove();
        };

        tabs.Visibility = Visibility.Collapsed;
        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.Children.Add(header);
        Grid.SetRow(tabs, 1);
        layout.Children.Add(tabs);
        Grid.SetRow(body, 2);
        layout.Children.Add(body);
        Grid.SetRow(footer, 3);
        layout.Children.Add(footer);
        Content = layout;

        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape && e.OriginalSource is not TextBox)
            {
                FadeClose();
                e.Handled = true;
            }
        };
        SourceInitialized += (_, _) => RoundCorners();
        Loaded += (_, _) => VPet_Simulator.Core.UiMotion.PopIn(layout, new Point(0.5, 0.5), 0.97, 220);
        Closing += (_, _) => SaveBounds();
        // les fenêtres V-Max comptent comme fenêtres ouvertes : le compagnon ne se déplace pas pendant ce temps
        mw.Windows.Add(this);
        Closed += (_, _) => mw.Windows.Remove(this);
    }

    /// <summary>Contenu principal (sans onglets)</summary>
    protected UIElement? Body { get => body.Content as UIElement; set => SetBody(value); }
    protected UIElement? Footer { get => footer.Content as UIElement; set => footer.Content = value; }

    private void SetBody(UIElement? value)
    {
        body.Content = value;
        if (value is FrameworkElement fe && VPet_Simulator.Core.UiMotion.Enabled)
            fe.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160)));
    }

    /// <summary>Ajoute un onglet ; son contenu est construit à la première ouverture</summary>
    protected void AddTab(string label, Func<FrameworkElement> build)
    {
        var chip = new RadioButton { Style = (Style)FindResource("HudChip"), Content = label, GroupName = "tabs-" + key, Margin = new Thickness(0, 0, 8, 6) };
        int index = tabList.Count;
        chip.Checked += (_, _) => ShowTab(index);
        tabList.Add((chip, build, null));
        tabs.Children.Add(chip);
        tabs.Visibility = tabList.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        if (tabList.Count == 1)
            chip.IsChecked = true;
    }

    /// <summary>Ouvre un onglet par son numéro</summary>
    public void ShowTab(int index)
    {
        if (index < 0 || index >= tabList.Count)
            return;
        var t = tabList[index];
        if (t.chip.IsChecked != true)
        {
            t.chip.IsChecked = true; // rappelle ShowTab via Checked
            return;
        }
        var built = t.built ?? t.build();
        tabList[index] = (t.chip, t.build, built);
        SetBody(built);
    }

    /// <summary>Reconstruit un onglet (données changées)</summary>
    protected void RefreshTab(int index)
    {
        if (index < 0 || index >= tabList.Count)
            return;
        var t = tabList[index];
        tabList[index] = (t.chip, t.build, null);
        if (t.chip.IsChecked == true)
            ShowTab(index);
    }

    /// <summary>Message court dans la fenêtre (remplace les anciens Toast de Panuon)</summary>
    protected void Notify(string text, HudToast.Kind kind = HudToast.Kind.Info) => MW.Toast(text, kind);

    private bool closing;

    /// <summary>Fermeture en fondu</summary>
    public void FadeClose()
    {
        if (closing)
            return;
        closing = true;
        if (Content is UIElement root && VPet_Simulator.Core.UiMotion.Enabled)
        {
            var a = new DoubleAnimation(0, TimeSpan.FromMilliseconds(130));
            a.Completed += (_, _) => Close();
            root.BeginAnimation(OpacityProperty, a);
        }
        else
            Close();
    }

    /// <summary>Ramène la fenêtre au premier plan (si elle est déjà ouverte)</summary>
    public void Present()
    {
        if (!IsVisible)
            Show();
        if (WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;
        Activate();
    }

    #region Briques communes
    protected Brush Res(string k) => (Brush)FindResource(k);
    protected Style St(string k) => (Style)FindResource(k);

    protected TextBlock Section(string text) => new()
    {
        Text = text.ToUpperInvariant(),
        Style = St("HudEyebrow"),
        Margin = new Thickness(0, 18, 0, 8),
    };

    /// <summary>Zone défilante avec la barre de défilement V-Max</summary>
    protected ScrollViewer Scroll(UIElement content, Thickness? padding = null) => new()
    {
        Content = content,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        Padding = padding ?? new Thickness(24, 0, 20, 20),
        Resources = { [typeof(System.Windows.Controls.Primitives.ScrollBar)] = new Style(typeof(System.Windows.Controls.Primitives.ScrollBar), St("HudScrollBar")) },
    };

    /// <summary>Champ de recherche arrondi (icône loupe + texte d'aide)</summary>
    protected TextBox SearchBox(string hint, out FrameworkElement container)
    {
        var tb = new TextBox { Style = St("HudInput"), Tag = hint };
        var icon = new TextBlock { Text = "", FontFamily = (FontFamily)FindResource("HudIcons"), FontSize = 13, Foreground = Res("HudTextMuted"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
        var dock = new DockPanel();
        DockPanel.SetDock(icon, Dock.Left);
        dock.Children.Add(icon);
        dock.Children.Add(tb);
        container = new Border
        {
            CornerRadius = new CornerRadius(14), Background = Res("HudSurfaceRaised"), BorderBrush = Res("HudStroke"),
            BorderThickness = new Thickness(1), Padding = new Thickness(12, 7, 12, 7), Child = dock,
        };
        return tb;
    }

    protected Button Primary(string text, Action run)
    {
        var b = new Button { Style = St("HudPrimaryButton"), Content = text };
        b.Click += (_, _) => run();
        return b;
    }

    protected Button Ghost(string text, Action run)
    {
        var b = new Button { Style = St("HudGhostButton"), Content = text };
        b.Click += (_, _) => run();
        return b;
    }

    protected Button IconButton(string glyph, string tip, Action run)
    {
        var b = new Button { Style = St("HudIconButton"), Content = glyph, ToolTip = tip };
        b.Click += (_, _) => run();
        return b;
    }

    /// <summary>Ajoute un bouton d'en-tête (avant Fermer)</summary>
    protected Button AddHeaderButton(string glyph, string tip, Action run)
    {
        var b = IconButton(glyph, tip, run);
        HeaderButtons.Children.Insert(Math.Max(0, HeaderButtons.Children.Count - 1), b);
        return b;
    }

    /// <summary>Carte (fond relevé, coins arrondis)</summary>
    protected Border Card(UIElement child, Thickness? padding = null) => new()
    {
        CornerRadius = new CornerRadius(16), Background = Res("HudSurfaceRaised"), BorderBrush = Res("HudStroke"),
        BorderThickness = new Thickness(1), Padding = padding ?? new Thickness(16, 12, 16, 14), Child = child,
    };

    /// <summary>État vide : une phrase qui dit quoi faire</summary>
    protected TextBlock Empty(string text) => new()
    {
        Text = text, Style = St("HudBodyText"), Foreground = Res("HudTextMuted"), FontSize = 13.5,
        HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 40, 0, 0), MaxWidth = 420,
    };
    #endregion

    #region Position et taille mémorisées
    private ILine Cfg => MW.Set["vmax_windows"];

    private void RestoreBounds_(double width, double height)
    {
        double w = Cfg.GetFloat(key + "_w", 0), h = Cfg.GetFloat(key + "_h", 0);
        var wa = SystemParameters.WorkArea;
        if (w < MinWidth || h < MinHeight)
        {
            w = Math.Min(width, wa.Width * 0.92);
            h = Math.Min(height, wa.Height * 0.92);
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }
        else
        {
            Left = Cfg.GetFloat(key + "_x", wa.Left + (wa.Width - w) / 2);
            Top = Cfg.GetFloat(key + "_y", wa.Top + (wa.Height - h) / 2);
            if (Left > SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 80 || Left + w < SystemParameters.VirtualScreenLeft + 80
                || Top > SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 80 || Top < SystemParameters.VirtualScreenTop - 20)
                WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }
        Width = w;
        Height = h;
    }

    private void SaveBounds()
    {
        if (WindowState != WindowState.Normal)
            return;
        Cfg.SetFloat(key + "_x", Left);
        Cfg.SetFloat(key + "_y", Top);
        Cfg.SetFloat(key + "_w", ActualWidth);
        Cfg.SetFloat(key + "_h", ActualHeight);
    }
    #endregion

    private void RoundCorners()
    {
        try
        {
            int pref = 2; // DWMWCP_ROUND
            DwmSetWindowAttribute(new WindowInteropHelper(this).Handle, 33, ref pref, sizeof(int));
            int dark = MW.IsDarkTheme ? 1 : 0;
            DwmSetWindowAttribute(new WindowInteropHelper(this).Handle, 20, ref dark, sizeof(int)); // barre de titre/bordure sombres
        }
        catch { }
    }

    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
}
