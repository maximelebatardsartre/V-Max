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
using Panuon.WPF.UI;

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
        // V-Max : fiabilise la persistance des réglages modifiés ici (sinon ils dépendaient de la sauvegarde à la
        // fermeture de la fenêtre principale — perdus si le process est tué entre-temps).
        try { mw.Save(); } catch { }
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
    private void Restart_Click(object sender, RoutedEventArgs e) => mw.Restart();

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
        if (TryFindResource("VMaxComboBox") is Style s)
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

    /// <summary>Encart explicatif pleine largeur, avec un bouton d'action facultatif</summary>
    private FrameworkElement InfoNote(string text, string? buttonText = null, Action? action = null)
    {
        var sp = new StackPanel();
        sp.Children.Add(new TextBlock
        {
            Text = text, TextWrapping = TextWrapping.Wrap, Foreground = (Brush)FindResource("PrimaryText"),
            FontSize = 13, LineHeight = 20,
        });
        if (buttonText != null && action != null)
        {
            var b = new Button { Style = (Style)FindResource("VMaxAccentButton"), Content = buttonText, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 12, 0, 0), MinWidth = 120 };
            b.Click += (_, _) => action();
            sp.Children.Add(b);
        }
        var accent = ((SolidColorBrush)FindResource("DARKPrimary")).Color;
        return new Border
        {
            CornerRadius = new CornerRadius(12), Padding = new Thickness(16, 14, 16, 14),
            Background = new SolidColorBrush(Color.FromArgb(0x1E, accent.R, accent.G, accent.B)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x55, accent.R, accent.G, accent.B)), BorderThickness = new Thickness(1),
            Child = sp,
        };
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
        categories.Add(new("routines", T("Routines de vie"), "", T("Le rythme de vie de ton compagnon, avec des horaires qui varient comme les tiens.")));
        categories.Add(new("voix", T("Voix"), "", T("Parler à ton compagnon et l'entendre répondre.")));
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

    private int versionTaps;
    private DateTime lastVersionTap;

    /// <summary>Numéro de version ; 7 clics rapprochés débloquent le Studio des mods</summary>
    private FrameworkElement VersionTapper()
    {
        var b = new Button { Content = $"V-Max {mw.Version}", Cursor = System.Windows.Input.Cursors.Arrow, FontFamily = (FontFamily)FindResource("HudMono") };
        b.SetResourceReference(StyleProperty, "HudGhostButton");
        b.Click += (_, _) =>
        {
            var now = DateTime.Now;
            versionTaps = now - lastVersionTap < TimeSpan.FromSeconds(1.5) ? versionTaps + 1 : 1;
            lastVersionTap = now;
            if (versionTaps < 7)
                return;
            versionTaps = 0;
            bool first = !HUD.StudioWindow.Unlocked(mw);
            mw.Set["vmax_dev"][(gbol)"studio"] = true;
            mw.Hud?.SetStudioHotkey(true);
            if (first)
                mw.Toast("Studio des mods débloqué. Raccourci : Ctrl+Maj+F12.", HUD.HudToast.Kind.Success);
            HUD.StudioWindow.Open(mw);
        };
        return b;
    }

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
        Add("general", "Fenêtre", "Position de départ fixe", "Utilisée quand « Revenir à la dernière position » est désactivé.",
            StartPointEditor, advancedOnly: true);

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

        Add("apparence", "Compagnon", "Taille du compagnon", "Taille d'affichage à l'écran. Astuce : Ctrl + molette sur le compagnon pour l'ajuster à tout moment.",
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
        var smartIntervals = new[] { 30, 60, 120, 300, 600, 1200, 1800, 2400, 3000, 3600 };
        Add("compagnon", "Déplacements", "Déplacement intelligent", "Ne bouge que pendant un moment après ta dernière interaction.",
            () => Toggle(() => set.SmartMove, v => set.SetSmartMove(v)), advancedOnly: true);
        Add("compagnon", "Déplacements", "Durée du déplacement intelligent", "",
            () => Combo(smartIntervals.Select(s => s < 60 ? $"{s} s" : $"{s / 60} min").ToList(),
                () => Math.Max(0, Array.IndexOf(smartIntervals, set.SmartMoveInterval)),
                i => set.SetSmartMoveInterval(smartIntervals[i]), width: 140), advancedOnly: true);
        Add("compagnon", "Déplacements", "Où Maxine peut aller",
            "Choisis sa liberté : « Tous les écrans » → elle se balade partout et passe d'un écran à l'autre sans collision (tu peux aussi la glisser à la main où tu veux). Ou limite-la à un seul écran, ou à une zone. À toi de voir.",
            MoveAreaEditor, fullWidth: true);
        Add("compagnon", "Déplacements", "Changer d'écran automatiquement", "Adapte la zone de déplacement à l'écran où tu déposes le compagnon.",
            () => Toggle(() => set.AutoChangeWindow, v => set.AutoChangeWindow = v), advancedOnly: true);

        Add("compagnon", "Habitat (mode autonome)", "Mode autonome",
            "Maxine vit sur ton bureau et se promène sur les sols/zones que tu traces. En l'activant la première fois, l'éditeur s'ouvre pour tracer ta carte.",
            () => Toggle(() => mw.Habitat?.IsActive == true, v =>
            {
                if (mw.Habitat == null) return;
                if (v != mw.Habitat.IsActive) _ = mw.ToggleHabitat();
            }));
        Add("compagnon", "Habitat (mode autonome)", "Vivre directement sur le bureau (transparent)",
            "Maxine vit sur ton VRAI bureau, en transparence temps réel : tu vois ton bureau et tes fenêtres, et tu traces sols/zones directement dessus (touche H pour masquer les fenêtres le temps du tracé). Désactive pour l'ancien mode « décor image ».",
            () => Toggle(() => mw.Habitat?.DesktopMode != false, v => { if (mw.Habitat != null) mw.Habitat.DesktopMode = v; }));
        var habitatPositions = new[] { ("front", "Devant les fenêtres"), ("behind", "Derrière les fenêtres") };
        Add("compagnon", "Habitat (mode autonome)", "Position de Maxine",
            "Sur le bureau : toujours au-dessus de tes fenêtres, ou derrière (comme un fond d'écran vivant).",
            () => Combo(habitatPositions.Select(p => T(p.Item2)).ToList(),
                () => Math.Max(0, Array.FindIndex(habitatPositions, p => p.Item1 == (mw.Habitat?.Position ?? "front"))),
                i => { if (mw.Habitat != null) mw.Habitat.Position = habitatPositions[i].Item1; }, width: 220));
        Add("compagnon", "Habitat (mode autonome)", "Décor",
            "Une image avec des espaces dégagés (une maison en coupe, par exemple). Chaque image garde sa propre carte.",
            HabitatImagePicker, fullWidth: true);
        Add("compagnon", "Habitat (mode autonome)", "Écrans détectés",
            "V-Max reconnaît automatiquement tes écrans (position, résolution, échelle). Rien à régler : tout s'adapte à ta configuration.",
            ScreenPanel, fullWidth: true);
        Add("compagnon", "Habitat (mode autonome)", "Sur tous les écrans",
            "Sur le bureau, le décor et les déplacements de Maxine s'étendent sur TOUS tes écrans (comme le fond d'écran « Étendu » de Windows) : elle se balade et passe d'un écran à l'autre. Détecté automatiquement — sans effet si tu n'as qu'un écran.",
            () => Toggle(() => mw.Habitat?.SpanScreens != false, v => { if (mw.Habitat != null) mw.Habitat.SpanScreens = v; }));
        Add("compagnon", "Habitat (mode autonome)", "Modifier la carte", "Trace les sols sur lesquels le compagnon marche et règle sa taille dans ce décor.",
            () => ActionButton(T("Ouvrir l'éditeur"), async () =>
            {
                if (mw.Habitat == null) return;
                if (!mw.Habitat.IsActive)
                {
                    var err = await mw.Habitat.EnableAsync();
                    if (err != null) { Pulse(T(err)); return; }
                }
                mw.Habitat.Window?.StartEditing();
                mw.Habitat.Window?.Activate();
            }));
        Add("compagnon", "Habitat (mode autonome)", "Partager",
            "Un fichier .vmaxhome réunit l'image du décor et sa carte (sols, échelles, pièces, emplacements). Ne partage que des images dont tu as les droits.",
            HabitatSharing, fullWidth: true);
        Add("compagnon", "Habitat (mode autonome)", "Sortir sur le bureau",
            "Quand tu affiches le bureau (Win+D), le compagnon quitte l'habitat et vit sur ton fond d'écran, si une carte a été tracée pour cette image.",
            () => Toggle(() => mw.Habitat?.DesktopEnabled != false, v => { if (mw.Habitat != null) mw.Habitat.DesktopEnabled = v; }));
        var idleChoices = new[] { 0, 2, 5, 10, 20, 30 };
        Add("compagnon", "Habitat (mode autonome)", "Au repos, sortir sur le bureau après",
            "Quand le PC est inactif, le compagnon passe sur le fond d'écran, au-dessus des fenêtres ; il revient dès que tu touches la souris ou le clavier.",
            () => Combo(idleChoices.Select(m => m == 0 ? T("Jamais") : $"{m} min").ToList(),
                () => Math.Max(0, Array.IndexOf(idleChoices, mw.Habitat?.IdleMinutes ?? 5)),
                i => { if (mw.Habitat != null) mw.Habitat.IdleMinutes = idleChoices[i]; }, width: 140), advancedOnly: true);
        Add("compagnon", "Habitat (mode autonome)", "Vivre dans le fond d'écran (expérimental)",
            "Le compagnon vit en permanence sur ton fond d'écran, derrière les fenêtres, comme un fond d'écran animé (carte du fond d'écran requise). "
            + "Il n'est alors plus cliquable : parle-lui avec Ctrl+Alt+Espace.",
            () => Toggle(() => mw.Habitat?.WallpaperLayerEnabled == true, v => { if (mw.Habitat != null) mw.Habitat.WallpaperLayerEnabled = v; }), advancedOnly: true);
        Add("compagnon", "Habitat (mode autonome)", "Habitat toujours au premier plan", "Garde la fenêtre habitat au-dessus des autres fenêtres.",
            () => Toggle(() => mw.Habitat?.AlwaysOnTop == true, v => { if (mw.Habitat != null) mw.Habitat.AlwaysOnTop = v; }), advancedOnly: true);

        // ---------------- Voix
        Add("voix", "Micro", "Activer le micro",
            "Rien n'écoute tant que tu ne l'as pas activé. Une fois activé, tu peux parler à ton compagnon avec la touche ci-dessous, et éventuellement avec « Hey Max ».",
            () => Toggle(() => mw.Voice?.Enabled == true, v => { if (mw.Voice != null) mw.Voice.Enabled = v; }));
        Add("voix", "Parler", "Touche à maintenir pour parler",
            "Maintiens la touche, parle, relâche : ta phrase part au compagnon, qui te répond. Fonctionne depuis n'importe quelle application.",
            () => Toggle(() => mw.Voice?.PushToTalk == true, v => { if (mw.Voice != null) mw.Voice.PushToTalk = v; }));
        Add("voix", "Parler", "Touche",
            "Choisis une touche peu utilisée : elle est réservée à V-Max tant que l'option est active.",
            () => Combo(Voice.PushToTalkKey.Choices.Select(c => c.label).ToList(),
                () => Math.Max(0, Array.FindIndex(Voice.PushToTalkKey.Choices, c => c.vk == (mw.Voice?.PttKey ?? 0xDE))),
                i => { if (mw.Voice != null) mw.Voice.PttKey = Voice.PushToTalkKey.Choices[i].vk; }, width: 200));
        Add("voix", "Parler", "« Hey Max »",
            "Dis « Hey Max » puis ta question : l'écoute s'arrête seule quand tu te tais. Le mot d'activation est reconnu sur ton PC, rien n'est envoyé tant que tu ne l'as pas prononcé.",
            () => Toggle(() => mw.Voice?.WakeEnabled == true, v => { if (mw.Voice != null) mw.Voice.WakeEnabled = v; }));
        var sensitivities = new[] { (0.55, "Sensible"), (0.7, "Normal"), (0.85, "Strict") };
        Add("voix", "Parler", "Sensibilité de « Hey Max »", "Plus strict : moins de déclenchements par erreur, mais il faut parler plus distinctement.",
            () => Combo(sensitivities.Select(s => T(s.Item2)).ToList(),
                () => Math.Max(0, Array.FindIndex(sensitivities, s => Math.Abs(s.Item1 - (mw.Voice?.WakeThreshold ?? 0.7)) < 0.01)),
                i => { if (mw.Voice != null) mw.Voice.WakeThreshold = sensitivities[i].Item1; }, width: 160), advancedOnly: true);
        Add("voix", "Confidentialité", "Transcription hors ligne uniquement",
            "Activé : ta voix ne quitte jamais le PC (reconnaissance de Windows, un peu moins précise). Désactivé : Groq ou Gemini transcrivent, avec la clé que tu as connectée.",
            () => Toggle(() => mw.Voice?.OfflineOnly == true, v => { if (mw.Voice != null) mw.Voice.OfflineOnly = v; }));
        var replies = new[] { ("voice", "Quand je lui parle"), ("always", "Toujours"), ("never", "Jamais") };
        Add("voix", "Réponses", "Répondre à voix haute",
            "Le compagnon lit ses réponses à voix haute. Parle (ou appuie sur la touche) pour lui couper la parole.",
            () => Combo(replies.Select(r => T(r.Item2)).ToList(),
                () => Math.Max(0, Array.FindIndex(replies, r => r.Item1 == (mw.Voice?.ReplyMode ?? "voice"))),
                i => { if (mw.Voice != null) mw.Voice.ReplyMode = replies[i].Item1; }, width: 200));
        Add("voix", "Réponses", "Voix",
            "Voix française neurale (moteur open-source Piper, 100 % hors-ligne). « Claire » est nette et posée, « Douce » plus chaleureuse. Téléchargée une seule fois (~85 Mo) à l'activation de la voix.",
            () => Combo(Voice.PiperTts.Catalog.Select(c => T(c.Label)).ToList(),
                () => Math.Max(0, Array.FindIndex(Voice.PiperTts.Catalog, c => c.Id == (mw.Voice?.PiperVoiceId ?? Voice.PiperTts.Catalog[0].Id))),
                i => { if (mw.Voice != null) mw.Voice.PiperVoiceId = Voice.PiperTts.Catalog[i].Id; }, width: 200));
        Add("voix", "Réponses", "Essayer la voix", "",
            () => ActionButton(T("Écouter un exemple"), () => mw.Voice?.Say("Salut, c'est Maxine ! Alors, qu'est-ce qu'on fait de beau aujourd'hui ?")));
        Add("voix", "Réponses", "Débit", "",
            () => SliderRow(-5, 5, 1, () => mw.Voice?.Rate ?? 0, v => { if (mw.Voice != null) mw.Voice.Rate = (int)v; }, v => v == 0 ? T("Normal") : (v > 0 ? "+" : "") + v.ToString("0")));

        Add("routines", "Comment ça marche", "À lire avant",
            "",
            () => InfoNote(
                "Les routines donnent un rythme à Maxine : « à telle heure, va faire telle activité à tel endroit ». "
                + "Pour que le lieu (Salon, Cuisine, Chambre…) ait un sens, le mode autonome doit être activé et son décor préparé :\n"
                + "   1.  Active « Mode autonome » dans l'onglet Compagnon.\n"
                + "   2.  Choisis un décor (ou garde ton fond d'écran).\n"
                + "   3.  Dans l'éditeur de carte, trace les sols où Maxine marche, puis délimite les pièces et nomme-les.\n\n"
                + "Sans carte tracée, Maxine suit quand même ses routines, mais sur place : elle ne se déplacera pas vers les pièces.",
                T("Ouvrir les réglages du mode autonome"), () => OpenCategory("compagnon", false)),
            fullWidth: true);
        Add("routines", "Routines de vie", "Activer les routines de vie avancées",
            "Chaque routine a une plage horaire : chaque jour, l'heure réelle est tirée au hasard dedans (12h14 un jour, 13h40 le lendemain). "
            + "Dans l'habitat, le compagnon se rend dans la pièce indiquée avant de commencer.",
            () => Toggle(() => mw.Life?.RoutinesEnabled == true, v => { if (mw.Life != null) mw.Life.RoutinesEnabled = v; }));
        Add("routines", "Routines de vie", "Autonomie financière",
            "S'il est presque à sec après une routine, il part de lui-même travailler (l'occupation la plus rentable qu'il sait faire) pour regagner de l'argent.",
            () => Toggle(() => mw.Life?.Book.EarnWhenBroke != false, v => { if (mw.Life != null) { mw.Life.Book.EarnWhenBroke = v; mw.Life.SaveBook(); } }));
        Add("routines", "Routines de vie", "Mes routines", "Action, lieu, plage de départ, jours et durée. Les modifications sont enregistrées aussitôt.",
            () => new RoutineEditor(mw, this).Build(), fullWidth: true);

        Add("compagnon", "Bac à sable", "Argent illimité",
            "Le porte-monnaie ne se vide jamais. Ta vraie somme est mise de côté et rendue quand tu désactives l'option.",
            () => Toggle(() => mw.Sandbox?.UnlimitedMoney == true, v => { if (mw.Sandbox != null) mw.Sandbox.UnlimitedMoney = v; }));
        Add("compagnon", "Bac à sable", "Jauges qui ne baissent jamais",
            "Faim, soif, humeur, énergie et santé peuvent remonter, mais ne descendent plus avec le temps.",
            () => Toggle(() => mw.Sandbox?.FrozenGauges == true, v => { if (mw.Sandbox != null) mw.Sandbox.FrozenGauges = v; }));
        Add("compagnon", "Bac à sable", "Repas et cadeaux gratuits",
            "Les objets gardent leurs effets mais ne coûtent rien.",
            () => Toggle(() => mw.Sandbox?.FreeItems == true, v => { if (mw.Sandbox != null) mw.Sandbox.FreeItems = v; }));

        Add("compagnon", "Économie", "Achat automatique",
            "Quand il a faim ou soif et qu'il te reste au moins 100 $, il achète lui-même de quoi manger ou boire.",
            () => Toggle(() => set.AutoBuy, v =>
            {
                if (v && mw.Core.Save!.Money < 100 && mw.Sandbox?.UnlimitedMoney != true)
                {
                    Pulse(T("Il faut au moins 100 $ pour l'achat automatique"));
                    set.AutoBuy = false;
                    return;
                }
                set.AutoBuy = v;
            }));
        Add("compagnon", "Économie", "Cadeaux automatiques", "Avec l'achat automatique, il s'offre aussi de temps en temps un cadeau.",
            () => Toggle(() => set.AutoGift, v => set.AutoGift = v), advancedOnly: true);

        Add("compagnon", "Simulation", "Besoins du compagnon", "Faim, soif, humeur et endurance évoluent avec le temps. Désactive-les pour un simple fond d'écran animé, sans contrainte.",
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
            () => SliderRow(0.05, 5, 0.05, () => set.PressLength / 1000.0, v => set.PressLength = (int)(v * 1000), v => $"{v:0.00} s"), advancedOnly: true);
        Add("compagnon", "Interactions", "Sensibilité à la musique", "Volume à partir duquel le compagnon se met à danser.",
            () => SliderRow(0.02, 1, 0.01, () => set.MusicCatch, v => set.MusicCatch = v, v => $"{v * 100:0} %"), advancedOnly: true);
        Add("compagnon", "Interactions", "Volume pour danser à fond", "Au-delà de ce volume, la danse devient plus énergique.",
            () => SliderRow(0.02, 1, 0.01, () => set.MusicMax, v => set.MusicMax = v, v => $"{v * 100:0} %"), advancedOnly: true);
        Add("compagnon", "Interactions", "Volume actuel", "Ce que le compagnon entend en ce moment : règle les deux seuils ci-dessus d'après cette valeur.",
            MusicMeter, advancedOnly: true);

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
            () => ActionButton(T("Ouvrir"), () => HUD.SavesWindow.Open(mw)));
        Add("sauvegardes", "Copies de secours", "Dossier des données", ExtensionValue.DataDirectory,
            () => ActionButton(T("Ouvrir le dossier"), () => { try { Process.Start(new ProcessStartInfo(ExtensionValue.DataDirectory) { UseShellExecute = true })?.Dispose(); } catch { } }),
            advancedOnly: true);

        Add("sauvegardes", "Compagnons multiples", "Plusieurs compagnons",
            "Chaque compagnon a ses propres réglages et sa propre sauvegarde, et peut tourner en même temps que les autres.",
            MultiSaveEditor, fullWidth: true, advancedOnly: true);
        Add("sauvegardes", "Zone sensible", "Recommencer de zéro",
            "Efface la progression (niveau, argent, objets). Les statistiques sont gardées si la sauvegarde n'a jamais été modifiée.",
            () => ActionButton(T("Recommencer…"), ResetGame), advancedOnly: true);

        // ---------------- Extensions
        Add("extensions", "Mods", "Gestion des mods", "Activer, désactiver et autoriser les mods et leurs plugins de code.",
            () => ActionButton(T("Gérer les mods"), () => HUD.ModsWindow.Open(mw), accent: true));
        Add("extensions", "Mods", "Raccourcis personnalisés", "Boutons du menu « Personnalisé » (liens, programmes, raccourcis clavier).",
            () => ActionButton(T("Modifier"), () => HUD.ShortcutsWindow.Open(mw)), advancedOnly: true);
        Add("extensions", "Maintenance", "Vider le cache des animations", "Reconstruit le cache au prochain démarrage (utile après une mise à jour de mod).",
            () => ActionButton(T("Vider"), () => { MainWindow.RequestCachePurge(); NeedRestart(); }), advancedOnly: true);

        // ---------------- À propos
        Add("apropos", "V-Max", "Version", $".NET {Environment.Version}  ·  {(ExtensionValue.IsPortable ? T("mode portable") : T("installation standard"))}",
            VersionTapper);
        Add("apropos", "V-Max", "Code source", "Le dépôt GitHub de V-Max.",
            () => Link(T("Dépôt GitHub"), ExtensionValue.RepositoryURL));
        Add("apropos", "Mises à jour", "Rechercher une mise à jour",
            "Maxine se met à jour toute seule au démarrage, à partir des versions publiées par Az. Tu peux aussi vérifier maintenant.",
            () => ActionButton(T("Vérifier maintenant"), () => UpdateService.CheckManually(mw), accent: true));
        if (HUD.StudioWindow.Unlocked(mw))
        {
            Add("apropos", "Développement", "Studio des mods", "Aperçu des animations et tri des mods du catalogue. Raccourci : Ctrl+Maj+F12.",
                () => ActionButton(T("Ouvrir"), () => HUD.StudioWindow.Open(mw), accent: true));
            Add("apropos", "Développement", "Masquer le Studio", "Retire la ligne ci-dessus et le raccourci (7 clics sur la version pour le retrouver).",
                () => ActionButton(T("Masquer"), () =>
                {
                    mw.Set["vmax_dev"][(gbol)"studio"] = false;
                    mw.Hud?.SetStudioHotkey(false);
                    Pulse(T("Studio masqué"));
                }));
        }
        Add("apropos", "V-Max", "Signaler un problème", "Prépare un rapport et ouvre un ticket GitHub.",
            () => ActionButton(T("Signaler"), () => mw.ShowReport()));
        Add("apropos", "Diagnostic", "Calcul automatique des prix équitables",
            "Corrige les objets et occupations des mods trop généreux. À désactiver seulement pour tester un mod.",
            () => Toggle(() => !set["gameconfig"].GetBool("noAutoCal"), v => { set["gameconfig"].SetBool("noAutoCal", !v); NeedRestart(); }), advancedOnly: true);
        Add("apropos", "Diagnostic", "Sauvegarde vérifiée",
            mw.HashCheck ? "Oui : sauvegarde jamais modifiée et sans mod déséquilibré." : "Non : la sauvegarde a été modifiée ou un mod déséquilibré a été utilisé.",
            () => new TextBlock { Text = mw.HashCheck ? "✓ " + T("Vérifiée") : T("Non vérifiée"), Foreground = (Brush)FindResource(mw.HashCheck ? "VMaxSubtleText" : "PrimaryText"), VerticalAlignment = VerticalAlignment.Center }, advancedOnly: true);
        Add("apropos", "Crédits", "Auteurs des mods", "Les créateurs des mods installés.",
            () => ActionButton(T("Voir la liste"), () => VDialog.Show(string.Join("\n", mw.CoreMODs.Select(m => m.Author).Where(a => !string.IsNullOrWhiteSpace(a)).Distinct().OrderBy(a => a)), T("Merci aux auteurs des mods"))), advancedOnly: true);
        Add("apropos", "Crédits", "Bibliothèques utilisées", "Composants open source et bibliothèques chargées par les mods.",
            () => ActionButton(T("Voir la liste"), () => VDialog.Show(string.Join("\n", ExtensionValue.DllReferenceDescriptions.Select(kv => kv.Key + " — " + kv.Value)
                .Concat(CoreMOD.LoadedDLL.Select(d => d)).Distinct()), T("Bibliothèques"))), advancedOnly: true);
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

        Add("ia", "IA locale", "IA locale (Qwen 3B)",
            "Une IA qui tourne sur ton PC, sans clé et sans connexion. À la première activation, Maxine télécharge le modèle (~2 Go). Plus lente qu'une IA en ligne, mais privée. Désactive-la pour libérer le processeur.",
            LocalAiToggle, fullWidth: true);
        foreach (var provider in Agent.Providers.ProviderRouter.Catalog)
        {
            var p = provider;
            if (p.Id == Agent.LocalAiService.ProviderId) continue; // géré par « IA locale » ci-dessus
            Add("ia", "IA connectées (bascule automatique)", p.Name, p.Tagline, () => ProviderEditor(p), fullWidth: true);
        }
        foreach (var provider in Agent.Providers.ProviderRouter.Catalog)
        {
            var p = provider;
            if (p.Id == Agent.LocalAiService.ProviderId) continue;
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
                try
                {
                    var p = Agent.AgentOrchestrator.AuditLogPath;
                    if (!System.IO.File.Exists(p))
                        System.IO.File.WriteAllText(p, "");
                    Process.Start(new ProcessStartInfo(p) { UseShellExecute = true })?.Dispose();
                }
                catch { }
            }), advancedOnly: true);
        Add("ia", "Compatibilité", "Module de discussion des mods",
            "Qui répond quand un mod ou un plugin passe par la discussion de VPet. La discussion V-Max (anneau, Ctrl+Alt+Espace) utilise toujours l'agent.",
            ChatModuleEditor, fullWidth: true, advancedOnly: true);
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
        if (TryFindResource("VMaxComboBox") is Style s)
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
                VDialog.Show(this, e.Message, p.Name, MessageBoxButton.OK, MessageBoxIcon.Warning);
            }
        });
        refresh.Margin = new Thickness(8, 0, 0, 0);
        line.Children.Add(combo);
        line.Children.Add(refresh);
        return line;
    }

    private FrameworkElement MusicMeter()
    {
        var text = new TextBlock { Width = 140, FontFamily = new FontFamily("Cascadia Mono, Consolas"), VerticalAlignment = VerticalAlignment.Center, Foreground = (Brush)FindResource("PrimaryText") };
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        timer.Tick += (_, _) =>
        {
            float v = mw.AudioPlayingVolume();
            text.Text = $"{v * 100:0} %" + (v > mw.Set.MusicMax ? "  ♪♪" : v > mw.Set.MusicCatch ? "  ♪" : "");
        };
        text.Loaded += (_, _) => timer.Start();
        text.Unloaded += (_, _) => timer.Stop();
        return text;
    }

    private FrameworkElement MoveAreaEditor()
    {
        var root = new StackPanel();
        var status = new TextBlock { Foreground = (Brush)FindResource("VMaxSubtleText"), FontSize = 12, Margin = new Thickness(0, 6, 0, 0) };
        void Refresh()
        {
            if (mw.Core.Controller is not MWController c)
                status.Text = T("Le mode habitat gère les déplacements.");
            else if (mw.Set.AutoChangeWindow)
                status.Text = T("Écran choisi automatiquement (là où tu poses le compagnon).");
            else if (c.IsAllScreens)
                status.Text = T("Libre sur tous les écrans — Maxine passe d'un écran à l'autre sans collision.");
            else if (c.IsPrimaryScreen)
                status.Text = T("Écran principal.");
            else
                status.Text = T("Écran / zone choisi : ") + $"{c.ScreenBorder.X}, {c.ScreenBorder.Y} — {c.ScreenBorder.Width} × {c.ScreenBorder.Height}";
        }
        var line = new StackPanel { Orientation = Orientation.Horizontal };
        var all = (Button)ActionButton(T("Tous les écrans (libre)"), () => { (mw.Core.Controller as MWController)?.SetAllScreens(); Refresh(); });
        var primary = (Button)ActionButton(T("Écran principal"), () => { (mw.Core.Controller as MWController)?.ResetScreenBorder(); Refresh(); });
        var current = (Button)ActionButton(T("Cet écran"), () => { (mw.Core.Controller as MWController)?.SetNowScreenActivate(); Refresh(); });
        var custom = (Button)ActionButton(T("Zone personnalisée…"), () => new HUD.MoveAreaWindow(mw, Refresh).Show());
        primary.Margin = current.Margin = custom.Margin = new Thickness(8, 0, 0, 0);
        line.Children.Add(all);
        line.Children.Add(primary);
        line.Children.Add(current);
        line.Children.Add(custom);
        root.Children.Add(line);
        root.Children.Add(status);
        Refresh();
        return root;
    }

    private FrameworkElement StartPointEditor()
    {
        var line = new StackPanel { Orientation = Orientation.Horizontal };
        var x = (TextBox)TextField(() => mw.Set.StartRecordPoint.X.ToString("0"), v => { if (double.TryParse(v, out var d)) mw.Set.StartRecordPoint = new Point(d, mw.Set.StartRecordPoint.Y); }, "X", 80);
        var y = (TextBox)TextField(() => mw.Set.StartRecordPoint.Y.ToString("0"), v => { if (double.TryParse(v, out var d)) mw.Set.StartRecordPoint = new Point(mw.Set.StartRecordPoint.X, d); }, "Y", 80);
        y.Margin = new Thickness(8, 0, 0, 0);
        var here = (Button)ActionButton(T("Position actuelle"), () =>
        {
            mw.Set.StartRecordPoint = new Point(mw.Left, mw.Top);
            x.Text = mw.Left.ToString("0");
            y.Text = mw.Top.ToString("0");
        });
        here.Margin = new Thickness(8, 0, 0, 0);
        line.Children.Add(x);
        line.Children.Add(y);
        line.Children.Add(here);
        return line;
    }

    private FrameworkElement ChatModuleEditor()
    {
        var root = new StackPanel();
        var options = new List<(string type, string? api, string label)>
        {
            ("DIY", Agent.VMaxAgentPlugin.TalkName, T("Agent V-Max (IA)")),
            ("LB", null, T("Réponses intégrées de VPet")),
        };
        foreach (var api in mw.TalkAPI.Where(a => a.APIName != Agent.VMaxAgentPlugin.TalkName))
            options.Add(("DIY", api.APIName, T("Plugin : ") + api.APIName));
        options.Add(("OFF", null, T("Désactivée")));
        string curType = mw.Set["CGPT"][(gstr)"type"];
        string curApi = mw.Set["CGPT"][(gstr)"DIY"];
        int current = options.FindIndex(o => o.type == curType && (o.type != "DIY" || o.api == curApi));
        var settingsButton = (Button)ActionButton(T("Réglages du plugin"), () => mw.TalkBoxCurr?.Setting());
        settingsButton.Margin = new Thickness(8, 0, 0, 0);
        void UpdateButton() => settingsButton.Visibility = mw.Set["CGPT"][(gstr)"type"] == "DIY" && mw.TalkBoxCurr != null && mw.TalkBoxCurr.APIName != Agent.VMaxAgentPlugin.TalkName ? Visibility.Visible : Visibility.Collapsed;
        var combo = Combo(options.Select(o => o.label).ToList(), () => Math.Max(0, current), i =>
        {
            var o = options[i];
            mw.RemoveTalkBox();
            mw.Set["CGPT"][(gstr)"type"] = o.type;
            if (o.type == "DIY")
            {
                mw.TalkAPIIndex = mw.TalkAPI.FindIndex(a => a.APIName == o.api);
                mw.Set["CGPT"][(gstr)"DIY"] = o.api ?? "";
                mw.LoadTalkDIY();
            }
            else if (o.type == "LB")
            {
                mw.TalkBox = new TalkSelect(mw);
                mw.Main.ToolBar!.MainGrid.Children.Add(mw.TalkBox);
            }
            UpdateButton();
        }, width: 300);
        var line = new StackPanel { Orientation = Orientation.Horizontal };
        line.Children.Add(combo);
        line.Children.Add(settingsButton);
        root.Children.Add(line);
        UpdateButton();
        return root;
    }

    private FrameworkElement MultiSaveEditor()
    {
        var root = new StackPanel();
        var list = new StackPanel();
        void Refresh()
        {
            list.Children.Clear();
            foreach (var name in App.MutiSaves.ToList())
            {
                bool loaded = App.MainWindows.Any(w => w.PrefixSave.Trim('-') == name);
                bool isDefault = name.Length == 0;
                bool startup = isDefault ? !System.IO.Directory.EnumerateFiles(ExtensionValue.DataDirectory, "startup_*").Any()
                    : System.IO.File.Exists(System.IO.Path.Combine(ExtensionValue.DataDirectory, "startup_" + name));
                var row = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
                var label = new TextBlock
                {
                    Text = (isDefault ? T("Compagnon principal") : name) + (loaded ? "  ·  " + T("ouvert") : "") + (startup ? "  ·  " + T("au démarrage") : ""),
                    VerticalAlignment = VerticalAlignment.Center, Foreground = (Brush)FindResource("PrimaryText"),
                };
                var actions = new StackPanel { Orientation = Orientation.Horizontal };
                var open = (Button)ActionButton(T("Ouvrir"), () =>
                {
                    if (App.MainWindows.Any(w => w.PrefixSave.Trim('-') == name)) { Pulse(T("Déjà ouvert")); return; }
                    new MainWindow(name, mw).Show();
                    Refresh();
                });
                open.IsEnabled = !loaded;
                var def = (Button)ActionButton(T("Au démarrage"), () =>
                {
                    foreach (var sp in new System.IO.DirectoryInfo(ExtensionValue.DataDirectory).GetFiles("startup_*"))
                        sp.Delete();
                    if (name.Length > 0)
                        System.IO.File.Create(System.IO.Path.Combine(ExtensionValue.DataDirectory, "startup_" + name)).Close();
                    Refresh();
                });
                def.IsEnabled = !startup;
                var del = (Button)ActionButton(T("Supprimer"), () =>
                {
                    if (VDialog.Show(T("Supprimer le compagnon « {0} » et ses réglages ? Cette action est définitive.").Replace("{0}", name), T("Supprimer un compagnon"),
                            MessageBoxButton.YesNo, MessageBoxIcon.Warning) != MessageBoxResult.Yes)
                        return;
                    System.IO.File.Delete(System.IO.Path.Combine(ExtensionValue.DataDirectory, $"Setting-{name}.lps"));
                    App.MutiSaves.Remove(name);
                    Refresh();
                });
                del.IsEnabled = !isDefault && !loaded;
                def.Margin = del.Margin = new Thickness(8, 0, 0, 0);
                actions.Children.Add(open);
                actions.Children.Add(def);
                actions.Children.Add(del);
                DockPanel.SetDock(actions, Dock.Right);
                row.Children.Add(actions);
                row.Children.Add(label);
                list.Children.Add(row);
            }
        }
        var create = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        var nameBox = (TextBox)TextField(() => "", _ => { }, T("Nom du nouveau compagnon"), 220);
        var add = (Button)ActionButton(T("Créer"), () =>
        {
            var name = nameBox.Text.Trim();
            if (name.Length == 0) return;
            if (name.IndexOfAny(@"()#:|/\?*<>-".ToCharArray()) >= 0) { Pulse(T("Évite les symboles ( ) # : | / \\ ? * < > -")); return; }
            if (App.MutiSaves.Any(x => string.Equals(x, name, StringComparison.OrdinalIgnoreCase))) { Pulse(T("Ce nom existe déjà")); return; }
            var lps = new LPS(mw.Set);
            lps.SetInt("savetimes", 0);
            System.IO.File.WriteAllText(System.IO.Path.Combine(ExtensionValue.DataDirectory, $"Setting-{name}.lps"), lps.ToString());
            App.MutiSaves.Add(name);
            new MainWindow(name, mw).Show();
            nameBox.Text = "";
            Refresh();
        }, accent: true);
        add.Margin = new Thickness(8, 0, 0, 0);
        create.Children.Add(nameBox);
        create.Children.Add(add);
        root.Children.Add(list);
        root.Children.Add(create);
        Refresh();
        return root;
    }

    /// <summary>Recommencer de zéro (règle de VPet : les statistiques d'une sauvegarde jamais modifiée sont gardées)</summary>
    private void ResetGame()
    {
        if (VDialog.Show(T("Effacer toute la progression de ton compagnon et recommencer de zéro ? Pense à faire une copie de secours si tu hésites."),
                T("Recommencer de zéro"), MessageBoxButton.YesNo, MessageBoxIcon.Warning) != MessageBoxResult.Yes)
            return;
        var oldsave = mw.GameSavesData;
        mw.GameSavesData = new GameSave_v2(mw.Core.Save!.Name);
        mw.Core.Save = mw.GameSavesData.GameSave;
        mw.GameSavesData.GameSave.Event_LevelUp += mw.LevelUP;
        if (oldsave.HashCheck)
        {
            mw.GameSavesData.Statistics = oldsave.Statistics;
            if (oldsave.GameSave.Money > 10000000 || oldsave.GameSave.Money < -1000000000 || oldsave.GameSave.Exp > 100000000 || oldsave.GameSave.Exp < -10000000000)
            {
                mw.Core.Save!.Money = 10000;
                mw.Core.Save!.Exp = 10000;
            }
        }
        mw.HashCheck = true;
        Pulse(T("C'est reparti de zéro"));
    }

    private FrameworkElement VoicePicker()
    {
        var line = new StackPanel { Orientation = Orientation.Horizontal };
        if (mw.Voice == null)
            return new TextBlock { Text = T("La voix n'est pas disponible."), Foreground = (Brush)FindResource("VMaxSubtleText") };
        var voices = mw.Voice.Voices();
        var combo = (ComboBox)Combo(voices, () => Math.Max(0, voices.FindIndex(v => v.Contains(mw.Voice.VoiceName, StringComparison.OrdinalIgnoreCase))),
            i => mw.Voice.VoiceName = voices[i], width: 320);
        line.Children.Add(combo);
        var test = (Button)ActionButton(T("Écouter un exemple"), () => mw.Voice.Say("Bonjour ! Je suis " + (mw.Core.Save?.Name ?? "Max") + ". Maintiens la touche et parle-moi quand tu veux."));
        test.Margin = new Thickness(8, 0, 0, 0);
        line.Children.Add(test);
        return line;
    }

    private FrameworkElement HabitatSharing()
    {
        var line = new StackPanel { Orientation = Orientation.Horizontal };
        var export = (Button)ActionButton(T("Exporter cet habitat…"), () =>
        {
            var h = mw.Habitat;
            if (h?.IsActive != true || h.ImagePath == null)
            {
                Pulse(T("Active d'abord le mode habitat avec le décor à partager"));
                return;
            }
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Title = T("Exporter l'habitat"),
                FileName = Habitat.HabitatPackage.SuggestedName(h.WindowMap),
                Filter = T("Habitat V-Max") + " (*.vmaxhome)|*.vmaxhome",
                DefaultExt = ".vmaxhome",
            };
            if (dlg.ShowDialog(this) != true)
                return;
            try
            {
                Habitat.HabitatPackage.Export(h.ImagePath, h.WindowMap, dlg.FileName, mw.GameSavesData.GameSave.HostName);
                Pulse(T("Habitat exporté"));
            }
            catch (Exception e)
            {
                VDialog.Show(this, e.Message, T("Exporter l'habitat"), MessageBoxButton.OK, MessageBoxIcon.Warning);
            }
        });
        var import = (Button)ActionButton(T("Importer un habitat…"), async () =>
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = T("Importer un habitat"),
                Filter = T("Habitat V-Max") + " (*.vmaxhome)|*.vmaxhome",
            };
            if (dlg.ShowDialog(this) != true || mw.Habitat == null)
                return;
            try
            {
                var image = Habitat.HabitatPackage.Import(dlg.FileName);
                var err = await mw.Habitat.UseImageAsync(image);
                if (err == null && !mw.Habitat.IsActive)
                    err = await mw.Habitat.EnableAsync();
                Pulse(err ?? T("Habitat importé"));
            }
            catch (Exception e)
            {
                VDialog.Show(this, e.Message, T("Importer un habitat"), MessageBoxButton.OK, MessageBoxIcon.Warning);
            }
        });
        import.Margin = new Thickness(8, 0, 0, 0);
        line.Children.Add(export);
        line.Children.Add(import);
        return line;
    }

    /// <summary>Interrupteur de l'IA locale, avec le suivi du téléchargement et de l'état</summary>
    private FrameworkElement LocalAiToggle()
    {
        var root = new StackPanel();
        var toggle = new CheckBox
        {
            Style = (Style)FindResource("VMaxToggle"),
            IsChecked = mw.LocalAi?.Enabled == true,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        var status = new TextBlock { Margin = new Thickness(0, 10, 0, 0), FontSize = 12.5, Foreground = (Brush)FindResource("VMaxSubtleText"), TextWrapping = TextWrapping.Wrap, MaxWidth = 620 };
        void SetStatus()
        {
            if (mw.LocalAi == null) { status.Text = ""; return; }
            status.Text = mw.LocalAi.Running ? T("IA locale active — Maxine peut répondre hors ligne.")
                : mw.LocalAi.Enabled ? T("IA locale en préparation…")
                : mw.LocalAi.ModelReady ? T("Modèle déjà téléchargé. Active pour l'utiliser.")
                : T("Non téléchargée. L'activer télécharge ~2 Go une seule fois.");
        }
        SetStatus();
        if (mw.LocalAi != null)
        {
            // handler nommé + désabonnement à Unloaded : sinon chaque rendu de la catégorie IA (ou frappe dans la
            // recherche) empilait un abonnement supplémentaire pointant un TextBlock devenu obsolète (fuite).
            void OnProgress(string text, double frac) => Dispatcher.BeginInvoke(() => status.Text = text);
            mw.LocalAi.Progress += OnProgress;
            root.Unloaded += (_, _) => { if (mw.LocalAi != null) mw.LocalAi.Progress -= OnProgress; };
        }
        toggle.Checked += async (_, _) =>
        {
            if (mw.LocalAi == null) return;
            toggle.IsEnabled = false;
            status.Text = T("Préparation de l'IA locale…");
            var err = await mw.LocalAi.EnableAsync();
            toggle.IsEnabled = true;
            if (err != null)
            {
                toggle.IsChecked = false;
                mw.LocalAi.Disable();
                Pulse(err);
            }
            SetStatus();
        };
        toggle.Unchecked += (_, _) =>
        {
            mw.LocalAi?.Disable();
            SetStatus();
        };
        root.Children.Add(toggle);
        root.Children.Add(status);
        return root;
    }

    /// <summary>Liste des écrans détectés (nom, résolution, échelle, principal), avec rafraîchissement</summary>
    private FrameworkElement ScreenPanel()
    {
        var root = new StackPanel();
        var list = new StackPanel();
        var mono = new FontFamily("Cascadia Mono, Consolas"); // police résolue dans cette fenêtre (cf. reste des paramètres)

        // mini-carte de la disposition réelle des écrans (comme les paramètres d'affichage Windows)
        FrameworkElement ScreenLayout(List<Screens.ScreenDetail> ss)
        {
            double minX = ss.Min(s => (double)s.X), minY = ss.Min(s => (double)s.Y);
            double maxX = ss.Max(s => (double)s.X + s.Width), maxY = ss.Max(s => (double)s.Y + s.Height);
            double vw = Math.Max(1, maxX - minX), vh = Math.Max(1, maxY - minY);
            const double areaW = 540, maxH = 190, pad = 6;
            double scale = Math.Min((areaW - 2 * pad) / vw, (maxH - 2 * pad) / vh);
            var canvas = new Canvas { Width = areaW, Height = vh * scale + 2 * pad, Margin = new Thickness(0, 2, 0, 16), HorizontalAlignment = HorizontalAlignment.Left };
            foreach (var s in ss)
            {
                var cell = new Border
                {
                    Width = Math.Max(30, s.Width * scale - 4), Height = Math.Max(22, s.Height * scale - 4),
                    Background = (Brush)FindResource(s.Primary ? "VMaxNavSelected" : "VMaxCard"),
                    BorderBrush = (Brush)FindResource(s.Primary ? "DARKPrimary" : "VMaxStroke"),
                    BorderThickness = new Thickness(s.Primary ? 2 : 1), CornerRadius = new CornerRadius(6),
                    Child = new TextBlock
                    {
                        Text = $"{s.Width}×{s.Height}\n{s.ScaleText}" + (s.Primary ? "  ★" : ""),
                        FontSize = 9.5, FontFamily = mono, Foreground = (Brush)FindResource("VMaxSubtleText"),
                        TextAlignment = TextAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                    },
                };
                Canvas.SetLeft(cell, pad + (s.X - minX) * scale);
                Canvas.SetTop(cell, pad + (s.Y - minY) * scale);
                canvas.Children.Add(cell);
            }
            return canvas;
        }

        void Build()
        {
            list.Children.Clear();
            List<Screens.ScreenDetail> screens;
            try { screens = Screens.All(); } catch { screens = new List<Screens.ScreenDetail>(); }
            if (screens.Count > 1)
                list.Children.Add(ScreenLayout(screens));
            foreach (var s in screens)
            {
                try
                {
                    var card = new Border
                    {
                        CornerRadius = new CornerRadius(10), Padding = new Thickness(14, 10, 14, 10), Margin = new Thickness(0, 0, 0, 8),
                        Background = (Brush)FindResource("VMaxCard"), BorderBrush = (Brush)FindResource(s.Primary ? "DARKPrimary" : "VMaxStroke"),
                        BorderThickness = new Thickness(s.Primary ? 1.5 : 1),
                    };
                    var dock = new DockPanel();
                    var right = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right };
                    right.Children.Add(new TextBlock { Text = s.Resolution, FontFamily = mono, FontSize = 13, Foreground = (Brush)FindResource("PrimaryText"), HorizontalAlignment = HorizontalAlignment.Right });
                    right.Children.Add(new TextBlock { Text = "échelle " + s.ScaleText, FontSize = 11.5, Foreground = (Brush)FindResource("VMaxSubtleText"), HorizontalAlignment = HorizontalAlignment.Right });
                    DockPanel.SetDock(right, Dock.Right);
                    dock.Children.Add(right);
                    var left = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                    var title = new StackPanel { Orientation = Orientation.Horizontal };
                    title.Children.Add(new TextBlock { Text = s.FriendlyName, FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = (Brush)FindResource("PrimaryText"), VerticalAlignment = VerticalAlignment.Center });
                    if (s.Primary)
                    {
                        var badge = new Border { CornerRadius = new CornerRadius(6), Background = (Brush)FindResource("DARKPrimary"), Padding = new Thickness(6, 1, 6, 2), Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
                        badge.Child = new TextBlock { Text = T("Principal"), FontSize = 10.5, Foreground = (Brush)FindResource("DARKPrimaryText") };
                        title.Children.Add(badge);
                    }
                    left.Children.Add(title);
                    left.Children.Add(new TextBlock { Text = s.DeviceName.Replace(@"\\.\", "") + "  ·  position " + s.X + ", " + s.Y, FontSize = 11.5, Foreground = (Brush)FindResource("VMaxSubtleText") });
                    dock.Children.Add(left);
                    card.Child = dock;
                    list.Children.Add(card);
                }
                catch { /* un écran qui pose souci ne doit jamais vider toute la liste */ }
            }
            if (list.Children.Count == 0)
                list.Children.Add(new TextBlock { Text = T("Aucun écran détecté pour l'instant. Clique sur Rafraîchir, ou relance Maxine."), FontSize = 12.5, Foreground = (Brush)FindResource("VMaxSubtleText"), TextWrapping = TextWrapping.Wrap });
        }
        var refresh = (Button)ActionButton(T("Rafraîchir"), Build);
        refresh.HorizontalAlignment = HorizontalAlignment.Left;
        refresh.Margin = new Thickness(0, 2, 0, 0);
        Build();
        root.Children.Add(list);
        root.Children.Add(refresh);
        return root;
    }

    private FrameworkElement HabitatImagePicker()
    {
        var root = new StackPanel();
        var line = new StackPanel { Orientation = Orientation.Horizontal };
        var status = new TextBlock { Margin = new Thickness(0, 6, 0, 0), FontSize = 12, Foreground = (Brush)FindResource("VMaxSubtleText"), TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 560 };
        void Refresh()
        {
            var chosen = mw.Habitat?.ChosenImage;
            status.Text = chosen != null ? chosen : T("Ton fond d'écran actuel") + (Habitat.WallpaperInfo.CurrentPath() is { } wp ? " · " + wp : " (introuvable : choisis une image)");
        }
        var pick = (Button)ActionButton(T("Choisir une image…"), async () =>
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = T("Image de l'habitat"),
                Filter = T("Images") + "|*.png;*.jpg;*.jpeg;*.bmp;*.webp|" + T("Tous les fichiers") + "|*.*",
            };
            if (dlg.ShowDialog(this) != true || mw.Habitat == null) return;
            var err = await mw.Habitat.UseImageAsync(dlg.FileName);
            Refresh();
            if (err != null) Pulse(T(err));
        });
        var wallpaper = (Button)ActionButton(T("Utiliser mon fond d'écran"), async () =>
        {
            if (mw.Habitat == null) return;
            var err = await mw.Habitat.UseImageAsync(null);
            Refresh();
            if (err != null) Pulse(T(err));
        });
        wallpaper.Margin = new Thickness(8, 0, 0, 0);
        line.Children.Add(pick);
        line.Children.Add(wallpaper);
        root.Children.Add(line);
        root.Children.Add(status);
        Refresh();
        return root;
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
