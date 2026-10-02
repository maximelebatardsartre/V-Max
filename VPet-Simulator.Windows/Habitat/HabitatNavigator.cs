using System;
using System.Collections.Generic;
using System.Linq;

namespace VPet_Simulator.Windows.Habitat;

public enum NavStepKind { Walk, Climb, Drop }

/// <summary>
/// Une étape d'un trajet : marcher sur un sol, grimper le long d'un axe, ou se laisser tomber vers un sol plus bas
/// </summary>
public sealed record NavStep(NavStepKind Kind, string FromFloor, double FromX, string ToFloor, double ToX, HabitatClimb? Climb = null);

/// <summary>
/// Ce que le personnage sait faire (selon les animations disponibles)
/// </summary>
public sealed record NavCapabilities(bool ClimbUp, bool ClimbDown)
{
    public static readonly NavCapabilities All = new(true, true);
}

/// <summary>
/// V-Max habitat : trajets entre deux points de la carte. Graphe de points de passage (quelques dizaines de nœuds)
/// et A* : marcher le long des sols, grimper les axes d'escalade, se laisser tomber par les chutes.
/// Les axes d'escalade sont impraticables si le personnage n'a pas l'animation correspondante.
/// </summary>
public sealed class HabitatNavigator
{
    private const double ClimbCost = 1.6;   // grimper coûte plus que marcher
    private const double DropCost = 0.6;    // tomber est rapide
    private const double DropPenalty = 40;  // … mais pas gratuit (évite les chutes inutiles)

    private readonly HabitatMap map;
    private readonly NavCapabilities caps;
    /// <summary>Tolérance d'accroche entre les axes et les sols (pixels de l'image)</summary>
    private readonly double tolerance;

    private sealed record Node(int Index, string Floor, double X, double Y);
    private sealed record Edge(int To, double Cost, NavStepKind Kind, HabitatClimb? Climb);

    private readonly List<Node> nodes = new();
    private readonly List<List<Edge>> edges = new();

    public HabitatNavigator(HabitatMap map, NavCapabilities caps, double? tolerance = null)
    {
        this.map = map;
        this.caps = caps;
        this.tolerance = tolerance ?? Math.Max(6, map.PetHeight * 0.08);
    }

    /// <summary>
    /// Sol où s'accroche l'extrémité d'un axe d'escalade (le plus proche en hauteur, qui couvre x à la tolérance près)
    /// </summary>
    public HabitatFloor? FloorAtEnd(double x, double y) =>
        map.Floors.Where(f => Math.Abs(f.Y - y) <= tolerance && x >= f.Left - tolerance && x <= f.Right + tolerance)
                  .OrderBy(f => Math.Abs(f.Y - y)).FirstOrDefault();

    /// <summary>
    /// Axes d'escalade reliés à un sol à chaque bout (les autres sont ignorés)
    /// </summary>
    public IEnumerable<(HabitatClimb climb, HabitatFloor top, HabitatFloor bottom)> ConnectedClimbs()
    {
        foreach (var c in map.Climbs)
        {
            double top = Math.Min(c.Y1, c.Y2), bottom = Math.Max(c.Y1, c.Y2);
            var ft = FloorAtEnd(c.X, top);
            var fb = FloorAtEnd(c.X, bottom);
            if (ft != null && fb != null && ft != fb)
                yield return (c, ft, fb);
        }
    }

    /// <summary>
    /// Trajet le plus court, ou null si la destination est inaccessible
    /// </summary>
    public List<NavStep>? FindPath(string fromFloor, double fromX, string toFloor, double toX)
    {
        var start = map.Floor(fromFloor);
        var goal = map.Floor(toFloor);
        if (start == null || goal == null)
            return null;
        Build(start, start.Clamp(fromX), goal, goal.Clamp(toX));
        int s = 0, g = 1;

        // A* : heuristique admissible (distance × coût minimal par pixel)
        var dist = Enumerable.Repeat(double.PositiveInfinity, nodes.Count).ToArray();
        var prev = new (int node, Edge? edge)[nodes.Count];
        for (int i = 0; i < prev.Length; i++) prev[i] = (-1, null);
        var open = new PriorityQueue<int, double>();
        dist[s] = 0;
        open.Enqueue(s, H(s, g));
        var closed = new HashSet<int>();
        while (open.TryDequeue(out int u, out _))
        {
            if (!closed.Add(u))
                continue;
            if (u == g)
                break;
            foreach (var e in edges[u])
            {
                double nd = dist[u] + e.Cost;
                if (nd < dist[e.To])
                {
                    dist[e.To] = nd;
                    prev[e.To] = (u, e);
                    open.Enqueue(e.To, nd + H(e.To, g));
                }
            }
        }
        if (double.IsPositiveInfinity(dist[g]))
            return null;

        var chain = new List<(int from, int to, Edge edge)>();
        for (int v = g; v != s; v = prev[v].node)
            chain.Add((prev[v].node, v, prev[v].edge!));
        chain.Reverse();

        var steps = new List<NavStep>();
        foreach (var (a, b, e) in chain)
        {
            var na = nodes[a];
            var nb = nodes[b];
            var step = new NavStep(e.Kind, na.Floor, na.X, nb.Floor, nb.X, e.Climb);
            // deux marches successives sur le même sol n'en font qu'une
            if (step.Kind == NavStepKind.Walk && steps.Count > 0 && steps[^1] is { Kind: NavStepKind.Walk } last && last.ToFloor == step.FromFloor && step.FromFloor == step.ToFloor)
                steps[^1] = last with { ToX = step.ToX, ToFloor = step.ToFloor };
            else if (!(step.Kind == NavStepKind.Walk && step.FromFloor == step.ToFloor && Math.Abs(step.FromX - step.ToX) < 0.5))
                steps.Add(step);
        }
        return steps;
    }

    /// <summary>Destination atteignable depuis ce point ?</summary>
    public bool CanReach(string fromFloor, double fromX, string toFloor, double toX) => FindPath(fromFloor, fromX, toFloor, toX) != null;

    private double H(int a, int b)
    {
        double dx = nodes[a].X - nodes[b].X, dy = nodes[a].Y - nodes[b].Y;
        return Math.Sqrt(dx * dx + dy * dy) * Math.Min(1, DropCost);
    }

    private void Build(HabitatFloor start, double sx, HabitatFloor goal, double gx)
    {
        nodes.Clear();
        edges.Clear();
        int Add(HabitatFloor f, double x)
        {
            var n = new Node(nodes.Count, f.Id, x, f.Y);
            nodes.Add(n);
            edges.Add(new List<Edge>());
            return n.Index;
        }
        void Link(int a, int b, double cost, NavStepKind kind, HabitatClimb? c = null) => edges[a].Add(new Edge(b, cost, kind, c));

        Add(start, sx);   // 0 : départ
        Add(goal, gx);    // 1 : arrivée

        foreach (var (c, top, bottom) in ConnectedClimbs())
        {
            int nb = Add(bottom, bottom.Clamp(c.X));
            int nt = Add(top, top.Clamp(c.X));
            double h = Math.Abs(bottom.Y - top.Y);
            if (caps.ClimbUp)
                Link(nb, nt, h * ClimbCost, NavStepKind.Climb, c);
            if (caps.ClimbDown)
                Link(nt, nb, h * ClimbCost, NavStepKind.Climb, c);
        }
        foreach (var d in map.Drops)
        {
            var from = map.Floor(d.From);
            var to = map.Floor(d.To);
            if (from == null || to == null || to.Y <= from.Y)
                continue;
            int a = Add(from, from.Clamp(d.X));
            int b = Add(to, to.Clamp(d.X));
            Link(a, b, (to.Y - from.Y) * DropCost + DropPenalty, NavStepKind.Drop);
        }
        // sols contigus (même hauteur, extrémités qui se touchent) : on passe de l'un à l'autre en marchant
        foreach (var f in map.Floors)
            foreach (var o in map.Floors)
                if (f != o && Math.Abs(f.Y - o.Y) <= tolerance && Math.Abs(f.Right - o.Left) <= tolerance)
                {
                    int a = Add(f, f.Right);
                    int b = Add(o, o.Left);
                    Link(a, b, Math.Abs(o.Left - f.Right) + 1, NavStepKind.Walk);
                    Link(b, a, Math.Abs(o.Left - f.Right) + 1, NavStepKind.Walk);
                }
        // sur un même sol, chaque point rejoint les autres en marchant
        foreach (var group in nodes.GroupBy(n => n.Floor))
        {
            var list = group.ToList();
            foreach (var a in list)
                foreach (var b in list)
                    if (a != b)
                        Link(a.Index, b.Index, Math.Abs(a.X - b.X), NavStepKind.Walk);
        }
    }
}
