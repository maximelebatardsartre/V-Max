using LinePutScript;
using LinePutScript.Localization.WPF;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using VPet_Simulator.Core;
using VPet_Simulator.Windows.Interface;

namespace VPet_Simulator.Windows;

/// <summary>
/// V-Max : fenêtre de paramètres 2026 (mode Essentiel / Avancé, recherche, catégories, application immédiate).
/// Les réglages sont décrits de façon déclarative dans <see cref="DefineSettings"/> ; l'interface est générée.
/// Les fonctions non encore portées restent accessibles via « Interface classique ».
/// </summary>
public partial class winVMaxSettings : Window
{
    private readonly MainWindow mw;
    private readonly List<Category> categories = new();
    private readonly List<SettingDef> settings = new();
    private bool advanced;
    private bool loading = true;
    private string? currentCategory;

    private static string T(string s) => s.Translate();

    private sealed record Category(string Key, string Title, string Icon, string Subtitle);

    private sealed class SettingDef
    {
        public required string Category;
        public required string Section;
        public required string Title;
        public string Description = "";
        public bool Advanced;
        public bool RequiresRestart;
        public required Func<FrameworkElement> Build;
        /// <summary>Contrôle sur toute la largeur, sous le titre (texte long, liens…)</summary>
        public bool FullWidth;
    }

    public winVMaxSettings(MainWindow mw)
    {
        this.mw = mw;
        InitializeComponent();
        Root.Opacity = 0;
        advanced = mw.Set["vmax"].GetBool("settings_advanced");
        DefineCategories();
        DefineSettings();
        foreach (var c in categories)
            NavList.Items.Add(new ListBoxItem { Content = NavContent(c), Tag = c.Key });
        (advanced ? RbAdvanced : RbBasic).IsChecked = true;
        loading = false;
        NavList.SelectedIndex = 0;
    }

    #region Fenêtre (effets, animation d'ouverture, raccourcis)
    private void Window_SourceInitialized(object? sender, EventArgs e)
    {
        if (WindowEffects.Apply(this, WindowEffects.Backdrop.Mica, mw.IsDarkTheme))
            Background = Brushes.Transparent;
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        Root.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)) { EasingFunction = ease });
        RootScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.98, 1, TimeSpan.FromMilliseconds(260)) { EasingFunction = ease });
        RootScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.98, 1, TimeSpan.FromMilliseconds(260)) { EasingFunction = ease });
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control)
        {
            TbSearch.Focus();
            TbSearch.SelectAll();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            if (TbSearch.Text.Length > 0)
                TbSearch.Text = "";
            else
                Close();
            e.Handled = true;
        }
    }

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        mw.Topmost = mw.Set.TopMost;
        mw.winVMaxSetting = null;
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
    private void Restart_Click(object sender, RoutedEventArgs e) => mw.Restart();

    private void Legacy_Click(object sender, RoutedEventArgs e)
    {
        int page = currentCategory switch
        {
            "compagnon" => 2,
            "sauvegardes" or "general" => 1,
            "extensions" => 5,
            "apropos" => 6,
            _ => 0,
        };
        mw.ShowLegacySetting(page);
    }
    #endregion

    #region Navigation, mode, recherche
    private static FrameworkElement NavContent(Category c)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        sp.Children.Add(new TextBlock { Text = c.Icon, FontFamily = (FontFamily)Application.Current.FindResource("VMaxIconFont"), FontSize = 16, VerticalAlignment = VerticalAlignment.Center, Width = 28 });
        sp.Children.Add(new TextBlock { Text = c.Title, VerticalAlignment = VerticalAlignment.Center, FontSize = 14 });
        return sp;
    }

    private void NavList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (NavList.SelectedItem is ListBoxItem item && item.Tag is string key)
        {
            if (TbSearch.Text.Length > 0)
            {
                loading = true;
                TbSearch.Text = "";
                loading = false;
            }
            ShowCategory(key);
        }
    }

    private void Mode_Checked(object sender, RoutedEventArgs e)
    {
        advanced = RbAdvanced.IsChecked == true;
        if (loading)
            return;
        mw.Set["vmax"].SetBool("settings_advanced", advanced);
        Refresh();
    }

    private void TbSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (loading)
            return;
        Refresh();
    }

    private void Refresh()
    {
        if (TbSearch.Text.Trim().Length > 0)
            ShowSearch(TbSearch.Text.Trim());
        else if (currentCategory != null)
            ShowCategory(currentCategory);
    }

    private static string Fold(string s)
    {
        var d = s.ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(d.Length);
        foreach (var ch in d)
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
                sb.Append(ch);
        return sb.ToString();
    }

    private void ShowSearch(string query)
    {
        var q = Fold(query);
        var hits = settings.Where(s => Fold(T(s.Title) + " " + T(s.Description) + " " + T(s.Section)).Contains(q)).ToList();
        BeginPage();
        AddHeader(T("Recherche"), hits.Count == 0 ? T("Aucun réglage ne correspond à « {0} ».").Replace("{0}", query)
            : (hits.Count == 1 ? T("1 réglage trouvé") : T("{0} réglages trouvés").Replace("{0}", hits.Count.ToString())));
        foreach (var group in hits.GroupBy(h => h.Category))
        {
            var cat = categories.First(c => c.Key == group.Key);
            RenderSection(cat.Title, group, showAdvancedBadge: true);
        }
        EndPage();
    }

    private void ShowCategory(string key)
    {
        currentCategory = key;
        var cat = categories.First(c => c.Key == key);
        BeginPage();
        AddHeader(cat.Title, cat.Subtitle);
        var visible = settings.Where(s => s.Category == key && (advanced || !s.Advanced)).ToList();
        foreach (var section in visible.GroupBy(s => s.Section))
            RenderSection(section.Key, section, showAdvancedBadge: false);
        int hidden = settings.Count(s => s.Category == key && s.Advanced) - (advanced ? settings.Count(s => s.Category == key && s.Advanced) : 0);
        if (hidden > 0)
        {
            var link = new Button { Style = (Style)FindResource("VMaxButton"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 8, 0, 0) };
            link.Content = hidden == 1 ? T("Afficher 1 réglage avancé") : T("Afficher {0} réglages avancés").Replace("{0}", hidden.ToString());
            link.Click += (_, _) => RbAdvanced.IsChecked = true;
            ContentHost.Children.Add(link);
        }
        EndPage();
    }

    private void BeginPage()
    {
        ContentHost.Children.Clear();
        ContentScroll.ScrollToTop();
    }

    private void EndPage()
    {
        // Micro-animation : le contenu glisse de 8 px et apparaît en fondu
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        ContentHost.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)) { EasingFunction = ease });
        ContentMove.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(8, 0, TimeSpan.FromMilliseconds(220)) { EasingFunction = ease });
    }

    private void AddHeader(string title, string subtitle)
    {
        ContentHost.Children.Add(new TextBlock { Text = title, FontSize = 28, FontWeight = FontWeights.SemiBold, Margin = new Thickness(2, 4, 0, 2) });
        if (!string.IsNullOrEmpty(subtitle))
            ContentHost.Children.Add(new TextBlock { Text = subtitle, FontSize = 13, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, 0, 0, 4), Foreground = (Brush)FindResource("VMaxSubtleText") });
    }

    private void RenderSection(string section, IEnumerable<SettingDef> defs, bool showAdvancedBadge)
    {
        ContentHost.Children.Add(new TextBlock { Text = T(section), Style = (Style)FindResource("VMaxSectionTitle") });
        var card = new Border { Style = (Style)FindResource("VMaxCardStyle") };
        var stack = new StackPanel();
        card.Child = stack;
        bool first = true;
        foreach (var def in defs)
        {
            if (!first)
                stack.Children.Add(new Border { Height = 1, Background = (Brush)FindResource("VMaxStroke"), Margin = new Thickness(-16, 0, -16, 0) });
            first = false;
            stack.Children.Add(RenderRow(def, showAdvancedBadge));
        }
        ContentHost.Children.Add(card);
    }

    private FrameworkElement RenderRow(SettingDef def, bool showAdvancedBadge)
    {
        var grid = new Grid { Margin = new Thickness(0, 10, 0, 10), MinHeight = 40 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 16, 0) };
        var titleLine = new StackPanel { Orientation = Orientation.Horizontal };
        titleLine.Children.Add(new TextBlock { Text = T(def.Title), FontSize = 14, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center });
        if (def.RequiresRestart)
            titleLine.Children.Add(Badge(T("Redémarrage requis")));
        if (showAdvancedBadge && def.Advanced)
            titleLine.Children.Add(Badge(T("Avancé")));
        text.Children.Add(titleLine);
        if (!string.IsNullOrEmpty(def.Description))
            text.Children.Add(new TextBlock { Text = T(def.Description), FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0), Foreground = (Brush)FindResource("VMaxSubtleText") });
        grid.Children.Add(text);
        var control = def.Build();
        if (def.FullWidth)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetColumnSpan(text, 2);
            Grid.SetRow(control, 1);
            Grid.SetColumnSpan(control, 2);
            control.Margin = new Thickness(0, 8, 0, 0);
        }
        else
        {
            Grid.SetColumn(control, 1);
            control.VerticalAlignment = VerticalAlignment.Center;
        }
        grid.Children.Add(control);
        return grid;
    }

    private Border Badge(string text) => new Border
    {
        Style = (Style)FindResource("VMaxBadge"),
        Child = new TextBlock { Text = text, FontSize = 11 }
    };

    private void NeedRestart() => RestartBar.Visibility = Visibility.Visible;

    /// <summary>
    /// Affiche une catégorie (et éventuellement le mode avancé)
    /// </summary>
    public void OpenCategory(string? category, bool showAdvanced)
    {
        if (showAdvanced)
            RbAdvanced.IsChecked = true;
        var item = NavList.Items.OfType<ListBoxItem>().FirstOrDefault(i => (string)i.Tag == category);
        if (item != null)
            NavList.SelectedItem = item;
    }
    #endregion

    #region Fabriques de contrôles
    private FrameworkElement Toggle(Func<bool> get, Action<bool> set, bool restart = false)
    {
        var cb = new CheckBox { Style = (Style)FindResource("VMaxToggle"), IsChecked = get() };
        cb.Checked += (_, _) => { set(true); if (restart) NeedRestart(); };
        cb.Unchecked += (_, _) => { set(false); if (restart) NeedRestart(); };
        return cb;
    }

    private FrameworkElement SliderRow(double min, double max, double step, Func<double> get, Action<double> set, Func<double, string> format, bool restart = false)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        var value = new TextBlock { Width = 64, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0), Foreground = (Brush)FindResource("VMaxSubtleText") };
        var slider = new Slider
        {
            Width = 220, Minimum = min, Maximum = max, TickFrequency = step, IsSnapToTickEnabled = true,
            Value = Math.Clamp(get(), min, max), VerticalAlignment = VerticalAlignment.Center,
        };
        if (TryFindResource("StandardSliderStyle") is Style s)
            slider.Style = s;
        value.Text = format(slider.Value);
        var debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        debounce.Tick += (_, _) =>
        {
            debounce.Stop();
            set(slider.Value);
            if (restart) NeedRestart();
        };
        slider.ValueChanged += (_, _) =>
        {
            value.Text = format(slider.Value);
            debounce.Stop();
            debounce.Start();
        };
        sp.Children.Add(value);
        sp.Children.Add(slider);
        return sp;
    }

    private FrameworkElement Combo(IList<string> items, Func<int> getIndex, Action<int> setIndex, bool restart = false, double width = 240)
    {
        var cb = new ComboBox { Width = width };
        if (TryFindResource("StandardComboBoxStyle") is Style s)
            cb.Style = s;
        foreach (var i in items)
            cb.Items.Add(i);
        cb.SelectedIndex = getIndex();
        cb.SelectionChanged += (_, _) =>
        {
            if (cb.SelectedIndex < 0)
                return;
            setIndex(cb.SelectedIndex);
            if (restart) NeedRestart();
        };
        return cb;
    }

    private FrameworkElement TextField(Func<string> get, Action<string> set, string placeholder = "", double width = 240)
    {
        var tb = new TextBox { Style = (Style)FindResource("VMaxTextBox"), Width = width, Text = get(), Tag = placeholder };
        tb.LostFocus += (_, _) => set(tb.Text);
        tb.KeyDown += (_, e) => { if (e.Key == Key.Enter) set(tb.Text); };
        return tb;
    }

    private FrameworkElement ActionButton(string text, Action action, bool accent = false)
    {
        var b = new Button { Style = (Style)FindResource(accent ? "VMaxAccentButton" : "VMaxButton"), Content = text, MinWidth = 120 };
        b.Click += (_, _) => action();
        return b;
    }

    private FrameworkElement Link(string text, string url)
    {
        var tb = new TextBlock();
        var h = new Hyperlink(new Run(text)) { Foreground = (Brush)FindResource("DARKPrimary") };
        h.Click += (_, _) => ExtensionFunction.StartURL(url);
        tb.Inlines.Add(h);
        return tb;
    }
    #endregion

    #region Définition des catégories et des réglages
    private void DefineCategories()
    {
        categories.Add(new("general", T("Général"), "", T("Démarrage, comportement de la fenêtre et langue.")));
        categories.Add(new("apparence", T("Apparence"), "", T("Thème, taille et rendu du compagnon.")));
        categories.Add(new("compagnon", T("Compagnon"), "", T("Identité, déplacements et comportement de ton compagnon.")));
        categories.Add(new("ia", T("Intelligence artificielle"), "", T("Discussion et agent IA.")));
        categories.Add(new("sauvegardes", T("Sauvegardes"), "", T("Enregistrement automatique et copies de secours.")));
        categories.Add(new("extensions", T("Extensions"), "", T("Mods, plugins et raccourcis personnalisés.")));
        categories.Add(new("apropos", T("À propos"), "", T("Version, crédits et licences.")));
    }

    private static string LanguageName(string code) => code switch
    {
        "fr" => "Français",
        "en" => "English",
        "zh-Hans" => "简体中文",
        "zh-Hant" => "繁體中文",
        "ja" => "日本語",
        _ => code,
    };

    private void Add(string category, string section, string title, string description, Func<FrameworkElement> build,
        bool advancedOnly = false, bool restart = false, bool fullWidth = false)
        => settings.Add(new SettingDef
        {
            Category = category, Section = section, Title = title, Description = description, Build = build,
            Advanced = advancedOnly, RequiresRestart = restart, FullWidth = fullWidth,
        });

    private void DefineSettings()
    {
        var set = mw.Set;

        // ---------------- Général
        var cultures = LocalizeCore.AvailableCultures.Where(c => c != "null").ToList();
        Add("general", "Langue", "Langue de l'interface", "Langue des menus et des dialogues du compagnon.",
            () => Combo(cultures.Select(LanguageName).ToList(), () => cultures.IndexOf(set.Language),
                i => set.SetLanguage(cultures[i]), restart: true), restart: true);

        Add("general", "Démarrage", "Lancer avec Windows", "Démarre V-Max à l'ouverture de ta session.",
            () => Toggle(() => set.StartUPBoot, v => { set.StartUPBoot = v; StartupShortcut.Apply(mw); }));
        Add("general", "Démarrage", "Reprendre la dernière position", "Le compagnon réapparaît là où tu l'as laissé.",
            () => Toggle(() => set.StartRecordLast, v => set.StartRecordLast = v), advancedOnly: true);

        Add("general", "Fenêtre", "Toujours au premier plan", "Le compagnon reste visible au-dessus des autres fenêtres.",
            () => Toggle(() => set.TopMost, v =>
            {
                set.TopMost = v;
                mw.Topmost = v;
                SyncTrayCheck("NotifyIcon_TopMost", v);
            }));
        Add("general", "Fenêtre", "Clic traversant", "Les clics passent à travers le compagnon vers les fenêtres situées dessous.",
            () => Toggle(() => set.HitThrough, v =>
            {
                set["v"][(gbol)"HitThrough"] = true;
                set.HitThrough = v;
                if (v != mw.HitThrough)
                    mw.SetTransparentHitThrough();
                if (v && !set.PetHelper)
                {
                    set.PetHelper = true;
                    mw.LoadPetHelper();
                }
            }));
        Add("general", "Fenêtre", "Bouton flottant", "Petit bouton qui suit le compagnon pour basculer rapidement le premier plan et le clic traversant.",
            () => Toggle(() => set.PetHelper, v =>
            {
                set.PetHelper = v;
                if (v)
                    mw.LoadPetHelper();
                else
                {
                    mw.petHelper?.Close();
                    mw.petHelper = null;
                }
            }), advancedOnly: true);
        Add("general", "Fenêtre", "Masquer du sélecteur de tâches", "N'affiche pas V-Max dans Alt+Tab.",
            () => Toggle(() => set.HideFromTaskControl, v => set.HideFromTaskControl = v, restart: true), advancedOnly: true, restart: true);

        Add("general", "Développement", "Mode développeur", "Active la console de développement et les journaux détaillés.",
            () => Toggle(() => set.DeBug, v => set.DeBug = v, restart: true), advancedOnly: true, restart: true);

        // ---------------- Apparence
        var themeNames = new List<string> { T("Suivre Windows (clair / sombre)") };
        themeNames.AddRange(mw.Themes.Select(t => t.TranslateName));
        Add("apparence", "Thème", "Thème", "Couleurs de l'interface. « Suivre Windows » bascule automatiquement entre clair et sombre.",
            () => Combo(themeNames,
                () => mw.ThemeFollowsSystem ? 0 : mw.Themes.FindIndex(t => t.xName == mw.Theme?.xName) + 1,
                i =>
                {
                    if (i == 0)
                    {
                        mw.LoadTheme(SystemTheme.FollowSystem);
                        set.Theme = SystemTheme.FollowSystem;
                    }
                    else
                    {
                        mw.LoadTheme(mw.Themes[i - 1].xName);
                        set.Theme = mw.Theme?.xName ?? SystemTheme.FollowSystem;
                    }
                    WindowEffects.SetDark(this, mw.IsDarkTheme);
                }));
        var fontNames = new List<string> { T("Police système (Segoe UI)") };
        fontNames.AddRange(mw.Fonts.Select(f => f.TranslateName));
        Add("apparence", "Thème", "Police", "Police utilisée dans l'interface.",
            () => Combo(fontNames,
                () => set.Font == SystemTheme.SystemFont ? 0 : mw.Fonts.FindIndex(f => f.Name == set.Font) + 1,
                i =>
                {
                    string f = i == 0 ? SystemTheme.SystemFont : mw.Fonts[i - 1].Name;
                    mw.LoadFont(f);
                    set.Font = f;
                }), advancedOnly: true);

        Add("apparence", "Compagnon", "Taille du compagnon", "Taille d'affichage à l'écran.",
            () => SliderRow(0.25, 4, 0.05, () => set.ZoomLevel, v => mw.SetZoomLevel(v), v => $"{v * 100:0} %"));
        Add("apparence", "Compagnon", "Opacité", "Transparence du compagnon.",
            () => SliderRow(0.1, 1, 0.05, () => set.OpacityMain ? set.Opacity : 1, v =>
            {
                set.Opacity = v;
                set.OpacityMain = v < 0.999;
                mw.Opacity = set.OpacityMain ? v : 1;
            }, v => $"{v * 100:0} %"), advancedOnly: true);
        Add("apparence", "Compagnon", "Opacité seulement en clic traversant", "Le compagnon n'est transparent que lorsque le clic traversant est actif.",
            () => Toggle(() => set.OpacityHitThrough, v => set.OpacityHitThrough = v), advancedOnly: true);
        Add("apparence", "Compagnon", "Bulle à l'extérieur", "Affiche la bulle de dialogue au-dessus du compagnon plutôt que sur lui.",
            () => Toggle(() => set.MessageBarOutside, v =>
            {
                set.MessageBarOutside = v;
                if (v) mw.Main.MsgBar?.SetPlaceOUT(); else mw.Main.MsgBar?.SetPlaceIN();
            }), advancedOnly: true);
        if (mw.Pets.Count > 1)
            Add("apparence", "Compagnon", "Personnage", "Apparence et animations du compagnon.",
                () => Combo(mw.Pets.Select(p => p.Name.Translate()).ToList(),
                    () => Math.Max(0, mw.Pets.FindIndex(p => p.Name == set.PetGraph)),
                    i =>
                    {
                        var old = mw.Pets.Find(p => p.Name == set.PetGraph) ?? mw.Pets[0];
                        bool defaultName = mw.Core.Save!.Name == old.PetName.Translate();
                        set.PetGraph = mw.Pets[i].Name;
                        if (defaultName)
                            mw.Core.Save!.Name = mw.Pets[i].PetName.Translate();
                    }, restart: true), advancedOnly: true, restart: true);
        int screenWidth = (int)Math.Min(System.Windows.Forms.Screen.PrimaryScreen!.Bounds.Width, 1920);
        Add("apparence", "Rendu", "Résolution des animations", "Résolution du cache d'animations. Plus élevée = plus net mais plus de mémoire.",
            () => SliderRow(200, Math.Max(500, screenWidth), 10, () => set.Resolution, v => set.Resolution = (int)v, v => $"{v:0} px", restart: true),
            advancedOnly: true, restart: true);

        // ---------------- Compagnon
        Add("compagnon", "Identité", "Nom du compagnon", "",
            () => TextField(() => mw.Core.Save!.Name, v => { if (!string.IsNullOrWhiteSpace(v)) mw.Core.Save!.Name = v.Trim(); }));
        Add("compagnon", "Identité", "Ton prénom", "Comment ton compagnon t'appelle.",
            () => TextField(() => mw.GameSavesData.GameSave.HostName, v => mw.GameSavesData.GameSave.HostName = v.Trim(), T("Ton prénom")));
        Add("compagnon", "Identité", "Ton anniversaire", "Pour une petite surprise le jour venu (format JJ/MM/AAAA).",
            () => TextField(() => mw.GameSavesData.GetDateTime("HostBDay", mw.GameSavesData[(gdat)"birthday"]).ToString("dd/MM/yyyy"),
                v =>
                {
                    if (DateTime.TryParseExact(v.Trim(), "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) && d <= DateTime.Now)
                        mw.GameSavesData.SetDateTime("HostBDay", d);
                }, "JJ/MM/AAAA", 140));

        Add("compagnon", "Déplacements", "Se déplacer librement", "Le compagnon se promène, grimpe et explore ton bureau.",
            () => Toggle(() => set.AllowMove, v => set.SetAllowMove(v)));
        var smartIntervals = new[] { 30, 60, 120, 300, 600, 1200 };
        Add("compagnon", "Déplacements", "Déplacement intelligent", "Ne bouge que pendant un moment après ta dernière interaction.",
            () => Toggle(() => set.SmartMove, v => set.SetSmartMove(v)), advancedOnly: true);
        Add("compagnon", "Déplacements", "Durée du déplacement intelligent", "",
            () => Combo(smartIntervals.Select(s => s < 60 ? $"{s} s" : $"{s / 60} min").ToList(),
                () => Math.Max(0, Array.IndexOf(smartIntervals, set.SmartMoveInterval)),
                i => set.SetSmartMoveInterval(smartIntervals[i]), width: 140), advancedOnly: true);
        Add("compagnon", "Déplacements", "Changer d'écran automatiquement", "Adapte la zone de déplacement à l'écran où tu déposes le compagnon.",
            () => Toggle(() => set.AutoChangeWindow, v => set.AutoChangeWindow = v), advancedOnly: true);

        Add("compagnon", "Simulation", "Besoins du compagnon", "Faim, soif, humeur et endurance évoluent avec le temps.",
            () => Toggle(() => set.EnableFunction, v =>
            {
                set.EnableFunction = v;
                if (!v && mw.Main.State != Main.WorkingState.Nomal)
                {
                    mw.Main.WorkTimer!.Visibility = Visibility.Collapsed;
                    mw.Main.State = Main.WorkingState.Nomal;
                }
            }));
        var moods = new[] { T("Joyeux"), T("Normal"), T("Fatigué"), T("Malade") };
        Add("compagnon", "Simulation", "Humeur sans simulation", "Humeur affichée quand les besoins sont désactivés.",
            () => Combo(moods, () => (int)set.CalFunState, i =>
            {
                set.CalFunState = (IGameSave.ModeType)i;
                mw.Main.NoFunctionMOD = (IGameSave.ModeType)i;
                mw.Main.EventTimer_Elapsed();
            }, width: 160), advancedOnly: true);
        Add("compagnon", "Simulation", "Intervalle de calcul", "Fréquence de mise à jour des besoins et des comportements.",
            () => SliderRow(5, 60, 0.5, () => set.LogicInterval, v => set.SetLogicInterval(v), v => $"{v:0.#} s"), advancedOnly: true);
        Add("compagnon", "Simulation", "Fréquence des interactions spontanées", "Plus la valeur est basse, plus le compagnon agit de lui-même.",
            () => SliderRow(30, 1000, 10, () => set.InteractionCycle, v => set.InteractionCycle = (int)v, v => $"{v:0}"), advancedOnly: true);
        Add("compagnon", "Interactions", "Durée de l'appui long", "Temps d'appui avant de pouvoir soulever le compagnon.",
            () => SliderRow(0.05, 2, 0.05, () => set.PressLength / 1000.0, v => set.PressLength = (int)(v * 1000), v => $"{v:0.00} s"), advancedOnly: true);
        Add("compagnon", "Interactions", "Sensibilité à la musique", "Volume à partir duquel le compagnon se met à danser.",
            () => SliderRow(0.02, 1, 0.01, () => set.MusicCatch, v => set.MusicCatch = v, v => $"{v * 100:0} %"), advancedOnly: true);

        // ---------------- IA
        DefineAgentSettings();

        // ---------------- Sauvegardes
        var autosave = new[] { -1, 2, 5, 10, 20, 30, 60 };
        Add("sauvegardes", "Enregistrement", "Sauvegarde automatique", "",
            () => Combo(autosave.Select(a => a < 0 ? T("Désactivée") : a < 60 ? T("Toutes les {0} minutes").Replace("{0}", a.ToString()) : T("Toutes les heures")).ToList(),
                () => Math.Max(0, Array.IndexOf(autosave, set.AutoSaveInterval)), i => set.SetAutoSaveInterval(autosave[i])));
        Add("sauvegardes", "Enregistrement", "Enregistrer maintenant", "",
            () => ActionButton(T("Enregistrer"), () => { mw.Save(); Pulse(T("Enregistré")); }, accent: true));
        Add("sauvegardes", "Copies de secours", "Nombre de copies conservées", "Copies de secours gardées pour restaurer une ancienne sauvegarde.",
            () => SliderRow(1, 100, 1, () => set.BackupSaveMaxNum, v => set.BackupSaveMaxNum = (int)v, v => $"{v:0}"), advancedOnly: true);
        Add("sauvegardes", "Copies de secours", "Gestionnaire de sauvegardes", "Parcourir et restaurer les sauvegardes et leurs copies.",
            () => ActionButton(T("Ouvrir"), () => new winSaveManager(mw).ShowDialog()));
        Add("sauvegardes", "Copies de secours", "Dossier des données", ExtensionValue.DataDirectory,
            () => ActionButton(T("Ouvrir le dossier"), () => Process.Start(new ProcessStartInfo(ExtensionValue.DataDirectory) { UseShellExecute = true })?.Dispose()),
            advancedOnly: true);

        // ---------------- Extensions
        Add("extensions", "Mods", "Gestion des mods", "Activer, désactiver et autoriser les mods et leurs plugins de code.",
            () => ActionButton(T("Gérer les mods"), () => mw.ShowLegacySetting(5), accent: true));
        Add("extensions", "Mods", "Raccourcis personnalisés", "Boutons du menu « Personnalisé » (liens, programmes, raccourcis clavier).",
            () => ActionButton(T("Modifier"), () => mw.ShowLegacySetting(3)), advancedOnly: true);
        Add("extensions", "Maintenance", "Vider le cache des animations", "Reconstruit le cache au prochain démarrage (utile après une mise à jour de mod).",
            () => ActionButton(T("Vider"), () => { set.LastCacheDate = DateTime.MinValue; NeedRestart(); }), advancedOnly: true);

        // ---------------- À propos
        Add("apropos", "V-Max", "Version", $"V-Max {mw.Version}  ·  .NET {Environment.Version}  ·  {(ExtensionValue.IsPortable ? T("mode portable") : T("installation standard"))}",
            () => Link(T("Dépôt GitHub"), ExtensionValue.RepositoryURL));
        Add("apropos", "V-Max", "Signaler un problème", "Prépare un rapport et ouvre un ticket GitHub.",
            () => ActionButton(T("Signaler"), () => new winReport(mw).Show()));
        Add("apropos", "Crédits", "Basé sur VPet",
            "V-Max est un fork de VPet (Virtual Pet Simulator) de LorisYounger et exLB.org, publié sous licence Apache 2.0.",
            () => Link("github.com/LorisYounger/VPet", ExtensionValue.UpstreamURL));
        Add("apropos", "Crédits", "Animations du compagnon",
            "Les animations et images intégrées appartiennent à l'équipe VUP-Simulator et sont utilisées selon les conditions d'autorisation de VPet (mention de la source obligatoire, pas d'usage commercial sans accord).",
            () => Link(T("Conditions d'utilisation des animations"), ExtensionValue.UpstreamURL + "#animation-copyright-notice-and-authorization-terms"),
            fullWidth: true);
    }

    private void DefineAgentSettings()
    {
        var agent = mw.AgentPlugin?.Orchestrator;
        Add("ia", "Agent V-Max", "Discuter maintenant",
            "Ton compagnon discute avec une IA gratuite et peut agir sur ton PC avec ta permission. Ouvre aussi la discussion par l'anneau (clic droit) ou avec Ctrl+Alt+Espace.",
            () => ActionButton(T("Ouvrir la discussion"), () => { Close(); mw.Hud?.OpenChat(); }, accent: true));
        Add("ia", "Agent V-Max", "Nouvelle conversation", "Oublie l'échange en cours (l'agent ne garde aucune mémoire après la fermeture de V-Max).",
            () => ActionButton(T("Effacer"), () => { agent?.ResetConversation(); Pulse(T("Conversation effacée")); }));

        foreach (var provider in Agent.Providers.ProviderRouter.Catalog)
        {
            var p = provider;
            Add("ia", "IA connectées (bascule automatique)", p.Name, p.Tagline, () => ProviderEditor(p), fullWidth: true);
        }
        foreach (var provider in Agent.Providers.ProviderRouter.Catalog)
        {
            var p = provider;
            Add("ia", "Modèles", p.Name, p.Id == "gemini"
                    ? "« gemini-flash-latest » suit automatiquement le modèle rapide le plus récent."
                    : "Laisse vide pour un choix automatique (modèle gratuit compatible avec les actions).",
                () => ModelEditor(p), advancedOnly: true);
        }

        foreach (var tool in Agent.AgentToolRegistry.All)
        {
            var t = tool;
            string risk = t.Risk switch
            {
                Agent.ToolRisk.ReadOnly => T("Lecture seule"),
                Agent.ToolRisk.Low => T("Effet limité, exécuté directement"),
                Agent.ToolRisk.Sensitive => T("Demande ton accord"),
                _ => T("Demande ton accord à chaque fois"),
            };
            Add("ia", "Actions autorisées", t.Title, risk,
                () => Toggle(() => agent?.IsToolEnabled(t) ?? true, v => agent?.SetToolEnabled(t, v)), advancedOnly: true);
        }
        Add("ia", "Transparence", "Journal des actions", "Chaque action de l'agent (outil, arguments, décision, résultat) est enregistrée localement.",
            () => ActionButton(T("Ouvrir le journal"), () =>
            {
                var p = Agent.AgentOrchestrator.AuditLogPath;
                if (!System.IO.File.Exists(p))
                    System.IO.File.WriteAllText(p, "");
                Process.Start(new ProcessStartInfo(p) { UseShellExecute = true })?.Dispose();
            }), advancedOnly: true);
        Add("ia", "Transparence", "L'agent répond aussi aux plugins de discussion",
            "Utilise l'agent comme module de discussion de VPet (sinon, réponses intégrées ou plugin tiers).",
            () => Toggle(() => Agent.VMaxAgentPlugin.IsActive(mw), v => Agent.VMaxAgentPlugin.Activate(mw, v)), advancedOnly: true);
        Add("ia", "Transparence", "Module de discussion classique", "Réponses intégrées de VPet ou plugin de discussion tiers.",
            () => ActionButton(T("Configurer"), () => mw.ShowLegacySetting(1)), advancedOnly: true);
    }

    /// <summary>
    /// Ligne d'un fournisseur : état, clé (vérifiée avant enregistrement) ou détection de l'IA locale
    /// </summary>
    private FrameworkElement ProviderEditor(Agent.Providers.ProviderInfo p)
    {
        var root = new StackPanel();
        var line = new StackPanel { Orientation = Orientation.Horizontal };
        var status = new TextBlock { Margin = new Thickness(0, 6, 0, 0), FontSize = 12, Foreground = (Brush)FindResource("VMaxSubtleText") };
        void Refresh()
        {
            var (ok, text) = Agent.Providers.ProviderRouter.StatusOf(p);
            status.Text = text.Length == 0 ? T(p.IsLocal ? "Non détecté" : "Aucune clé enregistrée.") : (ok ? "✓ " : "") + T(text);
        }
        if (p.IsLocal)
        {
            var detect = (Button)ActionButton(T("Détecter"), async () =>
            {
                await Agent.Providers.ProviderRouter.RefreshLocalAsync();
                Agent.Providers.ProviderRouter.Reset(p.Id);
                Refresh();
            });
            line.Children.Add(detect);
        }
        else
        {
            var box = new PasswordBox
            {
                Width = 320, MinHeight = 32, Padding = new Thickness(8, 5, 8, 5), VerticalContentAlignment = VerticalAlignment.Center,
                Background = (Brush)FindResource("VMaxControlFill"), BorderBrush = (Brush)FindResource("VMaxControlStroke"),
                Foreground = (Brush)FindResource("PrimaryText"), CaretBrush = (Brush)FindResource("PrimaryText"),
            };
            System.Windows.Automation.AutomationProperties.SetName(box, T("Clé API") + " " + p.Name);
            var save = (Button)ActionButton(T("Vérifier et enregistrer"), async () =>
            {
                var key = box.Password.Trim();
                if (key.Length < 10) { Pulse(T("Clé trop courte")); return; }
                Pulse(T("Vérification de la clé…"));
                var error = await Agent.Providers.ProviderRouter.ValidateKeyAsync(p, key);
                if (error != null)
                {
                    status.Text = T("Cette clé ne fonctionne pas : ") + error;
                    return;
                }
                Agent.SecretStore.Set(p.SecretName!, key);
                Agent.Providers.ProviderRouter.Reset(p.Id);
                box.Clear();
                Refresh();
                Pulse(T("Clé enregistrée"));
            });
            save.Margin = new Thickness(8, 0, 0, 0);
            var remove = (Button)ActionButton(T("Supprimer"), () =>
            {
                Agent.SecretStore.Delete(p.SecretName!);
                Agent.Providers.ProviderRouter.Reset(p.Id);
                Refresh();
            });
            remove.Margin = new Thickness(8, 0, 0, 0);
            line.Children.Add(box);
            line.Children.Add(save);
            line.Children.Add(remove);
        }
        root.Children.Add(line);
        var help = new StackPanel { Orientation = Orientation.Horizontal };
        help.Children.Add(status);
        if (p.SignupUrl != null)
        {
            var link = Link(T(p.IsLocal ? "Installer" : "Obtenir une clé gratuite"), p.SignupUrl);
            link.Margin = new Thickness(12, 6, 0, 0);
            help.Children.Add(link);
        }
        root.Children.Add(help);
        Refresh();
        return root;
    }

    private FrameworkElement ModelEditor(Agent.Providers.ProviderInfo p)
    {
        var agent = mw.AgentPlugin?.Orchestrator;
        var line = new StackPanel { Orientation = Orientation.Horizontal };
        var combo = new ComboBox { Width = 260, IsEditable = true, Text = agent?.ModelOf(p.Id) ?? "" };
        if (TryFindResource("StandardComboBoxStyle") is Style s)
            combo.Style = s;
        if (p.Id == "gemini")
            combo.Items.Add(Agent.GeminiClient.DefaultModel);
        void Apply() => agent?.SetModelOf(p.Id, combo.SelectedItem as string ?? combo.Text);
        combo.SelectionChanged += (_, _) => Apply();
        combo.LostFocus += (_, _) => Apply();
        var refresh = (Button)ActionButton(T("Actualiser"), async () =>
        {
            var key = Agent.Providers.ProviderRouter.KeyFor(p);
            if (!p.IsLocal && string.IsNullOrEmpty(key)) { Pulse(T("Aucune clé API enregistrée")); return; }
            try
            {
                List<string> models = p.Id == "gemini"
                    ? await Agent.GeminiClient.ListModelsAsync(key!)
                    : (await ((Agent.Providers.OpenAiCompatibleProvider)p.Create(key, null)).ListModelsAsync(default))
                        .Select(m => m["id"]?.GetValue<string>() ?? "").Where(m => m.Length > 0).OrderBy(m => m).ToList();
                var current = agent?.ModelOf(p.Id);
                combo.Items.Clear();
                if (p.Id == "gemini")
                    combo.Items.Add(Agent.GeminiClient.DefaultModel);
                foreach (var m in models)
                    combo.Items.Add(m);
                combo.Text = current ?? "";
                Pulse(models.Count + " " + T("modèles disponibles"));
            }
            catch (Exception e)
            {
                MessageBox.Show(this, e.Message, p.Name, MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        });
        refresh.Margin = new Thickness(8, 0, 0, 0);
        line.Children.Add(combo);
        line.Children.Add(refresh);
        return line;
    }

    private void SyncTrayCheck(string name, bool value)
    {
        try
        {
            if (mw.notifyIcon?.ContextMenuStrip?.Items.Find(name, false).FirstOrDefault() is System.Windows.Forms.ToolStripMenuItem item)
                item.Checked = value;
        }
        catch { }
    }

    /// <summary>
    /// Petit message de confirmation non bloquant (remplace les boîtes modales)
    /// </summary>
    private void Pulse(string message)
    {
        var tip = new Border
        {
            Background = (Brush)FindResource("DARKPrimary"),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(12, 6, 12, 6),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 24, 20),
            Child = new TextBlock { Text = message, Foreground = (Brush)FindResource("DARKPrimaryText") },
            IsHitTestVisible = false,
        };
        Grid.SetRow(tip, 2);
        Grid.SetColumn(tip, 1);
        Root.Children.Add(tip);
        var anim = new DoubleAnimationUsingKeyFrames();
        anim.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        anim.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(150))));
        anim.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(1600))));
        anim.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(1900))));
        anim.Completed += (_, _) => Root.Children.Remove(tip);
        tip.BeginAnimation(OpacityProperty, anim);
    }
    #endregion
}
