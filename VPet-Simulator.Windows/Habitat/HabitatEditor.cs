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
/// V-Max habitat : éditeur de carte intégré à la fenêtre habitat (étape 1 : les sols).
/// Tout se fait en pixels de l'image ; la vue (zoom, défilement) n'est qu'une transformation.
/// Aimantation des sols entre eux et sur les bords de l'image, fantôme du compagnon à l'échelle,
/// annuler / rétablir, validation en direct.
/// </summary>
internal sealed class HabitatEditor
{
    private enum Tool { Select, Floor }
    private enum Drag { None, Draw, MoveFloor, MoveEnd1, MoveEnd2, Pan }

    private const double SnapPx = 10;     // aimantation, en pixels d'écran
    private const double HitPx = 9;       // tolérance de sélection, en pixels d'écran

    private readonly HabitatWindow win;
    private readonly HabitatMode mode;
    private readonly HabitatMap work;
    private readonly Stack<string> undo = new(), redo = new();
    private Tool tool;
    private string? selected;

    private Drag drag;
    private Point dragStart;          // image
    private Point panStart;           // fenêtre
    private HabitatProjection panView;
    private HabitatFloor? dragOrigin; // copie du sol avant déplacement
    private bool dragChanged;

    private readonly Line preview = new() { IsHitTestVisible = false, StrokeDashArray = new DoubleCollection { 2, 1.5 }, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
    private readonly Image ghost = new() { IsHitTestVisible = false, Opacity = 0.55, Stretch = Stretch.Fill };
    private readonly Border toolbar;
    private readonly TextBlock issues;
    private readonly Slider size;
    private readonly RadioButton selectTool, floorTool;
    private bool syncingSlider;

    public bool HasCustomView { get; private set; }

    public HabitatEditor(HabitatWindow win, HabitatMode mode)
    {
        this.win = win;
        this.mode = mode;
        work = mode.Map.Clone();
        ghost.Source = mode.Metrics.Standing;
        preview.Stroke = (Brush)win.FindResource("HudAccent");

        selectTool = ToolChip("", "Sélection", "Sélectionner, déplacer, supprimer (1)", Tool.Select);
        floorTool = ToolChip("", "Sol", "Tracer un sol : glisser horizontalement (2)", Tool.Floor);
        size = new Slider
        {
            Minimum = Math.Round(work.Image.Height * 0.04),
            Maximum = Math.Round(work.Image.Height * 0.6),
            Value = work.PetHeight,
            Width = 130,
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
        issues = new TextBlock { FontSize = 12, Foreground = (Brush)win.FindResource("HudAmber"), Margin = new Thickness(4, 6, 4, 0), TextWrapping = TextWrapping.Wrap, MaxWidth = 560, HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center };

        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(selectTool);
        row.Children.Add(floorTool);
        row.Children.Add(Separator());
        row.Children.Add(new TextBlock { Text = "", FontFamily = (FontFamily)win.FindResource("HudIcons"), FontSize = 14, Foreground = (Brush)win.FindResource("HudTextMuted"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0), ToolTip = "Taille du compagnon" });
        row.Children.Add(size);
        row.Children.Add(Separator());
        row.Children.Add(IconButton("", "Annuler (Ctrl+Z)", Undo));
        row.Children.Add(IconButton("", "Rétablir (Ctrl+Y)", Redo));
        row.Children.Add(IconButton("", "Ajuster la vue (F)", FitView));
        row.Children.Add(Separator());
        var cancel = new Button { Style = (Style)win.FindResource("HudGhostButton"), Content = "Annuler", Margin = new Thickness(0, 0, 6, 0), ToolTip = "Quitter sans enregistrer" };
        cancel.Click += (_, _) => win.StopEditing(null);
        var done = new Button { Style = (Style)win.FindResource("HudPrimaryButton"), Content = "Terminer", ToolTip = "Enregistrer la carte (Entrée)" };
        done.Click += (_, _) => Finish();
        row.Children.Add(cancel);
        row.Children.Add(done);
        var stack = new StackPanel();
        stack.Children.Add(row);
        stack.Children.Add(issues);
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
        win.Stage.Children.Add(toolbar);
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
        win.Overlay.Children.Clear();
        win.Cursor = null;
    }

    private void Finish()
    {
        work.Floors.RemoveAll(f => f.Length < 1);
        win.StopEditing(work);
    }

    #region Rendu
    private double Scale => Math.Max(0.0001, win.ViewProjection.ScaleY);
    private double Px(double screenPixels) => screenPixels / Scale;

    public void OnViewChanged() => Render();

    private void Render()
    {
        var o = win.Overlay;
        o.Children.Clear();
        var accent = (Brush)win.FindResource("HudAccent");
        var surface = (Brush)win.FindResource("HudSurface");
        foreach (var f in work.Floors)
        {
            bool sel = f.Id == selected;
            if (sel)
                o.Children.Add(new Line { X1 = f.Left, X2 = f.Right, Y1 = f.Y, Y2 = f.Y, Stroke = accent, Opacity = 0.25, StrokeThickness = Px(12), StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, IsHitTestVisible = false });
            o.Children.Add(new Line { X1 = f.Left, X2 = f.Right, Y1 = f.Y, Y2 = f.Y, Stroke = accent, StrokeThickness = Px(sel ? 4 : 3), StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, IsHitTestVisible = false });
            foreach (var x in new[] { f.Left, f.Right })
            {
                double r = Px(sel ? 7 : 5);
                var dot = new Ellipse { Width = r * 2, Height = r * 2, Fill = surface, Stroke = accent, StrokeThickness = Px(2), IsHitTestVisible = false };
                Canvas.SetLeft(dot, x - r);
                Canvas.SetTop(dot, f.Y - r);
                o.Children.Add(dot);
            }
        }
        preview.StrokeThickness = Px(3);
        o.Children.Add(preview);
        o.Children.Add(ghost);
        PlaceGhost(lastMouse);

        var list = work.Validate();
        issues.Text = list.Count == 0 ? "" : list[0] + (list.Count > 1 ? $"  (+{list.Count - 1})" : "");
        issues.Visibility = list.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        syncingSlider = true;
        size.Value = work.PetHeight;
        syncingSlider = false;
    }

    private Point? lastMouse;

    /// <summary>QA : simule le survol d'un point de l'image (fantôme) et la sélection d'un sol</summary>
    internal void QaHover(Point image, string? select)
    {
        selected = select;
        lastMouse = image;
        Render();
    }

    /// <summary>
    /// Fantôme du compagnon à l'échelle, debout sur le sol sous le curseur (ou sur le sol en cours de tracé)
    /// </summary>
    private void PlaceGhost(Point? at)
    {
        if (at is not Point p || mode.Metrics.Standing == null)
        {
            ghost.Visibility = Visibility.Collapsed;
            return;
        }
        double feetY = p.Y;
        if (drag == Drag.Draw)
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

    #region Souris
    private Point ImagePoint(MouseEventArgs e) => win.ViewProjection.ToImage(e.GetPosition(win.Stage));

    private bool OverToolbar(MouseEventArgs e) => toolbar.IsMouseOver;

    private void OnDown(object sender, MouseButtonEventArgs e)
    {
        if (OverToolbar(e) || win.IsOverChrome || e.OriginalSource is DependencyObject d && IsInChrome(d))
            return;
        var p = ImagePoint(e);
        win.Stage.CaptureMouse();
        dragChanged = false;
        if (tool == Tool.Floor)
        {
            drag = Drag.Draw;
            dragStart = new Point(SnapX(p.X, null), SnapY(p.Y, null));
            preview.X1 = preview.X2 = dragStart.X;
            preview.Y1 = preview.Y2 = dragStart.Y;
            preview.Visibility = Visibility.Visible;
            return;
        }
        // sélection : extrémité, puis corps d'un sol, sinon défilement de la vue
        var (floor, part) = HitTest(p);
        selected = floor?.Id;
        if (floor != null)
        {
            dragOrigin = new HabitatFloor { Id = floor.Id, X1 = floor.X1, X2 = floor.X2, Y = floor.Y };
            dragStart = p;
            drag = part;
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
        for (var x = d; x != null; x = VisualTreeHelper.GetParent(x))
            if (x is ButtonBase)
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
                preview.X2 = SnapX(p.X, null);
                PlaceGhost(new Point(preview.X2, dragStart.Y));
                return;
            case Drag.MoveFloor when Find(selected) is { } f && dragOrigin != null:
                double dx = p.X - dragStart.X, dy = p.Y - dragStart.Y;
                f.Y = SnapY(dragOrigin.Y + dy, f.Id);
                f.X1 = dragOrigin.X1 + dx;
                f.X2 = dragOrigin.X2 + dx;
                dragChanged = true;
                Render();
                return;
            case Drag.MoveEnd1 when Find(selected) is { } f:
                f.X1 = SnapX(p.X, f.Id);
                dragChanged = true;
                Render();
                return;
            case Drag.MoveEnd2 when Find(selected) is { } f:
                f.X2 = SnapX(p.X, f.Id);
                dragChanged = true;
                Render();
                return;
        }
        // survol : curseur selon ce qui est sous la souris
        if (tool == Tool.Floor)
            win.Cursor = Cursors.Cross;
        else
        {
            var (floor, part) = HitTest(p);
            win.Cursor = floor == null ? Cursors.Arrow : part == Drag.MoveFloor ? Cursors.SizeAll : Cursors.SizeWE;
        }
        PlaceGhost(p);
    }

    private void OnUp(object sender, MouseButtonEventArgs e)
    {
        win.Stage.ReleaseMouseCapture();
        if (drag == Drag.Draw)
        {
            preview.Visibility = Visibility.Collapsed;
            double x2 = preview.X2;
            if (Math.Abs(x2 - dragStart.X) >= Math.Max(Px(16), 8))
            {
                Checkpoint();
                var f = new HabitatFloor { Id = work.NewId("f"), Y = Math.Round(dragStart.Y), X1 = Math.Round(Math.Min(dragStart.X, x2)), X2 = Math.Round(Math.Max(dragStart.X, x2)) };
                work.Floors.Add(f);
                selected = f.Id;
            }
        }
        else if (drag is Drag.MoveEnd1 or Drag.MoveEnd2 or Drag.MoveFloor && Find(selected) is { } f)
        {
            if (!dragChanged)
                undo.Pop(); // simple clic : pas d'étape d'annulation
            Normalize(f);
        }
        drag = Drag.None;
        dragOrigin = null;
        Render();
    }

    private void OnWheel(object sender, MouseWheelEventArgs e)
    {
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

    #region Clavier
    public void OnKey(KeyEventArgs e)
    {
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
            case Key.Delete or Key.Back when Find(selected) is { } f:
                Checkpoint();
                work.Floors.Remove(f);
                selected = null;
                Render();
                break;
            case Key.Up or Key.Down or Key.Left or Key.Right when Find(selected) is { } f:
                Checkpoint();
                double step = shift ? 10 : 1;
                if (e.Key == Key.Up) f.Y -= step;
                if (e.Key == Key.Down) f.Y += step;
                if (e.Key == Key.Left) { f.X1 -= step; f.X2 -= step; }
                if (e.Key == Key.Right) { f.X1 += step; f.X2 += step; }
                Render();
                break;
            case Key.D1 or Key.NumPad1:
                SetTool(Tool.Select);
                break;
            case Key.D2 or Key.NumPad2:
                SetTool(Tool.Floor);
                break;
            case Key.F when !ctrl:
                FitView();
                break;
            case Key.Enter:
                Finish();
                break;
            case Key.Escape:
                if (drag != Drag.None)
                {
                    preview.Visibility = Visibility.Collapsed;
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
    #endregion

    #region Aimantation et sélection
    private HabitatFloor? Find(string? id) => id == null ? null : work.Floors.FirstOrDefault(f => f.Id == id);

    /// <summary>Hauteur aimantée : sur les autres sols et sur le bas de l'image</summary>
    private double SnapY(double y, string? except)
    {
        double t = Px(SnapPx);
        var targets = work.Floors.Where(f => f.Id != except).Select(f => f.Y).Append(work.Image.Height);
        foreach (var ty in targets.OrderBy(ty => Math.Abs(ty - y)))
            return Math.Abs(ty - y) <= t ? ty : Math.Round(y);
        return Math.Round(y);
    }

    /// <summary>Position horizontale aimantée : bords de l'image et extrémités des autres sols</summary>
    private double SnapX(double x, string? except)
    {
        double t = Px(SnapPx);
        var targets = work.Floors.Where(f => f.Id != except).SelectMany(f => new[] { f.Left, f.Right }).Append(0).Append(work.Image.Width);
        var best = targets.OrderBy(tx => Math.Abs(tx - x)).First();
        return Math.Abs(best - x) <= t ? best : Math.Round(Math.Clamp(x, 0, work.Image.Width));
    }

    private (HabitatFloor? floor, Drag part) HitTest(Point p)
    {
        double t = Px(HitPx);
        // l'extrémité la plus proche d'abord, sur le sol sélectionné en priorité
        foreach (var f in work.Floors.OrderBy(f => f.Id == selected ? 0 : 1))
        {
            if (Math.Abs(p.Y - f.Y) <= t && Math.Abs(p.X - f.X1) <= t)
                return (f, Drag.MoveEnd1);
            if (Math.Abs(p.Y - f.Y) <= t && Math.Abs(p.X - f.X2) <= t)
                return (f, Drag.MoveEnd2);
        }
        var body = work.Floors.Where(f => Math.Abs(p.Y - f.Y) <= t && p.X >= f.Left - t && p.X <= f.Right + t)
            .OrderBy(f => Math.Abs(p.Y - f.Y)).FirstOrDefault();
        return (body, body == null ? Drag.None : Drag.MoveFloor);
    }

    private void Normalize(HabitatFloor f)
    {
        f.X1 = Math.Round(Math.Clamp(Math.Min(f.X1, f.X2), 0, work.Image.Width));
        f.X2 = Math.Round(Math.Clamp(Math.Max(f.X1, f.X2), 0, work.Image.Width));
        f.Y = Math.Round(Math.Clamp(f.Y, 0, work.Image.Height));
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
        work.PetHeight = m.PetHeight;
        if (Find(selected) == null)
            selected = null;
        Render();
    }
    #endregion

    #region Barre d'outils
    private void SetTool(Tool t)
    {
        tool = t;
        selectTool.IsChecked = t == Tool.Select;
        floorTool.IsChecked = t == Tool.Floor;
        win.Cursor = t == Tool.Floor ? Cursors.Cross : Cursors.Arrow;
    }

    private RadioButton ToolChip(string glyph, string label, string tip, Tool t)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        content.Children.Add(new TextBlock { Text = glyph, FontFamily = (FontFamily)win.FindResource("HudIcons"), FontSize = 13, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
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

    private FrameworkElement Separator() => new Border { Width = 1, Margin = new Thickness(8, 6, 8, 6), Background = (Brush)win.FindResource("HudStroke") };
    #endregion
}
