using System;
using System.Windows;
using System.Windows.Media.Animation;
using VPet_Simulator.Core;
using static VPet_Simulator.Core.GraphInfo;

namespace VPet_Simulator.Windows.Habitat;

/// <summary>
/// V-Max habitat : contrôleur de déplacement du compagnon dans un habitat.
/// Le moteur de VPet ne connaît que 4 distances (gauche, droite, haut, bas) ; ici elles sont mesurées par rapport
/// au <b>sol courant</b> plutôt qu'aux bords de l'écran. L'errance native (walk, crawl…) fonctionne donc sur les sols
/// tracés sans modifier aucune animation. Au dépôt, le compagnon tombe sur le premier sol sous lui.
/// </summary>
public sealed class HabitatController : IController
{
    private readonly MainWindow mw;
    private readonly IController inner;
    private readonly HabitatMode mode;
    private bool landing;

    public HabitatController(MainWindow mw, IController inner, HabitatMode mode)
    {
        this.mw = mw;
        this.inner = inner;
        this.mode = mode;
    }

    /// <summary>Contrôleur classique remplacé (restauré à la sortie du mode habitat)</summary>
    public IController Inner => inner;

    private T OnUi<T>(Func<T> f) => mw.Dispatcher.CheckAccess() ? f() : mw.Dispatcher.Invoke(f);
    private void OnUi(Action a)
    {
        if (mw.Dispatcher.CheckAccess()) a();
        else mw.Dispatcher.Invoke(a);
    }

    #region Géométrie du compagnon (unités WPF, écran)
    private double Size => mw.ActualWidth > 1 ? mw.ActualWidth : 500 * ZoomRatio;
    private double CenterX => mw.Left + Size * mode.Metrics.CenterRatio;
    private double FeetY => mw.Top + Size * mode.Metrics.FootRatio;
    private double HalfBody => Size * mode.Metrics.HalfWidthRatio;
    /// <summary>Marge que le moteur natif garde avant de s'arrêter (CheckLeft/Right = 100 unités)</summary>
    private double EngineMargin => 100 * ZoomRatio;

    private HabitatFloor? CurrentFloor
    {
        get
        {
            var p = mode.Projection;
            var tolerance = Math.Max(3, 6 / Math.Max(0.01, p.ScaleY));
            return mode.Map.FloorAt(p.ToImageX(CenterX), p.ToImageY(FeetY), tolerance);
        }
    }

    private bool Dragging => mw.Main.DisplayType.Type is GraphType.Raised_Dynamic or GraphType.Raised_Static;
    #endregion

    #region Distances virtuelles
    public double GetWindowsDistanceLeft() => OnUi(() =>
    {
        var f = CurrentFloor;
        if (f == null)
            return EngineMargin * 3;
        return CenterX - HalfBody - mode.Projection.ToScreenX(f.Left) + EngineMargin;
    });

    public double GetWindowsDistanceRight() => OnUi(() =>
    {
        var f = CurrentFloor;
        if (f == null)
            return EngineMargin * 3;
        return mode.Projection.ToScreenX(f.Right) - CenterX - HalfBody + EngineMargin;
    });

    /// <summary>
    /// Étape 1 : pas encore d'échelles. 150 unités se situent entre les seuils des animations d'escalade
    /// (climb.* exige plus de 200, climb.top moins de 100) : aucune escalade ne se déclenche.
    /// </summary>
    public double GetWindowsDistanceUp() => 150 * ZoomRatio;

    public double GetWindowsDistanceDown() => OnUi(() =>
    {
        var p = mode.Projection;
        var below = mode.Map.FloorBelow(p.ToImageX(CenterX), p.ToImageY(FeetY) - 2 / Math.Max(0.01, p.ScaleY));
        return below == null ? 0 : Math.Max(0, p.ToScreenY(below.Y) - FeetY);
    });
    #endregion

    public void MoveWindows(double X, double Y)
    {
        OnUi(() =>
        {
            var floor = Dragging || landing ? null : CurrentFloor;
            inner.MoveWindows(X, Y);
            if (floor == null)
                return;
            // en marchant, les pieds restent sur le sol et le corps ne dépasse pas ses extrémités
            var p = mode.Projection;
            mw.Top = p.ToScreenY(floor.Y) - Size * mode.Metrics.FootRatio;
            double minX = p.ToScreenX(floor.Left) + HalfBody * 0.5, maxX = p.ToScreenX(floor.Right) - HalfBody * 0.5;
            if (maxX > minX)
            {
                double cx = Math.Clamp(CenterX, minX, maxX);
                mw.Left = cx - Size * mode.Metrics.CenterRatio;
            }
            mode.Remember(p.ToImageX(CenterX), floor.Id);
        });
    }

    /// <summary>
    /// Appelé à la fin d'un glisser-déposer et d'un déplacement : gravité vers le premier sol sous le compagnon
    /// </summary>
    public bool CheckPosition()
    {
        OnUi(() => Land(animated: true));
        return false;
    }

    public void ResetPosition() => OnUi(() => Land(animated: false));

    /// <summary>
    /// Pose le compagnon sur un sol : celui sous ses pieds, sinon le premier dessous, sinon le plus proche
    /// </summary>
    public void Land(bool animated)
    {
        var p = mode.Projection;
        if (mode.Map.Floors.Count == 0 || CurrentFloor != null)
        {
            if (CurrentFloor is { } here)
                mode.Remember(p.ToImageX(CenterX), here.Id);
            return;
        }
        double ix = p.ToImageX(CenterX), iy = p.ToImageY(FeetY);
        var target = mode.Map.LandingFloor(ix, iy);
        if (target == null)
            return;
        double tx = target.Clamp(ix);
        double left = p.ToScreenX(tx) - Size * mode.Metrics.CenterRatio;
        double top = p.ToScreenY(target.Y) - Size * mode.Metrics.FootRatio;
        mode.Remember(tx, target.Id);
        if (!animated || !UiMotion.Enabled || Math.Abs(top - mw.Top) < 2)
        {
            mw.Left = left;
            mw.Top = top;
            return;
        }
        // chute : accélération vers le bas (durée selon la hauteur), petit glissement horizontal si besoin
        double fall = Math.Abs(top - mw.Top);
        var duration = TimeSpan.FromMilliseconds(Math.Clamp(Math.Sqrt(fall) * 22, 160, 650));
        landing = true;
        var down = new DoubleAnimation(mw.Top, top, duration) { EasingFunction = new QuadraticEase { EasingMode = top > mw.Top ? EasingMode.EaseIn : EasingMode.EaseOut } };
        var side = new DoubleAnimation(mw.Left, left, duration) { EasingFunction = new SineEase { EasingMode = EasingMode.EaseOut } };
        down.Completed += (_, _) =>
        {
            mw.BeginAnimation(Window.TopProperty, null);
            mw.BeginAnimation(Window.LeftProperty, null);
            mw.Top = top;
            mw.Left = left;
            landing = false;
        };
        mw.BeginAnimation(Window.LeftProperty, side);
        mw.BeginAnimation(Window.TopProperty, down);
    }

    #region Délégué au contrôleur classique
    public bool IfInActivateScreen() => true;
    public void SetNowScreenActivate() { }
    public bool AutoChangeWindow => false;
    public double ZoomRatio => inner.ZoomRatio;
    public int PressLength => inner.PressLength;
    public void ShowPanel() => inner.ShowPanel();
    public bool EnableFunction => inner.EnableFunction;
    public int InteractionCycle => inner.InteractionCycle;
    public bool RePositionActive { get; set; } = true;
    #endregion
}
