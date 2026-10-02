using LinePutScript;
using LinePutScript.Localization.WPF;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using VPet_Simulator.Core;
using VPet_Simulator.Windows.Interface;

namespace VPet_Simulator.Windows.HUD;

/// <summary>
/// V-Max : gestionnaire de mods (remplace l'onglet « MOD管理 » des anciens paramètres).
/// À gauche : recherche, filtres et liste ; à droite : fiche du mod (versions, contenu, activation, dossier,
/// réglages du plugin, autorisation du code). Toute la logique reprend celle de winGameSetting
/// (ModInfo, ordre de la liste, compatibilité, détection des réglages, PluginTrustStore).
/// Les changements s'appliquent au redémarrage : une barre le rappelle en bas de la fenêtre.
/// </summary>
public sealed class ModsWindow : HudWindow
{
    private static ModsWindow? instance;

    /// <summary>Un changement attend un redémarrage (survit à la fermeture de la fenêtre)</summary>
    private static bool restartPending;

    private enum ModFilter { All, On, Off, Code }

    private readonly List<ModInfo> mods = new();
    private readonly Dictionary<string, ImageSource?> icons = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<ModInfo, Border> rows = new();
    private readonly TextBox search;
    private readonly WrapPanel chips = new() { Margin = new Thickness(0, 10, 0, 6) };
    private readonly StackPanel list = new();
    private readonly ScrollViewer listScroll;
    private readonly ScrollViewer detailScroll;
    private readonly Border restartBar;
    private ModFilter filter = ModFilter.All;
    private ModInfo? selected;

    private ModsWindow(MainWindow mw) : base(mw, "mods", "EXTENSIONS", "Mods", 1020, 700)
    {
        AddHeaderButton("", "Actualiser la liste", () =>
        {
            Reload();
            Notify("Liste des mods actualisée.");
        });
        AddHeaderButton("", "Ouvrir le dossier des mods", () =>
        {
            if (Directory.Exists(MainWindow.ModPath))
                OpenFolder(MainWindow.ModPath);
            else
                Notify("Le dossier des mods est introuvable.", HudToast.Kind.Warning);
        });

        // colonne de gauche : recherche, filtres, liste
        search = SearchBox("Nom, auteur, contenu…", out var searchBox);
        search.TextChanged += (_, _) => RenderList();
        listScroll = Scroll(list, new Thickness(0, 0, 8, 0));
        var left = new DockPanel { Width = 340, Margin = new Thickness(24, 0, 12, 20) };
        DockPanel.SetDock(searchBox, Dock.Top);
        DockPanel.SetDock(chips, Dock.Top);
        left.Children.Add(searchBox);
        left.Children.Add(chips);
        left.Children.Add(listScroll);

        // colonne de droite : fiche
        detailScroll = Scroll(new Grid(), new Thickness(12, 0, 24, 20));
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.Children.Add(left);
        Grid.SetColumn(detailScroll, 1);
        grid.Children.Add(detailScroll);
        Body = grid;

        restartBar = BuildRestartBar();
        Closed += (_, _) => instance = null;
        Reload();
        UpdateRestartBar();
    }

    /// <summary>Ouvre (ou ramène) le gestionnaire de mods, éventuellement sur un mod précis (nom interne du mod)</summary>
    public static ModsWindow Open(MainWindow mw, string? modName = null)
    {
        instance ??= new ModsWindow(mw);
        if (!string.IsNullOrWhiteSpace(modName))
            instance.Select(modName);
        instance.Present();
        return instance;
    }

    #region Données (reprise de winGameSetting)
    private bool IsOn(ModInfo m) => MW.Set.IsOnMod(m.Name);

    private static IEnumerable<DirectoryInfo> GetModDirectories()
    {
        if (Directory.Exists(MainWindow.ModPath))
        {
            foreach (var di in new DirectoryInfo(MainWindow.ModPath).EnumerateDirectories())
                yield return di;
        }
    }

    /// <summary>Relit les mods (chargés et présents dans le dossier) dans l'ordre de l'ancienne liste</summary>
    private void Reload()
    {
        string? selectedPath = selected?.Path.FullName;
        mods.Clear();
        var modInfoByPath = new Dictionary<string, ModInfo>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < MW.CoreMODs.Count; i++)
        {
            CoreMOD core = MW.CoreMODs[i];
            modInfoByPath[core.Path.FullName] = ModInfo.FromCoreMod(core, i);
        }
        try
        {
            foreach (var di in GetModDirectories())
            {
                if (!File.Exists(Path.Combine(di.FullName, "info.lps")))
                    continue;
                if (!modInfoByPath.ContainsKey(di.FullName))
                    modInfoByPath[di.FullName] = ModInfo.FromDirectory(di);
            }
        }
        catch { }

        mods.AddRange(modInfoByPath.Values
            .OrderByDescending(x => MW.Set.IsOnMod(x.Name))
            .ThenByDescending(x => MW.Set.IsOnMod(x.Name) && x.IsLoaded)
            .ThenBy(x => MW.Set.IsOnMod(x.Name) && x.IsLoaded ? x.LoadOrder : int.MaxValue)
            .ThenBy(x => !x.Path.FullName.Contains("DLC"))
            .ThenBy(x => !x.Author.Contains("LorisYounger"))
            .ThenBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase));

        selected = selectedPath == null ? null : mods.FirstOrDefault(x => x.Path.FullName.Equals(selectedPath, StringComparison.OrdinalIgnoreCase));
        BuildChips();
        RenderList();
    }

    private bool Passes(ModInfo m) => filter switch
    {
        ModFilter.On => IsOn(m),
        ModFilter.Off => !IsOn(m),
        ModFilter.Code => m.IsPlugin,
        _ => true,
    };

    /// <summary>Compatibilité de version (mêmes seuils que l'ancienne fiche) : texte, risque, explication</summary>
    private (string text, bool risky, string? explain) Compat(ModInfo m)
    {
        int v = MW.version;
        string gv = CoreMOD.INTtoVER(m.GameVer);
        if (m.GameVer / 100 == v / 100)
            return (gv, false, null);
        if (m.GameVer < v)
        {
            if (m.GameVer / 1000 == v / 1000)
                return (gv + " · compatible", false, null);
            string cmp = $"\nv{gv} < v{MW.Version}";
            return m.IsPlugin
                ? (gv + " · plus ancienne que V-Max : peut poser problème", true,
                    "Ce mod vise une version plus ancienne et contient du code : il peut y avoir de sérieux problèmes de compatibilité. Demande une mise à jour à son auteur." + cmp)
                : (gv + " · plus ancienne que V-Max : peut poser problème", false,
                    "Ce mod vise une version plus ancienne, mais la compatibilité de V-Max devrait le faire fonctionner. Pour être tranquille, demande une mise à jour à son auteur." + cmp);
        }
        if (m.GameVer / 1000 == v / 1000)
            return (gv + " · compatible", false, null);
        return (gv + " · prévue pour une version plus récente", true,
            $"Ce mod vise une version plus récente : il peut y avoir des problèmes de compatibilité. Mets V-Max à jour.\nv{gv} > v{MW.Version}");
    }

    private bool NeedsApproval(ModInfo m) => m.IsPlugin && !PluginTrustStore.IsModApproved(m.Name, m.Path);

    /// <summary>Plugin du mod qui a ses propres réglages (même détection que l'ancien bouton « Réglages »)</summary>
    private MainPlugin? SettingsPlugin(ModInfo m)
    {
        var mod = m.CoreMod;
        if (mod == null)
            return null;
        foreach (var mainplug in MW.Plugins)
        {
            try
            {
                if (mainplug.PluginName == mod.Name &&
                    mainplug.GetType().GetMethod("Setting")!.DeclaringType != typeof(MainPlugin)
                    && mainplug.GetType().Assembly.Location.Contains(mod.Path.FullName))
                    return mainplug;
            }
            catch { }
        }
        return null;
    }

    private ImageSource? ModIcon(ModInfo m)
    {
        string file = Path.Combine(m.Path.FullName, "icon.png");
        if (icons.TryGetValue(file, out var cached))
            return cached;
        ImageSource? img = null;
        try
        {
            if (File.Exists(file))
            {
                var bi = new BitmapImage();
                bi.BeginInit();
                bi.CacheOption = BitmapCacheOption.OnLoad;
                bi.StreamSource = new MemoryStream(File.ReadAllBytes(file));
                bi.DecodePixelWidth = 192;
                bi.EndInit();
                bi.Freeze();
                img = bi;
            }
        }
        catch { }
        icons[file] = img;
        return img;
    }
    #endregion

    #region Liste
    private void BuildChips()
    {
        chips.Children.Clear();
        void Chip(string label, ModFilter f, int count)
        {
            var c = new RadioButton { Style = St("HudChip"), Content = $"{label} · {count}", GroupName = "mods-filter", Margin = new Thickness(0, 0, 6, 6), IsChecked = filter == f };
            c.Checked += (_, _) =>
            {
                if (filter == f) return;
                filter = f;
                RenderList();
            };
            chips.Children.Add(c);
        }
        Chip("Tous", ModFilter.All, mods.Count);
        Chip("Activés", ModFilter.On, mods.Count(IsOn));
        Chip("Désactivés", ModFilter.Off, mods.Count(m => !IsOn(m)));
        Chip("Avec code", ModFilter.Code, mods.Count(m => m.IsPlugin));
    }

    private void RenderList()
    {
        var visible = mods.Where(m => Passes(m) && m.MatchesSearch(search.Text)).ToList();
        list.Children.Clear();
        rows.Clear();
        if (selected == null || !visible.Contains(selected))
            selected = visible.FirstOrDefault();
        foreach (var m in visible)
        {
            var row = Row(m);
            rows[m] = row;
            list.Children.Add(row);
        }
        if (visible.Count == 0)
            list.Children.Add(Empty(mods.Count == 0
                ? "Aucun mod pour l'instant. Dépose un mod dans le dossier « mod » de V-Max, puis actualise."
                : "Aucun mod ne correspond."));
        ShowDetail();
    }

    private void Select(string modName)
    {
        var m = mods.FirstOrDefault(x => x.Name == modName);
        if (m == null)
            return;
        if (!Passes(m) || !m.MatchesSearch(search.Text))
        {
            filter = ModFilter.All;
            BuildChips();
            search.Text = "";
            RenderList();
        }
        SetSelected(m);
        if (rows.TryGetValue(m, out var row))
            row.BringIntoView();
    }

    private void SetSelected(ModInfo m)
    {
        selected = m;
        foreach (var (info, row) in rows)
            StyleRow(row, info == m, false);
        ShowDetail();
    }

    private void StyleRow(Border row, bool sel, bool hover)
    {
        row.Background = sel ? Res("HudAccentSoft") : hover ? Res("HudSurfaceHover") : Brushes.Transparent;
        row.BorderBrush = sel ? Res("HudAccent") : Brushes.Transparent;
    }

    private Border Row(ModInfo m)
    {
        bool on = IsOn(m);
        var icon = IconTile(m, 40, 10, 18);
        icon.Margin = new Thickness(0, 0, 12, 0);
        icon.VerticalAlignment = VerticalAlignment.Top;
        icon.Opacity = on ? 1 : 0.55;

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = m.Name.Translate(), FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = Res(on ? "HudText" : "HudTextMuted"), TextTrimming = TextTrimming.CharacterEllipsis });
        if (!string.IsNullOrWhiteSpace(m.Author))
            text.Children.Add(new TextBlock { Text = m.Author, FontSize = 12, Foreground = Res("HudTextMuted"), TextTrimming = TextTrimming.CharacterEllipsis });
        var badges = new WrapPanel { Margin = new Thickness(0, 2, 0, 0) };
        badges.Children.Add(Badge(on ? "Activé" : "Désactivé", on ? "HudSuccess" : "HudTextMuted"));
        if (m.IsPlugin)
            badges.Children.Add(Badge("Code", "HudSilver"));
        if (NeedsApproval(m))
            badges.Children.Add(Badge("À autoriser", "HudAmber"));
        if (Compat(m).risky)
            badges.Children.Add(Badge("Version", "HudAmber"));
        text.Children.Add(badges);

        var dock = new DockPanel();
        DockPanel.SetDock(icon, Dock.Left);
        dock.Children.Add(icon);
        dock.Children.Add(text);
        var row = new Border
        {
            Padding = new Thickness(10, 9, 10, 9), Margin = new Thickness(0, 0, 0, 4), CornerRadius = new CornerRadius(12),
            BorderThickness = new Thickness(1), Child = dock, Cursor = Cursors.Hand,
        };
        StyleRow(row, selected == m, false);
        row.MouseEnter += (_, _) => StyleRow(row, selected == m, true);
        row.MouseLeave += (_, _) => StyleRow(row, selected == m, false);
        row.MouseLeftButtonUp += (_, _) => { if (selected != m) SetSelected(m); };
        return row;
    }

    private Border Badge(string text, string brush) => new()
    {
        CornerRadius = new CornerRadius(8), BorderBrush = Res(brush), BorderThickness = new Thickness(1),
        Padding = new Thickness(7, 0, 7, 1), Margin = new Thickness(0, 3, 4, 0),
        Child = new TextBlock { Text = text, FontFamily = (FontFamily)FindResource("HudDisplay"), FontWeight = FontWeights.SemiBold, FontSize = 10.5, Foreground = Res(brush) },
    };

    /// <summary>Icône du mod (icon.png) en tuile arrondie, ou pièce de puzzle</summary>
    private Border IconTile(ModInfo m, double size, double radius, double glyphSize)
    {
        var img = ModIcon(m);
        return new Border
        {
            Width = size, Height = size, CornerRadius = new CornerRadius(radius),
            Background = img != null ? new ImageBrush(img) { Stretch = Stretch.UniformToFill } : Res("HudSurfaceHover"),
            BorderBrush = Res("HudStroke"), BorderThickness = new Thickness(1),
            Child = img != null ? null : new TextBlock
            {
                Text = "", FontFamily = (FontFamily)FindResource("HudIcons"), FontSize = glyphSize, Foreground = Res("HudTextMuted"),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            },
        };
    }
    #endregion

    #region Fiche
    private void ShowDetail()
    {
        detailScroll.Content = selected == null ? Empty(mods.Count == 0 ? "Rien à afficher." : "Choisis un mod à gauche.") : Detail(selected);
        detailScroll.ScrollToTop();
    }

    private FrameworkElement Detail(ModInfo m)
    {
        bool on = IsOn(m);
        var compat = Compat(m);
        var sp = new StackPanel();

        // en-tête : icône, statut, nom, auteur
        var icon = IconTile(m, 88, 20, 36);
        icon.Margin = new Thickness(0, 0, 18, 0);
        icon.VerticalAlignment = VerticalAlignment.Top;
        var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        titles.Children.Add(new TextBlock { Text = on ? "ACTIVÉ" : "DÉSACTIVÉ", Style = St("HudEyebrow"), Foreground = Res(on ? "HudSuccess" : "HudTextMuted") });
        titles.Children.Add(new TextBlock { Text = m.Name.Translate(), Style = St("HudTitle"), FontSize = 24, TextWrapping = TextWrapping.Wrap, Foreground = Res(on ? (compat.risky ? "HudAmber" : "HudText") : "HudTextMuted") });
        titles.Children.Add(new TextBlock { Text = string.IsNullOrWhiteSpace(m.Author) ? "Auteur inconnu" : "par " + m.Author, FontSize = 13, Foreground = Res("HudTextMuted"), TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 2, 0, 0) });
        var head = new DockPanel();
        DockPanel.SetDock(icon, Dock.Left);
        head.Children.Add(icon);
        head.Children.Add(titles);
        sp.Children.Add(head);

        // versions
        var facts = new Grid { Margin = new Thickness(0, 18, 0, 0) };
        facts.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        facts.ColumnDefinitions.Add(new ColumnDefinition());
        void Fact(string label, string value, string brush = "HudText", bool mono = false)
        {
            int r = facts.RowDefinitions.Count;
            facts.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var l = new TextBlock { Text = label, FontSize = 12.5, Foreground = Res("HudTextMuted"), Margin = new Thickness(0, 0, 12, 6) };
            var v = new TextBlock { Text = value, FontSize = 13, Foreground = Res(brush), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6) };
            if (mono)
                v.FontFamily = (FontFamily)FindResource("HudMono");
            Grid.SetRow(l, r);
            Grid.SetRow(v, r);
            Grid.SetColumn(v, 1);
            facts.Children.Add(l);
            facts.Children.Add(v);
        }
        Fact("Version du mod", CoreMOD.INTtoVER(m.Ver));
        Fact("Version de jeu", compat.text, compat.risky ? "HudAmber" : "HudText");
        Fact("Dossier", m.Path.Name, "HudTextMuted", true);
        sp.Children.Add(facts);
        if (compat.explain != null)
            sp.Children.Add(Note("", compat.explain, compat.risky ? "HudAmber" : "HudTextMuted"));

        // description et contenu
        sp.Children.Add(Section("Description"));
        string intro = m.Intro.Translate();
        sp.Children.Add(new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(intro) ? "Pas de description." : intro,
            Style = St("HudBodyText"), Foreground = Res(string.IsNullOrWhiteSpace(intro) ? "HudTextMuted" : "HudText"),
        });
        if (m.Tag.Count > 0)
        {
            sp.Children.Add(Section("Contenu"));
            var tags = new WrapPanel();
            foreach (string tag in m.Tag)
                tags.Children.Add(new Border
                {
                    CornerRadius = new CornerRadius(12), BorderBrush = Res(tag == "plugin" ? "HudSilver" : "HudStroke"), BorderThickness = new Thickness(1),
                    Background = Res("HudSurfaceHover"), Padding = new Thickness(10, 3, 10, 4), Margin = new Thickness(0, 0, 6, 6),
                    Child = new TextBlock { Text = tag.Translate(), FontSize = 12, Foreground = Res("HudText") },
                });
            sp.Children.Add(tags);
        }

        // code à autoriser
        if (NeedsApproval(m))
        {
            var box = new StackPanel();
            var line = new DockPanel();
            var shield = new TextBlock { Text = "", FontFamily = (FontFamily)FindResource("HudIcons"), FontSize = 18, Foreground = Res("HudAmber"), Margin = new Thickness(0, 1, 12, 0), VerticalAlignment = VerticalAlignment.Top };
            DockPanel.SetDock(shield, Dock.Left);
            line.Children.Add(shield);
            var t = new StackPanel();
            t.Children.Add(new TextBlock { Text = "Ce mod contient du code", FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = Res("HudAmber") });
            t.Children.Add(new TextBlock { Text = "Tant que tu ne l'as pas autorisé, son code n'est pas chargé. N'autorise que des mods de confiance.", Style = St("HudBodyText"), FontSize = 13, Foreground = Res("HudTextMuted"), Margin = new Thickness(0, 2, 0, 10) });
            var allow = GlyphButton("", "Autoriser le code…", () => Approve(m));
            allow.Foreground = Res("HudAmber");
            allow.HorizontalAlignment = HorizontalAlignment.Left;
            t.Children.Add(allow);
            line.Children.Add(t);
            box.Children.Add(line);
            sp.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(14), BorderBrush = Res("HudAmber"), BorderThickness = new Thickness(1),
                Background = Res("HudSurfaceHover"), Padding = new Thickness(14, 12, 14, 14), Margin = new Thickness(0, 20, 0, 0), Child = box,
            });
        }

        // actions
        bool locked = m.Name == "Core" || CoreMOD.OnModDefList.Contains(m.Name);
        var actions = new WrapPanel { Margin = new Thickness(0, 22, 0, 0) };
        void Add(Button b)
        {
            b.Margin = new Thickness(0, 0, 10, 8);
            actions.Children.Add(b);
        }
        if (!locked)
        {
            if (on)
                Add(GlyphButton("", "Désactiver", () => Disable(m)));
            else
                Add(GlyphButton("", "Activer", () => Enable(m), primary: true));
        }
        Add(GlyphButton("", "Ouvrir le dossier", () => OpenFolder(m.Path.FullName)));
        if (SettingsPlugin(m) is { } plugin)
            Add(GlyphButton("", "Réglages du mod", () =>
            {
                try
                {
                    plugin.Setting();
                }
                catch (Exception e)
                {
                    Notify("Impossible d'ouvrir les réglages de ce mod.\n" + e.Message, HudToast.Kind.Warning);
                }
            }));
        sp.Children.Add(actions);
        if (locked)
            sp.Children.Add(Note("", m.Name == "Core"
                ? "Core contient les fichiers de base de V-Max : il reste toujours activé."
                : "Ce mod fait partie de la base de V-Max : il reste toujours activé.", "HudTextMuted"));

        var card = Card(sp, new Thickness(22, 20, 22, 20));
        card.VerticalAlignment = VerticalAlignment.Top;
        return card;
    }

    private FrameworkElement Note(string glyph, string text, string brush)
    {
        var dock = new DockPanel { Margin = new Thickness(0, 6, 0, 0) };
        var g = new TextBlock { Text = glyph, FontFamily = (FontFamily)FindResource("HudIcons"), FontSize = 13, Foreground = Res(brush), Margin = new Thickness(0, 2, 8, 0), VerticalAlignment = VerticalAlignment.Top };
        DockPanel.SetDock(g, Dock.Left);
        dock.Children.Add(g);
        dock.Children.Add(new TextBlock { Text = text, FontSize = 12.5, Foreground = Res(brush), TextWrapping = TextWrapping.Wrap, LineHeight = 18 });
        return dock;
    }

    /// <summary>Bouton avec icône (fantôme, ou principal pour l'unique action rubis)</summary>
    private Button GlyphButton(string glyph, string text, Action run, bool primary = false)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        content.Children.Add(new TextBlock { Text = glyph, FontFamily = (FontFamily)FindResource("HudIcons"), FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 1, 8, 0) });
        content.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });
        var b = new Button { Style = St(primary ? "HudPrimaryButton" : "HudGhostButton"), Content = content };
        b.Click += (_, _) => run();
        return b;
    }
    #endregion

    #region Actions
    private void Enable(ModInfo m)
    {
        MW.Set.OnMod(m.Name);
        MarkRestart();
        Notify($"« {m.Name.Translate()} » sera activé au prochain démarrage.", HudToast.Kind.Success);
        Reload();
    }

    private void Disable(ModInfo m)
    {
        if (m.Name == "Core")
        {
            VDialog.Show(this, "Core contient les fichiers de base de V-Max : il ne peut pas être désactivé.", "Désactivation impossible");
            return;
        }
        else if (CoreMOD.OnModDefList.Contains(m.Name))
            return;
        MW.Set.OnModRemove(m.Name);
        MarkRestart();
        Notify($"« {m.Name.Translate()} » sera désactivé au prochain démarrage.");
        Reload();
    }

    private void Approve(ModInfo modInfo)
    {
        // V-Max : avertissement de sécurité volontairement NON traduisible par les mods (anti-usurpation, AUDIT S-05)
        var dlls = string.Join("\n", PluginTrustStore.PluginDlls(modInfo.Path).Select(d => "  • " + d.Name + "  (SHA-256 " + PluginTrustStore.ComputeHash(d.FullName)[..16] + "…)"));
        if (VDialog.Show(this, $"Autoriser le code du mod « {modInfo.Name} » ?\n\n"
            + "Un plugin de code s'exécute avec tous vos droits : il peut lire vos fichiers, accéder au réseau et lancer des programmes.\n"
            + "N'autorisez que des mods de confiance. L'autorisation est liée à la version exacte des fichiers ci-dessous ; "
            + "toute modification demandera une nouvelle autorisation.\n\n" + dlls,
            "Autoriser un plugin de code", MessageBoxButton.YesNo, Panuon.WPF.UI.MessageBoxIcon.Warning) == MessageBoxResult.Yes)
        {
            PluginTrustStore.ApproveMod(modInfo.Name, modInfo.Path);
            MarkRestart();
            Notify("Code autorisé : il sera chargé au prochain démarrage.", HudToast.Kind.Success);
            Reload();
        }
    }

    private void OpenFolder(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
        }
        catch (Exception e)
        {
            Notify("Impossible d'ouvrir le dossier.\n" + e.Message, HudToast.Kind.Warning);
        }
    }

    private void MarkRestart()
    {
        restartPending = true;
        UpdateRestartBar();
    }

    private void UpdateRestartBar() => Footer = restartPending ? restartBar : null;

    private Border BuildRestartBar()
    {
        var glyph = new TextBlock { Text = "", FontFamily = (FontFamily)FindResource("HudIcons"), FontSize = 15, Foreground = Res("HudAmber"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
        var text = new TextBlock { Text = "Redémarre V-Max pour appliquer les changements.", FontSize = 13.5, Foreground = Res("HudText"), VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
        var button = Primary("Redémarrer maintenant", RestartNow);
        button.Margin = new Thickness(12, 0, 0, 0);
        var dock = new DockPanel();
        DockPanel.SetDock(glyph, Dock.Left);
        DockPanel.SetDock(button, Dock.Right);
        dock.Children.Add(glyph);
        dock.Children.Add(button);
        dock.Children.Add(text);
        return new Border
        {
            Margin = new Thickness(24, 0, 24, 18), Padding = new Thickness(16, 10, 10, 10), CornerRadius = new CornerRadius(14),
            Background = Res("HudSurfaceRaised"), BorderBrush = Res("HudAmber"), BorderThickness = new Thickness(1), Child = dock,
        };
    }

    private void RestartNow()
    {
        if (VDialog.Show(this, "Redémarrer V-Max maintenant ?\nTa partie est sauvegardée avant le redémarrage.", "Redémarrer V-Max",
                MessageBoxButton.YesNo, Panuon.WPF.UI.MessageBoxIcon.Warning) != MessageBoxResult.Yes)
            return;
        restartPending = false;
        Close();
        MW.Restart();
    }
    #endregion

    /// <summary>Fiche d'un mod, chargé ou seulement présent dans le dossier (copie de winGameSetting.ModInfo)</summary>
    private sealed class ModInfo
    {
        public CoreMOD? CoreMod { get; }
        public int LoadOrder { get; }
        public string Name { get; }
        public string Author { get; }
        public long AuthorID { get; }
        public ulong ItemID { get; }
        public string Intro { get; }
        public DirectoryInfo Path { get; }
        public int GameVer { get; }
        public int Ver { get; }
        public HashSet<string> Tag { get; }

        public bool IsLoaded => CoreMod != null;
        public bool IsPlugin => Tag.Contains("plugin");

        private ModInfo(CoreMOD? coreMod, int loadOrder, string name, string author, long authorID, ulong itemID, string intro, DirectoryInfo path, int gameVer, int ver, HashSet<string> tag)
        {
            CoreMod = coreMod;
            LoadOrder = loadOrder;
            Name = name;
            Author = author;
            AuthorID = authorID;
            ItemID = itemID;
            Intro = intro;
            Path = path;
            GameVer = gameVer;
            Ver = ver;
            Tag = tag;
        }

        public bool MatchesSearch(string? searchText)
        {
            if (string.IsNullOrWhiteSpace(searchText))
                return true;

            var terms = searchText.Split(new[] { ' ', '\t', '\\', '/', ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (terms.Length == 0)
                return true;

            var tagTranslateText = string.Join(" ", Tag.Select(x => x.Translate()));
            var searchPool = string.Join("\n", new[]
            {
                Name,
                Name.Translate(),
                Intro,
                Intro.Translate(),
                Author,
                tagTranslateText
            });

            return terms.Any(term => searchPool.Contains(term, StringComparison.OrdinalIgnoreCase));
        }

        public static ModInfo FromCoreMod(CoreMOD mod, int loadOrder)
        {
            return new ModInfo(mod, loadOrder, mod.Name, mod.Author, mod.AuthorID, mod.ItemID, mod.Intro,
                mod.Path, mod.GameVer, mod.Ver, new HashSet<string>(mod.Tag));
        }

        public static ModInfo FromDirectory(DirectoryInfo directory)
        {
            string name = directory.Name;
            string author = string.Empty;
            long authorID = 0;
            ulong itemID = 0;
            string intro = string.Empty;
            int gameVer = 0;
            int ver = 0;
            HashSet<string> tag = new HashSet<string>();

            foreach (var di in directory.EnumerateDirectories())
                tag.Add(di.Name.ToLowerInvariant());

            string infoFile = System.IO.Path.Combine(directory.FullName, "info.lps");
            if (File.Exists(infoFile))
            {
                try
                {
                    var modlps = new LpsDocument(File.ReadAllText(infoFile));
                    name = modlps.FindLine("vupmod")?.Info ?? name;
                    intro = modlps.FindLine("intro")?.Info ?? string.Empty;
                    gameVer = modlps.FindSub("gamever")?.InfoToInt ?? 0;
                    ver = modlps.FindSub("ver")?.InfoToInt ?? 0;
                    author = modlps.FindSub("author")?.Info.Split('[').FirstOrDefault() ?? string.Empty;
                    authorID = modlps.FindLine("authorid")?.InfoToInt64 ?? 0;
                    var itemStr = modlps.FindLine("itemid")?.info;
                    if (!string.IsNullOrWhiteSpace(itemStr))
                        ulong.TryParse(itemStr, out itemID);
                }
                catch
                {
                }
            }

            return new ModInfo(null, int.MaxValue, name, author, authorID, itemID, intro, directory, gameVer, ver, tag);
        }
    }
}
