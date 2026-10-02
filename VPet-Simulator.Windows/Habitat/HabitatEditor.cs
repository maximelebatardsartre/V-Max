using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace VPet_Simulator.Windows.Habitat;

/// <summary>
/// V-Max habitat : éditeur de carte intégré à la fenêtre habitat. Tout se fait en direct sur l'image, en pixels de
/// l'image ; la vue (zoom, défilement) n'est qu'une transformation.
/// Outils : Sélection, Sol, Escalade, Chute, Pièce, Emplacement. Aimantation (les extrémités des échelles se collent
/// aux sols), fantôme du compagnon à l'échelle et fantôme des activités avec leurs meubles, panneau de propriétés,
/// annuler / rétablir, validation en direct (échelles non reliées, pièces inaccessibles…).
/// </summary>
internal sealed class HabitatEditor
{
    private enum Tool { Select, Floor, Climb, Drop, Room, Spot }
    private enum Kind { None, Floor, Climb, Drop, Room, Spot }
    /// <summary>End1/End2 : extrémités d'un sol (X1/X2) ou d'une échelle (haut/bas) ; Corner : coin bas-droit d'une pièce</summary>
    private enum Part { None, Body, End1, End2, Corner }
    private enum Drag { None, Draw, Move, Pan }
    private sealed record Sel(Kind Kind, string Id);
    private sealed record Issue(string Text, Sel? Target);

    private const double SnapPx = 10;     // aimantation, en pixels d'écran
    private const double HitPx = 9;       // tolérance de sélection, en pixels d'écran

    /// <summary>Types de pièces proposés (le type sert aux routines quand le nom ne correspond pas)</summary>
    internal static readonly (string tag, string label)[] RoomTypes =
    [
        ("kitchen", "Cuisine"), ("bedroom", "Chambre"), ("living", "Salon"), ("bathroom", "Salle de bain"),
        ("office", "Bureau"), ("garden", "Jardin"), ("other", "Autre"),
    ];

    private readonly HabitatWindow win;
    private readonly HabitatMode mode;
    private readonly HabitatMap work;
    private readonly NavCapabilities caps;
    private readonly Stack<string> undo = new(), redo = new();
    private Tool tool;
    private Sel? selected;

    private Drag drag;
    private Part dragPart;
    private Point dragStart, drawNow;  // image
    private Point panStart;            // fenêtre
    private HabitatProjection panView;
    private HabitatMap origin = new(); // copie de l'élément avant déplacement
    private bool dragChanged;
    private Point? lastMouse;

    private readonly Canvas previewLayer = new() { IsHitTestVisible = false };
    private readonly Image ghost = new() { IsHitTestVisible = false, Opacity = 0.55, Stretch = Stretch.Fill };
    private readonly Border toolbar;
    private readonly Border props;
    private readonly TextBlock issueText;
    private readonly Slider size;
    private readonly Dictionary<Tool, RadioButton> toolChips = new();
    private List<Issue> issues = new();
    private bool syncingSlider;

    public bool HasCustomView { get; private set; }

    public HabitatEditor(HabitatWindow win, HabitatMode mode)
    {
        this.win = win;
        this.mode = mode;
        work = mode.Map.Clone();
        caps = new HabitatPilot(mode.MW, mode).Capabilities();
        ghost.Source = mode.Metrics.Standing;

        // outils préremplis dans toolChips, puis assemblés par groupes thématiques (barre structurée)
        toolChips[Tool.Select] = ToolChip("", "Sélection", "Sélectionner, déplacer, supprimer (1)", Tool.Select);
        toolChips[Tool.Floor] = ToolChip("", "Sol", "Tracer un sol : glisser horizontalement (2)", Tool.Floor);
        toolChips[Tool.Climb] = ToolChip("", "Escalade", "Tracer une échelle ou un pilier : glisser verticalement entre deux sols (3)", Tool.Climb);
        toolChips[Tool.Drop] = ToolChip("", "Chute", "Cliquer sur un sol d'où le compagnon peut sauter vers le sol du dessous (4)", Tool.Drop);
        toolChips[Tool.Room] = ToolChip("", "Pièce", "Encadrer une pièce puis la nommer (5)", Tool.Room);
        toolChips[Tool.Spot] = ToolChip("", "Emplacement", "Cliquer sur un sol pour y placer une activité (dormir, manger…) (6)", Tool.Spot);

        size = new Slider
        {
            Minimum = Math.Round(work.Image.Height * 0.04),
            Maximum = Math.Round(work.Image.Height * 0.6),
            Value = work.PetHeight,
            Width = 104,
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = "Taille du compagnon dans ce décor",
        };
        size.PreviewMouseDown += (_, _) => Checkpoint();
        size.ValueChanged += (_, e) =>
        {
            if (syncingSlider)
                return;
            work.PetHeight = Math.Round(e.NewValue);
            Render();
        };
        var sizeIcon = new TextBlock { Text = "", FontFamily = Font("HudIcons"), FontSize = 14, Foreground = Res("HudTextMuted"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0), ToolTip = "Taille du compagnon" };

        var cancel = new Button { Style = (Style)win.FindResource("HudGhostButton"), Content = "Annuler", Margin = new Thickness(0, 0, 6, 0), ToolTip = "Quitter sans enregistrer" };
        cancel.Click += (_, _) => win.StopEditing(null);
        var done = new Button { Style = (Style)win.FindResource("HudPrimaryButton"), Content = "Terminer", ToolTip = "Enregistrer la carte (Entrée)" };
        done.Click += (_, _) => Finish();

        // un groupe = petit intitulé + rangée d'éléments ; séparateurs verticaux entre groupes
        StackPanel Grp(string? label, params UIElement[] items)
        {
            var col = new StackPanel { Margin = new Thickness(7, 0, 7, 0), VerticalAlignment = VerticalAlignment.Center };
            if (label != null)
                col.Children.Add(new TextBlock { Text = label, FontSize = 9, FontWeight = FontWeights.SemiBold, Foreground = Res("HudTextMuted"), Margin = new Thickness(3, 0, 0, 3), Opacity = 0.75 });
            var rr = new StackPanel { Orientation = Orientation.Horizontal };
            foreach (var it in items)
                rr.Children.Add(it);
            col.Children.Add(rr);
            return col;
        }
        Border Div() => new Border { Width = 1, Margin = new Thickness(3, 10, 3, 6), Background = new SolidColorBrush(Color.FromArgb(0x33, 0x9A, 0xA2, 0xB4)) };

        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(Grp(null, toolChips[Tool.Select]));
        row.Children.Add(Div());
        row.Children.Add(Grp("STRUCTURE", toolChips[Tool.Floor], toolChips[Tool.Climb], toolChips[Tool.Drop]));
        row.Children.Add(Div());
        row.Children.Add(Grp("ZONES", toolChips[Tool.Room], toolChips[Tool.Spot]));
        row.Children.Add(Div());
        row.Children.Add(Grp("TAILLE", sizeIcon, size));
        row.Children.Add(Div());
        row.Children.Add(Grp("ASSISTANTS", IconButton("", "Détecter les sols automatiquement", DetectFloors), IconButton("", "Suggérer les pièces avec l'IA (vision)", AskRoomSuggestions)));
        row.Children.Add(Div());
        row.Children.Add(Grp("VUE", IconButton("", "Annuler (Ctrl+Z)", Undo), IconButton("", "Rétablir (Ctrl+Y)", Redo), IconButton("", "Ajuster la vue (F)", FitView)));
        row.Children.Add(Div());
        row.Children.Add(Grp(null, cancel, done));

        issueText = new TextBlock { FontSize = 12, Foreground = Res("HudAmber"), Margin = new Thickness(4, 6, 4, 0), TextWrapping = TextWrapping.Wrap, MaxWidth = 640, HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center, Cursor = Cursors.Hand, ToolTip = "Cliquer pour sélectionner l'élément concerné" };
        issueText.MouseLeftButtonUp += (_, e) =>
        {
            if (issues.FirstOrDefault(i => i.Target != null)?.Target is { } t)
            {
                selected = t;
                SetTool(Tool.Select);
                Render();
            }
            e.Handled = true;
        };
        var stack = new StackPanel();
        stack.Children.Add(row);
        stack.Children.Add(issueText);
        toolbar = new Border
        {
            Style = (Style)win.FindResource("HudPanel"),
            CornerRadius = new CornerRadius(20),
            Padding = new Thickness(10, 8, 10, 8),
            Margin = new Thickness(12, 0, 12, 14),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            Child = stack,
        };
        props = new Border
        {
            Style = (Style)win.FindResource("HudPanel"),
            CornerRadius = new CornerRadius(18),
            Padding = new Thickness(16, 12, 16, 14),
            Margin = new Thickness(0, 52, 14, 0),
            Width = 270,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Visibility = Visibility.Collapsed,
        };
        win.Stage.Children.Add(toolbar);
        win.Stage.Children.Add(props);
        VPet_Simulator.Core.UiMotion.SlideIn(toolbar, 12, 220);

        win.Stage.Background = Brushes.Transparent; // reçoit les clics hors de l'image
        win.Stage.MouseLeftButtonDown += OnDown;
        win.Stage.MouseRightButtonDown += OnPanDown;
        win.Stage.MouseDown += OnMiddleDown;
        win.Stage.MouseMove += OnMove;
        win.Stage.MouseLeftButtonUp += OnUp;
        win.Stage.MouseRightButtonUp += OnUp;
        win.Stage.MouseUp += OnMiddleUp;
        win.Stage.MouseWheel += OnWheel;
        win.Stage.MouseLeave += (_, _) => ghost.Visibility = Visibility.Collapsed;

        SetTool(work.Floors.Count == 0 ? Tool.Floor : Tool.Select);
        Render();
        win.Focus();
    }

    public void Detach()
    {
        win.Stage.MouseLeftButtonDown -= OnDown;
        win.Stage.MouseRightButtonDown -= OnPanDown;
        win.Stage.MouseDown -= OnMiddleDown;
        win.Stage.MouseMove -= OnMove;
        win.Stage.MouseLeftButtonUp -= OnUp;
        win.Stage.MouseRightButtonUp -= OnUp;
        win.Stage.MouseUp -= OnMiddleUp;
        win.Stage.MouseWheel -= OnWheel;
        win.Stage.Children.Remove(toolbar);
        win.Stage.Children.Remove(props);
        win.Overlay.Children.Clear();
        win.Cursor = null;
    }

    private void Finish()
    {
        work.Floors.RemoveAll(f => f.Length < 1);
        work.Climbs.RemoveAll(c => Math.Abs(c.Y2 - c.Y1) < 1);
        win.StopEditing(work);
    }

    private bool windowsHidden;

    /// <summary>
    /// Masque / réaffiche toutes les fenêtres ouvertes (touche H) pour tracer sur le bureau nu, puis les récupérer.
    /// En mode bureau transparent, pratique pour voir le fond sans les fenêtres par-dessus.
    /// </summary>
    private void ToggleWindows()
    {
        try
        {
            var t = Type.GetTypeFromProgID("Shell.Application");
            if (t == null)
                return;
            dynamic shell = Activator.CreateInstance(t)!;
            if (windowsHidden)
                shell.UndoMinimizeALL();
            else
                shell.MinimizeAll();
            windowsHidden = !windowsHidden;
        }
        catch { }
    }

    /// <summary>QA : simule le survol d'un point de l'image (fantôme) et la sélection d'un élément</summary>
    internal void QaHover(Point image, string? select)
    {
        selected = select == null ? null : Find(select);
        lastMouse = image;
        Render();
    }

    /// <summary>
    /// En multi-écrans « Span », trace des repères verticaux là où tombent les frontières entre écrans sur le décor.
    /// Aide au tracé : tu vois où commence/finit chaque écran. Détecté automatiquement (aucune valeur en dur).
    /// </summary>
    private void DrawScreenGuides(Canvas o)
    {
        if (!mode.SpanScreens)
            return;
        System.Collections.Generic.List<Screens.ScreenDetail> ss;
        try { ss = Screens.All(); } catch { return; }
        if (ss.Count < 2)
            return;
        double minX = ss.Min(s => (double)s.X), maxX = ss.Max(s => (double)s.X + s.Width);
        double vw = maxX - minX;
        if (vw <= 0)
            return;
        double imgW = work.Image.Width, imgH = work.Image.Height;
        var accent = Res("HudAccent");
        foreach (var bx in ss.Select(s => (double)s.X + s.Width).Where(x => x < maxX - 1).Distinct())
        {
            double ix = (bx - minX) / vw * imgW;
            o.Children.Add(new Line
            {
                X1 = ix, X2 = ix, Y1 = 0, Y2 = imgH, Stroke = accent, StrokeThickness = Px(1.5),
                StrokeDashArray = new DoubleCollection { 6, 5 }, Opacity = 0.45, IsHitTestVisible = false,
            });
            var tag = new Border
            {
                Background = accent, CornerRadius = new CornerRadius(Px(7)), Opacity = 0.9, IsHitTestVisible = false,
                Padding = new Thickness(Px(7), Px(2), Px(7), Px(3)),
                Child = new TextBlock { Text = "bord d'écran", FontSize = Px(11), Foreground = Res("HudOnAccent") },
            };
            Canvas.SetLeft(tag, ix + Px(5));
            Canvas.SetTop(tag, Px(6));
            o.Children.Add(tag);
        }
    }

    #region Rendu
    private double Scale => Math.Max(0.0001, win.ViewProjection.ScaleY);
    private double Px(double screenPixels) => screenPixels / Scale;
    private Brush Res(string key) => (Brush)win.FindResource(key);
    private FontFamily Font(string key) => (FontFamily)win.FindResource(key);

    public void OnViewChanged() => Render();

    /// <param name="withProps">faux pendant la saisie d'un nom : le panneau (et le focus) est conservé</param>
    private void Render(bool withProps = true)
    {
        var o = win.Overlay;
        o.Children.Clear();
        var accent = Res("HudAccent");
        var amber = Res("HudAmber");
        var silver = Res("HudSilver");
        var success = Res("HudSuccess");
        var surface = Res("HudSurface");

        DrawScreenGuides(o);

        // pièces (sous tout le reste)
        foreach (var r in work.Rooms)
        {
            bool sel = Is(Kind.Room, r.Id);
            var rect = new Rectangle
            {
                Width = Math.Max(1, r.Width), Height = Math.Max(1, r.Height),
                Fill = new SolidColorBrush(Color.FromArgb(sel ? (byte)0x30 : (byte)0x18, 0xC9, 0xCF, 0xDA)),
                Stroke = sel ? accent : silver, StrokeThickness = Px(sel ? 2 : 1.5),
                StrokeDashArray = sel ? null : new DoubleCollection { 4, 3 },
                IsHitTestVisible = false,
            };
            Canvas.SetLeft(rect, r.X);
            Canvas.SetTop(rect, r.Y);
            o.Children.Add(rect);
            var label = new Border
            {
                Background = sel ? accent : surface,
                CornerRadius = new CornerRadius(Px(8)),
                Padding = new Thickness(Px(8), Px(3), Px(8), Px(4)),
                Child = new TextBlock { Text = string.IsNullOrWhiteSpace(r.Name) ? "Sans nom" : r.Name, FontSize = Px(12.5), FontWeight = FontWeights.SemiBold, Foreground = sel ? Res("HudOnAccent") : Res("HudText") },
                IsHitTestVisible = false,
            };
            Canvas.SetLeft(label, r.X + Px(6));
            Canvas.SetTop(label, r.Y + Px(6));
            o.Children.Add(label);
            if (sel)
                o.Children.Add(Handle(r.X + r.Width, r.Y + r.Height, accent, surface, square: true));
        }

        // emplacements : fantôme de l'animation (avec ses meubles) posé sur le sol
        foreach (var s in work.Spots)
        {
            var f = work.Floor(s.Floor);
            if (f == null)
                continue;
            bool sel = Is(Kind.Spot, s.Id);
            var img = PetSprites.Frame(mode.MW, s.Activity ?? "relax") ?? mode.Metrics.Standing;
            if (img != null)
            {
                double sz = mode.Metrics.WindowSizeFor(work.PetHeight);
                var gi = new Image { Source = img, Width = sz, Height = sz, Opacity = sel ? 0.8 : 0.45, IsHitTestVisible = false, Stretch = Stretch.Fill };
                Canvas.SetLeft(gi, s.X - sz * mode.Metrics.CenterRatio);
                Canvas.SetTop(gi, f.Y - sz * mode.Metrics.FootRatio);
                o.Children.Add(gi);
            }
            o.Children.Add(Handle(s.X, f.Y, sel ? accent : silver, surface, square: false, radius: sel ? 8 : 6, glyph: ""));
        }

        foreach (var f in work.Floors)
        {
            bool sel = Is(Kind.Floor, f.Id);
            if (sel)
                o.Children.Add(Seg(f.Left, f.Y, f.Right, f.Y, accent, 12, 0.25));
            o.Children.Add(Seg(f.Left, f.Y, f.Right, f.Y, accent, sel ? 4 : 3));
            o.Children.Add(Handle(f.Left, f.Y, accent, surface, radius: sel ? 7 : 5));
            o.Children.Add(Handle(f.Right, f.Y, accent, surface, radius: sel ? 7 : 5));
        }

        foreach (var d in work.Drops)
        {
            var from = work.Floor(d.From);
            var to = work.Floor(d.To);
            if (from == null || to == null)
                continue;
            bool sel = Is(Kind.Drop, d.Id);
            var line = Seg(d.X, from.Y, d.X, to.Y - Px(10), sel ? accent : silver, sel ? 3 : 2);
            line.StrokeDashArray = new DoubleCollection { 3, 2 };
            o.Children.Add(line);
            var head = new Polygon
            {
                Points = new PointCollection { new(d.X - Px(8), to.Y - Px(14)), new(d.X + Px(8), to.Y - Px(14)), new(d.X, to.Y) },
                Fill = sel ? accent : silver,
                IsHitTestVisible = false,
            };
            o.Children.Add(head);
            o.Children.Add(Handle(d.X, from.Y, sel ? accent : silver, surface, radius: sel ? 7 : 5));
        }

        var nav = new HabitatNavigator(work, caps);
        var connected = nav.ConnectedClimbs().Select(c => c.climb.Id).ToHashSet();
        foreach (var c in work.Climbs)
        {
            bool sel = Is(Kind.Climb, c.Id);
            double top = Math.Min(c.Y1, c.Y2), bottom = Math.Max(c.Y1, c.Y2);
            if (sel)
                o.Children.Add(Seg(c.X, top, c.X, bottom, amber, 12, 0.25));
            o.Children.Add(Seg(c.X, top, c.X, bottom, amber, sel ? 4 : 3));
            // extrémités : vertes si accrochées à un sol, ambre sinon
            o.Children.Add(Handle(c.X, top, nav.FloorAtEnd(c.X, top) != null ? success : amber, surface, radius: sel ? 7 : 5));
            o.Children.Add(Handle(c.X, bottom, nav.FloorAtEnd(c.X, bottom) != null ? success : amber, surface, radius: sel ? 7 : 5));
            if (!connected.Contains(c.Id))
                o.Children.Add(Seg(c.X, top, c.X, bottom, amber, 1, 1));
        }

        previewLayer.Children.Clear();
        o.Children.Add(previewLayer);
        if (drag == Drag.Draw)
            RenderPreview();
        o.Children.Add(ghost);
        PlaceGhost(lastMouse);

        issues = Validate(nav, connected);
        issueText.Text = issues.Count == 0 ? "" : issues[0].Text + (issues.Count > 1 ? $"  (+{issues.Count - 1})" : "");
        issueText.Visibility = issues.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        syncingSlider = true;
        size.Value = work.PetHeight;
        syncingSlider = false;
        if (withProps)
            RenderProps();
    }

    private Line Seg(double x1, double y1, double x2, double y2, Brush b, double px, double opacity = 1) => new()
    {
        X1 = x1, Y1 = y1, X2 = x2, Y2 = y2, Stroke = b, Opacity = opacity, StrokeThickness = Px(px),
        StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, IsHitTestVisible = false,
    };

    private FrameworkElement Handle(double x, double y, Brush stroke, Brush fill, bool square = false, double radius = 6, string? glyph = null)
    {
        double r = Px(radius);
        FrameworkElement shape = square
            ? new Rectangle { Width = r * 2, Height = r * 2, Fill = fill, Stroke = stroke, StrokeThickness = Px(2), RadiusX = Px(2), RadiusY = Px(2) }
            : new Ellipse { Width = r * 2, Height = r * 2, Fill = glyph != null ? stroke : fill, Stroke = stroke, StrokeThickness = Px(2) };
        shape.IsHitTestVisible = false;
        Canvas.SetLeft(shape, x - r);
        Canvas.SetTop(shape, y - r);
        return shape;
    }

    private void RenderPreview()
    {
        var accent = Res("HudAccent");
        switch (tool)
        {
            case Tool.Floor:
                previewLayer.Children.Add(Dashed(Seg(dragStart.X, dragStart.Y, drawNow.X, dragStart.Y, accent, 3)));
                break;
            case Tool.Climb:
                previewLayer.Children.Add(Dashed(Seg(dragStart.X, dragStart.Y, dragStart.X, drawNow.Y, Res("HudAmber"), 3)));
                break;
            case Tool.Room:
                var r = Normalize(dragStart, drawNow);
                var rect = new Rectangle { Width = r.Width, Height = r.Height, Stroke = accent, StrokeThickness = Px(2), StrokeDashArray = new DoubleCollection { 4, 3 }, Fill = new SolidColorBrush(Color.FromArgb(0x20, 0xE2, 0x45, 0x5B)) };
                Canvas.SetLeft(rect, r.X);
                Canvas.SetTop(rect, r.Y);
                previewLayer.Children.Add(rect);
                break;
        }
    }

    private static Line Dashed(Line l)
    {
        l.StrokeDashArray = new DoubleCollection { 2, 1.5 };
        return l;
    }

    /// <summary>
    /// Fantôme du compagnon à l'échelle, debout sur le sol sous le curseur (ou sur le sol en cours de tracé)
    /// </summary>
    private void PlaceGhost(Point? at)
    {
        bool show = tool is Tool.Select or Tool.Floor or Tool.Spot or Tool.Drop;
        if (!show || at is not Point p || mode.Metrics.Standing == null)
        {
            ghost.Visibility = Visibility.Collapsed;
            return;
        }
        double feetY = p.Y;
        if (drag == Drag.Draw && tool == Tool.Floor)
            feetY = dragStart.Y;
        else if (work.FloorBelow(p.X, p.Y - Px(HitPx)) is { } below && below.Y - p.Y < work.PetHeight * 1.5)
            feetY = below.Y;
        double s = mode.Metrics.WindowSizeFor(work.PetHeight);
        ghost.Width = ghost.Height = s;
        Canvas.SetLeft(ghost, p.X - s * mode.Metrics.CenterRatio);
        Canvas.SetTop(ghost, feetY - s * mode.Metrics.FootRatio);
        ghost.Visibility = Visibility.Visible;
    }
    #endregion

    #region Validation
    private List<Issue> Validate(HabitatNavigator nav, HashSet<string> connected)
    {
        var list = work.Validate().Select(t => new Issue(t, null)).ToList();
        if (work.Climbs.Count > 0 && !caps.ClimbUp)
            list.Add(new Issue("Ce personnage n'a pas d'animation d'escalade : il ne pourra pas utiliser les échelles.", null));
        foreach (var c in work.Climbs.Where(c => !connected.Contains(c.Id)))
            list.Add(new Issue($"L'échelle {c.Id} ne relie pas deux sols : amène ses extrémités sur un sol (elles deviennent vertes).", new Sel(Kind.Climb, c.Id)));
        var main = work.MainFloor;
        foreach (var r in work.Rooms)
        {
            var target = work.TargetIn(r, null);
            if (target == null)
                list.Add(new Issue($"La pièce « {r.Name} » ne contient aucun sol.", new Sel(Kind.Room, r.Id)));
            else if (main != null && nav.FindPath(main.Id, (main.Left + main.Right) / 2, target.Value.floor.Id, target.Value.x) == null)
                list.Add(new Issue($"La pièce « {r.Name} » est inaccessible depuis le sol principal : ajoute une échelle ou une chute.", new Sel(Kind.Room, r.Id)));
        }
        if (work.Rooms.GroupBy(r => HabitatMap.Fold(r.Name)).FirstOrDefault(g => g.Count() > 1) is { } dup)
            list.Add(new Issue($"Deux pièces s'appellent « {dup.First().Name} » : les routines prendront la première.", new Sel(Kind.Room, dup.Last().Id)));
        foreach (var s in work.Spots.Where(s => s.Room == null || work.Rooms.All(r => r.Id != s.Room)))
            list.Add(new Issue($"L'emplacement {s.Id} n'est dans aucune pièce : les routines ne l'utiliseront pas.", new Sel(Kind.Spot, s.Id)));
        return list;
    }
    #endregion

    #region Souris
    private Point ImagePoint(MouseEventArgs e) => win.ViewProjection.ToImage(e.GetPosition(win.Stage));

    private void OnDown(object sender, MouseButtonEventArgs e)
    {
        if (toolbar.IsMouseOver || props.IsMouseOver || win.IsOverChrome || e.OriginalSource is DependencyObject d && IsInChrome(d))
            return;
        var p = ImagePoint(e);
        win.Stage.CaptureMouse();
        win.Focus();
        dragChanged = false;
        switch (tool)
        {
            case Tool.Floor:
                drag = Drag.Draw;
                dragStart = new Point(SnapX(p.X, null), SnapY(p.Y, null));
                drawNow = dragStart;
                return;
            case Tool.Climb:
                drag = Drag.Draw;
                dragStart = new Point(SnapX(p.X, null), SnapClimbY(SnapX(p.X, null), p.Y));
                drawNow = dragStart;
                return;
            case Tool.Room:
                drag = Drag.Draw;
                dragStart = drawNow = p;
                return;
            case Tool.Drop:
                AddDrop(p);
                win.Stage.ReleaseMouseCapture();
                return;
            case Tool.Spot:
                AddSpot(p);
                win.Stage.ReleaseMouseCapture();
                return;
        }
        // sélection : poignées, puis éléments, sinon défilement de la vue
        var (sel, part) = HitTest(p);
        selected = sel;
        if (sel != null)
        {
            dragStart = p;
            dragPart = part;
            origin = HabitatMap.FromJson(Snapshot(sel));
            drag = Drag.Move;
            Checkpoint();
        }
        else
        {
            drag = Drag.Pan;
            panStart = e.GetPosition(win.Stage);
            panView = win.ViewProjection;
        }
        Render();
    }

    private static bool IsInChrome(DependencyObject d)
    {
        for (var x = d; x != null; x = x is Visual ? VisualTreeHelper.GetParent(x) : null)
            if (x is ButtonBase or TextBoxBase)
                return true;
        return false;
    }

    private void OnPanDown(object sender, MouseButtonEventArgs e)
    {
        drag = Drag.Pan;
        panStart = e.GetPosition(win.Stage);
        panView = win.ViewProjection;
        win.Stage.CaptureMouse();
        e.Handled = true;
    }

    private void OnMiddleDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Middle)
            OnPanDown(sender, e);
    }

    private void OnMiddleUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Middle)
            OnUp(sender, e);
    }

    private void OnMove(object sender, MouseEventArgs e)
    {
        var p = ImagePoint(e);
        lastMouse = p;
        switch (drag)
        {
            case Drag.Pan:
                var now = e.GetPosition(win.Stage);
                HasCustomView = true;
                win.SetView(panView with { OffsetX = panView.OffsetX + now.X - panStart.X, OffsetY = panView.OffsetY + now.Y - panStart.Y });
                return;
            case Drag.Draw:
                drawNow = tool switch
                {
                    Tool.Floor => new Point(SnapX(p.X, null), dragStart.Y),
                    Tool.Climb => new Point(dragStart.X, SnapClimbY(dragStart.X, p.Y)),
                    _ => p,
                };
                previewLayer.Children.Clear();
                RenderPreview();
                PlaceGhost(tool == Tool.Floor ? drawNow : p);
                return;
            case Drag.Move when selected != null:
                MoveSelected(p);
                dragChanged = true;
                Render();
                return;
        }
        // survol : curseur selon ce qui est sous la souris
        if (tool == Tool.Select)
        {
            var (sel, part) = HitTest(p);
            win.Cursor = sel == null ? Cursors.Arrow
                : part == Part.Corner ? Cursors.SizeNWSE
                : part is Part.End1 or Part.End2 ? (sel.Kind == Kind.Climb ? Cursors.SizeNS : Cursors.SizeWE)
                : Cursors.SizeAll;
        }
        PlaceGhost(p);
    }

    private void OnUp(object sender, MouseButtonEventArgs e)
    {
        win.Stage.ReleaseMouseCapture();
        if (drag == Drag.Draw)
            CommitDraw();
        else if (drag == Drag.Move && !dragChanged && undo.Count > 0)
            undo.Pop(); // simple clic : pas d'étape d'annulation
        drag = Drag.None;
        Render();
    }

    private void OnWheel(object sender, MouseWheelEventArgs e)
    {
        if (props.IsMouseOver)
            return;
        // molette : zoom autour du curseur
        var at = e.GetPosition(win.Stage);
        var v = win.ViewProjection;
        double fit = win.FitView().ScaleY;
        double s = Math.Clamp(v.ScaleY * Math.Pow(1.15, e.Delta / 120.0), fit * 0.5, fit * 16);
        double k = s / v.ScaleY;
        HasCustomView = true;
        win.SetView(new HabitatProjection(s, s, at.X - (at.X - v.OffsetX) * k, at.Y - (at.Y - v.OffsetY) * k));
        e.Handled = true;
    }

    private void FitView()
    {
        HasCustomView = false;
        win.SetView(win.FitView());
    }
    #endregion

    #region Assistants
    /// <summary>Propose des sols en analysant l'image (annulable d'un Ctrl+Z)</summary>
    internal async void DetectFloors()
    {
        if (mode.Image == null)
            return;
        Flash("Analyse de l'image…");
        var image = mode.Image;
        var copy = work.Clone();
        var found = await System.Threading.Tasks.Task.Run(() => FloorDetector.Suggest(image, copy));
        if (found.Count == 0)
        {
            Flash("Aucun sol évident dans cette image : trace-les avec l'outil Sol.");
            return;
        }
        Checkpoint();
        work.Floors.AddRange(found);
        selected = new Sel(Kind.Floor, found[0].Id);
        SetTool(Tool.Select);
        Render();
        Flash($"{found.Count} sol(s) proposé(s) : vérifie-les, ajuste ou supprime ceux qui ne vont pas (Ctrl+Z pour tout annuler).");
    }
    private bool askingAi;

    /// <summary>Demande l'accord avant d'envoyer l'image à l'IA</summary>
    private void AskRoomSuggestions()
    {
        if (!HabitatVision.Available)
        {
            Flash("La suggestion des pièces utilise Gemini (vision) : ajoute une clé gratuite dans Paramètres › Intelligence artificielle.");
            return;
        }
        selected = null;
        askingAi = true;
        Render();
    }

    /// <summary>Envoie l'image réduite à Gemini et ajoute les pièces proposées (annulable d'un Ctrl+Z)</summary>
    internal async void SuggestRooms()
    {
        askingAi = false;
        if (mode.Image == null)
            return;
        Flash("L'IA observe le décor…");
        Render(withProps: true);
        List<HabitatRoom> rooms;
        try
        {
            var model = mode.MW.AgentPlugin?.Orchestrator.ModelOf("gemini");
            rooms = await HabitatVision.SuggestRoomsAsync(mode.Image, work.Clone(), model, System.Threading.CancellationToken.None);
        }
        catch (Exception e)
        {
            Flash("Suggestion impossible : " + e.Message);
            return;
        }
        // on n'ajoute pas ce qui recouvre déjà une pièce existante
        var fresh = rooms.Where(r => !work.Rooms.Any(o => Overlap(o, r) > 0.5)).ToList();
        if (fresh.Count == 0)
        {
            Flash(rooms.Count == 0 ? "L'IA n'a pas repéré de pièces dans ce décor." : "Les pièces proposées existent déjà.");
            return;
        }
        Checkpoint();
        foreach (var r in fresh)
        {
            r.Id = work.NewId("r");
            work.Rooms.Add(r);
            foreach (var s in work.Spots.Where(s => s.Room == null && work.Floor(s.Floor) is { } sf && r.Contains(s.X, sf.Y - 2)))
                s.Room = r.Id;
        }
        selected = new Sel(Kind.Room, fresh[0].Id);
        SetTool(Tool.Select);
        Render();
        Flash($"{fresh.Count} pièce(s) proposée(s) par l'IA : vérifie les cadres et les noms (Ctrl+Z pour tout annuler).");
    }

    /// <summary>Part de la plus petite pièce couverte par l'autre</summary>
    private static double Overlap(HabitatRoom a, HabitatRoom b)
    {
        double w = Math.Max(0, Math.Min(a.X + a.Width, b.X + b.Width) - Math.Max(a.X, b.X));
        double h = Math.Max(0, Math.Min(a.Y + a.Height, b.Y + b.Height) - Math.Max(a.Y, b.Y));
        return w * h / Math.Max(1, Math.Min(a.Width * a.Height, b.Width * b.Height));
    }
    #endregion

    #region Création
    private void CommitDraw()
    {
        double min = Math.Max(Px(16), 8);
        switch (tool)
        {
            case Tool.Floor when Math.Abs(drawNow.X - dragStart.X) >= min:
                Checkpoint();
                var f = new HabitatFloor { Id = work.NewId("f"), Y = Math.Round(dragStart.Y), X1 = Math.Round(Math.Min(dragStart.X, drawNow.X)), X2 = Math.Round(Math.Max(dragStart.X, drawNow.X)) };
                work.Floors.Add(f);
                selected = new Sel(Kind.Floor, f.Id);
                break;
            case Tool.Climb when Math.Abs(drawNow.Y - dragStart.Y) >= min:
                Checkpoint();
                var c = new HabitatClimb { Id = work.NewId("c"), X = Math.Round(dragStart.X), Y1 = Math.Round(Math.Min(dragStart.Y, drawNow.Y)), Y2 = Math.Round(Math.Max(dragStart.Y, drawNow.Y)) };
                work.Climbs.Add(c);
                selected = new Sel(Kind.Climb, c.Id);
                break;
            case Tool.Room:
                var r = Normalize(dragStart, drawNow);
                if (r.Width < Px(30) || r.Height < Px(30))
                    break;
                Checkpoint();
                var room = new HabitatRoom { Id = work.NewId("r"), Name = "Pièce " + (work.Rooms.Count + 1), X = Math.Round(r.X), Y = Math.Round(r.Y), Width = Math.Round(r.Width), Height = Math.Round(r.Height) };
                work.Rooms.Add(room);
                // les emplacements déjà posés dedans lui sont rattachés
                foreach (var s in work.Spots.Where(s => work.Floor(s.Floor) is { } sf && room.Contains(s.X, sf.Y - 2)))
                    s.Room = room.Id;
                selected = new Sel(Kind.Room, room.Id);
                focusName = true;
                break;
        }
    }

    private void AddDrop(Point p)
    {
        var from = work.FloorAt(p.X, p.Y, Px(14)) ?? work.FloorBelow(p.X, p.Y - Px(4));
        if (from == null || from.Y - p.Y > work.PetHeight * 1.5)
        {
            Flash("Clique sur un sol (ou juste au-dessus) pour placer une chute.");
            return;
        }
        double x = from.Clamp(p.X);
        var to = work.FloorBelow(x, from.Y + 1);
        if (to == null)
        {
            Flash("Il n'y a pas de sol sous ce point : le compagnon n'aurait nulle part où atterrir.");
            return;
        }
        Checkpoint();
        var d = new HabitatDrop { Id = work.NewId("d"), From = from.Id, To = to.Id, X = Math.Round(x) };
        work.Drops.Add(d);
        selected = new Sel(Kind.Drop, d.Id);
        Render();
    }

    private void AddSpot(Point p)
    {
        var floor = work.FloorAt(p.X, p.Y, Px(14)) ?? work.FloorBelow(p.X, p.Y - Px(4));
        if (floor == null || floor.Y - p.Y > work.PetHeight * 1.5)
        {
            Flash("Clique sur un sol (ou juste au-dessus) pour placer un emplacement.");
            return;
        }
        Checkpoint();
        double x = floor.Clamp(p.X);
        var room = work.RoomAt(x, floor.Y - work.PetHeight * 0.3);
        var s = new HabitatSpot
        {
            Id = work.NewId("s"),
            Floor = floor.Id,
            X = Math.Round(x),
            Room = room?.Id,
            Activity = room?.Tag switch { "bedroom" => "sleep", "kitchen" => "eat", _ => "relax" },
        };
        work.Spots.Add(s);
        selected = new Sel(Kind.Spot, s.Id);
        Render();
    }

    private static Rect Normalize(Point a, Point b) => new(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));

    private void Flash(string text)
    {
        issueText.Text = text;
        issueText.Visibility = Visibility.Visible;
    }
    #endregion

    #region Déplacement des éléments
    private void MoveSelected(Point p)
    {
        double dx = p.X - dragStart.X, dy = p.Y - dragStart.Y;
        switch (selected!.Kind)
        {
            case Kind.Floor when work.Floor(selected.Id) is { } f && origin.Floors.FirstOrDefault() is { } o:
                if (dragPart == Part.End1) f.X1 = SnapX(p.X, f.Id);
                else if (dragPart == Part.End2) f.X2 = SnapX(p.X, f.Id);
                else
                {
                    f.Y = SnapY(o.Y + dy, f.Id);
                    f.X1 = o.X1 + dx;
                    f.X2 = o.X2 + dx;
                }
                break;
            case Kind.Climb when work.Climbs.FirstOrDefault(c => c.Id == selected.Id) is { } c && origin.Climbs.FirstOrDefault() is { } o:
                double top = Math.Min(o.Y1, o.Y2), bottom = Math.Max(o.Y1, o.Y2);
                if (dragPart == Part.End1) { c.Y1 = SnapClimbY(c.X, p.Y); c.Y2 = bottom; }
                else if (dragPart == Part.End2) { c.Y1 = top; c.Y2 = SnapClimbY(c.X, p.Y); }
                else c.X = SnapX(o.X + dx, null);
                break;
            case Kind.Drop when work.Drops.FirstOrDefault(d => d.Id == selected.Id) is { } d && work.Floor(d.From) is { } from:
                d.X = Math.Round(from.Clamp(p.X));
                if (work.FloorBelow(d.X, from.Y + 1) is { } to)
                    d.To = to.Id;
                break;
            case Kind.Room when work.Rooms.FirstOrDefault(r => r.Id == selected.Id) is { } r && origin.Rooms.FirstOrDefault() is { } o:
                if (dragPart == Part.Corner)
                {
                    r.Width = Math.Max(Px(30), Math.Round(o.Width + dx));
                    r.Height = Math.Max(Px(30), Math.Round(o.Height + dy));
                }
                else
                {
                    r.X = Math.Round(o.X + dx);
                    r.Y = Math.Round(o.Y + dy);
                }
                break;
            case Kind.Spot when work.Spots.FirstOrDefault(s => s.Id == selected.Id) is { } s:
                var floor = work.FloorAt(p.X, p.Y, Px(20)) ?? work.FloorBelow(p.X, p.Y - Px(4)) ?? work.Floor(s.Floor);
                if (floor == null)
                    break;
                s.Floor = floor.Id;
                s.X = Math.Round(floor.Clamp(p.X));
                s.Room = work.RoomAt(s.X, floor.Y - work.PetHeight * 0.3)?.Id ?? s.Room;
                break;
        }
    }

    /// <summary>Copie JSON d'un élément (dans une carte minimale) pour les déplacements relatifs</summary>
    private string Snapshot(Sel s)
    {
        var m = new HabitatMap();
        switch (s.Kind)
        {
            case Kind.Floor: m.Floors.Add(work.Floor(s.Id)!); break;
            case Kind.Climb: m.Climbs.Add(work.Climbs.First(c => c.Id == s.Id)); break;
            case Kind.Room: m.Rooms.Add(work.Rooms.First(r => r.Id == s.Id)); break;
        }
        return m.ToJson();
    }
    #endregion

    #region Clavier
    public void OnKey(KeyEventArgs e)
    {
        // la saisie d'un nom de pièce garde ses touches
        if (e.OriginalSource is TextBox)
        {
            if (e.Key is Key.Enter or Key.Escape)
            {
                win.Focus();
                e.Handled = true;
            }
            return;
        }
        bool ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control), shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        switch (e.Key)
        {
            case Key.Z when ctrl && shift:
            case Key.Y when ctrl:
                Redo();
                break;
            case Key.Z when ctrl:
                Undo();
                break;
            case Key.Delete or Key.Back when selected != null:
                DeleteSelected();
                break;
            case Key.Up or Key.Down or Key.Left or Key.Right when selected != null:
                Checkpoint();
                double step = shift ? 10 : 1;
                double dx = e.Key == Key.Left ? -step : e.Key == Key.Right ? step : 0;
                double dy = e.Key == Key.Up ? -step : e.Key == Key.Down ? step : 0;
                Nudge(dx, dy);
                Render();
                break;
            case >= Key.D1 and <= Key.D6:
                SetTool((Tool)(e.Key - Key.D1));
                break;
            case >= Key.NumPad1 and <= Key.NumPad6:
                SetTool((Tool)(e.Key - Key.NumPad1));
                break;
            case Key.F when !ctrl:
                FitView();
                break;
            case Key.H when !ctrl:
                ToggleWindows();
                break;
            case Key.Enter:
                Finish();
                break;
            case Key.Escape:
                if (drag != Drag.None)
                {
                    drag = Drag.None;
                    win.Stage.ReleaseMouseCapture();
                }
                else
                    selected = null;
                Render();
                break;
            default:
                return;
        }
        e.Handled = true;
    }

    private void Nudge(double dx, double dy)
    {
        switch (selected!.Kind)
        {
            case Kind.Floor when work.Floor(selected.Id) is { } f:
                f.X1 += dx; f.X2 += dx; f.Y += dy;
                break;
            case Kind.Climb when work.Climbs.FirstOrDefault(c => c.Id == selected.Id) is { } c:
                c.X += dx; c.Y1 += dy; c.Y2 += dy;
                break;
            case Kind.Drop when work.Drops.FirstOrDefault(d => d.Id == selected.Id) is { } d:
                d.X += dx;
                break;
            case Kind.Room when work.Rooms.FirstOrDefault(r => r.Id == selected.Id) is { } r:
                r.X += dx; r.Y += dy;
                break;
            case Kind.Spot when work.Spots.FirstOrDefault(s => s.Id == selected.Id) is { } s:
                s.X += dx;
                break;
        }
    }

    private void DeleteSelected()
    {
        if (selected == null)
            return;
        Checkpoint();
        switch (selected.Kind)
        {
            case Kind.Floor:
                work.Floors.RemoveAll(f => f.Id == selected.Id);
                // ce qui dépendait de ce sol disparaît avec lui
                work.Drops.RemoveAll(d => d.From == selected.Id || d.To == selected.Id);
                work.Spots.RemoveAll(s => s.Floor == selected.Id);
                break;
            case Kind.Climb: work.Climbs.RemoveAll(c => c.Id == selected.Id); break;
            case Kind.Drop: work.Drops.RemoveAll(d => d.Id == selected.Id); break;
            case Kind.Room:
                work.Rooms.RemoveAll(r => r.Id == selected.Id);
                foreach (var s in work.Spots.Where(s => s.Room == selected.Id))
                    s.Room = null;
                break;
            case Kind.Spot: work.Spots.RemoveAll(s => s.Id == selected.Id); break;
        }
        selected = null;
        Render();
    }
    #endregion

    #region Aimantation et sélection
    private bool Is(Kind k, string id) => selected?.Kind == k && selected.Id == id;

    private Sel? Find(string id) =>
        work.Floors.Any(f => f.Id == id) ? new Sel(Kind.Floor, id)
        : work.Climbs.Any(c => c.Id == id) ? new Sel(Kind.Climb, id)
        : work.Drops.Any(d => d.Id == id) ? new Sel(Kind.Drop, id)
        : work.Rooms.Any(r => r.Id == id) ? new Sel(Kind.Room, id)
        : work.Spots.Any(s => s.Id == id) ? new Sel(Kind.Spot, id)
        : null;

    /// <summary>Hauteur aimantée : sur les autres sols et sur le bas de l'image</summary>
    private double SnapY(double y, string? except)
    {
        double t = Px(SnapPx);
        var best = work.Floors.Where(f => f.Id != except).Select(f => f.Y).Append(work.Image.Height).OrderBy(ty => Math.Abs(ty - y)).First();
        return Math.Abs(best - y) <= t ? best : Math.Round(y);
    }

    /// <summary>Position horizontale aimantée : bords de l'image, extrémités des sols, axes d'escalade</summary>
    private double SnapX(double x, string? except)
    {
        double t = Px(SnapPx);
        var targets = work.Floors.Where(f => f.Id != except).SelectMany(f => new[] { f.Left, f.Right })
            .Concat(work.Climbs.Where(c => c.Id != except).Select(c => c.X))
            .Append(0).Append(work.Image.Width);
        var best = targets.OrderBy(tx => Math.Abs(tx - x)).First();
        return Math.Abs(best - x) <= t ? best : Math.Round(Math.Clamp(x, 0, work.Image.Width));
    }

    /// <summary>
    /// Extrémité d'échelle aimantée sur le sol le plus proche autour de x : c'est ce qui crée la connexion du trajet
    /// </summary>
    private double SnapClimbY(double x, double y)
    {
        double t = Math.Max(Px(SnapPx * 1.6), work.PetHeight * 0.06);
        var reach = work.PetHeight * 0.6;
        var near = work.Floors.Where(f => x >= f.Left - reach && x <= f.Right + reach)
            .OrderBy(f => Math.Abs(f.Y - y)).FirstOrDefault();
        return near != null && Math.Abs(near.Y - y) <= t ? near.Y : Math.Round(y);
    }

    private (Sel? sel, Part part) HitTest(Point p)
    {
        double t = Px(HitPx);
        // poignées d'abord (élément sélectionné en priorité)
        foreach (var s in work.Spots)
            if (work.Floor(s.Floor) is { } sf && Math.Abs(p.X - s.X) <= t * 1.4 && Math.Abs(p.Y - sf.Y) <= t * 1.4)
                return (new Sel(Kind.Spot, s.Id), Part.Body);
        foreach (var c in work.Climbs.OrderBy(c => Is(Kind.Climb, c.Id) ? 0 : 1))
        {
            double top = Math.Min(c.Y1, c.Y2), bottom = Math.Max(c.Y1, c.Y2);
            if (Math.Abs(p.X - c.X) <= t && Math.Abs(p.Y - top) <= t) return (new Sel(Kind.Climb, c.Id), Part.End1);
            if (Math.Abs(p.X - c.X) <= t && Math.Abs(p.Y - bottom) <= t) return (new Sel(Kind.Climb, c.Id), Part.End2);
        }
        foreach (var f in work.Floors.OrderBy(f => Is(Kind.Floor, f.Id) ? 0 : 1))
        {
            if (Math.Abs(p.Y - f.Y) <= t && Math.Abs(p.X - f.X1) <= t) return (new Sel(Kind.Floor, f.Id), Part.End1);
            if (Math.Abs(p.Y - f.Y) <= t && Math.Abs(p.X - f.X2) <= t) return (new Sel(Kind.Floor, f.Id), Part.End2);
        }
        foreach (var r in work.Rooms.Where(r => Is(Kind.Room, r.Id)))
            if (Math.Abs(p.X - (r.X + r.Width)) <= t && Math.Abs(p.Y - (r.Y + r.Height)) <= t)
                return (new Sel(Kind.Room, r.Id), Part.Corner);
        foreach (var d in work.Drops)
            if (work.Floor(d.From) is { } from && work.Floor(d.To) is { } to && Math.Abs(p.X - d.X) <= t && p.Y >= from.Y - t && p.Y <= to.Y + t)
                return (new Sel(Kind.Drop, d.Id), Part.Body);
        foreach (var c in work.Climbs)
            if (Math.Abs(p.X - c.X) <= t && p.Y >= Math.Min(c.Y1, c.Y2) - t && p.Y <= Math.Max(c.Y1, c.Y2) + t)
                return (new Sel(Kind.Climb, c.Id), Part.Body);
        var floor = work.Floors.Where(f => Math.Abs(p.Y - f.Y) <= t && p.X >= f.Left - t && p.X <= f.Right + t).OrderBy(f => Math.Abs(p.Y - f.Y)).FirstOrDefault();
        if (floor != null)
            return (new Sel(Kind.Floor, floor.Id), Part.Body);
        // pièces : par l'étiquette ou le bord, ou n'importe où dedans (la plus petite)
        var room = work.Rooms.Where(r => r.Contains(p.X, p.Y)).OrderBy(r => r.Width * r.Height).FirstOrDefault();
        return room != null ? (new Sel(Kind.Room, room.Id), Part.Body) : (null, Part.None);
    }
    #endregion

    #region Panneau de propriétés
    private bool focusName;

    private void RenderProps()
    {
        if (askingAi)
        {
            var ask = new StackPanel();
            ask.Children.Add(new TextBlock { Text = "IA", Style = (Style)win.FindResource("HudEyebrow") });
            ask.Children.Add(new TextBlock { Text = "Suggérer les pièces", Style = (Style)win.FindResource("HudTitle"), FontSize = 17, Margin = new Thickness(0, 0, 0, 8) });
            ask.Children.Add(new TextBlock
            {
                Text = "Une copie réduite de l'image du décor sera envoyée à Google Gemini pour repérer les pièces. Rien d'autre n'est envoyé ; tu valides ensuite chaque cadre.",
                FontSize = 12.5, Foreground = Res("HudTextMuted"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12),
            });
            var buttons = new StackPanel { Orientation = Orientation.Horizontal };
            var go = new Button { Style = (Style)win.FindResource("HudPrimaryButton"), Content = "Envoyer", Margin = new Thickness(0, 0, 8, 0) };
            go.Click += (_, _) => SuggestRooms();
            var no = new Button { Style = (Style)win.FindResource("HudGhostButton"), Content = "Annuler" };
            no.Click += (_, _) => { askingAi = false; Render(); };
            buttons.Children.Add(go);
            buttons.Children.Add(no);
            ask.Children.Add(buttons);
            props.Child = ask;
            props.Visibility = Visibility.Visible;
            return;
        }
        if (selected == null || Find(selected.Id) == null)
        {
            props.Visibility = Visibility.Collapsed;
            return;
        }
        var sp = new StackPanel();
        void Title(string eyebrow, string title)
        {
            sp.Children.Add(new TextBlock { Text = eyebrow, Style = (Style)win.FindResource("HudEyebrow") });
            sp.Children.Add(new TextBlock { Text = title, Style = (Style)win.FindResource("HudTitle"), FontSize = 17, Margin = new Thickness(0, 0, 0, 8) });
        }
        void Note(string text, string brush = "HudTextMuted") =>
            sp.Children.Add(new TextBlock { Text = text, FontSize = 12.5, Foreground = Res(brush), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) });

        switch (selected.Kind)
        {
            case Kind.Floor when work.Floor(selected.Id) is { } f:
                Title("SOL", f.Id);
                Note($"Longueur : {f.Length:0} px · hauteur {f.Y:0} px");
                break;
            case Kind.Climb when work.Climbs.FirstOrDefault(c => c.Id == selected.Id) is { } c:
                Title("ESCALADE", c.Id);
                var nav = new HabitatNavigator(work, caps);
                double top = Math.Min(c.Y1, c.Y2), bottom = Math.Max(c.Y1, c.Y2);
                var ft = nav.FloorAtEnd(c.X, top);
                var fb = nav.FloorAtEnd(c.X, bottom);
                if (ft != null && fb != null && ft != fb)
                    Note($"Relie {fb.Id} (bas) à {ft.Id} (haut).", "HudSuccess");
                else
                    Note("Amène chaque extrémité sur un sol : elle devient verte quand elle est accrochée.", "HudAmber");
                sp.Children.Add(new TextBlock { Text = "PAROI SAISIE PAR LE COMPAGNON", Style = (Style)win.FindResource("HudEyebrow"), Margin = new Thickness(0, 4, 0, 6) });
                sp.Children.Add(Chips(new[] { ("left", "À gauche"), ("right", "À droite") }, c.Side, v => c.Side = v));
                break;
            case Kind.Drop when work.Drops.FirstOrDefault(d => d.Id == selected.Id) is { } d:
                Title("CHUTE", d.Id);
                Note($"Saute de {d.From} vers {d.To}. Le compagnon s'en sert pour redescendre plus vite (jamais pour monter).");
                break;
            case Kind.Room when work.Rooms.FirstOrDefault(r => r.Id == selected.Id) is { } r:
                Title("PIÈCE", string.IsNullOrWhiteSpace(r.Name) ? "Sans nom" : r.Name);
                var nameBox = new TextBox { Style = (Style)win.FindResource("HudInput"), Text = r.Name, Tag = "Nom de la pièce" };
                nameBox.GotKeyboardFocus += (_, _) => Checkpoint();
                nameBox.TextChanged += (_, _) =>
                {
                    r.Name = nameBox.Text;
                    Render(withProps: false);
                };
                sp.Children.Add(new Border
                {
                    CornerRadius = new CornerRadius(12), Background = Res("HudSurfaceRaised"), BorderBrush = Res("HudStroke"), BorderThickness = new Thickness(1),
                    Padding = new Thickness(10, 6, 10, 6), Margin = new Thickness(0, 0, 0, 10), Child = nameBox,
                });
                sp.Children.Add(new TextBlock { Text = "TYPE (POUR LES ROUTINES)", Style = (Style)win.FindResource("HudEyebrow"), Margin = new Thickness(0, 0, 0, 6) });
                sp.Children.Add(Chips(RoomTypes, r.Tag ?? "other", v =>
                {
                    // un nom par défaut prend le nom du type choisi
                    if (r.Name.StartsWith("Pièce ") || string.IsNullOrWhiteSpace(r.Name) || RoomTypes.Any(t => t.label == r.Name))
                        r.Name = RoomTypes.First(t => t.tag == v).label;
                    r.Tag = v;
                }));
                Note($"{work.Spots.Count(s => s.Room == r.Id)} emplacement(s) d'activité dans cette pièce.");
                if (focusName)
                {
                    focusName = false;
                    nameBox.Loaded += (_, _) => { nameBox.Focus(); nameBox.SelectAll(); };
                }
                break;
            case Kind.Spot when work.Spots.FirstOrDefault(s => s.Id == selected.Id) is { } s:
                var room = work.Rooms.FirstOrDefault(x => x.Id == s.Room);
                Title("EMPLACEMENT", room != null ? "Dans « " + room.Name + " »" : "Hors des pièces");
                Note("Le fantôme montre l'animation avec ses meubles : place-le sur un espace dégagé du décor.");
                var list = new ScrollViewer { MaxHeight = 220, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
                list.Resources[typeof(ScrollBar)] = new Style(typeof(ScrollBar), (Style)win.FindResource("HudScrollBar"));
                list.Content = Chips(Activities(), s.Activity ?? "relax", v => s.Activity = v);
                sp.Children.Add(list);
                break;
        }
        var del = new Button { Style = (Style)win.FindResource("HudGhostButton"), Content = "Supprimer", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 6, 0, 0), ToolTip = "Suppr" };
        del.Click += (_, _) => DeleteSelected();
        sp.Children.Add(del);
        props.Child = sp;
        props.Visibility = Visibility.Visible;
    }


    /// <summary>Activités proposées pour un emplacement : besoins de base puis occupations du personnage</summary>
    private (string value, string label)[] Activities()
    {
        var list = new List<(string, string)> { ("sleep", "Dormir"), ("eat", "Manger"), ("drink", "Boire"), ("relax", "Se détendre") };
        mode.MW.Main.WorkList(out var ws, out var ss, out var ps);
        foreach (var w in ws.Concat(ss).Concat(ps))
            list.Add(("work:" + w.Name, w.NameTrans));
        return list.ToArray();
    }

    private FrameworkElement Chips((string value, string label)[] options, string current, Action<string> set)
    {
        var wrap = new WrapPanel();
        string group = "props-" + Guid.NewGuid().ToString("N");
        foreach (var (value, label) in options)
        {
            var chip = new RadioButton { Style = (Style)win.FindResource("HudChip"), Content = label, GroupName = group, IsChecked = value == current, Margin = new Thickness(0, 0, 6, 6) };
            chip.Checked += (_, _) =>
            {
                if (value == current)
                    return;
                Checkpoint();
                set(value);
                current = value;
                Render();
            };
            wrap.Children.Add(chip);
        }
        return wrap;
    }
    #endregion

    #region Annuler / rétablir
    private void Checkpoint()
    {
        undo.Push(work.ToJson());
        redo.Clear();
        if (undo.Count > 200)
        {
            var keep = undo.Take(200).Reverse().ToList();
            undo.Clear();
            foreach (var s in keep) undo.Push(s);
        }
    }

    private void Undo()
    {
        if (undo.Count == 0)
            return;
        redo.Push(work.ToJson());
        Restore(undo.Pop());
    }

    private void Redo()
    {
        if (redo.Count == 0)
            return;
        undo.Push(work.ToJson());
        Restore(redo.Pop());
    }

    private void Restore(string json)
    {
        var m = HabitatMap.FromJson(json);
        work.Floors = m.Floors;
        work.Climbs = m.Climbs;
        work.Drops = m.Drops;
        work.Rooms = m.Rooms;
        work.Spots = m.Spots;
        work.PetHeight = m.PetHeight;
        if (selected != null && Find(selected.Id) == null)
            selected = null;
        Render();
    }
    #endregion

    #region Barre d'outils
    private void SetTool(Tool t)
    {
        tool = t;
        foreach (var (k, chip) in toolChips)
            chip.IsChecked = k == t;
        win.Cursor = t == Tool.Select ? Cursors.Arrow : Cursors.Cross;
        PlaceGhost(lastMouse);
    }

    private RadioButton ToolChip(string glyph, string label, string tip, Tool t)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        content.Children.Add(new TextBlock { Text = glyph, FontFamily = Font("HudIcons"), FontSize = 13, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
        content.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
        var r = new RadioButton { Style = (Style)win.FindResource("HudChip"), Content = content, ToolTip = tip, GroupName = "habitat-tool", Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
        r.Checked += (_, _) => { if (tool != t) SetTool(t); };
        return r;
    }

    private Button IconButton(string glyph, string tip, Action run)
    {
        var b = new Button { Style = (Style)win.FindResource("HudIconButton"), Content = glyph, ToolTip = tip };
        b.Click += (_, _) => run();
        return b;
    }

    private FrameworkElement Separator() => new Border { Width = 1, Margin = new Thickness(8, 6, 8, 6), Background = Res("HudStroke") };
    #endregion
}
