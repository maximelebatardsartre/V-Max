using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using VPet_Simulator.Windows.Habitat;

namespace VPet_Simulator.Windows;

/// <summary>
/// V-Max : éditeur des routines de vie (paramètres). Une carte par routine : action, lieu, plage de départ,
/// jours, durée, et l'heure tirée pour la prochaine fois. Chaque modification est enregistrée aussitôt.
/// </summary>
internal sealed class RoutineEditor
{
    private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");
    private static readonly string[] DayLetters = ["D", "L", "M", "M", "J", "V", "S"]; // index = DayOfWeek
    private static readonly int[] DayOrder = [1, 2, 3, 4, 5, 6, 0];                      // lundi d'abord

    private readonly MainWindow mw;
    private readonly FrameworkElement res;
    private readonly StackPanel list = new();
    private readonly List<LifeRoutine> routines;

    public RoutineEditor(MainWindow mw, FrameworkElement resources)
    {
        this.mw = mw;
        res = resources;
        routines = mw.Life?.Book.Routines.Select(r => r.Clone()).ToList() ?? new();
    }

    private LifeBrain? Life => mw.Life;
    private Brush B(string key) => (Brush)res.FindResource(key);
    private Style S(string key) => (Style)res.FindResource(key);

    public FrameworkElement Build()
    {
        var root = new StackPanel();
        root.Children.Add(list);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
        var add = new Button { Style = S("VMaxAccentButton"), Content = "Ajouter une routine", MinWidth = 150 };
        add.Click += (_, _) =>
        {
            routines.Add(new LifeRoutine { Action = "relax", Place = Places().FirstOrDefault(), From = "15:00", To = "17:00", MinMinutes = 15, MaxMinutes = 30 });
            Save();
            Refresh();
        };
        var preset = new Button { Style = S("VMaxButton"), Content = "Repartir de la journée type", Margin = new Thickness(8, 0, 0, 0), MinWidth = 150 };
        preset.Click += (_, _) =>
        {
            routines.Clear();
            routines.AddRange(RoutineBook.Default().Routines);
            Save();
            Refresh();
        };
        actions.Children.Add(add);
        actions.Children.Add(preset);
        root.Children.Add(actions);
        Refresh();
        return root;
    }

    private void Save() => Life?.ReplaceRoutines(routines);

    private void Refresh()
    {
        list.Children.Clear();
        if (routines.Count == 0)
            list.Children.Add(new TextBlock { Text = "Aucune routine. Ajoute-en une, ou repars de la journée type.", Foreground = B("VMaxSubtleText"), Margin = new Thickness(0, 0, 0, 10) });
        foreach (var r in routines)
            list.Children.Add(Card(r));
    }

    private FrameworkElement Card(LifeRoutine r)
    {
        var status = new TextBlock { FontSize = 12, Foreground = B("VMaxSubtleText") };
        void UpdateStatus() => status.Text = Status(r);
        void Changed()
        {
            Save();
            UpdateStatus();
        }

        // ligne 1 : activée, action, lieu, lancer, supprimer
        var enabled = new CheckBox { Style = S("VMaxToggle"), IsChecked = r.Enabled, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0), ToolTip = "Activer cette routine" };
        enabled.Checked += (_, _) => { r.Enabled = true; Changed(); };
        enabled.Unchecked += (_, _) => { r.Enabled = false; Changed(); };

        var actionsList = Actions();
        var action = new ComboBox { Width = 190, VerticalAlignment = VerticalAlignment.Center };
        if (res.TryFindResource("VMaxComboBox") is Style cs)
            action.Style = cs;
        foreach (var (_, label) in actionsList)
            action.Items.Add(label);
        action.SelectedIndex = Math.Max(0, Array.FindIndex(actionsList, a => a.value == r.Action));

        var place = new ComboBox { Width = 170, IsEditable = true, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0), ToolTip = "Pièce de l'habitat (vide : là où il se trouve)" };
        if (res.TryFindResource("VMaxComboBox") is Style ps)
            place.Style = ps;
        foreach (var p in Places().Append(r.Place ?? "").Where(p => !string.IsNullOrWhiteSpace(p)).Distinct())
            place.Items.Add(p);
        place.Text = r.Place ?? "";
        void SetPlace()
        {
            var v = (place.SelectedItem as string ?? place.Text ?? "").Trim();
            if (v == (r.Place ?? ""))
                return;
            r.Place = v.Length == 0 ? null : v;
            Changed();
        }
        place.SelectionChanged += (_, _) => SetPlace();
        place.LostFocus += (_, _) => SetPlace();

        var run = new Button { Style = S("VMaxButton"), Content = "Essayer", MinWidth = 90, Margin = new Thickness(8, 0, 0, 0), ToolTip = "Lancer cette routine tout de suite (ne compte pas pour aujourd'hui)" };
        run.Click += async (_, _) =>
        {
            if (Life == null)
                return;
            var duration = TimeSpan.FromMinutes((r.MinMinutes + r.MaxMinutes) / 2.0);
            var msg = await Life.StartAsync(r.Place, r.Action, duration, "user");
            mw.Toast(msg, HUD.HudToast.Kind.Info);
        };
        var delete = new Button { Style = S("VMaxButton"), Content = "Supprimer", MinWidth = 90, Margin = new Thickness(8, 0, 0, 0) };
        delete.Click += (_, _) =>
        {
            routines.Remove(r);
            Save();
            Refresh();
        };

        var line1 = new WrapPanel();
        line1.Children.Add(enabled);
        line1.Children.Add(action);
        line1.Children.Add(place);

        // ligne 2 : plage de départ, jours, durée
        var from = TimeBox(r.From, v => { r.From = v; Changed(); });
        var to = TimeBox(r.To, v => { r.To = v; Changed(); });
        var line2 = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
        line2.Children.Add(Label("Départ entre"));
        line2.Children.Add(from);
        line2.Children.Add(Label("et"));
        line2.Children.Add(to);
        line2.Children.Add(new Border { Width = 18 });
        foreach (var d in DayOrder)
            line2.Children.Add(DayChip(r, d, Changed));

        var durationPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(18, 0, 0, 0) };
        var min = NumberBox(r.MinMinutes, v => { r.MinMinutes = v; Changed(); });
        var max = NumberBox(r.MaxMinutes, v => { r.MaxMinutes = v; Changed(); });
        durationPanel.Children.Add(Label("Durée"));
        durationPanel.Children.Add(min);
        durationPanel.Children.Add(Label("à"));
        durationPanel.Children.Add(max);
        durationPanel.Children.Add(Label("min"));
        line2.Children.Add(durationPanel);
        void UpdateDurationVisibility() => durationPanel.Visibility = r.Action is "eat" or "drink" ? Visibility.Collapsed : Visibility.Visible;
        UpdateDurationVisibility();

        action.SelectionChanged += (_, _) =>
        {
            if (action.SelectedIndex < 0)
                return;
            var v = actionsList[action.SelectedIndex].value;
            if (v == r.Action)
                return;
            r.Action = v;
            // durées par défaut raisonnables quand on change d'action
            if (v == "sleep" && r.MaxMinutes < 60) { r.MinMinutes = 420; r.MaxMinutes = 510; }
            if (v == "relax" && r.MaxMinutes == 0) { r.MinMinutes = 15; r.MaxMinutes = 30; }
            ((TextBox)min).Text = r.MinMinutes.ToString(Fr);
            ((TextBox)max).Text = r.MaxMinutes.ToString(Fr);
            UpdateDurationVisibility();
            Changed();
        };

        var line3 = new DockPanel { Margin = new Thickness(0, 6, 0, 0) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Bottom };
        buttons.Children.Add(run);
        buttons.Children.Add(delete);
        DockPanel.SetDock(buttons, Dock.Right);
        line3.Children.Add(buttons);
        status.VerticalAlignment = VerticalAlignment.Center;
        status.TextWrapping = TextWrapping.Wrap;
        line3.Children.Add(status);
        var sp = new StackPanel();
        sp.Children.Add(line1);
        sp.Children.Add(line2);
        sp.Children.Add(line3);
        UpdateStatus();
        return new Border
        {
            Background = B("VMaxCard"),
            BorderBrush = B("VMaxStroke"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14, 12, 14, 12),
            Margin = new Thickness(0, 0, 0, 8),
            Child = sp,
        };
    }

    #region Champs
    private TextBlock Label(string text) => new() { Text = text, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0), Foreground = B("VMaxSubtleText") };

    private FrameworkElement TimeBox(string value, Action<string> set)
    {
        var tb = new TextBox { Style = S("VMaxTextBox"), Width = 70, Text = value, Margin = new Thickness(0, 0, 8, 0), Tag = "HH:mm", ToolTip = "Heure au format 24 h, ex. 12:30" };
        void Commit()
        {
            var t = tb.Text.Trim().Replace('h', ':').Replace('H', ':');
            if (t.EndsWith(':')) t += "00";
            if (TimeSpan.TryParse(t, CultureInfo.InvariantCulture, out var ts) && ts >= TimeSpan.Zero && ts < TimeSpan.FromDays(1))
            {
                var v = $"{(int)ts.TotalHours:00}:{ts.Minutes:00}";
                tb.Text = v;
                tb.ClearValue(Control.BorderBrushProperty);
                if (v != value)
                {
                    value = v;
                    set(v);
                }
            }
            else
                tb.BorderBrush = B("HudAmber");
        }
        tb.LostFocus += (_, _) => Commit();
        tb.KeyDown += (_, e) => { if (e.Key == Key.Enter) Commit(); };
        return tb;
    }

    private FrameworkElement NumberBox(int value, Action<int> set)
    {
        var tb = new TextBox { Style = S("VMaxTextBox"), Width = 60, Text = value.ToString(Fr), Margin = new Thickness(0, 0, 8, 0) };
        void Commit()
        {
            if (int.TryParse(tb.Text.Trim(), out var v) && v >= 0 && v <= 24 * 60)
            {
                tb.ClearValue(Control.BorderBrushProperty);
                if (v != value)
                {
                    value = v;
                    set(v);
                }
            }
            else
                tb.BorderBrush = B("HudAmber");
        }
        tb.LostFocus += (_, _) => Commit();
        tb.KeyDown += (_, e) => { if (e.Key == Key.Enter) Commit(); };
        return tb;
    }

    private FrameworkElement DayChip(LifeRoutine r, int day, Action changed)
    {
        var text = new TextBlock { Text = DayLetters[day], FontSize = 12, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var chip = new Border
        {
            Width = 28, Height = 28, CornerRadius = new CornerRadius(14), Margin = new Thickness(0, 0, 4, 0),
            Cursor = Cursors.Hand, Child = text, BorderThickness = new Thickness(1),
            ToolTip = CultureInfo.GetCultureInfo("fr-FR").DateTimeFormat.GetDayName((DayOfWeek)day),
        };
        void Paint()
        {
            bool on = (r.Days & (1 << day)) != 0;
            chip.Background = on ? B("DARKPrimary") : Brushes.Transparent;
            chip.BorderBrush = on ? B("DARKPrimary") : B("VMaxControlStroke");
            text.Foreground = on ? B("DARKPrimaryText") : B("VMaxSubtleText");
        }
        chip.MouseLeftButtonUp += (_, _) =>
        {
            int next = r.Days ^ (1 << day);
            if (next == 0)
                return; // au moins un jour
            r.Days = next;
            Paint();
            changed();
        };
        Paint();
        return chip;
    }
    #endregion

    #region Listes et état
    private (string value, string label)[] Actions()
    {
        var list = new List<(string, string)> { ("sleep", "Dormir"), ("eat", "Manger"), ("drink", "Boire"), ("relax", "Se détendre") };
        mw.Main.WorkList(out var ws, out var ss, out var ps);
        foreach (var w in ws.Concat(ss).Concat(ps))
            list.Add(("work:" + w.Name, w.NameTrans));
        return list.ToArray();
    }

    /// <summary>Pièces de l'habitat actif, sinon des noms courants</summary>
    private IEnumerable<string> Places()
    {
        var rooms = mw.Habitat?.IsActive == true ? mw.Habitat.Map.Rooms.Select(r => r.Name).Where(n => !string.IsNullOrWhiteSpace(n)).ToList() : new List<string>();
        return rooms.Count > 0 ? rooms : new[] { "Cuisine", "Chambre", "Salon", "Salle de bain", "Bureau", "Jardin" };
    }

    private string Status(LifeRoutine r)
    {
        if (!r.Enabled)
            return "Désactivée.";
        var book = Life?.Book;
        var next = RoutinePlanner.Next(r, DateTime.Now, k => book?.WasPlayed(k) == true);
        if (next == null)
            return "Aucun jour actif.";
        string when = next.Start.Date == DateTime.Today ? "aujourd'hui"
            : next.Start.Date == DateTime.Today.AddDays(1) ? "demain"
            : next.Start.ToString("dddd", Fr);
        string text = $"Prochaine fois : {when} vers {next.Start:HH'h'mm}";
        if (next.Duration > TimeSpan.Zero)
            text += $", pendant {Duration(next.Duration)}";
        if (next.Start < DateTime.Now)
            text = $"En attente : l'heure tirée ({next.Start:HH'h'mm}) est passée, la routine démarre dès que le compagnon est libre";
        if (mw.Habitat?.IsActive == true && !string.IsNullOrWhiteSpace(r.Place) && mw.Habitat.Map.RoomNamed(r.Place) == null)
            text += $" · ⚠ aucune pièce « {r.Place} » dans l'habitat actuel";
        if (book?.Enabled != true)
            text += " · (routines désactivées)";
        return text + ".";
    }

    private static string Duration(TimeSpan d) =>
        d.TotalMinutes < 60 ? $"{d.TotalMinutes:0} min" : d.Minutes == 0 ? $"{(int)d.TotalHours} h" : $"{(int)d.TotalHours} h {d.Minutes:00}";
    #endregion
}
