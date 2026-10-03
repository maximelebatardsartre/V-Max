using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using VPet_Simulator.Core;
using VPet_Simulator.Windows.HUD;
using static VPet_Simulator.Core.GraphHelper;
using static VPet_Simulator.Core.GraphInfo;
using static VPet_Simulator.Windows.Interface.Food;

namespace VPet_Simulator.Windows.Habitat;

/// <summary>
/// V-Max : comportement du compagnon. Reçoit des intentions (« va dans la cuisine manger »), venues des routines
/// de vie, de l'agent IA ou de l'utilisateur ; calcule le trajet dans l'habitat, le pilote, puis lance l'activité
/// (les animations apportent leurs meubles). Sans habitat, l'activité se joue sur place.
/// </summary>
public sealed class LifeBrain
{
    private readonly MainWindow mw;
    private readonly DispatcherTimer scheduler;
    private readonly DispatcherTimer watcher;
    private Func<bool>? nativeMove;
    private CancellationTokenSource? cts;
    private HabitatPilot? pilot;
    /// <summary>Portion de sol de la pièce d'arrivée (pour y traîner un peu après un repas)</summary>
    private (double left, double right)? roomLeash;

    public LifeBrain(MainWindow mw)
    {
        this.mw = mw;
        Book = RoutineBook.Load();
        scheduler = new DispatcherTimer(TimeSpan.FromSeconds(20), DispatcherPriority.Background, (_, _) => Tick(), mw.Dispatcher);
        watcher = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => Watch(), mw.Dispatcher);
        // les déplacements aléatoires de VPet se taisent pendant une intention (sauf flânerie dans la pièce)
        nativeMove = mw.Main.DisplayMove;
        mw.Main.DisplayMove = () => Current == null || Current.Activity == "relax" && !Navigating ? nativeMove!() : false;
        scheduler.Start();
    }

    public RoutineBook Book { get; private set; }

    /// <summary>Intention en cours (null = vit sa vie librement)</summary>
    public Intent? Current { get; private set; }
    public bool Navigating { get; private set; }
    public event Action? Changed;

    public sealed record Intent(string? Place, string Activity, DateTime Until, string Source, string Label);

    #region Routines
    public bool RoutinesEnabled
    {
        get => Book.Enabled;
        set
        {
            Book.Enabled = value;
            SaveBook();
            Changed?.Invoke();
        }
    }

    /// <summary>QA : routines en mémoire seulement (rien n'est écrit dans routines.json)</summary>
    internal bool Transient { get; set; }

    /// <summary>QA : remplace le carnet de routines en mémoire et force une vérification</summary>
    internal void QaUse(RoutineBook book)
    {
        Transient = true;
        Book = book;
        Tick();
    }

    public void SaveBook()
    {
        if (Transient)
            return;
        try
        {
            Book.Save();
        }
        catch (Exception e)
        {
            mw.Toast("Impossible d'enregistrer les routines : " + e.Message, HudToast.Kind.Warning);
        }
    }

    public void ReplaceRoutines(System.Collections.Generic.IEnumerable<LifeRoutine> routines)
    {
        Book.Routines = routines.Select(r => r.Clone()).ToList();
        SaveBook();
        Changed?.Invoke();
    }

    /// <summary>Fin de la dernière intention et pause choisie avant la suivante (pas d'enchaînement mécanique)</summary>
    private DateTime breatherUntil = DateTime.MinValue;
    private DateTime lastEarn = DateTime.MinValue;

    /// <summary>Vérifie toutes les 20 s si une routine doit démarrer</summary>
    private void Tick()
    {
        if (!Book.Enabled || Current != null || !Available())
            return;
        var due = RoutinePlanner.Due(Book.Routines, DateTime.Now, Book.WasPlayed);
        var o = due.FirstOrDefault();
        if (o == null)
        {
            // Aucune routine prévue maintenant : elle VIT quand même. Si une jauge le réclame (faim, soif,
            // fatigue, moral), elle agit d'elle-même — c'est ce qui rend le compagnon « vivant » et pas scripté.
            MaybeSpontaneous();
            return;
        }
        // il souffle un peu entre deux routines, sauf si la plage se termine bientôt
        if (DateTime.Now < breatherUntil && o.WindowEnd - DateTime.Now > TimeSpan.FromMinutes(3))
            return;
        Book.Played.Add(o.Key);
        if (o.Routine.OnlyIfNeeded && !Needs(o.Routine.Action))
        {
            // pas faim, pas soif, pas fatigué : la routine est passée pour aujourd'hui
            SaveBook();
            return;
        }
        // les autres occurrences en retard de la même vague sont abandonnées (pas d'enchaînement mécanique)
        foreach (var skipped in due.Skip(1).Where(d => d.Start < DateTime.Now.AddMinutes(-30)))
            Book.Played.Add(skipped.Key);
        SaveBook();
        _ = StartAsync(o.Routine.Place, o.Routine.Action, o.Duration, "routine");
    }

    private DateTime lastSpontaneous = DateTime.MinValue;

    /// <summary>
    /// Comportement autonome « vivant » hors routine : quand rien n'est prévu, Maxine suit ses propres besoins
    /// (dormir si épuisée, manger/boire si les jauges chutent, se divertir si le moral est bas, flâner sinon).
    /// Cadencé (pas d'enchaînement) et ne tourne que si les fonctions/jauges sont actives.
    /// </summary>
    private void MaybeSpontaneous()
    {
        if (!mw.Set.EnableFunction)                                  // jauges désactivées → pas de besoins à suivre
            return;
        if (DateTime.Now < breatherUntil)                           // elle vient d'agir : elle souffle
            return;
        if (DateTime.Now - lastSpontaneous < TimeSpan.FromMinutes(2))
            return;
        var s = mw.Core.Save!;
        double max = Math.Max(1, s.StrengthMax);
        string? act =
            s.Strength / max < 0.25 ? "sleep" :
            s.StrengthFood / max < 0.4 ? "eat" :
            s.StrengthDrink / max < 0.4 ? "drink" :
            s.Feeling / Math.Max(1, s.FeelingMax) < 0.3 ? PickLeisure() :
            Random.Shared.NextDouble() < 0.35 ? PickAmbiance() :     // la VIE : ménage, repos, émotes…
            Random.Shared.NextDouble() < 0.25 ? "relax" :            // flânerie occasionnelle
            null;
        if (act == null)
            return;
        lastSpontaneous = DateTime.Now;
        // sur place (ou là où l'animation l'emmène) : le fix « routines sans pièce » gère l'absence de carte
        _ = StartAsync(null, act, TimeSpan.Zero, "autonomy");
    }

    /// <summary>Choisit un loisir faisable (remonte le moral) ; à défaut, une simple détente.</summary>
    private string PickLeisure()
    {
        mw.Main.WorkList(out _, out _, out var ps);
        // un vrai loisir (pas une animation d'ambiance), débloqué
        var w = ps.Where(w => !Career.ActivityCatalog.IsAmbiance(w)).OrderBy(_ => Random.Shared.Next()).FirstOrDefault();
        return w != null ? "work:" + w.Name : "relax";
    }

    /// <summary>Choisit une animation de VIE/ambiance (ménage, repos, émote…) à jouer sur place, sans gain.</summary>
    private string PickAmbiance()
    {
        mw.Main.WorkList(out _, out _, out var ps);
        var a = ps.Where(w => Career.ActivityCatalog.IsAmbiance(w)).OrderBy(_ => Random.Shared.Next()).FirstOrDefault();
        return a != null ? "life:" + a.Name : "relax";
    }

    /// <summary>A-t-il besoin de cette action (jauge sous 80 %, 90 % pour le sommeil) ?</summary>
    private bool Needs(string action)
    {
        var s = mw.Core.Save!;
        if (!mw.Set.EnableFunction)
            return true;
        double max = Math.Max(1, s.StrengthMax);
        return action switch
        {
            "eat" => s.StrengthFood / max < 0.8,
            "drink" => s.StrengthDrink / max < 0.8,
            "sleep" => s.Strength / max < 0.9,
            _ => true,
        };
    }

    /// <summary>Le compagnon est-il libre (pas tenu, pas occupé à la demande de l'utilisateur) ?</summary>
    private bool Available()
    {
        var m = mw.Main;
        if (m.DisplayType.Type is GraphType.Raised_Dynamic or GraphType.Raised_Static)
            return false;
        if (m.State != Main.WorkingState.Nomal)
            return false;
        return mw.Habitat?.Window?.IsEditing != true;
    }
    #endregion

    /// <summary>
    /// Démarre une intention. Retourne un message lisible (succès ou raison de l'échec).
    /// </summary>
    public async Task<string> StartAsync(string? place, string activity, TimeSpan duration, string source)
    {
        Cancel(null);
        roomLeash = null;
        var habitat = mw.Habitat;
        string label = Describe(activity, place);
        var until = DateTime.Now + (duration > TimeSpan.Zero ? duration : DefaultDuration(activity));
        var mine = new Intent(place, activity, until, source, label);
        Current = mine;
        cts = new CancellationTokenSource();
        var token = cts.Token;
        Changed?.Invoke();

        if (habitat?.IsActive == true && !string.IsNullOrWhiteSpace(place))
        {
            // On ESSAIE de rejoindre la pièce. Mais si elle n'existe pas (habitat sans pièces, pièce absente,
            // pas de sol, ou pas de chemin), on NE renonce PAS : l'action se fait SUR PLACE. C'est le contrat
            // annoncé dans les réglages (« Sans carte tracée, Maxine suit quand même ses routines, mais sur place »).
            var room = habitat.Map.RoomNamed(place);
            var target = room != null ? habitat.Map.TargetIn(room, activity == "relax" ? null : activity, Random.Shared) : null;
            var here = habitat.Locate();
            System.Collections.Generic.List<NavStep>? path = null;
            if (room != null && target != null && here != null)
            {
                pilot ??= new HabitatPilot(mw, habitat);
                var nav = new HabitatNavigator(habitat.Map, pilot.Capabilities());
                path = nav.FindPath(here.Value.floor.Id, here.Value.x, target.Value.floor.Id, target.Value.x);
            }
            if (path != null)
            {
                if (source == "routine")
                    Announce(activity, room!.Name);
                Navigating = true;
                Changed?.Invoke();
                bool arrived = await pilot!.RunAsync(path, token);
                if (Current != mine)
                    return "Remplacé par une autre demande.";
                Navigating = false;
                if (!arrived || token.IsCancellationRequested)
                {
                    Current = null;
                    Changed?.Invoke();
                    return "Trajet interrompu.";
                }
                // pour flâner, il reste dans la pièce
                var seg = habitat.Map.FloorsIn(room!).FirstOrDefault(s => s.floor.Id == target!.Value.floor.Id);
                roomLeash = seg.floor != null ? (seg.left, seg.right) : null;
                habitat.Leash = activity == "relax" ? roomLeash : null;
            }
            else if (source == "routine")
                Announce(activity, place); // pas de pièce/chemin : on annonce et on agit sur place
        }
        else if (source == "routine")
            Announce(activity, place);

        Perform(activity);
        watcher.Start();
        Changed?.Invoke();
        return label;
    }

    /// <summary>Abandonne l'intention en cours (l'activité déjà commencée suit son cours naturel)</summary>
    public void Cancel(string? reason)
    {
        cts?.Cancel();
        cts = null;
        watcher.Stop();
        Navigating = false;
        if (mw.Habitat != null)
            mw.Habitat.Leash = null;
        if (Current != null)
        {
            Current = null;
            Changed?.Invoke();
        }
    }

    #region Activités
    private void Perform(string activity)
    {
        var m = mw.Main;
        switch (activity)
        {
            case "sleep":
                if (m.State == Main.WorkingState.Nomal)
                    m.DisplaySleep(true);
                break;
            case "eat":
            case "drink":
                var food = PetCare.Choose(mw, activity == "eat" ? FoodType.Meal : FoodType.Drink)
                           ?? (activity == "eat" ? PetCare.Choose(mw, FoodType.Snack) : null);
                var error = food == null ? "Rien d'abordable à " + (activity == "eat" ? "manger" : "boire") + " pour l'instant." : PetCare.Feed(mw, food, interactive: false);
                if (!string.IsNullOrEmpty(error))
                    mw.Toast(error, HudToast.Kind.Info);
                // un repas est court : l'intention se termine après l'animation
                Current = Current! with { Until = DateTime.Now.AddSeconds(12) };
                break;
            default:
                if (activity.StartsWith("work:"))
                {
                    var name = activity[5..];
                    m.WorkList(out var ws, out var ss, out var ps);
                    var work = ws.Concat(ss).Concat(ps).FirstOrDefault(w => string.Equals(w.Name, name, StringComparison.OrdinalIgnoreCase));
                    if (work == null || !m.StartWork(ActivitiesPanel.Prepare(mw, work)))
                        Current = Current! with { Until = DateTime.Now };
                }
                else if (activity.StartsWith("life:"))
                {
                    // activité de VIE : on joue l'animation directement (A_Start puis boucle), sans travail ni gain
                    var name = activity[5..];
                    m.WorkList(out _, out _, out var ps);
                    var w = ps.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
                    if (w != null && m.State == Main.WorkingState.Nomal)
                        try { w.Display(m); } catch { Current = Current! with { Until = DateTime.Now }; }
                    else
                        Current = Current! with { Until = DateTime.Now };
                }
                break;
        }
    }

    /// <summary>Fin des activités à durée (sommeil, flânerie, occupation raccourcie) et détection des interruptions</summary>
    private void Watch()
    {
        var c = Current;
        if (c == null)
        {
            watcher.Stop();
            return;
        }
        var m = mw.Main;
        bool grabbed = m.DisplayType.Type is GraphType.Raised_Dynamic or GraphType.Raised_Static;
        bool over = DateTime.Now >= c.Until;
        switch (c.Activity)
        {
            case "sleep":
                if (m.State != Main.WorkingState.Sleep)
                    over = true; // réveillé par l'utilisateur
                else if (over)
                {
                    m.State = Main.WorkingState.Nomal;
                    m.Display(GraphType.Sleep, AnimatType.C_End, m.DisplayNomal);
                }
                break;
            default:
                if (c.Activity.StartsWith("work:"))
                {
                    if (m.State != Main.WorkingState.Work)
                        over = true;
                    else if (over)
                        m.WorkTimer?.Stop(reason: WorkTimer.FinishWorkInfo.StopReason.MenualStop);
                }
                else if (c.Activity.StartsWith("life:") && over)
                    try { m.DisplayToNomal(); } catch { } // fin de l'animation de vie : retour à la normale
                break;
        }
        if (grabbed)
        {
            Cancel(null);
            return;
        }
        if (!over)
            return;
        // après un repas ou une boisson, il ne repart pas aussitôt : il traîne un peu dans la pièce
        if (c.Activity is "eat" or "drink" && c.Source != "linger" && roomLeash is { } leash && mw.Habitat?.IsActive == true)
        {
            mw.Habitat.Leash = leash;
            Current = c with { Activity = "relax", Source = "linger", Until = DateTime.Now.AddMinutes(1 + Random.Shared.NextDouble() * 3) };
            Changed?.Invoke();
            return;
        }
        bool wasWork = c.Activity.StartsWith("work:");
        Cancel(null);
        breatherUntil = DateTime.Now.AddMinutes(3 + Random.Shared.NextDouble() * 7);
        if (!wasWork)
            EarnIfBroke();
    }

    /// <summary>
    /// Autonomie financière : à court d'argent, il part travailler (l'occupation la plus rentable qu'il sait faire)
    /// </summary>
    private void EarnIfBroke()
    {
        var s = mw.Core.Save!;
        if (!Book.Enabled || !Book.EarnWhenBroke || !mw.Set.EnableFunction || mw.Sandbox?.UnlimitedMoney == true || s.Money >= 50
            || DateTime.Now - lastEarn < TimeSpan.FromHours(2) || s.Mode == VPet_Simulator.Core.IGameSave.ModeType.Ill)
            return;
        mw.Main.WorkList(out var ws, out _, out _);
        // seules les activités débloquées (progression de carrière) ; on privilégie le JOB ACTIF
        var ok = ws.Where(w => Career.CareerState.I.IsUnlocked(w)).ToList();
        var job = Career.CareerState.I.ActiveJobTrack;
        Work? best = null;
        if (job is { Kind: Career.CareerKind.Work })
            best = ok.Where(w => Career.ActivityCatalog.TrackOf(w) == job).Select(w => ActivitiesPanel.Prepare(mw, w))
                .OrderByDescending(w => VPet_Simulator.Windows.Interface.ExtensionFunction.Get(w)).FirstOrDefault();
        best ??= ok.Select(w => ActivitiesPanel.Prepare(mw, w))
            .OrderByDescending(w => VPet_Simulator.Windows.Interface.ExtensionFunction.Get(w)).FirstOrDefault();
        if (best == null)
            return;
        lastEarn = DateTime.Now;
        var office = mw.Habitat?.IsActive == true ? mw.Habitat.Map.Rooms.FirstOrDefault(r => r.Tag == "office")?.Name : null;
        try { mw.Main.MsgBar?.Show(s.Name, "Je suis presque à sec… je vais gagner un peu d'argent."); } catch { }
        _ = StartAsync(office, "work:" + best.Name, TimeSpan.Zero, "autonomy");
    }

    private static TimeSpan DefaultDuration(string activity)
    {
        if (activity.StartsWith("life:"))
            return TimeSpan.FromSeconds(20 + Random.Shared.Next(40)); // une animation de vie dure un court moment
        return activity switch
        {
            "sleep" => TimeSpan.FromMinutes(450),
            "relax" => TimeSpan.FromMinutes(20),
            "eat" or "drink" => TimeSpan.FromSeconds(30),
            _ => TimeSpan.FromHours(6), // une occupation s'arrête d'elle-même à la fin de sa durée native
        };
    }

    /// <summary>Petite phrase au départ d'une routine</summary>
    private void Announce(string activity, string? place)
    {
        string[] lines = activity switch
        {
            "sleep" => ["Je tombe de sommeil…", "Bonne nuit !", "Au lit !"],
            "eat" => ["J'ai un petit creux.", "C'est l'heure de manger !", "Je vais grignoter quelque chose."],
            "drink" => ["J'ai soif.", "Une petite pause boisson."],
            "relax" => ["Je vais me poser un peu.", "Pause !"],
            _ => ["Au travail !", "Je m'y mets."],
        };
        try
        {
            // bulle seule : une animation « parler » couperait la marche
            mw.Main.MsgBar?.Show(mw.Core.Save!.Name, lines[Random.Shared.Next(lines.Length)]);
        }
        catch { }
    }
    #endregion

    /// <summary>Libellé lisible d'une action</summary>
    public static string Describe(string activity, string? place)
    {
        string what = activity switch
        {
            "sleep" => "Dormir",
            "eat" => "Manger",
            "drink" => "Boire",
            "relax" => "Se détendre",
            _ when activity.StartsWith("work:") => activity[5..],
            _ when activity.StartsWith("life:") => activity[5..],
            _ => activity,
        };
        return string.IsNullOrWhiteSpace(place) ? what : what + " · " + place;
    }

    /// <summary>Où est le compagnon ? (pièce de l'habitat)</summary>
    public string WhereAmI()
    {
        var h = mw.Habitat;
        if (h?.IsActive != true)
            return "Sur ton bureau (le mode habitat n'est pas actif).";
        var here = h.Locate();
        if (here == null)
            return "Quelque part dans l'habitat.";
        var room = h.Map.RoomAt(here.Value.x, here.Value.floor.Y - h.Map.PetHeight * 0.3);
        return room != null ? $"Dans « {room.Name} »." : "Dans l'habitat, hors des pièces nommées.";
    }
}
