using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using VPet_Simulator.Core;
using VPet_Simulator.Windows.Interface;
using static VPet_Simulator.Core.GraphHelper;
using static VPet_Simulator.Windows.Interface.ScheduleTask;

namespace VPet_Simulator.Windows.HUD;

/// <summary>
/// V-Max : planning des occupations (remplace la fenêtre « 工作面板 » de VPet).
/// Onglets : Occupations (fiche, multiplicateur, favoris, ajout au planning), Planning (emploi du temps avec pauses,
/// ratio travail/repos, démarrage) et Contrats (agence d'emploi, centre de formation, renouvellement automatique).
/// Toute la logique reprend celle de VPet (ScheduleTask, Package, multiplicateur « double_ »).
/// </summary>
public sealed class PlanningWindow : HudWindow
{
    private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");
    private static PlanningWindow? instance;
    private const int ScheduleLevel = 15;

    private Work.WorkType category = Work.WorkType.Work;
    private bool favorites;
    private Work? selected;

    private PlanningWindow(MainWindow mw) : base(mw, "planning", "OCCUPATIONS", "Planning", 980, 700)
    {
        AddTab("Occupations", BuildWorks);
        AddTab("Planning", BuildSchedule);
        AddTab("Contrats", BuildContracts);
        Closed += (_, _) => instance = null;
    }

    /// <summary>Ouvre (ou ramène) le planning sur une catégorie d'occupations ; onglet 0 occupations, 1 planning, 2 contrats</summary>
    public static PlanningWindow Open(MainWindow mw, Work.WorkType? type = null, int tab = 0)
    {
        instance ??= new PlanningWindow(mw);
        if (type is { } t)
        {
            instance.category = t;
            instance.favorites = false;
            instance.selected = null;
            instance.RefreshTab(0);
        }
        instance.Present();
        instance.ShowTab(tab);
        return instance;
    }

    private int Level => MW.Core.Save!.Level;

    #region Occupations
    private FrameworkElement BuildWorks()
    {
        MW.Main.WorkList(out var ws, out var ss, out var ps);
        var all = ws.Concat(ss).Concat(ps).ToList();
        var shown = favorites ? MW.WorkStar() : category switch { Work.WorkType.Study => ss, Work.WorkType.Play => ps, _ => ws };
        selected ??= shown.FirstOrDefault(w => w.LevelLimit <= Level) ?? shown.FirstOrDefault();

        // colonne de gauche : catégories et liste
        var chips = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
        void Chip(string label, Work.WorkType? t, bool fav, bool any)
        {
            if (!any) return;
            var c = new RadioButton { Style = St("HudChip"), Content = label, GroupName = "plan-cat", Margin = new Thickness(0, 0, 6, 6), IsChecked = fav ? favorites : !favorites && t == category };
            c.Checked += (_, _) =>
            {
                if (fav == favorites && t == category) return;
                favorites = fav;
                if (t is { } tt) category = tt;
                selected = null;
                RefreshTab(0);
            };
            chips.Children.Add(c);
        }
        Chip("Travail", Work.WorkType.Work, false, ws.Count > 0);
        Chip("Études", Work.WorkType.Study, false, ss.Count > 0);
        Chip("Loisirs", Work.WorkType.Play, false, ps.Count > 0);
        Chip("Favoris", null, true, true);

        var list = new StackPanel();
        foreach (var w in shown.OrderBy(w => w.LevelLimit))
            list.Children.Add(WorkRow(w));
        if (shown.Count == 0)
            list.Children.Add(Empty(favorites ? "Aucun favori : ouvre une occupation et touche l'étoile." : "Aucune occupation de ce type."));
        var left = new DockPanel { Width = 330, Margin = new Thickness(24, 0, 12, 20) };
        DockPanel.SetDock(chips, Dock.Top);
        left.Children.Add(chips);
        left.Children.Add(Scroll(list, new Thickness(0, 0, 8, 0)));

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.Children.Add(left);
        var detail = selected != null ? WorkDetail(selected) : Empty("Choisis une occupation à gauche.");
        var right = Scroll(detail, new Thickness(12, 0, 24, 20));
        Grid.SetColumn(right, 1);
        grid.Children.Add(right);
        return grid;
    }

    private static string Glyph(Work.WorkType t) => t switch { Work.WorkType.Study => "", Work.WorkType.Play => "", _ => "" };

    private FrameworkElement WorkRow(Work w)
    {
        bool locked = MW.Set.EnableFunction && w.LevelLimit > Level;
        bool sel = selected?.Name == w.Name;
        var icon = new TextBlock { Text = locked ? "" : Glyph(w.Type), FontFamily = (FontFamily)FindResource("HudIcons"), FontSize = 15, Foreground = Res(locked ? "HudTextMuted" : "HudText"), VerticalAlignment = VerticalAlignment.Center, Width = 26 };
        var name = new TextBlock { Text = w.NameTrans, FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = Res(locked ? "HudTextMuted" : "HudText"), TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        var right = new TextBlock { Text = locked ? $"Niv. {w.LevelLimit}" : IsStar(w) ? "" : "", FontFamily = locked ? (FontFamily)FindResource("HudMono") : (FontFamily)FindResource("HudIcons"), FontSize = 11.5, Foreground = Res(locked ? "HudTextMuted" : "HudAmber"), VerticalAlignment = VerticalAlignment.Center };
        var dock = new DockPanel();
        DockPanel.SetDock(icon, Dock.Left);
        DockPanel.SetDock(right, Dock.Right);
        dock.Children.Add(icon);
        dock.Children.Add(right);
        dock.Children.Add(name);
        var row = new Border
        {
            Padding = new Thickness(12, 9, 12, 9), Margin = new Thickness(0, 0, 0, 4), CornerRadius = new CornerRadius(12),
            Background = sel ? Res("HudAccentSoft") : Brushes.Transparent, BorderBrush = sel ? Res("HudAccent") : Brushes.Transparent, BorderThickness = new Thickness(1),
            Child = dock, Cursor = System.Windows.Input.Cursors.Hand,
        };
        row.MouseEnter += (_, _) => { if (!sel) row.Background = Res("HudSurfaceHover"); };
        row.MouseLeave += (_, _) => { if (!sel) row.Background = Brushes.Transparent; };
        row.MouseLeftButtonUp += (_, _) => { selected = w; RefreshTab(0); };
        return row;
    }

    private int Multiplier(Work w) => MW.Set["workmenu"].GetInt("double_" + w.Name, 1);

    /// <summary>Multiplicateur maximal (règle de VPet) ; 1 = pas de multiplicateur</summary>
    private int MaxMultiplier(Work w) => w.LevelLimit > Level ? 1 : Math.Max(1, Math.Min(4000, Level) / (w.LevelLimit + 10));

    private Work Displayed(Work w)
    {
        int m = Math.Min(Multiplier(w), MaxMultiplier(w));
        var d = m > 1 ? w.Double(m) : (Work)w.Clone();
        if (!MW.Set["gameconfig"].GetBool("noAutoCal") && d.IsOverLoad())
            d.FixOverLoad();
        return d;
    }

    private bool IsStar(Work w) => MW.Set["work_star"].GetBool(w.Name);

    private FrameworkElement WorkDetail(Work w)
    {
        var d = Displayed(w);
        bool locked = MW.Set.EnableFunction && w.LevelLimit > Level;
        var sp = new StackPanel();

        // en-tête : image et nom
        var source = MW.ImageSources.FindSource("work_" + MW.Set.PetGraph + "_" + w.Graph) ?? MW.ImageSources.FindSource("work_" + MW.Set.PetGraph + "_" + w.Name);
        ImageSource? img = null;
        try
        {
            img = source != null ? ImageResources.NewSafeBitmapImage(source) : MW.ImageSources.FindImage("work_" + MW.Set.PetGraph + "_t_" + w.Type, "work_" + w.Type);
        }
        catch { }
        var picture = new Border
        {
            Width = 180, Height = 180, CornerRadius = new CornerRadius(18), Background = Res("HudSurfaceHover"), Margin = new Thickness(0, 0, 20, 0), ClipToBounds = true,
            Child = img != null ? new Image { Source = img, Stretch = Stretch.Uniform, Margin = new Thickness(8) } : new TextBlock { Text = Glyph(w.Type), FontFamily = (FontFamily)FindResource("HudIcons"), FontSize = 48, Foreground = Res("HudTextMuted"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        };
        var title = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        title.Children.Add(new TextBlock { Text = w.Type switch { Work.WorkType.Study => "ÉTUDES", Work.WorkType.Play => "LOISIRS", _ => "TRAVAIL" }, Style = St("HudEyebrow") });
        title.Children.Add(new TextBlock { Text = w.NameTrans, Style = St("HudTitle"), FontSize = 24, TextWrapping = TextWrapping.Wrap });
        var star = new Button { Style = St("HudGhostButton"), Content = IsStar(w) ? "  Favori" : "  Ajouter aux favoris", Margin = new Thickness(0, 10, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, FontFamily = (FontFamily)FindResource("HudBody") };
        star.Click += (_, _) =>
        {
            MW.Set["work_star"].SetBool(w.Name, !IsStar(w));
            RefreshStarMenus();
            RefreshTab(0);
        };
        title.Children.Add(star);
        var head = new DockPanel();
        DockPanel.SetDock(picture, Dock.Left);
        head.Children.Add(picture);
        head.Children.Add(title);
        sp.Children.Add(head);

        // chiffres
        sp.Children.Add(Section("Ce que ça donne"));
        var stats = new UniformGrid { Columns = 3 };
        string unit = w.Type == Work.WorkType.Work ? "$/min" : "XP/min";
        stats.Children.Add(Stat("Gain", d.Get().ToString("N1", Fr) + " " + unit, "HudSuccess"));
        stats.Children.Add(Stat("Durée", d.Time > 100 ? (d.Time / 60.0).ToString("0.#", Fr) + " h" : d.Time + " min"));
        stats.Children.Add(Stat("Bonus de fin", "×" + (1 + d.FinishBonus).ToString("0.00", Fr)));
        stats.Children.Add(Stat("Satiété", "−" + d.StrengthFood.ToString("0.##", Fr) + "/tick"));
        stats.Children.Add(Stat("Soif", "−" + d.StrengthDrink.ToString("0.##", Fr) + "/tick"));
        stats.Children.Add(Stat("Humeur", (d.Feeling > 0 ? "−" : "+") + Math.Abs(d.Feeling).ToString("0.##", Fr) + "/tick"));
        stats.Children.Add(Stat("Niveau requis", d.LevelLimit.ToString(Fr), locked ? "HudAmber" : "HudText"));
        sp.Children.Add(stats);

        // multiplicateur
        int max = MaxMultiplier(w);
        if (max > 1)
        {
            sp.Children.Add(Section("Multiplicateur"));
            var value = new TextBlock { FontFamily = (FontFamily)FindResource("HudDisplay"), FontWeight = FontWeights.SemiBold, FontSize = 18, Width = 70, VerticalAlignment = VerticalAlignment.Center };
            var slider = new Slider { Minimum = 1, Maximum = max, Value = Math.Clamp(Multiplier(w), 1, max), IsSnapToTickEnabled = true, TickFrequency = max > 25 ? max / 25 : 1, VerticalAlignment = VerticalAlignment.Center };
            value.Text = "×" + (int)slider.Value;
            slider.ValueChanged += (_, e) => value.Text = "×" + (int)e.NewValue;
            slider.PreviewMouseUp += (_, _) => { MW.Set["workmenu"].SetInt("double_" + w.Name, (int)slider.Value); RefreshTab(0); };
            var row = new DockPanel();
            DockPanel.SetDock(value, Dock.Left);
            row.Children.Add(value);
            row.Children.Add(slider);
            sp.Children.Add(row);
            sp.Children.Add(new TextBlock { Text = "Plus de gains, mais plus de besoins et un niveau requis plus élevé.", FontSize = 12, Foreground = Res("HudTextMuted"), Margin = new Thickness(0, 4, 0, 0) });
        }

        // actions
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 22, 0, 0) };
        var start = Primary(w.Type switch { Work.WorkType.Study => "Commencer à étudier", Work.WorkType.Play => "Commencer à jouer", _ => "Commencer à travailler" }, () =>
        {
            if (MW.Main.StartWork(Displayed(w)))
                FadeClose();
        });
        start.IsEnabled = !locked;
        actions.Children.Add(start);
        var add = Ghost("Ajouter au planning", () => AddToSchedule(w));
        add.Margin = new Thickness(10, 0, 0, 0);
        add.IsEnabled = !locked;
        actions.Children.Add(add);
        sp.Children.Add(actions);
        if (locked)
            sp.Children.Add(new TextBlock { Text = $"Disponible au niveau {w.LevelLimit} (tu es niveau {Level}).", Foreground = Res("HudAmber"), FontSize = 12.5, Margin = new Thickness(0, 8, 0, 0) });
        return sp;
    }

    private FrameworkElement Stat(string label, string value, string brush = "HudText")
    {
        var s = new StackPanel { Margin = new Thickness(0, 0, 10, 14) };
        s.Children.Add(new TextBlock { Text = value, FontFamily = (FontFamily)FindResource("HudDisplay"), FontWeight = FontWeights.SemiBold, FontSize = 18, Foreground = Res(brush) });
        s.Children.Add(new TextBlock { Text = label, FontSize = 12, Foreground = Res("HudTextMuted") });
        return s;
    }

    private void AddToSchedule(Work w)
    {
        var st = MW.ScheduleTask;
        var d = Displayed(w);
        int mult = Math.Min(Multiplier(w), MaxMultiplier(w));
        switch (w.Type)
        {
            case Work.WorkType.Work:
                if (st.PackageWork?.IsActive() != true)
                {
                    Notify("Signe d'abord un contrat avec une agence d'emploi (onglet Contrats).", HudToast.Kind.Warning);
                    return;
                }
                if (st.PackageWork.Level < d.LevelLimit)
                {
                    Notify($"Ton contrat d'agence couvre le niveau {st.PackageWork.Level}, cette occupation demande {d.LevelLimit}.", HudToast.Kind.Warning);
                    return;
                }
                st.AddWork(w, mult);
                break;
            case Work.WorkType.Study:
                if (st.PackageStudy?.IsActive() != true)
                {
                    Notify("Signe d'abord un contrat avec un centre de formation (onglet Contrats).", HudToast.Kind.Warning);
                    return;
                }
                if (st.PackageStudy.Level < d.LevelLimit)
                {
                    Notify($"Ton contrat de formation couvre le niveau {st.PackageStudy.Level}, cette étude demande {d.LevelLimit}.", HudToast.Kind.Warning);
                    return;
                }
                st.AddStudy(w, mult);
                break;
            default:
                if (Level < ScheduleLevel)
                {
                    Notify($"Le planning s'ouvre au niveau {ScheduleLevel}.", HudToast.Kind.Warning);
                    return;
                }
                st.AddPlay(w, mult);
                break;
        }
        Notify($"« {w.NameTrans} » ajouté au planning.", HudToast.Kind.Success);
        RefreshTab(1);
    }

    /// <summary>Favoris : menus de la barre de VPet (repris par le panneau « Plus » et les plugins), comme l'ancien planning</summary>
    private void RefreshStarMenus()
    {
        var tb = MW.Main.ToolBar;
        if (tb == null || MW.WorkStarMenu == null)
            return;
        MW.WorkStarMenu.Items.Clear();
        tb.MenuStudy.Items.Clear();
        tb.MenuWork.Items.Clear();
        tb.MenuPlay.Items.Clear();
        foreach (var v in MW.WorkStar())
        {
            var work = v;
            MenuItem Item()
            {
                var mi = new MenuItem { Header = work.NameTrans };
                mi.Click += (_, _) => tb.StartWork(work.Double(MW.Set["workmenu"].GetInt("double_" + work.Name, 1)));
                return mi;
            }
            MW.WorkStarMenu.Items.Add(Item());
            (work.Type switch { Work.WorkType.Study => tb.MenuStudy, Work.WorkType.Play => tb.MenuPlay, _ => tb.MenuWork }).Items.Add(Item());
        }
    }
    #endregion

    #region Planning
    private FrameworkElement BuildSchedule()
    {
        if (Level < ScheduleLevel)
            return Empty($"Le planning s'ouvre au niveau {ScheduleLevel} : ton compagnon enchaînera alors seul occupations et pauses.");
        var st = MW.ScheduleTask;
        var items = st.ScheduleItems;
        bool running = st.IsOn;

        var list = new StackPanel();
        if (items.Count == 0)
            list.Children.Add(Empty("Le planning est vide : ajoute des occupations depuis l'onglet Occupations, puis des pauses."));
        for (int i = 0; i < items.Count; i++)
        {
            var item = items[i];
            if (item is WorkScheduleItem && (i == 0 || items[i - 1] is not RestScheduleItem) && !running)
                list.Children.Add(InsertRestButton(i));
            list.Children.Add(ScheduleRow(item, i, running));
        }
        var addRest = Ghost("+ Ajouter une pause à la fin", () =>
        {
            if (items.LastOrDefault() is RestScheduleItem last) last.RestTime += 30;
            else st.AddRest(30);
            RefreshTab(1);
        });
        addRest.HorizontalAlignment = HorizontalAlignment.Left;
        addRest.Margin = new Thickness(0, 8, 0, 0);
        addRest.IsEnabled = !running;
        list.Children.Add(addRest);

        // bilan
        int work = items.Sum(x => x.WorkTime), rest = items.Sum(x => x.RestTime);
        double ratio = work + rest == 0 ? 0 : work / (double)(work + rest);
        bool tooMuch = ratio > 0.71;
        var gauge = new ArcGauge { Width = 120, Height = 120, Thickness = 8, Track = Res("HudStroke"), Fill = Res(tooMuch ? "HudAmber" : "HudAccent"), ReserveFill = Brushes.Transparent, Value = ratio };
        var pct = new TextBlock { Text = ratio.ToString("P0", Fr), FontFamily = (FontFamily)FindResource("HudDisplay"), FontWeight = FontWeights.SemiBold, FontSize = 24, Foreground = Res(tooMuch ? "HudAmber" : "HudText"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var dial = new Grid { Width = 120, Height = 120, HorizontalAlignment = HorizontalAlignment.Center };
        dial.Children.Add(gauge);
        dial.Children.Add(pct);
        var summary = new StackPanel();
        summary.Children.Add(new TextBlock { Text = "TEMPS DE TRAVAIL", Style = St("HudEyebrow"), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 10) });
        summary.Children.Add(dial);
        summary.Children.Add(new TextBlock { Text = $"{work} min de travail · {rest} min de pause", FontSize = 12.5, Foreground = Res("HudTextMuted"), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 10, 0, 0) });
        if (tooMuch)
            summary.Children.Add(new TextBlock { Text = "Trop de travail : ajoute des pauses (71 % maximum).", FontSize = 12.5, Foreground = Res("HudAmber"), TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 6, 0, 0) });
        var toggle = running
            ? Ghost("Arrêter le planning", () => { st.Stop(); RefreshTab(1); })
            : Primary("Démarrer le planning", () =>
            {
                if (tooMuch)
                {
                    Notify("Trop de travail : ajoute des pauses avant de démarrer.", HudToast.Kind.Warning);
                    return;
                }
                st.Start();
                RefreshTab(1);
            });
        toggle.Margin = new Thickness(0, 16, 0, 0);
        toggle.HorizontalAlignment = HorizontalAlignment.Center;
        toggle.IsEnabled = running || items.Count > 0;
        summary.Children.Add(toggle);
        summary.Children.Add(new TextBlock
        {
            Text = "Les occupations du planning utilisent tes contrats : sans agence ou centre de formation actif, elles sont ignorées.",
            FontSize = 12, Foreground = Res("HudTextMuted"), TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 14, 0, 0),
        });

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(280) });
        var scroll = Scroll(list, new Thickness(24, 0, 12, 20));
        grid.Children.Add(scroll);
        var right = Card(summary, new Thickness(20));
        right.Margin = new Thickness(12, 0, 24, 20);
        right.VerticalAlignment = VerticalAlignment.Top;
        Grid.SetColumn(right, 1);
        grid.Children.Add(right);
        return grid;
    }

    private FrameworkElement InsertRestButton(int index)
    {
        var b = new Button { Style = St("HudGhostButton"), Content = "+ pause", FontSize = 11.5, Padding = new Thickness(10, 2, 10, 3), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(40, 0, 0, 4), Opacity = 0.75 };
        b.Click += (_, _) =>
        {
            MW.ScheduleTask.ScheduleItems.Insert(index, new RestScheduleItem(MW.ScheduleTask, 30));
            RefreshTab(1);
        };
        return b;
    }

    private FrameworkElement ScheduleRow(ScheduleItemBase item, int index, bool running)
    {
        var items = MW.ScheduleTask.ScheduleItems;
        var dock = new DockPanel();
        bool now = item.IsNow;
        string glyph = item switch { RestScheduleItem => "", StudyScheduleItem => "", PlayScheduleItem => "", _ => "" };
        dock.Children.Add(new TextBlock { Text = glyph, FontFamily = (FontFamily)FindResource("HudIcons"), FontSize = 16, Foreground = Res(now ? "HudAccent" : "HudText"), Width = 30, VerticalAlignment = VerticalAlignment.Center });
        DockPanel.SetDock(dock.Children[0], Dock.Left);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        if (!running)
        {
            buttons.Children.Add(IconButton("", "Monter", () => Move(item, -1)));
            buttons.Children.Add(IconButton("", "Descendre", () => Move(item, +1)));
            buttons.Children.Add(IconButton("", "Retirer", () => Remove(item)));
        }
        DockPanel.SetDock(buttons, Dock.Right);
        dock.Children.Add(buttons);

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        if (item is RestScheduleItem rest)
        {
            var line = new StackPanel { Orientation = Orientation.Horizontal };
            line.Children.Add(new TextBlock { Text = "Pause", FontSize = 14, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) });
            var minus = IconButton("", "−10 min", () => { rest.RestTime = Math.Max(10, rest.RestTime - 10); RefreshTab(1); });
            var plus = IconButton("", "+10 min", () => { rest.RestTime += 10; RefreshTab(1); });
            minus.IsEnabled = plus.IsEnabled = !running;
            line.Children.Add(minus);
            line.Children.Add(new TextBlock { Text = rest.RestTime + " min", FontFamily = (FontFamily)FindResource("HudDisplay"), FontSize = 15, Width = 64, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
            line.Children.Add(plus);
            text.Children.Add(line);
        }
        else if (item is WorkScheduleItem w)
        {
            text.Children.Add(new TextBlock { Text = w.WorkName, FontSize = 14, FontWeight = FontWeights.SemiBold });
            string time = item is PlayScheduleItem ? $"{w.WorkTime} min de jeu + {w.RestTime} min de repos" : $"{w.WorkTime} min";
            text.Children.Add(new TextBlock { Text = $"{w.WorkLevel} · {time}", FontSize = 12, Foreground = Res("HudTextMuted"), FontFamily = (FontFamily)FindResource("HudMono") });
            bool notOk = item switch
            {
                StudyScheduleItem s => s.IsOKVisibility == Visibility.Visible,
                PlayScheduleItem => false,
                _ => w.IsOKVisibility == Visibility.Visible,
            };
            if (notOk)
                text.Children.Add(new TextBlock { Text = "Contrat absent ou de niveau insuffisant : cette étape sera sautée.", FontSize = 12, Foreground = Res("HudAmber"), Margin = new Thickness(0, 2, 0, 0) });
        }
        dock.Children.Add(text);
        return new Border
        {
            Padding = new Thickness(12, 10, 8, 10), Margin = new Thickness(0, 0, 0, 6), CornerRadius = new CornerRadius(14),
            Background = Res(now ? "HudAccentSoft" : "HudSurfaceRaised"), BorderBrush = Res(now ? "HudAccent" : "HudStroke"), BorderThickness = new Thickness(1),
            Child = dock,
        };
    }

    /// <summary>Déplacement d'une étape (les pauses voisines fusionnent, comme dans VPet)</summary>
    private void Move(ScheduleItemBase item, int delta)
    {
        var items = MW.ScheduleTask.ScheduleItems;
        int index = items.IndexOf(item);
        int target = index + delta;
        if (target < 0 || target >= items.Count)
            return;
        MergeAround(index);
        items.Remove(item);
        items.Insert(Math.Clamp(target, 0, items.Count), item);
        RefreshTab(1);
    }

    private void Remove(ScheduleItemBase item)
    {
        var items = MW.ScheduleTask.ScheduleItems;
        MergeAround(items.IndexOf(item));
        items.Remove(item);
        RefreshTab(1);
    }

    private void MergeAround(int index)
    {
        var items = MW.ScheduleTask.ScheduleItems;
        if (index > 0 && index < items.Count - 1 && items[index - 1] is RestScheduleItem before && items[index + 1] is RestScheduleItem after)
        {
            before.RestTime += after.RestTime;
            items.Remove(after);
        }
    }
    #endregion

    #region Contrats
    private Work.WorkType agency = Work.WorkType.Work;
    private PackageFull? offer;
    private int contractLevel = ScheduleLevel;

    private FrameworkElement BuildContracts()
    {
        if (Level < ScheduleLevel)
            return Empty($"Les contrats s'ouvrent au niveau {ScheduleLevel}.");
        var st = MW.ScheduleTask;
        var chips = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) };
        foreach (var (t, label) in new[] { (Work.WorkType.Work, "Agence d'emploi"), (Work.WorkType.Study, "Centre de formation") })
        {
            var c = new RadioButton { Style = St("HudChip"), Content = label, GroupName = "plan-agency", IsChecked = agency == t, Margin = new Thickness(0, 0, 6, 6) };
            c.Checked += (_, _) => { if (agency != t) { agency = t; offer = null; RefreshTab(2); } };
            chips.Children.Add(c);
        }

        // contrat en cours
        var current = agency == Work.WorkType.Work ? st.PackageWork : st.PackageStudy;
        var cur = new StackPanel();
        cur.Children.Add(new TextBlock { Text = "CONTRAT EN COURS", Style = St("HudEyebrow") });
        if (current == null)
            cur.Children.Add(new TextBlock { Text = "Aucun contrat signé.", Style = St("HudBodyText"), Foreground = Res("HudTextMuted"), Margin = new Thickness(0, 6, 0, 0) });
        else
        {
            double hours = (current.EndTime - DateTime.Now).TotalHours;
            cur.Children.Add(new TextBlock { Text = current.NameTrans, Style = St("HudTitle"), FontSize = 18, Margin = new Thickness(0, 4, 0, 2) });
            cur.Children.Add(new TextBlock
            {
                Text = $"{(agency == Work.WorkType.Work ? "Commission" : "Efficacité")} {Rate(current.Commissions):P0} · niveau {current.Level} · "
                    + (hours <= 0 ? "expiré" : $"encore {(hours <= 24 ? hours.ToString("0.#", Fr) + " h" : (hours / 24).ToString("0", Fr) + " jours")} (jusqu'au {current.EndTime:dd/MM})"),
                FontSize = 12.5, Foreground = Res(hours <= 0 ? "HudAmber" : "HudTextMuted"), TextWrapping = TextWrapping.Wrap,
            });
            var renew = new CheckBox { Content = "Renouveler automatiquement", IsChecked = current.AutoRenew, Foreground = Res("HudText"), Margin = new Thickness(0, 10, 0, 0) };
            renew.Checked += (_, _) => { current.AutoRenew = true; st.AutoRenew(); RefreshTab(2); };
            renew.Unchecked += (_, _) => { current.AutoRenew = false; };
            cur.Children.Add(renew);
        }

        // offres
        var offers = MW.SchedulePackage.Where(p => p.WorkType == agency).ToList();
        offer ??= offers.FirstOrDefault();
        var offerList = new StackPanel();
        foreach (var p in offers)
        {
            var o = p;
            bool sel = offer == o;
            var line = new StackPanel();
            line.Children.Add(new TextBlock { Text = o.NameTrans, FontSize = 14, FontWeight = FontWeights.SemiBold });
            line.Children.Add(new TextBlock { Text = $"{(agency == Work.WorkType.Work ? "Commission" : "Efficacité")} {Rate(o.Commissions):P0} · {o.Duration} jours", FontSize = 12, Foreground = Res("HudTextMuted") });
            var b = new Border
            {
                Padding = new Thickness(12, 9, 12, 9), Margin = new Thickness(0, 0, 0, 4), CornerRadius = new CornerRadius(12), Cursor = System.Windows.Input.Cursors.Hand,
                Background = sel ? Res("HudAccentSoft") : Brushes.Transparent, BorderBrush = sel ? Res("HudAccent") : Res("HudStroke"), BorderThickness = new Thickness(1), Child = line,
            };
            b.MouseLeftButtonUp += (_, _) => { offer = o; RefreshTab(2); };
            offerList.Children.Add(b);
        }
        if (offers.Count == 0)
            offerList.Children.Add(Empty("Aucune offre disponible."));

        var left = new StackPanel();
        left.Children.Add(chips);
        left.Children.Add(Card(cur));
        left.Children.Add(Section("Offres"));
        left.Children.Add(offerList);

        // aperçu et signature
        var right = new StackPanel();
        if (offer != null)
        {
            int maxLevel = Math.Max(ScheduleLevel, Level / 5 * 5);
            contractLevel = Math.Clamp(contractLevel, ScheduleLevel, maxLevel);
            var levelText = new TextBlock { FontFamily = (FontFamily)FindResource("HudDisplay"), FontWeight = FontWeights.SemiBold, FontSize = 18, Width = 60 };
            var slider = new Slider { Minimum = ScheduleLevel, Maximum = maxLevel, Value = contractLevel, IsSnapToTickEnabled = true, TickFrequency = Level > 200 ? Level / 100 * 5 : 5, VerticalAlignment = VerticalAlignment.Center };
            var preview = new TextBlock { FontSize = 13, Foreground = Res("HudTextMuted"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0) };
            void Update()
            {
                contractLevel = (int)slider.Value;
                levelText.Text = contractLevel.ToString(Fr);
                double price = (200 * contractLevel - 100) * offer.Price;
                preview.Text = $"Couvre les occupations jusqu'au niveau {(int)(contractLevel / offer.LevelInNeed)} · {offer.Duration} jours · {price.ToString("N0", Fr)} $\n\n{offer.DescribeTrans}";
            }
            slider.ValueChanged += (_, _) => Update();
            Update();
            right.Children.Add(new TextBlock { Text = offer.NameTrans, Style = St("HudTitle"), FontSize = 20 });
            right.Children.Add(Section("Niveau du contrat"));
            var row = new DockPanel();
            DockPanel.SetDock(levelText, Dock.Left);
            row.Children.Add(levelText);
            row.Children.Add(slider);
            right.Children.Add(row);
            right.Children.Add(preview);
            var sign = Primary("Signer le contrat", () => Sign(offer, contractLevel));
            sign.Margin = new Thickness(0, 18, 0, 0);
            sign.HorizontalAlignment = HorizontalAlignment.Left;
            right.Children.Add(sign);
        }

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.Children.Add(Scroll(left, new Thickness(24, 0, 12, 20)));
        var rightCard = Card(right, new Thickness(20));
        rightCard.Margin = new Thickness(12, 0, 24, 20);
        rightCard.VerticalAlignment = VerticalAlignment.Top;
        Grid.SetColumn(rightCard, 1);
        grid.Children.Add(rightCard);
        return grid;
    }

    /// <summary>Commission (agence d'emploi) ou efficacité (centre de formation) : VPet stocke l'une ou son complément</summary>
    private double Rate(double commissions) => agency == Work.WorkType.Work ? commissions : 1 - commissions;

    /// <summary>Signature, avec remboursement au prorata d'un contrat actif remplacé (règle de VPet)</summary>
    private void Sign(PackageFull full, int level)
    {
        var st = MW.ScheduleTask;
        var package = new Package(full, level);
        if (package.Price > MW.Core.Save!.Money)
        {
            Notify($"Il te manque {(package.Price - MW.Core.Save.Money).ToString("N0", Fr)} $ pour ce contrat.", HudToast.Kind.Warning);
            return;
        }
        var current = full.WorkType == Work.WorkType.Work ? st.PackageWork : st.PackageStudy;
        double refund = 0;
        if (current?.IsActive() == true)
        {
            if (VDialog.Show($"Un contrat est déjà actif ({current.NameTrans}). Le remplacer ? La partie non utilisée est remboursée en partie.",
                    "Remplacer le contrat", MessageBoxButton.YesNo, Panuon.WPF.UI.MessageBoxIcon.Question) != MessageBoxResult.Yes)
                return;
            double left = (current.EndTime - DateTime.Now).TotalDays / 2;
            if (left > 0.5)
            {
                var pw = MW.SchedulePackage.Find(x => x.WorkType == full.WorkType && x.Name == current.Name);
                if (pw != null)
                {
                    var p = new Package(pw, current.Level);
                    refund = p.Price * (pw.Duration - left) / pw.Duration;
                    if (refund < 0 || refund > p.Price)
                        refund = 0;
                }
            }
        }
        if (full.WorkType == Work.WorkType.Work)
            st.PackageWork = package;
        else
            st.PackageStudy = package;
        MW.Core.Save.Money -= package.Price - refund;
        Notify($"Contrat « {package.NameTrans} » signé" + (refund > 0 ? $", {refund.ToString("N1", Fr)} $ remboursés." : "."), HudToast.Kind.Success);
        RefreshTab(2);
    }
    #endregion
}
