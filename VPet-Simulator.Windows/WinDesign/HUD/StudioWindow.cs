using LinePutScript;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using VPet_Simulator.Core;
using VPet_Simulator.Windows.Interface;

namespace VPet_Simulator.Windows.HUD;

/// <summary>
/// V-Max : Studio des mods (outil caché, débloqué par 7 clics sur la version dans Paramètres › À propos,
/// puis Ctrl+Maj+F12). Liste les mods du catalogue produit par tools/mods/ingest.py, lit leurs animations
/// directement depuis leurs images (sans déblocage, sans argent, sans effet sur la partie) et enregistre
/// le verdict de tri de chaque mod dans %APPDATA%\V-Max\studio\verdicts.json.
/// </summary>
public sealed class StudioWindow : HudWindow
{
    #region Données
    public sealed class CatalogFile
    {
        [JsonPropertyName("generated")] public string Generated { get; set; } = "";
        [JsonPropertyName("mods")] public List<StudioMod> Mods { get; set; } = [];
    }

    public sealed class StudioMod
    {
        [JsonPropertyName("id")] public string Id { get; set; } = "";
        [JsonPropertyName("category")] public string Category { get; set; } = "";
        [JsonPropertyName("reason")] public string? Reason { get; set; }
        [JsonPropertyName("source")] public string Source { get; set; } = "";
        [JsonPropertyName("name")] public string Name { get; set; } = "";
        [JsonPropertyName("name_en")] public string NameEn { get; set; } = "";
        [JsonPropertyName("author")] public string Author { get; set; } = "";
        [JsonPropertyName("intro")] public string Intro { get; set; } = "";
        [JsonPropertyName("workshop_url")] public string WorkshopUrl { get; set; } = "";
        [JsonPropertyName("tags")] public List<string> Tags { get; set; } = [];
        [JsonPropertyName("bytes")] public long Bytes { get; set; }
        [JsonPropertyName("works")] public List<StudioWork> Works { get; set; } = [];
        [JsonPropertyName("foods")] public List<StudioFood> Foods { get; set; } = [];
        [JsonPropertyName("plugins")] public List<StudioPlugin> Plugins { get; set; } = [];
        [JsonPropertyName("plugin_texts")] public List<string> PluginTexts { get; set; } = [];
        [JsonPropertyName("restored")] public List<string> Restored { get; set; } = [];
        [JsonPropertyName("animations")] public List<StudioAnimation> Animations { get; set; } = [];
        [JsonPropertyName("strings")] public List<StudioString> Strings { get; set; } = [];
    }

    public sealed class StudioWork
    {
        [JsonPropertyName("type")] public string Type { get; set; } = "";
        [JsonPropertyName("name")] public string Name { get; set; } = "";
        [JsonPropertyName("graph")] public string Graph { get; set; } = "";
        [JsonPropertyName("money")] public string Money { get; set; } = "";
        [JsonPropertyName("level")] public string Level { get; set; } = "";
        [JsonPropertyName("time")] public string Time { get; set; } = "";
    }

    public sealed class StudioFood
    {
        [JsonPropertyName("name")] public string Name { get; set; } = "";
        [JsonPropertyName("type")] public string Type { get; set; } = "";
        [JsonPropertyName("price")] public string Price { get; set; } = "";
    }

    public sealed class StudioPlugin
    {
        [JsonPropertyName("file")] public string File { get; set; } = "";
    }

    public sealed class StudioAnimation
    {
        [JsonPropertyName("type")] public string Type { get; set; } = "";
        [JsonPropertyName("name")] public string Name { get; set; } = "";
        [JsonPropertyName("modes")] public List<string> Modes { get; set; } = [];
        [JsonPropertyName("phases")] public List<string> Phases { get; set; } = [];
        [JsonPropertyName("frames")] public int Frames { get; set; }
        [JsonPropertyName("works")] public List<string> Works { get; set; } = [];
        [JsonPropertyName("variants")] public List<StudioVariant> Variants { get; set; } = [];
    }

    public sealed class StudioVariant
    {
        [JsonPropertyName("mode")] public string Mode { get; set; } = "";
        [JsonPropertyName("phase")] public string Phase { get; set; } = "";
        [JsonPropertyName("path")] public string Path { get; set; } = "";
        [JsonPropertyName("frames")] public int Frames { get; set; }
    }

    public sealed class StudioString
    {
        [JsonPropertyName("key")] public string Key { get; set; } = "";
        [JsonPropertyName("kind")] public string Kind { get; set; } = "";
        [JsonPropertyName("blocked")] public bool Blocked { get; set; }
        [JsonPropertyName("hints")] public Dictionary<string, string> Hints { get; set; } = [];
    }

    public sealed class Verdict
    {
        [JsonPropertyName("verdict")] public string Value { get; set; } = "";
        [JsonPropertyName("note")] public string Note { get; set; } = "";
        [JsonPropertyName("date")] public string Date { get; set; } = "";
    }

    public static string Folder => Path.Combine(ExtensionValue.DataDirectory, "studio");
    private static string CatalogPath => Path.Combine(Folder, "catalog.json");
    private static string VerdictsPath => Path.Combine(Folder, "verdicts.json");
    private static string TranslationsPath => Path.Combine(Folder, "translations.fr.json");

    private static readonly string[] HiddenCategories = ["Exclu", "Vide", "Erreur", "Langue"];
    private static readonly (string key, string label)[] VerdictChoices =
    [
        ("garder", "Garder"), ("integrer", "Intégrer au jeu"), ("revoir", "À revoir"), ("supprimer", "Supprimer"),
    ];

    private readonly List<StudioMod> mods = [];
    private readonly int hiddenCount;
    private readonly Dictionary<string, Verdict> verdicts = new();
    private readonly Dictionary<string, Dictionary<string, string>> french = new();
    #endregion

    private static StudioWindow? instance;

    /// <summary>Ouvre le Studio (une seule fenêtre)</summary>
    public static StudioWindow Open(MainWindow mw)
    {
        if (instance == null)
        {
            instance = new StudioWindow(mw);
            instance.Closed += (_, _) => instance = null;
        }
        instance.Present();
        return instance;
    }

    /// <summary>Le Studio a été débloqué (7 clics sur la version dans À propos)</summary>
    public static bool Unlocked(MainWindow mw) => mw.Set["vmax_dev"][(gbol)"studio"];

    private readonly WrapPanel filters = new() { Margin = new Thickness(0, 10, 0, 6) };
    private readonly StackPanel list = new();
    private readonly TextBlock progress = new();
    private readonly ContentControl center = new();
    private readonly ContentControl side = new();
    private readonly StudioPlayer player;
    private string category = "Tout";
    private bool onlyUntriaged;
    private string query = "";
    private StudioMod? current;

    private StudioWindow(MainWindow mw) : base(mw, "studio", "STUDIO", "Mods à trier", 1360, 840)
    {
        player = new StudioPlayer(mw);
        var catalog = Load<CatalogFile>(CatalogPath);
        if (catalog != null)
        {
            mods.AddRange(catalog.Mods.Where(m => !HiddenCategories.Contains(m.Category)).OrderBy(m => m.Category).ThenBy(m => m.Id));
            hiddenCount = catalog.Mods.Count - mods.Count;
        }
        if (Load<Dictionary<string, Verdict>>(VerdictsPath) is { } v)
            foreach (var kv in v)
                verdicts[kv.Key] = kv.Value;
        if (Load<Dictionary<string, Dictionary<string, string>>>(TranslationsPath) is { } fr)
            foreach (var kv in fr)
                french[kv.Key] = kv.Value;

        AddHeaderButton("\uE710", "Ajouter des mods (ou glisse un dossier ou un .zip dans la fenêtre)", PickFolders);
        AddHeaderButton("", "Recharger le catalogue", Reload);
        AddHeaderButton("", "Ouvrir le dossier du Studio", () => OpenPath(Folder));

        AllowDrop = true;
        DragEnter += OnDragOver;
        DragOver += OnDragOver;
        DragLeave += (_, _) => dropHint.Visibility = Visibility.Collapsed;
        Drop += OnDrop;

        if (catalog == null || mods.Count == 0)
        {
            Body = WithDrop(Empty("Aucun mod dans le Studio.\nGlisse ici un dossier de mod ou une archive .zip, ou utilise le bouton + en haut."));
            return;
        }

        // colonne gauche : recherche, catégories, liste
        var search = SearchBox("Nom, auteur, identifiant…", out var searchBox);
        search.TextChanged += (_, _) => { query = search.Text.Trim(); RefreshList(); };
        BuildFilters();
        progress.Style = St("HudBodyText");
        progress.FontSize = 12.5;
        progress.Foreground = Res("HudTextMuted");
        progress.Margin = new Thickness(2, 0, 0, 8);
        var left = new DockPanel { Width = 310, Margin = new Thickness(24, 0, 12, 12) };
        var top = new StackPanel();
        top.Children.Add(searchBox);
        top.Children.Add(filters);
        top.Children.Add(progress);
        DockPanel.SetDock(top, Dock.Top);
        left.Children.Add(top);
        var footnote = new TextBlock
        {
            Text = $"{hiddenCount} mods non affichés (exclus, vides ou packs de langue).", Style = St("HudBodyText"), FontSize = 11.5,
            Foreground = Res("HudTextMuted"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, 8, 0, 0),
        };
        DockPanel.SetDock(footnote, Dock.Bottom);
        left.Children.Add(footnote);
        left.Children.Add(new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(0, 0, 6, 0) });

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(360) });
        Grid.SetColumn(center, 1);
        Grid.SetColumn(side, 2);
        center.Margin = new Thickness(0, 0, 12, 12);
        side.Margin = new Thickness(0, 0, 20, 12);
        grid.Children.Add(left);
        grid.Children.Add(center);
        grid.Children.Add(side);
        Body = WithDrop(grid);

        PreviewKeyDown += OnKey;
        Closed += (_, _) => player.Stop();
        RefreshList();
        Select(mods.FirstOrDefault(m => !verdicts.ContainsKey(m.Id)) ?? mods.FirstOrDefault());
    }

    private static T? Load<T>(string path) where T : class
    {
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize<T>(File.ReadAllText(path)) : null;
        }
        catch (Exception e)
        {
            Debug.WriteLine("Studio : lecture impossible de " + path + " : " + e.Message);
            return null;
        }
    }

    private void Reload()
    {
        player.Stop();
        Close();
        Open(MW);
    }

    #region Ajout manuel (glisser-déposer)
    private readonly Border dropHint = new() { Visibility = Visibility.Collapsed, IsHitTestVisible = false };
    private bool importing;

    private FrameworkElement WithDrop(UIElement content)
    {
        var accent = ((SolidColorBrush)Res("HudAccent")).Color;
        dropHint.Background = new SolidColorBrush(Color.FromArgb(0xCC, 0x12, 0x12, 0x18));
        dropHint.BorderBrush = Res("HudAccent");
        dropHint.BorderThickness = new Thickness(2);
        dropHint.CornerRadius = new CornerRadius(18);
        dropHint.Margin = new Thickness(16, 0, 16, 16);
        var sp = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        sp.Children.Add(new TextBlock { Text = "\uE896", FontFamily = (FontFamily)FindResource("HudIcons"), FontSize = 40, Foreground = Res("HudAccent"), HorizontalAlignment = HorizontalAlignment.Center });
        sp.Children.Add(new TextBlock { Text = "Dépose ici un dossier de mod ou une archive .zip", Style = St("HudTitle"), FontSize = 20, Margin = new Thickness(0, 12, 0, 4), HorizontalAlignment = HorizontalAlignment.Center });
        sp.Children.Add(new TextBlock { Text = "Il sera copié dans le Studio, lu et classé, avec les mêmes contrôles que les autres.", Foreground = Res("HudTextMuted"), FontSize = 13, HorizontalAlignment = HorizontalAlignment.Center });
        dropHint.Child = sp;
        var host = new Grid();
        host.Children.Add(content);
        host.Children.Add(dropHint);
        return host;
    }

    private static string[] DroppedPaths(DragEventArgs e)
        => e.Data.GetDataPresent(DataFormats.FileDrop) && e.Data.GetData(DataFormats.FileDrop) is string[] p ? p : [];

    private void OnDragOver(object sender, DragEventArgs e)
    {
        var paths = DroppedPaths(e);
        bool ok = !importing && paths.Length > 0 && paths.All(p => Directory.Exists(p) || p.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
        e.Effects = ok ? DragDropEffects.Copy : DragDropEffects.None;
        dropHint.Visibility = ok ? Visibility.Visible : Visibility.Collapsed;
        e.Handled = true;
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        dropHint.Visibility = Visibility.Collapsed;
        var paths = DroppedPaths(e);
        if (paths.Length > 0)
            ImportPaths(paths);
        e.Handled = true;
    }

    private void PickFolders()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "Choisis un ou plusieurs dossiers de mods", Multiselect = true };
        if (dialog.ShowDialog(this) == true)
            ImportPaths(dialog.FolderNames);
    }

    private async void ImportPaths(string[] paths)
    {
        if (importing)
            return;
        importing = true;
        Notify(paths.Length == 1 ? "Import du mod en cours…" : $"Import de {paths.Length} éléments en cours…");
        var coreFood = Path.Combine(MainWindow.ModPath, "0000_core", "food");
        var results = await Task.Run(() => paths.SelectMany(p =>
        {
            try
            {
                return ModImporter.Import(p, Folder, coreFood);
            }
            catch (Exception ex)
            {
                return [new ModImporter.Result(Path.GetFileName(p), Path.GetFileName(p), "Erreur", ex.Message)];
            }
        }).ToList());
        importing = false;
        var added = results.Where(r => r.Refused == null).ToList();
        var refused = results.Where(r => r.Refused != null).ToList();
        if (refused.Count > 0)
            VDialog.Show(this, string.Join("\n", refused.Select(r => $"• {(string.IsNullOrWhiteSpace(r.Name) ? r.Id : r.Name)} : {r.Refused}")),
                refused.Count == 1 ? "Mod non ajouté" : $"{refused.Count} mods non ajoutés", MessageBoxButton.OK, Panuon.WPF.UI.MessageBoxIcon.Warning);
        if (added.Count == 0)
            return;
        MW.Toast(added.Count == 1 ? $"« {added[0].Name} » ajouté au Studio ({added[0].Category})." : $"{added.Count} mods ajoutés au Studio.", HudToast.Kind.Success);
        player.Stop();
        Close();
        Open(MW).SelectById(added[0].Id);
    }

    /// <summary>Affiche un mod précis (après un import)</summary>
    public void SelectById(string id)
    {
        var m = mods.FirstOrDefault(x => x.Id == id);
        if (m == null)
            return;
        category = "Tout";
        onlyUntriaged = false;
        BuildFilters();
        Select(m);
    }
    #endregion

    private static void OpenPath(string path)
    {
        try { Process.Start(new ProcessStartInfo("explorer.exe", "\"" + path + "\"") { UseShellExecute = true }); }
        catch { }
    }

    #region Liste
    private void BuildFilters()
    {
        filters.Children.Clear();
        var cats = new List<string> { "Tout" };
        cats.AddRange(mods.Select(m => m.Category).Distinct().OrderBy(c => c));
        foreach (var c in cats)
        {
            int n = c == "Tout" ? mods.Count : mods.Count(m => m.Category == c);
            var chip = new RadioButton { Style = St("HudChip"), Content = $"{c} · {n}", GroupName = "studio-cat", IsChecked = c == category, Margin = new Thickness(0, 0, 6, 6) };
            chip.Checked += (_, _) => { category = c; RefreshList(); };
            filters.Children.Add(chip);
        }
        var todo = new CheckBox { Content = "À trier seulement", Foreground = Res("HudText"), Margin = new Thickness(2, 4, 0, 0), IsChecked = onlyUntriaged };
        todo.Checked += (_, _) => { onlyUntriaged = true; RefreshList(); };
        todo.Unchecked += (_, _) => { onlyUntriaged = false; RefreshList(); };
        filters.Children.Add(todo);
    }

    private string DisplayName(StudioMod m)
    {
        if (french.TryGetValue(m.Id, out var fr) && fr.TryGetValue(m.Name, out var n) && !string.IsNullOrWhiteSpace(n))
            return n;
        return string.IsNullOrWhiteSpace(m.NameEn) || m.NameEn == m.Name ? m.Name : $"{m.Name} · {m.NameEn}";
    }

    private string? FrenchOf(StudioMod m, string key) => french.TryGetValue(m.Id, out var fr) && fr.TryGetValue(key, out var v) ? v : null;

    private void RefreshList()
    {
        list.Children.Clear();
        var q = query.ToLowerInvariant();
        var shown = mods.Where(m => category == "Tout" || m.Category == category)
            .Where(m => !onlyUntriaged || !verdicts.ContainsKey(m.Id))
            .Where(m => q.Length == 0 || $"{m.Id} {m.Name} {m.NameEn} {m.Author} {DisplayName(m)}".ToLowerInvariant().Contains(q))
            .ToList();
        foreach (var m in shown)
            list.Children.Add(Row(m));
        if (shown.Count == 0)
            list.Children.Add(new TextBlock { Text = "Aucun mod ne correspond.", Style = St("HudBodyText"), Foreground = Res("HudTextMuted"), Margin = new Thickness(4, 12, 0, 0) });
        int done = mods.Count(m => verdicts.ContainsKey(m.Id));
        progress.Text = $"{done} / {mods.Count} mods triés";
    }

    private Border Row(StudioMod m)
    {
        var icon = new Image { Width = 40, Height = 40, Stretch = Stretch.UniformToFill };
        var iconPath = Path.Combine(m.Source, "icon.png");
        if (File.Exists(iconPath))
            icon.Source = Thumb(iconPath, 80);
        var iconBox = new Border { Width = 40, Height = 40, CornerRadius = new CornerRadius(10), ClipToBounds = true, Background = Res("HudSurfaceRaised"), Child = icon, Margin = new Thickness(0, 0, 10, 0) };
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = DisplayName(m), Foreground = Res("HudText"), FontSize = 13.5, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
        var meta = $"{m.Category} · {m.Animations.Count} anim. · {m.Bytes / 1e6:0.#} Mo";
        text.Children.Add(new TextBlock { Text = meta, Foreground = Res("HudTextMuted"), FontSize = 11.5 });
        var dock = new DockPanel();
        if (verdicts.TryGetValue(m.Id, out var v))
        {
            var badge = VerdictBadge(v.Value);
            DockPanel.SetDock(badge, Dock.Right);
            dock.Children.Add(badge);
        }
        DockPanel.SetDock(iconBox, Dock.Left);
        dock.Children.Add(iconBox);
        dock.Children.Add(text);
        bool selected = current?.Id == m.Id;
        var row = new Border
        {
            Child = dock, Padding = new Thickness(8), Margin = new Thickness(0, 0, 0, 4), CornerRadius = new CornerRadius(12), Cursor = Cursors.Hand,
            Background = selected ? Res("HudAccentSoft") : Brushes.Transparent, BorderBrush = selected ? Res("HudAccent") : Brushes.Transparent, BorderThickness = new Thickness(1),
            ToolTip = $"Workshop {m.Id} · {m.Author}",
        };
        row.MouseEnter += (_, _) => { if (current?.Id != m.Id) row.Background = Res("HudSurfaceHover"); };
        row.MouseLeave += (_, _) => { if (current?.Id != m.Id) row.Background = Brushes.Transparent; };
        row.MouseLeftButtonUp += (_, _) => Select(m);
        return row;
    }

    private Border VerdictBadge(string verdict)
    {
        var (label, brush) = verdict switch
        {
            "garder" => ("Gardé", Res("HudSuccess")),
            "integrer" => ("Intégré", Res("HudAccent")),
            "revoir" => ("À revoir", Res("HudAmber")),
            "supprimer" => ("Supprimé", Res("HudTextMuted")),
            _ => (verdict, Res("HudTextMuted")),
        };
        return new Border
        {
            CornerRadius = new CornerRadius(8), BorderBrush = brush, BorderThickness = new Thickness(1), Padding = new Thickness(6, 1, 6, 2),
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0),
            Child = new TextBlock { Text = label, Foreground = brush, FontSize = 10.5, FontWeight = FontWeights.SemiBold },
        };
    }

    private static BitmapSource? Thumb(string path, int width)
    {
        try
        {
            var b = new BitmapImage();
            b.BeginInit();
            b.CacheOption = BitmapCacheOption.OnLoad;
            b.UriSource = new Uri(path);
            b.DecodePixelWidth = width;
            b.EndInit();
            b.Freeze();
            return b;
        }
        catch { return null; }
    }
    #endregion

    #region Mod sélectionné
    private void Select(StudioMod? m)
    {
        current = m;
        RefreshList();
        if (m == null)
        {
            center.Content = Empty("Rien à afficher.");
            side.Content = null;
            return;
        }
        center.Content = BuildCenter(m);
        side.Content = BuildSide(m);
    }

    private static readonly Dictionary<string, string> TypeNames = new()
    {
        ["Work"] = "Occupation", ["Common"] = "Animation libre", ["Idel"] = "Inactivité", ["StateONE"] = "Attente 1", ["StateTWO"] = "Attente 2",
        ["Touch_Head"] = "Caresse (tête)", ["Touch_Body"] = "Caresse (corps)", ["Say"] = "Parole", ["Move"] = "Déplacement", ["Default"] = "Repos",
        ["Sleep"] = "Sommeil", ["Raised_Dynamic"] = "Porté", ["Raised_Static"] = "Porté (immobile)", ["StartUP"] = "Réveil", ["Shutdown"] = "Au revoir",
    };

    private FrameworkElement BuildCenter(StudioMod m)
    {
        var dock = new DockPanel();
        if (m.Animations.Count == 0)
        {
            var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, MaxWidth = 460 };
            info.Children.Add(new TextBlock { Text = "Pas d'animation dans ce mod", Style = St("HudTitle"), FontSize = 20, TextAlignment = TextAlignment.Center });
            string what = m.Works.Count > 0 ? "Il ajoute des occupations qui réutilisent les animations du jeu de base."
                : m.Plugins.Count > 0 ? "C'est un plugin (du code) : il modifie le comportement du jeu sans images."
                : "Il ne contient que des textes ou des réglages.";
            info.Children.Add(new TextBlock { Text = what, Style = St("HudBodyText"), Foreground = Res("HudTextMuted"), TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) });
            player.Stop();
            return Card(info, new Thickness(24));
        }
        var anims = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
        RadioButton? first = null;
        foreach (var a in m.Animations)
        {
            var type = a.Works.Count > 0 ? "Occupation" : TypeNames.TryGetValue(a.Type, out var t) ? t : a.Type;
            var label = $"{type} · {a.Name}";
            var chip = new RadioButton { Style = St("HudChip"), Content = label, GroupName = "studio-anim", Margin = new Thickness(0, 0, 6, 6), ToolTip = $"{a.Frames} images · {string.Join(", ", a.Phases)}" };
            var anim = a;
            chip.Checked += (_, _) => player.Load(m, anim);
            anims.Children.Add(chip);
            first ??= chip;
        }
        DockPanel.SetDock(anims, Dock.Top);
        dock.Children.Add(anims);
        dock.Children.Add(player);
        if (first != null)
            first.IsChecked = true;
        return dock;
    }

    private FrameworkElement BuildSide(StudioMod m)
    {
        var sp = new StackPanel();
        sp.Children.Add(new TextBlock { Text = m.Category.ToUpperInvariant(), Style = St("HudEyebrow") });
        sp.Children.Add(new TextBlock { Text = DisplayName(m), Style = St("HudTitle"), FontSize = 20, TextWrapping = TextWrapping.Wrap });
        if (DisplayName(m) != m.Name)
            sp.Children.Add(new TextBlock { Text = m.Name, Foreground = Res("HudTextMuted"), FontSize = 12.5, TextWrapping = TextWrapping.Wrap });
        sp.Children.Add(new TextBlock { Text = $"par {m.Author} · {m.Bytes / 1e6:0.#} Mo · Workshop {m.Id}", Foreground = Res("HudTextMuted"), FontSize = 12, Margin = new Thickness(0, 4, 0, 0), TextWrapping = TextWrapping.Wrap });
        var intro = FrenchOf(m, m.Intro) ?? m.Intro;
        if (!string.IsNullOrWhiteSpace(intro) && !intro.EndsWith("_Description") && !intro.EndsWith("_DescriptionID"))
            sp.Children.Add(new TextBlock { Text = intro, Style = St("HudBodyText"), FontSize = 13, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) });
        var links = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
        var folder = Ghost("Dossier", () => OpenPath(m.Source));
        folder.Margin = new Thickness(0, 0, 6, 0);
        links.Children.Add(folder);
        links.Children.Add(Ghost("Page Workshop", () => ExtensionFunction.StartURL(m.WorkshopUrl)));
        sp.Children.Add(links);

        // verdict
        sp.Children.Add(Section("Ton verdict"));
        verdicts.TryGetValue(m.Id, out var v);
        var choices = new WrapPanel();
        foreach (var (key, label) in VerdictChoices)
        {
            var chip = new RadioButton { Style = St("HudChip"), Content = label, GroupName = "studio-verdict-" + m.Id, IsChecked = v?.Value == key, Margin = new Thickness(0, 0, 6, 6) };
            var k = key;
            chip.Checked += (_, _) => SetVerdict(m, k, null);
            choices.Children.Add(chip);
        }
        sp.Children.Add(choices);
        var note = new TextBox { Style = St("HudInput"), Tag = "Note (facultatif) : à couper, à renommer, à fusionner…", Text = v?.Note ?? "", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 54 };
        note.LostFocus += (_, _) => { if (verdicts.ContainsKey(m.Id) || note.Text.Length > 0) SetVerdict(m, null, note.Text); };
        sp.Children.Add(new Border
        {
            CornerRadius = new CornerRadius(12), Background = Res("HudSurfaceRaised"), BorderBrush = Res("HudStroke"), BorderThickness = new Thickness(1),
            Padding = new Thickness(10, 6, 10, 6), Margin = new Thickness(0, 2, 0, 0), Child = note,
        });
        sp.Children.Add(new TextBlock { Text = "Raccourcis : 1 Garder · 2 Intégrer · 3 À revoir · 4 Supprimer · ↓ mod suivant", Foreground = Res("HudTextMuted"), FontSize = 11, Margin = new Thickness(2, 6, 0, 0), TextWrapping = TextWrapping.Wrap });

        // contenu
        if (m.Works.Count > 0)
        {
            sp.Children.Add(Section($"Occupations · {m.Works.Count}"));
            foreach (var w in m.Works.Take(12))
            {
                var type = w.Type.ToLowerInvariant() switch { "study" => "Études", "play" => "Loisir", _ => "Travail" };
                var name = FrenchOf(m, w.Name) ?? w.Name;
                sp.Children.Add(Line($"{name}", $"{type} · {w.Money} $ · niv. {(string.IsNullOrEmpty(w.Level) ? "0" : w.Level)} · {w.Time} min"));
            }
        }
        if (m.Foods.Count > 0)
        {
            sp.Children.Add(Section($"Nourriture · {m.Foods.Count}"));
            foreach (var f in m.Foods.Take(6))
                sp.Children.Add(Line(FrenchOf(m, f.Name) ?? f.Name, $"{f.Type} · {f.Price} $"));
            if (m.Foods.Count > 6)
                sp.Children.Add(Line($"… et {m.Foods.Count - 6} autres", ""));
        }
        var texts = m.Strings.Where(s => !s.Blocked && s.Kind is "click" or "select" or "low").ToList();
        if (texts.Count > 0)
        {
            sp.Children.Add(Section($"Répliques · {texts.Count}"));
            foreach (var s in texts.Take(6))
            {
                var fr = FrenchOf(m, s.Key);
                sp.Children.Add(Line(fr ?? s.Key, fr != null ? s.Key : (s.Hints.TryGetValue("en", out var en) ? en : "pas encore traduit")));
            }
        }
        int blocked = m.Strings.Count(s => s.Blocked);
        if (blocked > 0)
            sp.Children.Add(Note($"{blocked} réplique(s) ambiguë(s) écartée(s) : elles ne seront ni traduites ni intégrées.", "HudAmber"));
        if (m.Plugins.Count > 0)
            sp.Children.Add(Note($"Contient du code ({string.Join(", ", m.Plugins.Select(p => p.File))}) : il faudra l'autoriser dans Paramètres › Extensions.", "HudAmber"));
        if (m.PluginTexts.Count > 0)
            sp.Children.Add(Note($"{m.PluginTexts.Count} fichier(s) texte lus par son plugin : ils ne passent pas par les traductions du jeu.", "HudTextMuted"));
        if (m.Restored.Count > 0)
            sp.Children.Add(Note("Un outil précédent avait renommé certains fichiers ; le Studio lit les originaux.", "HudTextMuted"));
        return Scroll(sp, new Thickness(0, 0, 8, 0));
    }

    private FrameworkElement Line(string title, string sub)
    {
        var sp = new StackPanel { Margin = new Thickness(0, 0, 0, 6) };
        sp.Children.Add(new TextBlock { Text = title, Foreground = Res("HudText"), FontSize = 13, TextWrapping = TextWrapping.Wrap });
        if (!string.IsNullOrEmpty(sub))
            sp.Children.Add(new TextBlock { Text = sub, Foreground = Res("HudTextMuted"), FontSize = 11.5, TextWrapping = TextWrapping.Wrap });
        return sp;
    }

    private FrameworkElement Note(string text, string brush) => new Border
    {
        BorderBrush = Res(brush), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = new Thickness(10, 7, 10, 8), Margin = new Thickness(0, 10, 0, 0),
        Child = new TextBlock { Text = text, Foreground = Res(brush == "HudTextMuted" ? "HudTextMuted" : "HudText"), FontSize = 12, TextWrapping = TextWrapping.Wrap },
    };

    private void SetVerdict(StudioMod m, string? value, string? note)
    {
        verdicts.TryGetValue(m.Id, out var v);
        v ??= new Verdict();
        if (value != null)
            v.Value = value;
        if (note != null)
            v.Note = note;
        if (string.IsNullOrEmpty(v.Value) && string.IsNullOrEmpty(v.Note))
            return;
        v.Date = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
        verdicts[m.Id] = v;
        try
        {
            Directory.CreateDirectory(Folder);
            File.WriteAllText(VerdictsPath, JsonSerializer.Serialize(verdicts, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
        }
        catch (Exception e)
        {
            Notify("Verdict non enregistré : " + e.Message, HudToast.Kind.Warning);
        }
        RefreshList();
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        if (current == null || e.OriginalSource is TextBox)
            return;
        int idx = e.Key switch { Key.D1 or Key.NumPad1 => 0, Key.D2 or Key.NumPad2 => 1, Key.D3 or Key.NumPad3 => 2, Key.D4 or Key.NumPad4 => 3, _ => -1 };
        if (idx >= 0)
        {
            SetVerdict(current, VerdictChoices[idx].key, null);
            side.Content = BuildSide(current);
            e.Handled = true;
        }
        else if (e.Key is Key.Down or Key.Up)
        {
            var order = mods.Where(m => category == "Tout" || m.Category == category).Where(m => !onlyUntriaged || !verdicts.ContainsKey(m.Id) || m.Id == current.Id).ToList();
            int i = order.FindIndex(m => m.Id == current.Id);
            int next = e.Key == Key.Down ? i + 1 : i - 1;
            if (next >= 0 && next < order.Count)
                Select(order[next]);
            e.Handled = true;
        }
        else if (e.Key == Key.Space)
        {
            player.TogglePlay();
            e.Handled = true;
        }
        else if (e.Key is Key.Left or Key.Right)
        {
            player.Step(e.Key == Key.Right ? 1 : -1);
            e.Handled = true;
        }
    }
    #endregion
}

/// <summary>
/// V-Max : lecteur d'animations du Studio. Lit les images d'origine du mod (sans passer par le moteur du jeu) :
/// enchaînement début → boucle → fin, une phase seule, humeur, vitesse et image par image.
/// </summary>
public sealed class StudioPlayer : Border
{
    private readonly MainWindow mw;
    private readonly Image image = new() { Stretch = Stretch.Uniform, Width = 420, Height = 420 };
    private readonly TextBlock status = new() { FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock title = new() { FontSize = 15, FontWeight = FontWeights.SemiBold };
    private readonly WrapPanel modes = new();
    private readonly WrapPanel phases = new();
    private readonly WrapPanel speeds = new();
    private readonly Button play;
    private readonly Button tryOnPet;
    private readonly DispatcherTimer timer = new(DispatcherPriority.Render);
    private readonly Dictionary<string, List<(BitmapSource img, int ms)>> cache = new();
    private CancellationTokenSource? loading;

    private StudioWindow.StudioMod? mod;
    private StudioWindow.StudioAnimation? anim;
    private string mode = "nomal";
    private string phase = "chain";
    private double speed = 1;
    // séquence en cours : liste de (images d'une variante)
    private List<List<(BitmapSource img, int ms)>> sequence = [];
    private int seqIndex, frame;

    public StudioPlayer(MainWindow mw)
    {
        this.mw = mw;
        CornerRadius = new CornerRadius(18);
        BorderThickness = new Thickness(1);
        SetResourceReference(BackgroundProperty, "HudSurfaceRaised");
        SetResourceReference(BorderBrushProperty, "HudStroke");
        Padding = new Thickness(18, 14, 18, 14);
        title.SetResourceReference(TextBlock.ForegroundProperty, "HudText");
        status.SetResourceReference(TextBlock.ForegroundProperty, "HudTextMuted");
        status.FontFamily = (FontFamily)Application.Current.FindResource("HudMono");

        play = IconButton("", "Lecture / pause (Espace)", TogglePlay);
        var prev = IconButton("", "Image précédente (←)", () => Step(-1));
        var next = IconButton("", "Image suivante (→)", () => Step(1));
        tryOnPet = new Button { Content = "Essayer sur le compagnon", Margin = new Thickness(12, 0, 0, 0) };
        tryOnPet.SetResourceReference(StyleProperty, "HudGhostButton");
        tryOnPet.Click += (_, _) => TryOnPet();

        foreach (var (label, value) in new[] { ("×0,5", 0.5), ("×1", 1.0), ("×2", 2.0) })
        {
            var chip = Chip(label, "studio-speed", value == 1);
            chip.Checked += (_, _) => { speed = value; };
            speeds.Children.Add(chip);
        }

        var stage = new Border
        {
            CornerRadius = new CornerRadius(14), Margin = new Thickness(0, 10, 0, 10), ClipToBounds = true,
            Background = Checker(), Child = image,
        };
        var bar = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        var controls = new StackPanel { Orientation = Orientation.Horizontal };
        controls.Children.Add(prev);
        controls.Children.Add(play);
        controls.Children.Add(next);
        controls.Children.Add(new Border { Width = 12 });
        controls.Children.Add(speeds);
        controls.Children.Add(tryOnPet);
        DockPanel.SetDock(controls, Dock.Left);
        bar.Children.Add(controls);

        var root = new DockPanel();
        var head = new StackPanel();
        var titleRow = new DockPanel();
        DockPanel.SetDock(status, Dock.Right);
        titleRow.Children.Add(status);
        titleRow.Children.Add(title);
        head.Children.Add(titleRow);
        var rows = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
        rows.Children.Add(Labeled("Phase", phases));
        rows.Children.Add(Labeled("Humeur", modes));
        head.Children.Add(rows);
        DockPanel.SetDock(head, Dock.Top);
        root.Children.Add(head);
        DockPanel.SetDock(bar, Dock.Bottom);
        root.Children.Add(bar);
        root.Children.Add(stage);
        Child = root;

        timer.Tick += (_, _) => Advance();
    }

    private static Brush Checker()
    {
        // damier discret : les zones transparentes des images restent lisibles
        var g = new DrawingGroup();
        g.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x33)), null, new RectangleGeometry(new Rect(0, 0, 24, 24))));
        g.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(0x32, 0x32, 0x3C)), null, new RectangleGeometry(new Rect(0, 0, 12, 12))));
        g.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(0x32, 0x32, 0x3C)), null, new RectangleGeometry(new Rect(12, 12, 12, 12))));
        var b = new DrawingBrush(g) { TileMode = TileMode.Tile, Viewport = new Rect(0, 0, 24, 24), ViewportUnits = BrushMappingMode.Absolute };
        b.Freeze();
        return b;
    }

    private static FrameworkElement Labeled(string label, Panel content)
    {
        var dock = new DockPanel { Margin = new Thickness(0, 0, 0, 2) };
        var t = new TextBlock { Text = label.ToUpperInvariant(), Width = 70, FontSize = 10.5, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 0, 6) };
        t.SetResourceReference(TextBlock.ForegroundProperty, "HudTextMuted");
        DockPanel.SetDock(t, Dock.Left);
        dock.Children.Add(t);
        dock.Children.Add(content);
        return dock;
    }

    private static RadioButton Chip(string label, string group, bool isChecked)
    {
        var chip = new RadioButton { Content = label, GroupName = group, IsChecked = isChecked, Margin = new Thickness(0, 0, 6, 6) };
        chip.SetResourceReference(StyleProperty, "HudChip");
        return chip;
    }

    private static Button IconButton(string glyph, string tip, Action run)
    {
        var b = new Button { Content = glyph, ToolTip = tip, Margin = new Thickness(0, 0, 4, 0) };
        b.SetResourceReference(StyleProperty, "HudIconButton");
        b.Click += (_, _) => run();
        return b;
    }

    private static readonly Dictionary<string, string> ModeNames = new() { ["nomal"] = "Normal", ["happy"] = "Heureux", ["poorcondition"] = "Mauvaise forme", ["ill"] = "Malade" };
    private static readonly Dictionary<string, string> PhaseNames = new() { ["A_Start"] = "Début", ["B_Loop"] = "Boucle", ["C_End"] = "Fin", ["Single"] = "Unique" };

    public void Load(StudioWindow.StudioMod m, StudioWindow.StudioAnimation a)
    {
        if (mod?.Id != m.Id)
            cache.Clear();
        mod = m;
        anim = a;
        title.Text = $"{a.Name} · {a.Frames} images";
        var availableModes = a.Variants.Select(v => v.Mode).Distinct().OrderBy(x => Array.IndexOf(new[] { "nomal", "happy", "poorcondition", "ill" }, x)).ToList();
        if (!availableModes.Contains(mode))
            mode = availableModes.FirstOrDefault() ?? "nomal";
        modes.Children.Clear();
        foreach (var md in availableModes)
        {
            var chip = Chip(ModeNames.TryGetValue(md, out var n) ? n : md, "studio-mode", md == mode);
            var value = md;
            chip.Checked += (_, _) => { mode = value; Restart(); };
            modes.Children.Add(chip);
        }
        var availablePhases = a.Variants.Select(v => v.Phase).Distinct().ToList();
        phases.Children.Clear();
        var options = new List<(string key, string label)>();
        if (availablePhases.Count > 1)
            options.Add(("chain", "Enchaînement"));
        foreach (var p in new[] { "A_Start", "B_Loop", "C_End", "Single" })
            if (availablePhases.Contains(p))
                options.Add((p, PhaseNames[p]));
        if (!options.Any(o => o.key == phase))
            phase = options[0].key;
        foreach (var (key, label) in options)
        {
            var chip = Chip(label, "studio-phase", key == phase);
            var value = key;
            chip.Checked += (_, _) => { phase = value; Restart(); };
            phases.Children.Add(chip);
        }
        tryOnPet.IsEnabled = mw.Core.Graph?.GraphsList.ContainsKey(a.Name) == true;
        tryOnPet.ToolTip = tryOnPet.IsEnabled ? "Joue cette animation sur ton compagnon" : "Ce mod n'est pas chargé dans V-Max : l'aperçu ci-dessus suffit pour le tri";
        Restart();
    }

    private void Restart()
    {
        if (mod == null || anim == null)
            return;
        timer.Stop();
        loading?.Cancel();
        var cts = loading = new CancellationTokenSource();
        var variants = anim.Variants.Where(v => v.Mode == mode).ToList();
        if (variants.Count == 0)
            variants = anim.Variants;
        List<StudioWindow.StudioVariant> chosen = phase switch
        {
            "chain" => variants.Where(v => v.Phase == "A_Start").Take(1)
                .Concat(variants.Where(v => v.Phase == "B_Loop"))
                .Concat(variants.Where(v => v.Phase == "B_Loop"))
                .Concat(variants.Where(v => v.Phase == "C_End").Take(1))
                .Concat(variants.Where(v => v.Phase == "Single"))
                .ToList(),
            _ => variants.Where(v => v.Phase == phase).ToList(),
        };
        status.Text = "Chargement des images…";
        _ = Task.Run(() =>
        {
            var seq = new List<List<(BitmapSource, int)>>();
            foreach (var v in chosen)
            {
                if (cts.IsCancellationRequested)
                    return;
                seq.Add(Frames(v.Path));
            }
            Dispatcher.BeginInvoke(() =>
            {
                if (cts.IsCancellationRequested)
                    return;
                sequence = seq.Where(s => s.Count > 0).ToList();
                seqIndex = 0;
                frame = 0;
                if (sequence.Count == 0)
                {
                    image.Source = null;
                    status.Text = "Aucune image lisible pour cette humeur.";
                    return;
                }
                Show();
                timer.Start();
                play.Content = "";
            });
        });
    }

    private List<(BitmapSource img, int ms)> Frames(string path)
    {
        lock (cache)
            if (cache.TryGetValue(path, out var hit))
                return hit;
        var list = new List<(BitmapSource, int)>();
        try
        {
            IEnumerable<string> files = Directory.Exists(path)
                ? Directory.GetFiles(path, "*.png").OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                : File.Exists(path) ? [path] : [];
            foreach (var f in files)
            {
                var b = new BitmapImage();
                b.BeginInit();
                b.CacheOption = BitmapCacheOption.OnLoad;
                b.UriSource = new Uri(f);
                b.DecodePixelWidth = 480;
                b.EndInit();
                b.Freeze();
                var m = Regex.Match(Path.GetFileNameWithoutExtension(f), @"_(\d+)$");
                list.Add((b, m.Success ? int.Parse(m.Groups[1].Value) : 125));
            }
        }
        catch (Exception e)
        {
            Debug.WriteLine("Studio : images illisibles " + path + " : " + e.Message);
        }
        lock (cache)
            cache[path] = list;
        return list;
    }

    private void Show()
    {
        if (sequence.Count == 0)
            return;
        var frames = sequence[seqIndex];
        var (img, ms) = frames[frame];
        image.Source = img;
        timer.Interval = TimeSpan.FromMilliseconds(Math.Max(15, ms / speed));
        status.Text = $"{(sequence.Count > 1 ? $"partie {seqIndex + 1}/{sequence.Count} · " : "")}image {frame + 1}/{frames.Count} · {ms} ms";
    }

    private void Advance()
    {
        if (sequence.Count == 0)
            return;
        frame++;
        if (frame >= sequence[seqIndex].Count)
        {
            frame = 0;
            seqIndex = (seqIndex + 1) % sequence.Count;
        }
        Show();
    }

    public void TogglePlay()
    {
        if (timer.IsEnabled)
        {
            timer.Stop();
            play.Content = "";
        }
        else if (sequence.Count > 0)
        {
            timer.Start();
            play.Content = "";
        }
    }

    public void Step(int delta)
    {
        if (sequence.Count == 0)
            return;
        timer.Stop();
        play.Content = "";
        frame += delta;
        if (frame >= sequence[seqIndex].Count)
        {
            frame = 0;
            seqIndex = (seqIndex + 1) % sequence.Count;
        }
        else if (frame < 0)
        {
            seqIndex = (seqIndex - 1 + sequence.Count) % sequence.Count;
            frame = sequence[seqIndex].Count - 1;
        }
        Show();
    }

    public void Stop()
    {
        timer.Stop();
        loading?.Cancel();
        cache.Clear();
        sequence = [];
    }

    private void TryOnPet()
    {
        if (anim == null || mw.Core.Graph == null || mw.Main == null)
            return;
        var name = anim.Name;
        var main = mw.Main;
        bool Has(GraphInfo.AnimatType t) => mw.Core.Graph.FindGraph(name, t, mw.Core.Save!.Mode) != null;
        void Phase(GraphInfo.AnimatType t, Action next)
        {
            if (Has(t))
                main.Display(name, t, next);
            else
                next();
        }
        // début → boucle → fin, puis retour au repos (ou l'animation unique)
        if (Has(GraphInfo.AnimatType.Single) && !Has(GraphInfo.AnimatType.A_Start))
            Phase(GraphInfo.AnimatType.Single, main.DisplayToNomal);
        else
            Phase(GraphInfo.AnimatType.A_Start, () => Phase(GraphInfo.AnimatType.B_Loop, () => Phase(GraphInfo.AnimatType.C_End, main.DisplayToNomal)));
    }
}
