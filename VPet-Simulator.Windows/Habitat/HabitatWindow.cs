using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shell;

namespace VPet_Simulator.Windows.Habitat;

/// <summary>
/// V-Max habitat : fenêtre « aquarium » qui affiche le décor. Redimensionnable, déplaçable sur n'importe quel écran ;
/// le compagnon lui est rattaché (fenêtre propriétaire) et la suit. Une barre discrète apparaît au survol du haut.
/// </summary>
public sealed class HabitatWindow : Window
{
    private readonly MainWindow mw;
    private readonly HabitatMode mode;
    /// <summary>Monde : image et calques de l'éditeur, en pixels de l'image, sous une seule transformation</summary>
    internal readonly Canvas World = new();
    internal readonly Canvas Overlay = new() { IsHitTestVisible = true };
    internal readonly Grid Stage = new() { ClipToBounds = true };
    private readonly MatrixTransform view = new();
    private readonly Border chrome;
    private readonly TextBlock title;
    private readonly Border emptyHint;
    private HabitatEditor? editor;

    public HabitatWindow(MainWindow mw, HabitatMode mode)
    {
        this.mw = mw;
        this.mode = mode;
        Title = "V-Max · Habitat";
        Icon = mw.Icon;
        WindowStyle = WindowStyle.None;
        // Mode « bureau » : fenêtre RÉELLEMENT transparente → on voit le vrai bureau (fenêtres comprises) à travers.
        // Mode aquarium (image) : fenêtre opaque classique, inchangée.
        AllowsTransparency = mode.DesktopDecor;
        ResizeMode = mode.DesktopDecor ? ResizeMode.NoResize : ResizeMode.CanResize;
        ShowInTaskbar = true;
        MinWidth = 320;
        MinHeight = 200;
        Background = mode.DesktopDecor ? System.Windows.Media.Brushes.Transparent : (Brush)FindResource("HudSurface");
        FontFamily = (FontFamily)FindResource("HudBody");
        Foreground = (Brush)FindResource("HudText");
        if (!mode.DesktopDecor)
            WindowChrome.SetWindowChrome(this, new WindowChrome { CaptionHeight = 0, ResizeBorderThickness = new Thickness(6), GlassFrameThickness = new Thickness(0), UseAeroCaptionButtons = false });
        RestoreBounds_();

        World.Width = mode.Map.Image.Width;
        World.Height = mode.Map.Image.Height;
        World.RenderTransform = view;
        if (mode.Image != null)
        {
            // décor image (aquarium) : on l'affiche. En mode bureau, pas d'image → le vrai bureau reste visible.
            var image = new Image { Source = mode.Image, Width = mode.Map.Image.Width, Height = mode.Map.Image.Height, Stretch = Stretch.Fill };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
            World.Children.Add(image);
        }
        Overlay.Width = World.Width;
        Overlay.Height = World.Height;
        World.Children.Add(Overlay);
        var worldHost = new Canvas();
        worldHost.Children.Add(World);
        Stage.Children.Add(worldHost);

        // barre discrète en haut : titre, éditer, épingler, quitter l'habitat
        title = new TextBlock { FontFamily = (FontFamily)FindResource("HudDisplay"), FontWeight = FontWeights.SemiBold, FontSize = 13, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 12, 0), TextTrimming = TextTrimming.CharacterEllipsis };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(ChromeButton("", "Modifier la carte", () => StartEditing()));
        var pin = ChromeButton(mode.AlwaysOnTop ? "" : "", "Toujours au premier plan", () => { });
        pin.Click += (_, _) =>
        {
            mode.AlwaysOnTop = !mode.AlwaysOnTop;
            pin.Content = mode.AlwaysOnTop ? "" : "";
        };
        buttons.Children.Add(pin);
        buttons.Children.Add(ChromeButton("", "Quitter l'habitat (le compagnon revient sur le bureau)", () => mode.Disable()));
        var bar = new Grid { Height = 40 };
        bar.Children.Add(title);
        bar.Children.Add(buttons);
        chrome = new Border
        {
            Child = bar,
            VerticalAlignment = VerticalAlignment.Top,
            Background = new LinearGradientBrush(Color.FromArgb(0xCC, 0x14, 0x15, 0x1D), Color.FromArgb(0x00, 0x14, 0x15, 0x1D), 90),
            Opacity = 0,
        };
        chrome.MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed && e.OriginalSource is not Button) DragMove(); };
        Stage.Children.Add(chrome);

        emptyHint = new Border
        {
            Style = (Style)FindResource("HudPanel"),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Padding = new Thickness(22, 16, 22, 18),
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false,
        };
        var hint = new StackPanel();
        hint.Children.Add(new TextBlock { Text = "Trace les sols de ton décor", Style = (Style)FindResource("HudTitle"), FontSize = 18 });
        hint.Children.Add(new TextBlock { Text = "Choisis l'outil Sol, puis glisse sur chaque plancher où le compagnon peut marcher.", Style = (Style)FindResource("HudBodyText"), Foreground = (Brush)FindResource("HudTextMuted"), FontSize = 13, MaxWidth = 340, Margin = new Thickness(0, 6, 0, 0) });
        emptyHint.Child = hint;
        Stage.Children.Add(emptyHint);

        Content = Stage;
        RefreshTitle();

        MouseMove += (_, e) => ShowChrome(e.GetPosition(this).Y < 56 || editor != null);
        MouseLeave += (_, _) => ShowChrome(editor != null);
        SizeChanged += (_, _) => { UpdateView(); Moved(); };
        LocationChanged += (_, _) => Moved();
        SourceInitialized += (_, _) => RoundCorners();
        // WPF ferme les fenêtres possédées avec leur propriétaire : on détache le compagnon AVANT la fermeture
        Closing += (_, _) =>
        {
            SaveBounds();
            mode.ReleasePet();
        };
        Closed += (_, _) =>
        {
            IsClosed = true;
            mode.Changed -= RefreshTitle;
            editor?.Detach();
            if (mode.Window == this)
                mode.Disable();
        };
        PreviewKeyDown += (_, e) => editor?.OnKey(e);
        mode.Changed += RefreshTitle;
    }

    public bool IsClosed { get; private set; }

    /// <summary>La souris est sur la barre du haut (déplacement de la fenêtre, boutons)</summary>
    internal bool IsOverChrome => chrome.IsMouseOver && chrome.Opacity > 0.5;

    /// <summary>Projection image → écran sans le zoom de l'éditeur (celle que suit le compagnon)</summary>
    public HabitatProjection ScreenProjection
    {
        get
        {
            var local = HabitatProjection.Compute(mode.Map.Image.Width, mode.Map.Image.Height, new Rect(0, 0, Math.Max(1, ActualWidth), Math.Max(1, ActualHeight)), ImageFit.Fit);
            return local with { OffsetX = local.OffsetX + Left, OffsetY = local.OffsetY + Top };
        }
    }

    /// <summary>Projection image → fenêtre de l'affichage courant (zoom de l'éditeur compris)</summary>
    internal HabitatProjection ViewProjection { get; private set; } = HabitatProjection.Identity;

    internal void SetView(HabitatProjection p)
    {
        ViewProjection = p;
        view.Matrix = new Matrix(p.ScaleX, 0, 0, p.ScaleY, p.OffsetX, p.OffsetY);
        editor?.OnViewChanged();
    }

    /// <summary>Vue « ajustée » : image entière dans la fenêtre</summary>
    internal HabitatProjection FitView() =>
        HabitatProjection.Compute(mode.Map.Image.Width, mode.Map.Image.Height, new Rect(0, 0, Math.Max(1, ActualWidth), Math.Max(1, ActualHeight)), ImageFit.Fit);

    private void UpdateView()
    {
        if (editor == null || !editor.HasCustomView)
            SetView(FitView());
    }

    private void Moved()
    {
        if (editor != null || !IsLoaded || mode.OnDesktop || WindowState == WindowState.Minimized)
            return;
        mode.Reproject();
        mode.PlacePet();
    }

    #region Édition
    public bool IsEditing => editor != null;

    internal void QaHover(Point image, string? select) => editor?.QaHover(image, select);
    internal void QaDetect() => editor?.DetectFloors();
    internal void QaSuggestRooms() => editor?.SuggestRooms();

    private Rect? aquariumBounds; // taille de l'« aquarium » mémorisée pendant l'édition

    public void StartEditing()
    {
        if (editor != null)
            return;
        mode.ExitDesktop();
        // V-Max : en édition, on passe en espace de travail confortable et centré au lieu de garder la taille de
        // l'« aquarium » (qui épouse le ratio de l'image → « bande étirée » pour une image panoramique).
        aquariumBounds = new Rect(Left, Top, ActualWidth, ActualHeight);
        if (WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;
        if (mode.DesktopDecor)
        {
            // mode bureau : la fenêtre est DÉJÀ l'overlay transparent plein écran → on ne la redimensionne pas,
            // on trace directement sur le vrai bureau visible à travers.
            aquariumBounds = null;
        }
        else if (mode.SpanScreens && System.Windows.Forms.Screen.AllScreens.Length > 1)
        {
            // MULTI-ÉCRANS : on édite EN PLEIN ÉCRAN sur tout le bureau virtuel, directement sur le décor qui couvre
            // tous tes écrans (ton fond d'écran) → tu traces tes sols/zones/limites là où Maxine les verra vraiment.
            var v = System.Windows.Forms.SystemInformation.VirtualScreen;
            var m = PresentationSource.FromVisual(mw)?.CompositionTarget?.TransformFromDevice ?? System.Windows.Media.Matrix.Identity;
            var tl = m.Transform(new Point(v.Left, v.Top));
            var br = m.Transform(new Point(v.Right, v.Bottom));
            Left = tl.X;
            Top = tl.Y;
            Width = br.X - tl.X;
            Height = br.Y - tl.Y;
        }
        else
        {
            // mono-écran : espace de travail confortable et centré (évite la « bande étirée »).
            var wa = SystemParameters.WorkArea;
            double ew = Math.Min(wa.Width * 0.92, 1320), eh = Math.Min(wa.Height * 0.92, 860);
            Width = ew;
            Height = eh;
            Left = wa.Left + (wa.Width - ew) / 2;
            Top = wa.Top + (wa.Height - eh) / 2;
        }
        SetView(FitView());
        mode.SetEditing(true);
        editor = new HabitatEditor(this, mode);
        ShowChrome(true);
        RefreshTitle();
        emptyHint.Visibility = Visibility.Collapsed;
    }

    internal void StopEditing(HabitatMap? save)
    {
        if (editor == null)
            return;
        editor.Detach();
        editor = null;
        // retour à la taille de l'« aquarium » mémorisée à l'entrée en édition
        if (aquariumBounds is { } b)
        {
            Left = b.X;
            Top = b.Y;
            Width = b.Width;
            Height = b.Height;
            aquariumBounds = null;
        }
        SetView(FitView());
        if (save != null)
            mode.SaveMap(save);
        mode.SetEditing(false);
        mode.Reproject();
        mode.PlacePet();
        RefreshTitle();
        ShowChrome(false);
    }
    #endregion

    private void RefreshTitle()
    {
        title.Text = (editor != null ? "Modifier la carte · " : "Habitat · ") + (mode.Map.Image.Name ?? "Décor");
        emptyHint.Visibility = editor == null && mode.Map.Floors.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ShowChrome(bool show) =>
        chrome.BeginAnimation(OpacityProperty, new DoubleAnimation(show ? 1 : 0, TimeSpan.FromMilliseconds(show ? 120 : 260)));

    private Button ChromeButton(string glyph, string tip, Action run)
    {
        var b = new Button { Style = (Style)FindResource("HudIconButton"), Content = glyph, ToolTip = tip, Margin = new Thickness(0, 0, 4, 0) };
        b.Click += (_, _) => run();
        return b;
    }

    #region Position et taille mémorisées
    private void RestoreBounds_()
    {
        if (mode.DesktopDecor)
        {
            // mode bureau : la fenêtre transparente couvre TOUT le bureau virtuel (tous les écrans)
            var v = System.Windows.Forms.SystemInformation.VirtualScreen;
            var m = PresentationSource.FromVisual(mw)?.CompositionTarget?.TransformFromDevice ?? System.Windows.Media.Matrix.Identity;
            var tl = m.Transform(new Point(v.Left, v.Top));
            var br = m.Transform(new Point(v.Right, v.Bottom));
            Left = tl.X;
            Top = tl.Y;
            Width = br.X - tl.X;
            Height = br.Y - tl.Y;
            return;
        }
        var cfg = mw.Set["vmax_habitat"];
        double w = cfg.GetFloat("w", 0), h = cfg.GetFloat("h", 0);
        var wa = SystemParameters.WorkArea;
        if (w < MinWidth || h < MinHeight)
        {
            // première ouverture : 60 % de la largeur de l'écran, au format de l'image, centrée
            w = wa.Width * 0.6;
            h = w * mode.Map.Image.Height / Math.Max(1, mode.Map.Image.Width);
            if (h > wa.Height * 0.8)
            {
                h = wa.Height * 0.8;
                w = h * mode.Map.Image.Width / Math.Max(1, mode.Map.Image.Height);
            }
            Left = wa.Left + (wa.Width - w) / 2;
            Top = wa.Top + (wa.Height - h) / 2;
        }
        else
        {
            Left = cfg.GetFloat("x", wa.Left);
            Top = cfg.GetFloat("y", wa.Top);
            // fenêtre restée sur un écran débranché : on la ramène sur le bureau virtuel
            if (Left > SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 80 || Left + w < SystemParameters.VirtualScreenLeft + 80
                || Top > SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 80 || Top < SystemParameters.VirtualScreenTop - 20)
            {
                Left = wa.Left + (wa.Width - w) / 2;
                Top = wa.Top + (wa.Height - h) / 2;
            }
        }
        Width = w;
        Height = h;
    }

    internal void SaveBounds()
    {
        var cfg = mw.Set["vmax_habitat"];
        cfg.SetFloat("x", Left);
        cfg.SetFloat("y", Top);
        cfg.SetFloat("w", ActualWidth);
        cfg.SetFloat("h", ActualHeight);
    }
    #endregion

    private void RoundCorners()
    {
        try
        {
            int pref = 2; // DWMWCP_ROUND
            DwmSetWindowAttribute(new WindowInteropHelper(this).Handle, 33, ref pref, sizeof(int));
        }
        catch { }
    }

    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
}
