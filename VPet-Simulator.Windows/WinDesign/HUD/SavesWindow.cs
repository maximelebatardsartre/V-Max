using LinePutScript;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using VPet_Simulator.Core;
using VPet_Simulator.Windows.Interface;
using MessageBoxIcon = Panuon.WPF.UI.MessageBoxIcon;

namespace VPet_Simulator.Windows.HUD;

/// <summary>
/// V-Max : retour à une sauvegarde (remplace winSaveManager). Liste les sauvegardes automatiques
/// (dossier Saves) et les copies de secours (Saves_BKP) du profil, regroupées par jour, filtrables
/// par origine et par période ; chargement avec la même logique que l'ancien gestionnaire.
/// </summary>
public sealed class SavesWindow : HudWindow
{
    private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");
    private const int PageSize = 60;
    private static SavesWindow? instance;

    private enum Source { Local, Backup }

    private sealed class SaveEntry
    {
        public Source Source { get; init; }
        public string PetName { get; init; } = "";
        public DateTime SaveTime { get; init; }
        public int Level { get; init; }
        public int LevelMax { get; init; }
        public double Money { get; init; }
        public string FullPath { get; init; } = "";
        public bool HashCheck { get; init; }
    }

    private static readonly (string label, int? days)[] Periods =
    [
        ("7 jours", 7),
        ("30 jours", 30),
        ("1 an", 365),
        ("Tout", null),
    ];

    private List<SaveEntry> all = new();
    private List<SaveEntry> filtered = new();
    private Source? sourceFilter;
    private int? periodDays = 365; // comme l'ancien gestionnaire : les 12 derniers mois par défaut
    private int shown;
    private SaveEntry? selected;
    private Border? selectedRow;
    private bool loading, again;

    private readonly StackPanel list = new();
    private readonly ScrollViewer scroll;
    private readonly TextBlock countText;
    private readonly TextBlock selectionText;
    private readonly Button loadButton;
    private readonly Button saveNowButton;

    /// <summary>Ouvre la fenêtre (une seule instance) ou la ramène au premier plan</summary>
    public static SavesWindow Open(MainWindow mw)
    {
        if (instance == null)
        {
            instance = new SavesWindow(mw);
            instance.Closed += (_, _) => instance = null;
        }
        else
            instance.Refresh();
        instance.Present();
        return instance;
    }

    private SavesWindow(MainWindow mw) : base(mw, "saves", "SAUVEGARDES", "Revenir à une sauvegarde", 760, 680)
    {
        AddHeaderButton("", "Actualiser la liste", Refresh);
        AddHeaderButton("", "Ouvrir le dossier des sauvegardes", OpenFolder);

        // filtres
        var sources = new WrapPanel();
        AddChip(sources, "Toutes", "saves-src", true, () => sourceFilter = null);
        AddChip(sources, "Automatiques locales", "saves-src", false, () => sourceFilter = Source.Local);
        AddChip(sources, "Copies de secours", "saves-src", false, () => sourceFilter = Source.Backup);
        var periods = new WrapPanel();
        foreach (var (label, days) in Periods)
            AddChip(periods, label, "saves-period", days == periodDays, () => periodDays = days);

        var filters = new Grid { Margin = new Thickness(24, 0, 24, 4) };
        filters.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        filters.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        filters.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        filters.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        filters.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var srcLabel = new TextBlock { Text = "ORIGINE", Style = St("HudEyebrow"), Margin = new Thickness(0, 0, 0, 6) };
        var perLabel = new TextBlock { Text = "PÉRIODE", Style = St("HudEyebrow"), Margin = new Thickness(0, 0, 0, 6) };
        filters.Children.Add(srcLabel);
        Grid.SetRow(sources, 1);
        filters.Children.Add(sources);
        Grid.SetColumn(perLabel, 2);
        filters.Children.Add(perLabel);
        Grid.SetColumn(periods, 2);
        Grid.SetRow(periods, 1);
        filters.Children.Add(periods);

        countText = new TextBlock { FontSize = 12, Foreground = Res("HudTextMuted"), Margin = new Thickness(24, 6, 24, 0) };

        scroll = Scroll(list, new Thickness(24, 0, 20, 16));
        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        layout.Children.Add(filters);
        Grid.SetRow(countText, 1);
        layout.Children.Add(countText);
        Grid.SetRow(scroll, 2);
        layout.Children.Add(scroll);
        Body = layout;

        // pied : sauvegarder maintenant | sélection + Charger
        saveNowButton = Ghost("Sauvegarder maintenant", SaveNow);
        saveNowButton.ToolTip = "Enregistre la partie en cours tout de suite";
        selectionText = new TextBlock { FontSize = 12.5, Foreground = Res("HudTextMuted"), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(16, 0, 14, 0) };
        loadButton = Primary("Charger", LoadSelected);
        loadButton.IsEnabled = false;
        var foot = new DockPanel { Margin = new Thickness(24, 12, 24, 20) };
        DockPanel.SetDock(saveNowButton, Dock.Left);
        DockPanel.SetDock(loadButton, Dock.Right);
        foot.Children.Add(saveNowButton);
        foot.Children.Add(loadButton);
        foot.Children.Add(selectionText);
        var footBorder = new Border { BorderBrush = Res("HudStroke"), BorderThickness = new Thickness(0, 1, 0, 0), Child = foot };
        Footer = footBorder;

        Loaded += (_, _) => Refresh();
    }

    private void AddChip(Panel host, string label, string group, bool isChecked, Action select)
    {
        var chip = new RadioButton { Style = St("HudChip"), Content = label, GroupName = group, IsChecked = isChecked, Margin = new Thickness(0, 0, 6, 6) };
        chip.Checked += (_, _) =>
        {
            select();
            if (IsLoaded)
                ApplyFilters();
        };
        host.Children.Add(chip);
    }

    #region Données (même lecture que l'ancien gestionnaire)
    /// <summary>Relit les fichiers de sauvegarde (hors du fil de l'interface : il peut y en avoir beaucoup)</summary>
    public async void Refresh()
    {
        if (loading)
        {
            again = true; // relira une fois la lecture en cours terminée
            return;
        }
        loading = true;
        if (all.Count == 0)
        {
            list.Children.Clear();
            list.Children.Add(Empty("Lecture des sauvegardes…"));
        }
        string prefix = MW.PrefixSave;
        try
        {
            all = await Task.Run(() => ReadEntries(prefix));
        }
        catch (Exception e)
        {
            all = new();
            Notify("Impossible de lire les sauvegardes : " + e.Message, HudToast.Kind.Warning);
        }
        loading = false;
        if (again)
        {
            again = false;
            Refresh();
            return;
        }
        ApplyFilters();
    }

    private static List<SaveEntry> ReadEntries(string prefix)
    {
        var files = new List<FileInfo>();
        var pattern = $"Save{prefix}_*.lps";
        var saveDir = Path.Combine(ExtensionValue.DataDirectory, "Saves");
        var backupDir = Path.Combine(ExtensionValue.DataDirectory, "Saves_BKP");
        if (Directory.Exists(saveDir))
            files.AddRange(new DirectoryInfo(saveDir).GetFiles(pattern));
        if (Directory.Exists(backupDir))
            files.AddRange(new DirectoryInfo(backupDir).GetFiles(pattern));

        var entries = new List<SaveEntry>();
        foreach (var file in files)
        {
            try
            {
                var gs = new GameSave_v2(new LPS(File.ReadAllText(file.FullName)));
                entries.Add(new SaveEntry
                {
                    Source = file.Directory?.Name == "Saves_BKP" ? Source.Backup : Source.Local,
                    PetName = gs.GameSave.Name,
                    SaveTime = file.LastWriteTime,
                    Level = gs.GameSave.Level,
                    LevelMax = gs.GameSave.LevelMax,
                    Money = gs.GameSave.Money,
                    FullPath = file.FullName,
                    HashCheck = gs.HashCheck,
                });
            }
            catch
            {
                // fichier illisible : ignoré, comme dans l'ancien gestionnaire
            }
        }
        return entries.OrderByDescending(x => x.SaveTime).ToList();
    }
    #endregion

    #region Liste
    private void ApplyFilters()
    {
        IEnumerable<SaveEntry> f = all;
        if (sourceFilter is Source s)
            f = f.Where(x => x.Source == s);
        if (periodDays is int d)
        {
            var start = DateTime.Today.AddDays(-d);
            f = f.Where(x => x.SaveTime >= start);
        }
        filtered = f.OrderByDescending(x => x.SaveTime).ToList();

        list.Children.Clear();
        shown = 0;
        var keep = selected != null ? filtered.FirstOrDefault(x => x.FullPath == selected.FullPath) : null;
        Select(null, null);

        countText.Text = filtered.Count switch
        {
            0 => "",
            1 => "1 sauvegarde",
            var n => $"{n} sauvegardes",
        } + (all.Count > filtered.Count && filtered.Count > 0 ? $" sur {all.Count}" : "");

        if (filtered.Count == 0)
        {
            list.Children.Add(Empty(all.Count == 0
                ? "Aucune sauvegarde pour l'instant. Touche « Sauvegarder maintenant » pour en créer une."
                : "Aucune sauvegarde ne correspond à ces filtres. Élargis la période ou change d'origine."));
            return;
        }
        ShowMore();
        scroll.ScrollToTop();

        // garde la sélection si elle est toujours là, sinon la plus récente (comme l'ancien gestionnaire)
        var target = keep ?? filtered[0];
        if (rowOf.TryGetValue(target, out var row))
            Select(target, row);
    }

    private readonly Dictionary<SaveEntry, Border> rowOf = new();
    private DateTime? lastDay;

    private void ShowMore()
    {
        if (shown == 0)
        {
            rowOf.Clear();
            lastDay = null;
        }
        if (list.Children.Count > 0 && list.Children[^1] is Button more)
            list.Children.Remove(more);
        foreach (var e in filtered.Skip(shown).Take(PageSize))
        {
            if (lastDay != e.SaveTime.Date)
            {
                lastDay = e.SaveTime.Date;
                var header = Section(DayLabel(e.SaveTime.Date));
                if (list.Children.Count == 0)
                    header.Margin = new Thickness(0, 8, 0, 8);
                list.Children.Add(header);
            }
            list.Children.Add(Row(e));
        }
        shown = Math.Min(filtered.Count, shown + PageSize);
        if (shown < filtered.Count)
        {
            var b = Ghost($"Afficher plus ({filtered.Count - shown})", ShowMore);
            b.HorizontalAlignment = HorizontalAlignment.Center;
            b.Margin = new Thickness(0, 10, 0, 0);
            list.Children.Add(b);
        }
    }

    private static string DayLabel(DateTime day)
    {
        var today = DateTime.Today;
        if (day == today)
            return "Aujourd'hui";
        if (day == today.AddDays(-1))
            return "Hier";
        return day.ToString(day.Year == today.Year ? "dddd d MMMM" : "dddd d MMMM yyyy", Fr);
    }

    private static string Relative(DateTime t)
    {
        var d = DateTime.Now - t;
        if (d.TotalMinutes < 1)
            return "à l'instant";
        if (d.TotalHours < 1)
            return $"il y a {(int)d.TotalMinutes} min";
        if (d.TotalDays < 1)
            return $"il y a {(int)d.TotalHours} h";
        if (d.TotalDays < 30)
            return (int)d.TotalDays == 1 ? "il y a 1 jour" : $"il y a {(int)d.TotalDays} jours";
        if (d.TotalDays < 365)
            return $"il y a {(int)(d.TotalDays / 30)} mois";
        int y = (int)(d.TotalDays / 365);
        return y == 1 ? "il y a 1 an" : $"il y a {y} ans";
    }

    private static string LevelText(SaveEntry e) => $"Niv. {e.Level}" + (e.LevelMax > 0 ? $" (×{e.LevelMax})" : "");
    private static string MoneyText(SaveEntry e) => e.Money.ToString("N0", Fr) + " $";

    private Border Badge(string glyph, string text, string fg, string bg, string? tip)
    {
        var s = new StackPanel { Orientation = Orientation.Horizontal };
        if (glyph.Length > 0)
            s.Children.Add(new TextBlock { Text = glyph, FontFamily = (FontFamily)FindResource("HudIcons"), FontSize = 10, Foreground = Res(fg), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 5, 0) });
        s.Children.Add(new TextBlock { Text = text, FontFamily = (FontFamily)FindResource("HudDisplay"), FontWeight = FontWeights.SemiBold, FontSize = 11, Foreground = Res(fg), VerticalAlignment = VerticalAlignment.Center });
        return new Border
        {
            CornerRadius = new CornerRadius(10), Background = Res(bg), Padding = new Thickness(8, 2, 9, 3),
            Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, ToolTip = tip, Child = s,
        };
    }

    private Border Row(SaveEntry e)
    {
        // heure (grand) + relatif
        var when = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Width = 108 };
        when.Children.Add(new TextBlock { Text = e.SaveTime.ToString("HH:mm", Fr), FontFamily = (FontFamily)FindResource("HudDisplay"), FontWeight = FontWeights.SemiBold, FontSize = 22, Foreground = Res("HudText"), ToolTip = e.SaveTime.ToString("dddd d MMMM yyyy · HH:mm:ss", Fr) });
        when.Children.Add(new TextBlock { Text = Relative(e.SaveTime), FontSize = 11.5, Foreground = Res("HudTextMuted") });

        // compagnon, niveau, argent
        var who = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 12, 0) };
        who.Children.Add(new TextBlock { Text = string.IsNullOrWhiteSpace(e.PetName) ? "Sans nom" : e.PetName, FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = Res("HudText"), TextTrimming = TextTrimming.CharacterEllipsis });
        who.Children.Add(new TextBlock
        {
            Text = $"{LevelText(e)}  ·  {MoneyText(e)}",
            FontFamily = (FontFamily)FindResource("HudMono"), FontSize = 11.5, Foreground = Res("HudTextMuted"), Margin = new Thickness(0, 3, 0, 0),
            TextTrimming = TextTrimming.CharacterEllipsis,
        });

        var badges = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        if (e.HashCheck)
            badges.Children.Add(Badge("", "Vérifiée", "HudSuccess", "HudSurfaceHover", "Sauvegarde jamais modifiée et sans mod déséquilibré"));
        badges.Children.Add(e.Source == Source.Backup
            ? Badge("", "Secours", "HudAmber", "HudSurfaceHover", "Copie de secours locale (dossier Saves_BKP)")
            : Badge("", "Automatique", "HudSilver", "HudSurfaceHover", "Sauvegarde automatique locale (dossier Saves)"));

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(when);
        Grid.SetColumn(who, 1);
        grid.Children.Add(who);
        Grid.SetColumn(badges, 2);
        grid.Children.Add(badges);

        var row = new Border
        {
            CornerRadius = new CornerRadius(16), Background = Res("HudSurfaceRaised"), BorderBrush = Res("HudStroke"),
            BorderThickness = new Thickness(1), Padding = new Thickness(16, 10, 14, 11), Margin = new Thickness(0, 0, 0, 6),
            Child = grid, Cursor = Cursors.Hand, ToolTip = Path.GetFileName(e.FullPath),
        };
        row.MouseEnter += (_, _) => { if (selected != e) row.Background = Res("HudSurfaceHover"); };
        row.MouseLeave += (_, _) => { if (selected != e) row.Background = Res("HudSurfaceRaised"); };
        row.MouseLeftButtonDown += (_, ev) =>
        {
            Select(e, row);
            if (ev.ClickCount == 2)
                LoadSelected();
        };
        rowOf[e] = row;
        return row;
    }

    private void Select(SaveEntry? e, Border? row)
    {
        if (selectedRow != null)
        {
            selectedRow.Background = Res("HudSurfaceRaised");
            selectedRow.BorderBrush = Res("HudStroke");
        }
        selected = e;
        selectedRow = row;
        if (row != null)
        {
            row.Background = Res("HudAccentSoft");
            row.BorderBrush = Res("HudAccent");
        }
        loadButton.IsEnabled = e != null;
        selectionText.Text = e == null
            ? "Choisis une sauvegarde dans la liste."
            : $"{(string.IsNullOrWhiteSpace(e.PetName) ? "Sans nom" : e.PetName)} · {e.SaveTime.ToString("d MMM yyyy, HH:mm", Fr)} · {LevelText(e)}";
    }
    #endregion

    #region Actions
    /// <summary>Charge la sauvegarde choisie (logique de l'ancien LoadSelectedSave)</summary>
    private void LoadSelected()
    {
        if (selected is not SaveEntry e)
            return;

        if (!File.Exists(e.FullPath))
        {
            VDialog.Show(this, "Ce fichier de sauvegarde n'existe plus. Actualise la liste et réessaie.", "Chargement impossible", MessageBoxButton.OK, MessageBoxIcon.Warning);
            Refresh();
            return;
        }
        string lpsText = File.ReadAllText(e.FullPath);

        var message = $"{(string.IsNullOrWhiteSpace(e.PetName) ? "Sans nom" : e.PetName)} · {LevelText(e)}\n"
            + $"Sauvegardée le {e.SaveTime.ToString("dddd d MMMM yyyy 'à' HH:mm", Fr)}\n"
            + $"Argent : {MoneyText(e)}\n"
            + (e.HashCheck ? "Vérifiée : jamais modifiée, sans mod déséquilibré.\n" : "Non vérifiée : modifiée ou passée par un mod déséquilibré.\n")
            + "\nLa partie en cours sera remplacée par cette sauvegarde.";
        if (VDialog.Show(this, message, "Charger cette sauvegarde ?", MessageBoxButton.YesNo, MessageBoxIcon.Warning) != MessageBoxResult.Yes)
            return;

        try
        {
            if (MW.Main.State != Main.WorkingState.Nomal)
            {
                MW.Main.WorkTimer.Visibility = Visibility.Collapsed;
                MW.Main.State = Main.WorkingState.Nomal;
            }

            if (!MW.SavesLoad(new LPS(lpsText)))
                VDialog.Show(this, "Cette sauvegarde est endommagée et ne peut pas être chargée.\nC'est peut-être dû à une écriture interrompue ou à la synchronisation Steam Cloud. Essaie une autre sauvegarde, par exemple une copie de secours.",
                    "Sauvegarde endommagée", MessageBoxButton.OK, MessageBoxIcon.Error);
            else
                Notify("Sauvegarde chargée", HudToast.Kind.Success);
        }
        catch (Exception ex)
        {
            VDialog.Show(this, "Cette sauvegarde est endommagée et ne peut pas être chargée.\nC'est peut-être dû à des valeurs hors limites (mod ou objet déséquilibré).\n\n" + ex.Message,
                "Sauvegarde endommagée", MessageBoxButton.OK, MessageBoxIcon.Error);
        }
    }

    private void SaveNow()
    {
        try
        {
            MW.Save();
            Notify("Partie sauvegardée", HudToast.Kind.Success);
        }
        catch (Exception e)
        {
            Notify("La sauvegarde a échoué : " + e.Message, HudToast.Kind.Warning);
        }
        selected = null; // la nouvelle sauvegarde, la plus récente, sera sélectionnée
        Refresh();
    }

    private void OpenFolder()
    {
        try
        {
            var dir = Path.Combine(ExtensionValue.DataDirectory, "Saves");
            Directory.CreateDirectory(dir);
            Process.Start(new ProcessStartInfo("explorer.exe", "\"" + dir + "\"") { UseShellExecute = true });
        }
        catch (Exception e)
        {
            Notify("Impossible d'ouvrir le dossier : " + e.Message, HudToast.Kind.Warning);
        }
    }
    #endregion
}
