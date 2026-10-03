using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using VPet_Simulator.Core;
using static VPet_Simulator.Core.GraphHelper;
using static VPet_Simulator.Core.GraphInfo;

namespace VPet_Simulator.Windows.Habitat;

/// <summary>
/// V-Max habitat : exécute un trajet calculé par <see cref="HabitatNavigator"/> avec les animations natives
/// du personnage (walk, climb, fall) pendant que la fenêtre se déplace. S'interrompt si l'utilisateur attrape
/// le compagnon ou si une autre animation prend la main.
/// </summary>
internal sealed class HabitatPilot
{
    private readonly MainWindow mw;
    private readonly HabitatMode mode;

    public HabitatPilot(MainWindow mw, HabitatMode mode)
    {
        this.mw = mw;
        this.mode = mode;
    }

    public bool Busy { get; private set; }

    private Main Main => mw.Main;
    private double Size => 500 * mw.Set.ZoomLevel;
    private HabitatProjection P => mode.Projection;

    /// <summary>Ce que sait faire le personnage (animations d'escalade présentes)</summary>
    public NavCapabilities Capabilities() => new(HasLoop("climb.left") || HasLoop("climb.right"), HasLoop("climb.left") || HasLoop("climb.right"));

    private bool HasLoop(string name) => mw.Core.Graph?.FindGraph(name, AnimatType.B_Loop, mw.Core.Save!.Mode) != null;

    /// <summary>
    /// Joue le trajet. Retourne vrai si la destination est atteinte.
    /// </summary>
    public async Task<bool> RunAsync(IReadOnlyList<NavStep> steps, CancellationToken ct)
    {
        if (Busy)
            return false;
        Busy = true;
        try
        {
            foreach (var step in steps)
            {
                if (ct.IsCancellationRequested || Dragging)
                    return false;
                bool ok = step.Kind switch
                {
                    NavStepKind.Walk => await WalkAsync(step, ct),
                    NavStepKind.Climb => await ClimbAsync(step, ct),
                    NavStepKind.Drop => await DropAsync(step, ct),
                    _ => false,
                };
                if (!ok)
                    return false;
            }
            Main.DisplayToNomal();
            return true;
        }
        finally
        {
            Busy = false;
        }
    }

    private bool Dragging => Main.DisplayType.Type is GraphType.Raised_Dynamic or GraphType.Raised_Static;

    #region Marcher
    private Task<bool> WalkAsync(NavStep s, CancellationToken ct)
    {
        var floor = mode.Map.Floor(s.ToFloor) ?? mode.Map.Floor(s.FromFloor);
        if (floor == null)
            return Task.FromResult(false);
        double fromX = P.ToImageX(mw.Left + Size * mode.Metrics.CenterRatio);
        double target = floor.Clamp(s.ToX);
        if (Math.Abs(target - fromX) * P.ScaleX < 2)
        {
            PlaceStanding(floor, target);
            return Task.FromResult(true);
        }
        bool left = target < fromX;
        var move = PickMove(m => m.SpeedY == 0 && m.LocateType == Move.DirectionType.None && (m.SpeedX < 0) == left && m.SpeedX != 0,
                            left ? "walk.left" : "walk.right");
        // vitesse native : SpeedX unités toutes les Interval ms, à l'échelle du zoom
        double speed = move != null ? Math.Abs(move.SpeedX) * mw.Set.ZoomLevel / Math.Max(30, move.Interval) * 1000 : 110 * mw.Set.ZoomLevel;
        return Animate(move?.Graph, (dt) =>
        {
            double cx = mw.Left + Size * mode.Metrics.CenterRatio;
            double tx = P.ToScreenX(target);
            double step = speed * dt * (left ? -1 : 1);
            bool arrived = left ? cx + step <= tx : cx + step >= tx;
            double nx = arrived ? tx : cx + step;
            mw.Left = nx - Size * mode.Metrics.CenterRatio;
            mw.Top = P.ToScreenY(floor.Y) - Size * mode.Metrics.FootRatio;
            if (arrived)
                mode.Remember(target, floor.Id);
            return arrived;
        }, ct);
    }
    #endregion

    #region Grimper
    private Task<bool> ClimbAsync(NavStep s, CancellationToken ct)
    {
        var c = s.Climb;
        var from = mode.Map.Floor(s.FromFloor);
        var to = mode.Map.Floor(s.ToFloor);
        if (c == null || from == null || to == null)
            return Task.FromResult(false);
        bool up = to.Y < from.Y;
        string side = c.Side == "right" && HasLoop("climb.right") || !HasLoop("climb.left") ? "right" : "left";
        string graph = "climb." + side;
        var move = PickMove(m => m.Graph == graph && (m.SpeedY < 0) == up && m.SpeedY != 0, graph);
        double locate = move?.LocateLength > 0 ? move.LocateLength : side == "left" ? 145 : 185;
        double speed = move != null ? Math.Abs(move.SpeedY) * mw.Set.ZoomLevel / Math.Max(30, move.Interval) * 1000 : 80 * mw.Set.ZoomLevel;
        // accroche : la paroi de l'animation (LocateLength) se cale sur l'axe tracé
        double axis = P.ToScreenX(c.X);
        mw.Left = side == "left" ? axis - locate * mw.Set.ZoomLevel : axis - Size + locate * mw.Set.ZoomLevel;
        double startTop = P.ToScreenY(from.Y) - Size * mode.Metrics.FootRatio;
        double endTop = P.ToScreenY(to.Y) - Size * mode.Metrics.FootRatio;
        mw.Top = startTop;
        return Animate(graph, (dt) =>
        {
            double step = speed * dt * (up ? -1 : 1);
            bool arrived = up ? mw.Top + step <= endTop : mw.Top + step >= endTop;
            mw.Top = arrived ? endTop : mw.Top + step;
            if (arrived)
                PlaceStanding(to, to.Clamp(s.ToX));
            return arrived;
        }, ct);
    }
    #endregion

    #region Tomber
    private Task<bool> DropAsync(NavStep s, CancellationToken ct)
    {
        var from = mode.Map.Floor(s.FromFloor);
        var to = mode.Map.Floor(s.ToFloor);
        if (from == null || to == null)
            return Task.FromResult(false);
        double x = to.Clamp(s.ToX);
        bool left = Random.Shared.Next(2) == 0;
        string? graph = HasLoop(left ? "fall.left" : "fall.right") ? (left ? "fall.left" : "fall.right") : null;
        double endTop = P.ToScreenY(to.Y) - Size * mode.Metrics.FootRatio;
        double velocity = 0, g = 2400 * Math.Max(0.3, P.ScaleY);
        mw.Left = P.ToScreenX(x) - Size * mode.Metrics.CenterRatio;
        return Animate(graph, (dt) =>
        {
            velocity += g * dt;
            double ny = mw.Top + velocity * dt;
            bool arrived = ny >= endTop;
            mw.Top = arrived ? endTop : ny;
            if (arrived)
                PlaceStanding(to, x);
            return arrived;
        }, ct);
    }
    #endregion

    private void PlaceStanding(HabitatFloor floor, double x)
    {
        mw.Left = P.ToScreenX(x) - Size * mode.Metrics.CenterRatio;
        mw.Top = P.ToScreenY(floor.Y) - Size * mode.Metrics.FootRatio;
        mode.Remember(x, floor.Id);
    }

    /// <summary>
    /// Choisit un déplacement natif compatible avec l'humeur actuelle ; à défaut, celui du nom indiqué
    /// </summary>
    private Move? PickMove(Func<Move, bool> filter, string preferred)
    {
        var moves = mw.Core.Graph?.GraphConfig.Moves;
        if (moves == null)
            return null;
        var mood = Move.GetModeType(mw.Core.Save!.Mode);
        var ok = moves.Where(m => filter(m) && m.Mode.HasFlag(mood) && HasLoop(m.Graph)).ToList();
        return ok.FirstOrDefault(m => m.Graph == preferred) ?? ok.FirstOrDefault()
            ?? moves.FirstOrDefault(m => m.Graph == preferred && HasLoop(m.Graph));
    }

    /// <summary>
    /// Boucle d'animation (A_Start → B_Loop… → C_End) pendant qu'un pas de déplacement s'applique toutes les 30 ms.
    /// <paramref name="tick"/> reçoit le temps écoulé (s) et renvoie vrai à l'arrivée.
    /// </summary>
    private Task<bool> Animate(string? graph, Func<double, bool> tick, CancellationToken ct)
    {
        var tcs = new TaskCompletionSource<bool>();
        bool arrived = false, ended = false;
        var last = DateTime.Now;
        var foreign = DateTime.MinValue;
        var timer = new DispatcherTimer(DispatcherPriority.Render, mw.Dispatcher) { Interval = TimeSpan.FromMilliseconds(30) };

        void Finish(bool ok)
        {
            if (ended)
                return;
            ended = true;
            timer.Stop();
            tcs.TrySetResult(ok);
        }

        void Loop(string name)
        {
            if (ended)
                return;
            if (arrived)
            {
                Main.Display(name, AnimatType.C_End, () => Finish(true));
                return;
            }
            Main.Display(name, AnimatType.B_Loop, () => Loop(name));
        }

        // si l'intention est annulée alors que le timer est déjà mort (ex. tick qui a levé), on complète quand même
        ct.Register(() => { try { mw.Dispatcher.BeginInvoke(() => Finish(false)); } catch { } });

        timer.Tick += (_, _) =>
        {
          try
          {
            var now = DateTime.Now;
            double dt = Math.Min(0.1, (now - last).TotalSeconds);
            last = now;
            if (ct.IsCancellationRequested || Dragging)
            {
                Finish(false);
                return;
            }
            // une activité a commencé (sommeil, occupation lancée par l'utilisateur) : le trajet s'arrête
            if (Main.State != Main.WorkingState.Nomal)
            {
                Finish(false);
                return;
            }
            // une autre animation a pris la main (caresse, parole…) : on la laisse finir, puis on reprend la marche
            if (graph != null && !arrived && Main.DisplayType.Name != graph)
            {
                if (foreign == DateTime.MinValue)
                    foreign = now;
                else if ((now - foreign).TotalSeconds > 2.5 && Main.DisplayType.Type is GraphType.Default or GraphType.Idel or GraphType.Say)
                {
                    foreign = DateTime.MinValue;
                    Main.Display(graph, AnimatType.B_Loop, () => Loop(graph));
                }
                return; // immobile tant qu'une autre animation est affichée
            }
            foreign = DateTime.MinValue;
            if (!arrived && tick(dt))
            {
                arrived = true;
                if (graph == null)
                    Finish(true);
                else
                {
                    // filet de sécurité si la fin d'animation ne revient jamais
                    var guard = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
                    guard.Tick += (_, _) => { guard.Stop(); Finish(true); };
                    guard.Start();
                }
            }
          }
          catch
          {
              Finish(false); // un tick qui lève ne doit jamais figer l'intention (pet bloqué, vie autonome gelée)
          }
        };
        timer.Start();
        try
        {
            if (graph != null)
                Main.Display(graph, AnimatType.A_Start, () => Loop(graph));
        }
        catch { Finish(false); }
        return tcs.Task;
    }
}
