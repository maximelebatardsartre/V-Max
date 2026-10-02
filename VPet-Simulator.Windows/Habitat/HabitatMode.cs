using LinePutScript;
using System;
using System.ComponentModel;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using VPet_Simulator.Core;

namespace VPet_Simulator.Windows.Habitat;

/// <summary>
/// V-Max habitat : mode autonome. Le compagnon vit dans une fenêtre « habitat » qui affiche le décor choisi
/// (par défaut le fond d'écran) et se déplace sur les sols tracés par l'utilisateur.
/// Réglages dans Setting.lps, section vmax_habitat.
/// </summary>
public sealed class HabitatMode
{
    private readonly MainWindow mw;
    private HabitatController? controller;
    private IController? classicController;
    private double classicZoom;
    private Window? classicOwner;
    private DependencyPropertyDescriptor? topmostWatch;
    private bool persist = true;

    public HabitatMode(MainWindow mw)
    {
        this.mw = mw;
        Metrics = PetMetrics.Measure(mw);
        mw.PreviewMouseWheel += Pet_PreviewMouseWheel;
        // à la fermeture de V-Max, on mémorise la place de l'habitat (le mode reste activé pour le prochain lancement)
        mw.Closing += (_, _) =>
        {
            Window?.SaveBounds();
            mw.Voice?.Dispose();
            mw.LocalAi?.Stop();
        };
    }

    private ILine Cfg => mw.Set["vmax_habitat"];

    /// <summary>Fenêtre principale (compagnon)</summary>
    internal MainWindow MW => mw;

    public bool IsActive => Window != null;
    public HabitatWindow? Window { get; private set; }
    public HabitatMap Map { get; private set; } = new();
    /// <summary>Carte de la fenêtre habitat (Map peut être celle du fond d'écran quand le compagnon est sur le bureau)</summary>
    public HabitatMap WindowMap { get; private set; } = new();
    /// <summary>Le compagnon vit sur le vrai fond d'écran (Win+D ou inactivité)</summary>
    public bool OnDesktop { get; private set; }
    private HabitatDesktop? desktop;

    public BitmapSource? Image { get; private set; }
    public string? ImagePath { get; private set; }
    public PetMetrics Metrics { get; private set; } = PetMetrics.Default;
    /// <summary>Projection image → écran (unités WPF) de la fenêtre habitat, sans le zoom de l'éditeur</summary>
    public HabitatProjection Projection { get; private set; } = HabitatProjection.Identity;

    /// <summary>Activé au dernier lancement</summary>
    public bool Enabled
    {
        get => Cfg.GetBool("enabled");
        private set => Cfg.SetBool("enabled", value);
    }

    /// <summary>Image choisie (null = fond d'écran actuel)</summary>
    public string? ChosenImage
    {
        get => Cfg.GetString("image", null);
        set
        {
            if (string.IsNullOrEmpty(value)) Cfg.Remove("image");
            else Cfg.SetString("image", value);
        }
    }

    /// <summary>Sortir sur le vrai bureau quand il est affiché (Win+D) ou au repos</summary>
    public bool DesktopEnabled
    {
        get => !Cfg.GetBool("desktop_off");
        set => Cfg.SetBool("desktop_off", !value);
    }

    /// <summary>
    /// Multi-écrans : sur le bureau, le décor et les déplacements s'étendent sur TOUS les écrans (bureau virtuel),
    /// comme le mode « Étendu » des fonds d'écran Windows — Maxine passe d'un écran à l'autre. Activé par défaut ;
    /// sans effet sur un seul écran. Optimal quand les écrans partagent la même mise à l'échelle Windows ; pour des
    /// DPI différents le raccord peut être imparfait (amélioration prévue).
    /// </summary>
    public bool SpanScreens
    {
        get => !Cfg.GetBool("span_off");
        set => Cfg.SetBool("span_off", !value);
    }

    /// <summary>
    /// Vivre directement sur le bureau en TRANSPARENT (temps réel) : on voit le vrai bureau à travers, et on trace
    /// les sols/zones dessus. Activé par défaut (c'est l'expérience principale). Désactivé = ancien mode « décor image ».
    /// </summary>
    public bool DesktopMode
    {
        get => !Cfg.GetBool("desktop_mode_off");
        set => Cfg.SetBool("desktop_mode_off", !value);
    }

    /// <summary>Position du compagnon sur le bureau : « front » = toujours DEVANT les fenêtres ; « behind » = DERRIÈRE.</summary>
    public string Position
    {
        get => Cfg.GetString("position", "front") ?? "front";
        set
        {
            Cfg.SetString("position", value);
            if (Window != null && DesktopDecor)
                Window.Topmost = value != "behind";
        }
    }

    /// <summary>Expérimental : vivre en permanence dans la couche du fond d'écran, derrière les fenêtres</summary>
    public bool WallpaperLayerEnabled
    {
        get => Cfg.GetBool("wallpaper_layer");
        set
        {
            Cfg.SetBool("wallpaper_layer", value);
            if (!value && OnDesktop && layerAttached)
                ExitDesktop();
        }
    }

    internal string WallpaperLayer_Diagnostic => WallpaperLayer.Diagnostic;

    /// <summary>Le compagnon est accroché à la couche du fond d'écran</summary>
    public bool LayerAttached => layerAttached;
    private bool layerAttached;

    /// <summary>Minutes d'inactivité avant de sortir sur le bureau (0 = jamais)</summary>
    public int IdleMinutes
    {
        get => Cfg.GetInt("idle_minutes", 5);
        set => Cfg.SetInt("idle_minutes", Math.Max(0, value));
    }

    public bool AlwaysOnTop
    {
        get => Cfg.GetBool("topmost");
        set
        {
            Cfg.SetBool("topmost", value);
            if (Window != null)
                Window.Topmost = value;
        }
    }

    public event Action? Changed;

    /// <summary>
    /// Laisse : le compagnon reste dans cette portion de son sol (pixels de l'image), par exemple pour flâner dans une pièce
    /// </summary>
    public (double left, double right)? Leash { get; set; }

    /// <summary>Sol et position (pixels de l'image) du compagnon, ou null s'il n'est sur aucun sol</summary>
    public (HabitatFloor floor, double x)? Locate()
    {
        if (!IsActive)
            return null;
        double size = mw.ActualWidth > 1 ? mw.ActualWidth : 500 * mw.Set.ZoomLevel;
        double x = Projection.ToImageX(mw.Left + size * Metrics.CenterRatio);
        double y = Projection.ToImageY(mw.Top + size * Metrics.FootRatio);
        var f = Map.FloorAt(x, y, Math.Max(3, 8 / Math.Max(0.01, Projection.ScaleY))) ?? Map.LandingFloor(x, y);
        return f == null ? null : (f, f.Clamp(x));
    }

    #region Position mémorisée du compagnon (coordonnées de l'image)
    private double petX = double.NaN;
    private string? petFloor;

    internal void Remember(double imageX, string floorId)
    {
        petX = imageX;
        petFloor = floorId;
    }
    #endregion

    /// <summary>
    /// Active le mode habitat avec l'image choisie (ou le fond d'écran)
    /// </summary>
    public async Task<string?> EnableAsync(string? imagePath = null, bool persist = true)
    {
        if (IsActive)
            return null;
        this.persist = persist;
        var path = imagePath ?? (ChosenImage is { } chosen && File.Exists(chosen) ? chosen : WallpaperInfo.CurrentPath());
        if (path == null)
            return "Aucune image : choisis une image pour l'habitat (ton fond d'écran est une couleur unie).";
        HabitatMap map;
        BitmapSource image;
        try
        {
            (map, image) = await Task.Run(() => LoadFor(path));
        }
        catch (Exception e)
        {
            return "Impossible d'ouvrir cette image : " + e.Message;
        }
        Activate(map, image, path);
        return null;
    }

    /// <summary>Décor « bureau » : pas d'image, le vrai bureau est visible à travers la fenêtre transparente.</summary>
    public bool DesktopDecor { get; private set; }

    /// <summary>
    /// Active l'habitat TRANSPARENT sur le bureau : aucune image, on voit le vrai bureau (fenêtres comprises) en
    /// direct ; la carte (sols/zones) est en coordonnées du bureau virtuel, donc générique pour toute config
    /// (mono, multi, vertical, DPI mixte). Clé de carte = signature de la disposition des écrans.
    /// </summary>
    public Task<string?> EnableDesktopAsync(bool persist = true)
    {
        if (IsActive)
            return Task.FromResult<string?>(null);
        this.persist = persist;
        var vs = System.Windows.Forms.SystemInformation.VirtualScreen;
        string key = $"desktop-{vs.Width}x{vs.Height}-{System.Windows.Forms.Screen.AllScreens.Length}";
        var map = HabitatMap.TryLoad(key) ?? new HabitatMap();
        map.Image = new HabitatImage { Sha256 = key, Width = vs.Width, Height = vs.Height, Name = "Bureau" };
        Activate(map, null, null);
        return Task.FromResult<string?>(null);
    }

    /// <summary>Partie commune d'activation (image OU bureau transparent).</summary>
    private void Activate(HabitatMap map, BitmapSource? image, string? path)
    {
        Map = WindowMap = map;
        Image = image;
        ImagePath = path;
        DesktopDecor = image == null;
        Metrics = PetMetrics.Measure(mw);

        classicController = mw.Core.Controller;
        // au redémarrage en mode habitat, le zoom enregistré est celui de l'habitat : on garde la taille classique mémorisée
        double stored = Cfg.GetFloat("classic_zoom", -1);
        classicZoom = Enabled && stored > 0 ? stored : mw.Set.ZoomLevel;
        if (persist)
            Cfg.SetFloat("classic_zoom", classicZoom);
        classicOwner = mw.Owner;

        Window = new HabitatWindow(mw, this);
        // mode bureau : la position (devant/derrière les fenêtres) choisie par l'utilisateur ; sinon l'épingle.
        Window.Topmost = DesktopDecor ? Position != "behind" : AlwaysOnTop;
        Window.Show();
        mw.Owner = Window;
        mw.Topmost = false;
        topmostWatch = DependencyPropertyDescriptor.FromProperty(System.Windows.Window.TopmostProperty, typeof(Window));
        topmostWatch.AddValueChanged(mw, KeepPetBelowTop);

        controller = new HabitatController(mw, classicController!, this);
        mw.Core.Controller = controller;
        // l'habitat a besoin des déplacements, sans le « déplacement intelligent » qui les coupe au bout d'un moment
        mw.Main.SetMoveMode(true, false, mw.Set.SmartMoveInterval * 1000);
        if (persist)
            Enabled = true;

        Reproject();
        PlacePet();
        if (Map.Floors.Count == 0)
            Window.StartEditing();
        desktop ??= new HabitatDesktop(mw, this);
        desktop.Start();
        Changed?.Invoke();
    }

    /// <summary>Détache le compagnon de la fenêtre habitat (avant toute fermeture de celle-ci)</summary>
    internal void ReleasePet()
    {
        if (mw.Owner == Window && Window != null)
            mw.Owner = classicOwner;
    }

    /// <summary>Quitte l'habitat : le compagnon retrouve son comportement classique</summary>
    public void Disable()
    {
        if (!IsActive)
            return;
        desktop?.Stop();
        mw.Life?.Cancel(null);
        Leash = null;
        if (persist)
            Enabled = false;
        topmostWatch?.RemoveValueChanged(mw, KeepPetBelowTop);
        topmostWatch = null;
        mw.BeginAnimation(System.Windows.Window.TopProperty, null);
        mw.BeginAnimation(System.Windows.Window.LeftProperty, null);
        ReleasePet();
        if (classicController != null)
            mw.Core.Controller = classicController;
        mw.SetZoomLevel(classicZoom > 0 ? classicZoom : Cfg.GetFloat("classic_zoom", 0.5));
        mw.Topmost = mw.Set.TopMost;
        // garantit que le compagnon redevient cliquable : si on ferme la fenêtre habitat PENDANT l'édition (où
        // IsHitTestVisible est passé à false pour laisser passer les clics vers l'éditeur), il resterait sinon figé.
        mw.IsHitTestVisible = true;
        mw.Opacity = mw.Set.OpacityMain ? mw.Set.Opacity : 1;
        mw.Main.SetMoveMode(mw.Set.AllowMove, mw.Set.SmartMove, mw.Set.SmartMoveInterval * 1000);
        var w = Window;
        Window = null;
        controller = null;
        if (w is { IsClosed: false })
            w.Close();
        // le compagnon revient sur l'écran, posé en bas comme d'habitude
        mw.Core.Controller.ResetPosition();
        Changed?.Invoke();
    }

    /// <summary>Change l'image du décor (null = fond d'écran)</summary>
    public async Task<string?> UseImageAsync(string? path)
    {
        ChosenImage = path;
        if (!IsActive)
            return null;
        Disable();
        return await EnableAsync();
    }

    private static (HabitatMap, BitmapSource) LoadFor(string path)
    {
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.UriSource = new Uri(path);
        bmp.EndInit();
        bmp.Freeze();
        var sha = HabitatMap.Sha256Of(path);
        var map = HabitatMap.TryLoad(sha) ?? new HabitatMap
        {
            Image = new HabitatImage { Sha256 = sha, Width = bmp.PixelWidth, Height = bmp.PixelHeight },
            PetHeight = HabitatMap.DefaultPetHeight(bmp.PixelHeight),
        };
        map.Image.Path = path;
        map.Image.Name ??= Path.GetFileNameWithoutExtension(path);
        return (map, bmp);
    }

    /// <summary>Enregistre la carte (après édition ou changement de taille)</summary>
    public void SaveMap(HabitatMap map)
    {
        if (OnDesktop && Map != WindowMap)
        {
            // la carte du fond d'écran a été redimensionnée (Ctrl + molette) : on l'enregistre telle quelle
            try { map.Save(); } catch { }
            return;
        }
        Map = WindowMap = map;
        try
        {
            map.Save();
        }
        catch (Exception e)
        {
            mw.Toast("Impossible d'enregistrer la carte : " + e.Message, HUD.HudToast.Kind.Warning);
        }
        Reproject();
        PlacePet();
        Changed?.Invoke();
    }

    /// <summary>
    /// Recalcule la projection depuis la fenêtre habitat et ajuste la taille du compagnon
    /// </summary>
    public void Reproject()
    {
        if (Window == null)
            return;
        if (OnDesktop)
            Projection = desktopProjection();
        else
            Projection = Window.ScreenProjection;
        double size = Metrics.WindowSizeFor(Map.PetHeight * Projection.ScaleY);
        mw.SetZoomLevel(Math.Clamp(size / 500, 0.05, 8));
    }

    /// <summary>
    /// Replace le compagnon à sa position mémorisée dans l'image (après un déplacement ou un redimensionnement de la fenêtre)
    /// </summary>
    public void PlacePet()
    {
        if (Window == null || controller == null)
            return;
        var floor = Map.Floor(petFloor) ?? Map.MainFloor;
        double size = 500 * mw.Set.ZoomLevel;
        if (floor == null)
        {
            // pas encore de sol : le compagnon attend au centre bas de l'habitat
            var b = Projection.ImageBounds(Map.Image.Width, Map.Image.Height);
            mw.Left = b.X + b.Width / 2 - size * Metrics.CenterRatio;
            mw.Top = b.Bottom - size * Metrics.FootRatio;
            return;
        }
        double x = floor.Clamp(double.IsNaN(petX) ? (floor.Left + floor.Right) / 2 : petX);
        mw.BeginAnimation(System.Windows.Window.TopProperty, null);
        mw.BeginAnimation(System.Windows.Window.LeftProperty, null);
        mw.Left = Projection.ToScreenX(x) - size * Metrics.CenterRatio;
        mw.Top = Projection.ToScreenY(floor.Y) - size * Metrics.FootRatio;
        Remember(x, floor.Id);
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    /// <summary>QA : passe sur le bureau avec la carte de l'habitat, sur l'écran principal</summary>
    internal void QaEnterDesktop(bool layer = false)
    {
        qaForceLayer = layer;
        Rect bounds; ImageFit fit;
        if (SpanScreens)
        {// même logique que HabitatDesktop : couvre le bureau virtuel (tous les écrans)
            var v = System.Windows.Forms.SystemInformation.VirtualScreen;
            var m = System.Windows.PresentationSource.FromVisual(mw)?.CompositionTarget?.TransformFromDevice ?? System.Windows.Media.Matrix.Identity;
            bounds = new Rect(m.Transform(new Point(v.Left, v.Top)), m.Transform(new Point(v.Right, v.Bottom)));
            fit = ImageFit.Span;
        }
        else
        {
            bounds = new Rect(0, 0, SystemParameters.PrimaryScreenWidth, SystemParameters.PrimaryScreenHeight);
            fit = ImageFit.Fill;
        }
        EnterDesktop(WindowMap, bounds, fit, 1, topmost: !layer);
        qaForceLayer = false;
    }
    private bool qaForceLayer;

    #region Bureau (Win+D, inactivité)
    private Func<HabitatProjection> desktopProjection = () => HabitatProjection.Identity;
    private bool desktopTopmost;

    /// <summary>
    /// Le compagnon passe sur le vrai fond d'écran, avec la carte de cette image projetée sur l'écran
    /// </summary>
    internal void EnterDesktop(HabitatMap map, Rect monitor, ImageFit fit, double pixelToDip, bool topmost)
    {
        if (OnDesktop || Window == null)
            return;
        mw.Life?.Cancel(null);
        Leash = null;
        bool sameMap = map == WindowMap;
        OnDesktop = true;
        Map = map;
        desktopTopmost = topmost;
        desktopProjection = () => HabitatProjection.Compute(map.Image.Width, map.Image.Height, monitor, fit, pixelToDip);
        ReleasePet();
        // Windows masque les fenêtres possédées avec leur propriétaire réduit (Win+D) : on réaffiche le compagnon sans voler le focus
        ShowWindow(new System.Windows.Interop.WindowInteropHelper(mw).Handle, 4 /* SW_SHOWNOACTIVATE */);
        mw.Topmost = topmost;
        // expérimental : le compagnon passe dans la couche du fond d'écran, derrière les fenêtres
        if ((WallpaperLayerEnabled || qaForceLayer) && !topmost && WallpaperLayer.Attach(mw))
        {
            // Left/Top restent en coordonnées écran (WallpaperLayer corrige la conversion vers l'hôte)
            layerAttached = true;
        }
        if (!sameMap)
        {
            petX = double.NaN;
            petFloor = null;
        }
        Reproject();
        PlacePet();
        if (layerAttached)
            WallpaperLayer.Note($"posé Left={mw.Left:0} (projection {Projection.OffsetX:0}+{Projection.ScaleX:0.000}x), à l'écran {WallpaperLayer.ScreenPosition(mw)}");
        Changed?.Invoke();
    }

    /// <summary>Retour dans la fenêtre habitat</summary>
    internal void ExitDesktop()
    {
        if (!OnDesktop)
            return;
        mw.Life?.Cancel(null);
        Leash = null;
        bool sameMap = Map == WindowMap;
        if (layerAttached)
        {
            WallpaperLayer.Detach(mw);
            layerAttached = false;
        }
        OnDesktop = false;
        Map = WindowMap;
        desktopTopmost = false;
        mw.Topmost = false;
        if (Window != null)
        {
            // Win+D a pu réduire la fenêtre habitat ; une fenêtre « owned » par une fenêtre réduite est masquée, donc
            // le compagnon disparaîtrait au retour sur le bureau. On restaure d'abord la fenêtre habitat.
            if (Window.WindowState == System.Windows.WindowState.Minimized)
                Window.WindowState = System.Windows.WindowState.Normal;
            mw.Owner = Window;
        }
        if (!sameMap)
        {
            petX = double.NaN;
            petFloor = null;
        }
        Reproject();
        PlacePet();
        Changed?.Invoke();
    }
    #endregion

    /// <summary>Pendant l'édition, le compagnon s'efface (le fantôme le remplace)</summary>
    internal void SetEditing(bool editing)
    {
        mw.Opacity = editing ? 0 : mw.Set.OpacityMain ? mw.Set.Opacity : 1;
        mw.IsHitTestVisible = !editing;
        mw.Main.SetMoveMode(!editing, false, mw.Set.SmartMoveInterval * 1000);
    }

    private void KeepPetBelowTop(object? sender, EventArgs e)
    {
        // dans l'habitat, le compagnon suit la fenêtre habitat (fenêtre propriétaire) au lieu d'être toujours au premier plan
        if (IsActive && mw.Topmost && !(OnDesktop && desktopTopmost))
            mw.Topmost = false;
    }

    #region Taille à la volée : Ctrl + molette sur le compagnon
    private System.Windows.Threading.DispatcherTimer? sizeToast;

    private void Pet_PreviewMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
    {
        if (!System.Windows.Input.Keyboard.Modifiers.HasFlag(System.Windows.Input.ModifierKeys.Control))
            return;
        e.Handled = true;
        double factor = Math.Pow(1.06, e.Delta / 120.0);
        if (IsActive)
        {
            var map = Map;
            map.PetHeight = Math.Clamp(map.PetHeight * factor, map.Image.Height * 0.04, map.Image.Height * 0.9);
            Reproject();
            PlacePet();
        }
        else
            ResizeClassic(mw.Set.ZoomLevel * factor);
        sizeToast ??= new System.Windows.Threading.DispatcherTimer(TimeSpan.FromMilliseconds(450), System.Windows.Threading.DispatcherPriority.Normal, (_, _) =>
        {
            sizeToast!.Stop();
            if (IsActive)
                SaveMap(Map);
            mw.Toast("Taille : " + SizeDescription(), HUD.HudToast.Kind.Info, 2.5);
        }, mw.Dispatcher);
        sizeToast.Stop();
        sizeToast.Start();
    }

    /// <summary>
    /// Change la taille en gardant les pieds au même endroit
    /// </summary>
    public void ResizeClassic(double zoom)
    {
        zoom = Math.Clamp(zoom, 0.1, 4);
        double old = mw.ActualWidth > 1 ? mw.ActualWidth : 500 * mw.Set.ZoomLevel;
        double size = 500 * zoom;
        double feetX = mw.Left + old * Metrics.CenterRatio, feetY = mw.Top + old * Metrics.FootRatio;
        mw.SetZoomLevel(zoom);
        mw.Left = feetX - size * Metrics.CenterRatio;
        mw.Top = feetY - size * Metrics.FootRatio;
    }

    /// <summary>Taille actuelle, en pourcentage et par rapport à la hauteur de l'écran</summary>
    public string SizeDescription()
    {
        double visible = 500 * mw.Set.ZoomLevel * Metrics.StandingRatio;
        double screen = SystemParameters.WorkArea.Height;
        try
        {
            var src = PresentationSource.FromVisual(mw);
            var m = src?.CompositionTarget?.TransformToDevice ?? System.Windows.Media.Matrix.Identity;
            var center = new Point(mw.Left + mw.ActualWidth / 2, mw.Top + mw.ActualHeight / 2);
            var dev = m.Transform(center);
            var sc = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point((int)dev.X, (int)dev.Y));
            screen = sc.WorkingArea.Height / m.M22;
        }
        catch { }
        return $"{mw.Set.ZoomLevel * 100:0} % · {visible / screen * 100:0} % de la hauteur de l'écran";
    }
    #endregion
}
