using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using VPet_Simulator.Core;
using VPet_Simulator.Windows.Career;
using VPet_Simulator.Windows.Interface;
using static VPet_Simulator.Core.GraphHelper;

namespace VPet_Simulator.Windows.HUD;

/// <summary>
/// V-Max HUD : panneau UNIQUE « Carrière & occupations ». Onglet MÉTIERS = l'arbre gamifié (voies → métiers avec
/// barre d'XP, paliers, titres, salaire, activités à lancer, débloquées par progression) ; onglet LOISIRS = grille
/// libre. En-tête : voie/titre + activité en cours. Les animations d'ambiance ne sont pas listées (jouées en autonomie).
/// </summary>
public sealed class ActivitiesPanel : HudSidePanel
{
    private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

    private readonly StackPanel current = new();
    private readonly StackPanel content = new();
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private bool metierTab = true;
    private string voie = CareerTree.Families.First();

    public ActivitiesPanel(MainWindow pet) : base(pet, "CARRIÈRE & OCCUPATIONS", "Que fait-on ?", 420, 640)
    {
        AddHeaderButton("", "Planning détaillé", () => { HideAnimated(); PlanningWindow.Open(Pet, Work.WorkType.Work); });
        voie = CareerState.I.Voie ?? voie;

        var tabs = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 8) };
        var tMet = new RadioButton { Style = (Style)FindResource("HudChip"), Content = "Métiers", GroupName = "occtab", IsChecked = true, Margin = new Thickness(0, 0, 6, 0) };
        var tLoi = new RadioButton { Style = (Style)FindResource("HudChip"), Content = "Loisirs", GroupName = "occtab" };
        tMet.Checked += (_, _) => { metierTab = true; Rebuild(); };
        tLoi.Checked += (_, _) => { metierTab = false; Rebuild(); };
        tabs.Children.Add(tMet);
        tabs.Children.Add(tLoi);

        var root = new StackPanel();
        root.Children.Add(current);
        root.Children.Add(tabs);
        root.Children.Add(content);
        Body = root;

        CareerState.I.Changed += OnCareer;
        Closed += (_, _) => CareerState.I.Changed -= OnCareer;
        timer.Tick += (_, _) => RefreshCurrent();
        IsVisibleChanged += (_, _) => { if (IsVisible) timer.Start(); else timer.Stop(); };
    }

    private void OnCareer() => Dispatcher.Invoke(() => { if (IsVisible) Rebuild(); });

    /// <summary>Ouvre directement sur l'onglet Métiers (appelé par « ma carrière »).</summary>
    public void ShowMetiers() { metierTab = true; if (IsVisible) Rebuild(); }

    protected override void OnOpening()
    {
        try { ActivityCatalog.Apply(Pet); } catch { } // garantit le nettoyage des occupations avant affichage
        voie = CareerState.I.Voie ?? voie;
        if (!CareerTree.Families.Contains(voie)) voie = CareerTree.Families.First(); // famille renommée/obsolète
        RefreshCurrent();
        Rebuild();
    }

    #region Activité en cours
    private string? currentName;
    private TextBlock? elapsed;

    private void RefreshCurrent()
    {
        var main = Pet.Main;
        var work = main.State == Main.WorkingState.Work ? main.NowWork : null;
        if (work?.Name != currentName)
        {
            currentName = work?.Name;
            current.Children.Clear();
            elapsed = null;
            if (work != null && !ActivityCatalog.IsAmbiance(work))
                current.Children.Add(CurrentCard(work));
        }
        if (work != null && elapsed != null)
        {
            var ts = DateTime.Now - main.WorkTimer.StartTime;
            double total = Math.Max(1, work.Time);
            elapsed.Text = $"{(int)ts.TotalMinutes} / {work.Time} min · {Gain(work, main.WorkTimer.GetCount)} gagnés";
            if (current.Children.Count > 0 && current.Children[0] is Border b && b.Tag is Border fill && fill.Parent is Grid track && track.ActualWidth > 0)
                fill.Width = track.ActualWidth * Math.Clamp(ts.TotalMinutes / total, 0, 1);
        }
    }

    private FrameworkElement CurrentCard(Work work)
    {
        var stop = new Button { Style = (Style)FindResource("HudGhostButton"), Content = "Arrêter", VerticalAlignment = VerticalAlignment.Center };
        stop.Click += (_, _) => { Pet.Main.WorkTimer.Stop(reason: WorkTimer.FinishWorkInfo.StopReason.MenualStop); RefreshCurrent(); Rebuild(); };
        var title = new TextBlock { Text = work.NameTrans, FontFamily = (FontFamily)FindResource("HudDisplay"), FontWeight = FontWeights.SemiBold, FontSize = 17, Foreground = Res("HudText") };
        elapsed = new TextBlock { FontFamily = (FontFamily)FindResource("HudMono"), FontSize = 11, Foreground = Res("HudTextMuted"), Margin = new Thickness(0, 2, 0, 0) };
        var head = new DockPanel();
        DockPanel.SetDock(stop, Dock.Right);
        head.Children.Add(stop);
        var names = new StackPanel();
        names.Children.Add(new TextBlock { Text = "EN COURS", Style = (Style)FindResource("HudEyebrow"), Foreground = Res("HudAccent") });
        names.Children.Add(title);
        names.Children.Add(elapsed);
        head.Children.Add(names);
        var track = new Grid { Height = 4, Margin = new Thickness(0, 10, 0, 0) };
        track.Children.Add(new Border { CornerRadius = new CornerRadius(2), Background = Res("HudStroke") });
        var fill = new Border { CornerRadius = new CornerRadius(2), Background = Res("HudAccent"), HorizontalAlignment = HorizontalAlignment.Left, Width = 0 };
        track.Children.Add(fill);
        var sp = new StackPanel();
        sp.Children.Add(head);
        sp.Children.Add(track);
        return new Border { Tag = fill, Margin = new Thickness(0, 0, 0, 14), Padding = new Thickness(14, 12, 12, 14), CornerRadius = new CornerRadius(16), Background = Res("HudAccentSoft"), BorderBrush = Res("HudAccent"), BorderThickness = new Thickness(1), Child = sp };
    }
    #endregion

    #region Construction des onglets
    private void Rebuild()
    {
        content.Children.Clear();
        if (metierTab) BuildMetiers(); else BuildLoisirs();
    }

    private void BuildMetiers()
    {
        var st = CareerState.I;
        // bannière : job actif + titre
        if (st.ActiveJobTrack is { } j)
            content.Children.Add(Banner($"{j.Icon}  {st.TitleOf(j)}", "Job actif : " + j.Name, Res("HudAccent")));
        else
            content.Children.Add(Banner("Choisis ta voie", "Clique une voie puis « C'est mon job » sur un métier.", Res("HudTextMuted")));

        // chips de voies (familles)
        var chips = new WrapPanel { Margin = new Thickness(0, 4, 0, 8) };
        foreach (var fam in CareerTree.Families)
        {
            var c = new RadioButton { Style = (Style)FindResource("HudChip"), Content = fam, GroupName = "voie", IsChecked = fam == voie, Margin = new Thickness(0, 0, 6, 6) };
            c.Checked += (_, _) => { voie = fam; Rebuild(); };
            chips.Children.Add(c);
        }
        content.Children.Add(chips);

        // métiers de la voie sélectionnée
        Pet.Main.WorkList(out var ws, out var ss, out _);
        var pro = ws.Concat(ss).ToList();
        foreach (var track in CareerTree.InFamily(voie))
            content.Children.Add(TrackCard(track, pro.Where(w => ActivityCatalog.TrackOf(w) == track).ToList()));
    }

    private void BuildLoisirs()
    {
        Pet.Main.WorkList(out var ws, out var ss, out var ps);
        var loisirs = ws.Concat(ss).Concat(ps)
            .Where(w => !ActivityCatalog.IsAmbiance(w) && ActivityCatalog.TrackOf(w) == null)
            .GroupBy(w => w.Name).Select(g => g.First())
            .OrderBy(w => w.NameTrans, StringComparer.CurrentCultureIgnoreCase).ToList();
        content.Children.Add(new TextBlock { Text = "Tes loisirs : lance-les quand tu veux, juste pour le plaisir (pas d'argent).", Foreground = Res("HudTextMuted"), FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) });
        if (loisirs.Count == 0)
            content.Children.Add(new TextBlock { Text = "Aucun loisir pour ce compagnon.", Foreground = Res("HudTextMuted"), Margin = new Thickness(0, 8, 0, 0) });
        foreach (var w in loisirs)
            content.Children.Add(ActivityRow(Prepare(Pet, w), false, null));
    }

    private FrameworkElement TrackCard(CareerTrack t, List<Work> works)
    {
        var st = CareerState.I;
        bool active = st.ActiveJob == t.Id;
        int tier = st.TierIndex(t);
        var sp = new StackPanel();

        var head = new StackPanel { Orientation = Orientation.Horizontal };
        head.Children.Add(new TextBlock { Text = t.Icon + "  " + t.Name, Foreground = Res("HudText"), FontSize = 14.5, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        if (active)
            head.Children.Add(new Border { CornerRadius = new CornerRadius(6), Background = Res("HudAccent"), Padding = new Thickness(6, 1, 6, 2), Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Child = new TextBlock { Text = "MON JOB", FontSize = 9.5, Foreground = Brushes.White, FontWeight = FontWeights.Bold } });
        sp.Children.Add(head);

        string statut = "Statut : " + st.TitleOf(t);
        if (t.Kind == CareerKind.Work && st.CurrentTier(t).SalaryBonus > 0)
            statut += $"  ·  salaire +{st.CurrentTier(t).SalaryBonus * 100:0} %";
        sp.Children.Add(new TextBlock { Text = statut, Foreground = Res("HudAccent"), FontSize = 12, Margin = new Thickness(0, 3, 0, 5) });

        sp.Children.Add(XpBar(t));
        sp.Children.Add(Pips(t, tier));

        if (!active)
        {
            var b = new Button { Style = (Style)FindResource("HudGhostButton"), Content = "C'est mon job", FontSize = 11.5, Margin = new Thickness(0, 8, 0, 2), HorizontalAlignment = HorizontalAlignment.Left };
            b.Click += (_, _) => CareerState.I.ChooseJob(t.Id);
            sp.Children.Add(b);
        }

        // activités du métier (débloquées par palier)
        foreach (var w in works.OrderBy(ActivityCatalog.TierOf))
        {
            int req = ActivityCatalog.TierOf(w);
            bool locked = !st.IsUnlocked(w);
            sp.Children.Add(ActivityRow(Prepare(Pet, w), locked, locked ? t.Tiers[Math.Min(req, t.Tiers.Length - 1)] : null));
        }

        return new Border { CornerRadius = new CornerRadius(14), Padding = new Thickness(13, 11, 13, 12), Margin = new Thickness(0, 0, 0, 8), Background = Res("HudSurfaceRaised"), BorderBrush = active ? Res("HudAccent") : Res("HudStroke"), BorderThickness = new Thickness(1), Child = sp };
    }

    private FrameworkElement ActivityRow(Work w, bool locked, CareerTier? reqTier)
    {
        bool running = Pet.Main.State == Main.WorkingState.Work && Pet.Main.NowWork?.Name == w.Name;
        var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        info.Children.Add(new TextBlock { Text = w.NameTrans, FontSize = 13.5, FontWeight = FontWeights.SemiBold, Foreground = Res(locked ? "HudTextMuted" : "HudText"), TextTrimming = TextTrimming.CharacterEllipsis });
        info.Children.Add(new TextBlock { Text = GainRate(w), FontSize = 11.5, Foreground = Res(locked ? "HudTextMuted" : "HudSuccess"), Margin = new Thickness(0, 1, 0, 0) });

        FrameworkElement action;
        if (locked)
            action = new TextBlock { Text = "🔒 " + (reqTier?.Title ?? "Verrouillé"), FontSize = 11, Foreground = Res("HudAmber"), VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Right, MaxWidth = 110, TextWrapping = TextWrapping.Wrap };
        else
        {
            var b = new Button { Style = (Style)FindResource(running ? "HudGhostButton" : "HudPrimaryButton"), Content = running ? "Arrêter" : "Lancer", VerticalAlignment = VerticalAlignment.Center, Padding = new Thickness(12, 5, 12, 5) };
            b.Click += (_, _) => Start(w);
            action = b;
        }

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(info);
        Grid.SetColumn(action, 1);
        action.Margin = new Thickness(10, 0, 0, 0);
        grid.Children.Add(action);
        return new Border { CornerRadius = new CornerRadius(11), Padding = new Thickness(11, 8, 9, 8), Margin = new Thickness(0, 5, 0, 0), Background = Res("HudSurface"), BorderBrush = Res("HudStroke"), BorderThickness = new Thickness(1), Opacity = locked ? 0.75 : 1, Child = grid };
    }

    private FrameworkElement Banner(string big, string sub, Brush bigBrush)
    {
        var sp = new StackPanel();
        sp.Children.Add(new TextBlock { Text = "CARRIÈRE", Style = (Style)FindResource("HudEyebrow"), Foreground = Res("HudAccent") });
        sp.Children.Add(new TextBlock { Text = big, FontFamily = (FontFamily)FindResource("HudDisplay"), FontWeight = FontWeights.SemiBold, FontSize = 20, Foreground = bigBrush });
        sp.Children.Add(new TextBlock { Text = sub, Foreground = Res("HudTextMuted"), FontSize = 12, TextWrapping = TextWrapping.Wrap });
        return new Border { CornerRadius = new CornerRadius(16), Padding = new Thickness(14, 11, 14, 12), Margin = new Thickness(0, 0, 0, 8), Background = Res("HudSurfaceRaised"), BorderBrush = Res("HudAccent"), BorderThickness = new Thickness(1), Child = sp };
    }

    private FrameworkElement XpBar(CareerTrack t)
    {
        var st = CareerState.I;
        double xp = st.XpOf(t.Id);
        var next = st.NextTier(t);
        double from = st.CurrentTier(t).XpRequired;
        double to = next?.XpRequired ?? t.MaxXp;
        double frac = next == null ? 1 : Math.Clamp((xp - from) / Math.Max(1, to - from), 0, 1);
        var trackBar = new Border { Height = 7, CornerRadius = new CornerRadius(4), Background = Res("HudStroke") };
        var fill = new Border { Height = 7, CornerRadius = new CornerRadius(4), Background = Res("HudAccent") };
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0.0001, frac), GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0.0001, 1 - frac), GridUnitType.Star) });
        Grid.SetColumnSpan(trackBar, 2);
        grid.Children.Add(trackBar);
        Grid.SetColumn(fill, 0);
        grid.Children.Add(fill);
        var label = new TextBlock { Text = next == null ? $"Métier accompli · {xp:0} XP" : $"{xp:0} / {to:0} XP → {next.Title}", Foreground = Res("HudTextMuted"), FontSize = 11, Margin = new Thickness(0, 4, 0, 0) };
        var col = new StackPanel();
        col.Children.Add(grid);
        col.Children.Add(label);
        return col;
    }

    private FrameworkElement Pips(CareerTrack t, int tier)
    {
        var pips = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 7, 0, 0) };
        for (int i = 0; i < t.Tiers.Length; i++)
            pips.Children.Add(new Border { Width = 9, Height = 9, CornerRadius = new CornerRadius(5), Margin = new Thickness(0, 0, 5, 0), Background = i <= tier ? Res("HudAccent") : Res("HudStroke"), ToolTip = t.Tiers[i].Title + (i <= tier ? "" : $" (à {t.Tiers[i].XpRequired:0} XP)") });
        return pips;
    }
    #endregion

    #region Lancement & aides
    public static Work Prepare(MainWindow mw, Work w)
    {
        // Pour une activité curatée (métier/loisir/ambiance), on NE multiplie PAS : Double() appelle FixOverLoad()
        // sans condition et écraserait notre barème. La progression de carrière remplace le multiplicateur.
        int mult = ActivityCatalog.Of(w) != null ? 1 : mw.Set["workmenu"].GetInt("double_" + w.Name, 1);
        var work = mult > 1 ? w.Double(mult) : (Work)w.Clone();
        if (!mw.Set["gameconfig"].GetBool("noAutoCal") && work.IsOverLoad())
            work.FixOverLoad();
        return work;
    }

    private void Start(Work w)
    {
        var main = Pet.Main;
        if (Pet.Set.EnableFunction && Pet.Core.Save!.Mode == IGameSave.ModeType.Ill && !(main.State == Main.WorkingState.Work && main.NowWork?.Name == w.Name))
        {
            HudToast.Show(Pet, $"{Pet.Core.Save.Name} est malade : soigne-le d'abord (garde-manger › Soins).", HudToast.Kind.Warning, 5);
            return;
        }
        if (main.StartWork(w))
            HideAnimated();
        else
        {
            RefreshCurrent();
            Rebuild();
        }
    }

    private static string GainRate(Work w)
    {
        double rate = w.Get();
        string unit = w.Type == Work.WorkType.Work ? "$" : "XP";
        return $"≈ {rate.ToString("N1", Fr)} {unit}/min · {Duration(w.Time)}";
    }

    private static string Gain(Work w, double count) =>
        w.Type == Work.WorkType.Work ? count.ToString("N0", Fr) + " $" : count.ToString("N0", Fr) + " XP";

    private static string Duration(int minutes) =>
        minutes >= 60 ? $"{minutes / 60} h {(minutes % 60 == 0 ? "" : (minutes % 60).ToString("00"))}".TrimEnd() : $"{minutes} min";
    #endregion
}
