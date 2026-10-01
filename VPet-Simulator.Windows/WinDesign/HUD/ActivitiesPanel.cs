using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using VPet_Simulator.Core;
using VPet_Simulator.Windows.Interface;
using static VPet_Simulator.Core.GraphHelper;

namespace VPet_Simulator.Windows.HUD;

/// <summary>
/// V-Max HUD : occupations. L'activité en cours en tête (avec arrêt), puis les cartes travail / études / loisirs :
/// ce que ça rapporte, ce que ça coûte, la durée et le niveau requis. Le planning détaillé reste accessible.
/// </summary>
public sealed class ActivitiesPanel : HudSidePanel
{
    private static readonly (Work.WorkType type, string label)[] Categories =
    [
        (Work.WorkType.Work, "Travail"),
        (Work.WorkType.Study, "Études"),
        (Work.WorkType.Play, "Loisirs"),
    ];
    private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

    private readonly StackPanel current = new();
    private readonly StackPanel cards = new();
    private readonly WrapPanel chips = new() { Margin = new Thickness(0, 4, 0, 6) };
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private Work.WorkType category = Work.WorkType.Work;

    public ActivitiesPanel(MainWindow pet) : base(pet, "OCCUPATIONS", "Que fait-on ?", 420, 620)
    {
        AddHeaderButton("", "Planning et réglages avancés", () => { HideAnimated(); Pet.ShowWorkMenu(category); });
        foreach (var (type, label) in Categories)
        {
            var chip = new RadioButton { Style = (Style)FindResource("HudChip"), Content = label, GroupName = "activities", Margin = new Thickness(0, 0, 6, 6), IsChecked = type == category };
            chip.Checked += (_, _) => { category = type; Fill(); };
            chips.Children.Add(chip);
        }
        var root = new StackPanel();
        root.Children.Add(current);
        root.Children.Add(chips);
        root.Children.Add(cards);
        Body = root;
        timer.Tick += (_, _) => RefreshCurrent();
        IsVisibleChanged += (_, _) => { if (IsVisible) timer.Start(); else timer.Stop(); };
    }

    protected override void OnOpening()
    {
        RefreshCurrent();
        Fill();
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
            if (work != null)
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
        stop.Click += (_, _) =>
        {
            Pet.Main.WorkTimer.Stop(reason: WorkTimer.FinishWorkInfo.StopReason.MenualStop);
            RefreshCurrent();
            Fill();
        };
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
        return new Border
        {
            Tag = fill,
            Margin = new Thickness(0, 0, 0, 14),
            Padding = new Thickness(14, 12, 12, 14),
            CornerRadius = new CornerRadius(16),
            Background = Res("HudAccentSoft"),
            BorderBrush = Res("HudAccent"),
            BorderThickness = new Thickness(1),
            Child = sp,
        };
    }
    #endregion

    private void Fill()
    {
        cards.Children.Clear();
        Pet.Main.WorkList(out var ws, out var ss, out var ps);
        var list = category switch { Work.WorkType.Study => ss, Work.WorkType.Play => ps, _ => ws };
        if (list.Count == 0)
        {
            cards.Children.Add(new TextBlock { Text = "Aucune activité de ce type pour ce compagnon.", Style = (Style)FindResource("HudBodyText"), FontSize = 13, Foreground = Res("HudTextMuted"), Margin = new Thickness(0, 16, 0, 0) });
            return;
        }
        int level = Pet.Core.Save!.Level;
        foreach (var w in list.OrderBy(w => w.LevelLimit))
            cards.Children.Add(Card(Prepared(w), level));
    }

    /// <summary>Applique le multiplicateur choisi dans le planning et la correction d'équilibrage, comme VPet</summary>
    private Work Prepared(Work w)
    {
        int mult = Pet.Set["workmenu"].GetInt("double_" + w.Name, 1);
        var work = mult > 1 && w.LevelLimit <= Pet.Core.Save!.Level ? w.Double(mult) : (Work)w.Clone();
        if (!Pet.Set["gameconfig"].GetBool("noAutoCal") && work.IsOverLoad())
            work.FixOverLoad();
        return work;
    }

    private FrameworkElement Card(Work w, int level)
    {
        bool locked = Pet.Set.EnableFunction && w.LevelLimit > level;
        bool running = Pet.Main.State == Main.WorkingState.Work && Pet.Main.NowWork?.Name == w.Name;
        string icon = w.Type switch { Work.WorkType.Study => "", Work.WorkType.Play => "", _ => "" };

        var glyph = new Border
        {
            Width = 44, Height = 44, CornerRadius = new CornerRadius(14), Background = Res("HudSurfaceHover"), Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Top,
            Child = new TextBlock { Text = locked ? "" : icon, FontFamily = (FontFamily)FindResource("HudIcons"), FontSize = 18, Foreground = Res(locked ? "HudTextMuted" : "HudText"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        };
        var name = new TextBlock { Text = w.NameTrans, FontSize = 14.5, FontWeight = FontWeights.SemiBold, Foreground = Res(locked ? "HudTextMuted" : "HudText"), TextTrimming = TextTrimming.CharacterEllipsis };
        var gain = new TextBlock { Text = GainRate(w), FontFamily = (FontFamily)FindResource("HudDisplay"), FontSize = 13, Foreground = Res(locked ? "HudTextMuted" : "HudSuccess"), Margin = new Thickness(0, 2, 0, 0) };
        var costs = new TextBlock { Text = Costs(w), FontFamily = (FontFamily)FindResource("HudMono"), FontSize = 11, Foreground = Res("HudTextMuted"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) };
        var info = new StackPanel();
        info.Children.Add(name);
        info.Children.Add(gain);
        info.Children.Add(costs);

        FrameworkElement action;
        if (locked)
            action = new TextBlock { Text = $"Niv. {w.LevelLimit}", Style = (Style)FindResource("HudEyebrow"), VerticalAlignment = VerticalAlignment.Center };
        else
        {
            var b = new Button { Style = (Style)FindResource(running ? "HudGhostButton" : "HudPrimaryButton"), Content = running ? "Arrêter" : "Lancer", VerticalAlignment = VerticalAlignment.Center };
            b.Click += (_, _) => Start(w);
            action = b;
        }

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(glyph);
        Grid.SetColumn(info, 1);
        grid.Children.Add(info);
        Grid.SetColumn(action, 2);
        grid.Children.Add(action);
        action.Margin = new Thickness(10, 0, 0, 0);
        return new Border
        {
            Margin = new Thickness(0, 0, 0, 8),
            Padding = new Thickness(10),
            CornerRadius = new CornerRadius(16),
            BorderBrush = Res("HudStroke"),
            BorderThickness = new Thickness(1),
            Opacity = locked ? 0.7 : 1,
            Child = grid,
        };
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
            Fill();
        }
    }

    private static string GainRate(Work w)
    {
        double rate = w.Get();
        string unit = w.Type == Work.WorkType.Work ? "$" : "XP";
        string bonus = w.FinishBonus > 0 ? $" · bonus fin ×{(1 + w.FinishBonus).ToString("0.0", Fr)}" : "";
        return $"≈ {rate.ToString("N1", Fr)} {unit}/min · {Duration(w.Time)}{bonus}";
    }

    private static string Gain(Work w, double count) =>
        w.Type == Work.WorkType.Work ? count.ToString("N0", Fr) + " $" : count.ToString("N0", Fr) + " XP";

    private static string Duration(int minutes) =>
        minutes >= 60 ? $"{minutes / 60} h {(minutes % 60 == 0 ? "" : (minutes % 60).ToString("00"))}".TrimEnd() : $"{minutes} min";

    private static string Costs(Work w)
    {
        var parts = new List<string>();
        void Add(double v, string label)
        {
            if (Math.Abs(v) >= 0.05)
                parts.Add((v > 0 ? "−" : "+") + Math.Abs(v).ToString("0.#", Fr) + " " + label);
        }
        Add(w.StrengthFood, "satiété");
        Add(w.StrengthDrink, "soif");
        Add(w.Feeling, "humeur");
        return parts.Count == 0 ? "" : "par tick : " + string.Join(" · ", parts);
    }
}
