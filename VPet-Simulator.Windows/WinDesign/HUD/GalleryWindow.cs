using LinePutScript.Localization.WPF;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using VPet_Simulator.Core;
using VPet_Simulator.Windows.Interface;
using WpfAnimatedGif;

namespace VPet_Simulator.Windows.HUD;

/// <summary>
/// V-Max HUD : galerie de photos (remplace winGallery). Filtres en pastilles, grille de cartes chargée par pages
/// (miniatures décodées hors du fil d'interface), déblocage contre de l'argent, vue détaillée dans la fenêtre
/// (GIF animés, copie, enregistrement, navigation ← →), sélection et export.
/// Même logique métier que l'ancienne galerie : verrouillées d'abord, filtres « rien coché = tout », étiquettes en ET.
/// </summary>
public sealed class GalleryWindow : HudWindow
{
    private const int PageSize = 40;
    private const int ThumbPixels = 440;
    private const double MinCardWidth = 210;
    private const int TagsCollapsed = 12;
    private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");
    private static GalleryWindow? instance;

    // filtres
    private readonly TextBox search;
    private readonly DispatcherTimer searchDelay = new() { Interval = TimeSpan.FromMilliseconds(220) };
    private readonly Pill illustrations, miniatures, unlockedOnly, lockedOnly, favorites;
    private readonly List<Pill> sortPills = new();
    private int sort;
    private readonly List<string> allTags;
    private readonly HashSet<string> tagFilter = new();
    private readonly WrapPanel tagPanel = new() { Margin = new Thickness(0, 2, 0, 0) };
    private bool tagsExpanded;

    // grille
    private readonly WrapPanel grid = new();
    private readonly ScrollViewer scroll;
    private readonly Button more;
    private readonly StackPanel empty = new() { Visibility = Visibility.Collapsed };
    private readonly TextBlock emptyText;
    private List<Photo> filtered = new();
    private int shown;
    private int unlockedAtRefresh = -1;
    private readonly Dictionary<Photo, FrameworkElement> cardOf = new();
    private readonly Dictionary<Photo, Action> repaintOf = new();
    private readonly HashSet<Photo> selection = new();
    private readonly Dictionary<Photo, BitmapSource?> colorThumbs = new(), grayThumbs = new();

    // en-tête et pied
    private readonly TextBlock counter;
    private readonly TextBlock selectionInfo;
    private readonly Button selectAll;

    // vue détaillée
    private readonly Grid overlay = new() { Visibility = Visibility.Collapsed, Focusable = true };
    private readonly Grid detailContent = new() { Margin = new Thickness(24, 4, 24, 24) };
    private readonly Grid imageArea = new() { Background = Brushes.Transparent };
    private readonly Image detailImage = new() { Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock detailLoading;
    private readonly Border infoPanel;
    private readonly TextBlock detailEyebrow, detailTitle, detailDate, detailDescription, detailPosition;
    private readonly WrapPanel detailTags = new() { Margin = new Thickness(0, 12, 0, 0) };
    private readonly Button detailStar, prevButton, nextButton;
    private List<Photo> navList = new();
    private int navIndex;
    private Photo? current;
    private int detailGeneration;

    private GalleryWindow(MainWindow mw) : base(mw, "gallery", "GALERIE", "Photos", 1100, 780)
    {
        // chaque ouverture vérifie les conditions et débloque ce qui peut l'être (comme l'ancienne galerie)
        mw.CheckGalleryUnlock();

        // sous-titre : mention légale de l'ancienne galerie
        if (TitleText.Parent is Panel titles)
            titles.Children.Add(new TextBlock
            {
                Text = "Contenu réservé à un usage personnel (pas d'usage commercial).",
                FontSize = 12, Foreground = Res("HudTextMuted"), Margin = new Thickness(0, 3, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis,
            });

        counter = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 14, 0) };
        HeaderButtons.Children.Insert(0, counter);
        HeaderButtons.VerticalAlignment = VerticalAlignment.Center;

        // étiquettes, de la plus fréquente à la plus rare
        allTags = mw.Photos.SelectMany(p => p.TagsTrans).Where(t => !string.IsNullOrWhiteSpace(t))
            .GroupBy(t => t).OrderByDescending(g => g.Count()).Select(g => g.Key).ToList();

        #region Barre de filtres
        search = SearchBox("Chercher par nom ou description…", out var searchContainer);
        search.TextChanged += (_, _) => { searchDelay.Stop(); searchDelay.Start(); };
        searchDelay.Tick += (_, _) => { searchDelay.Stop(); Refresh(); };

        var sortRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 0, 0) };
        sortRow.Children.Add(new TextBlock { Text = "TRIER", Style = St("HudEyebrow"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) });
        foreach (var (label, index) in new[] { ("Par défaut", 0), ("Nom", 1), ("Date de déblocage", 2) })
        {
            var pill = new Pill(label) { On = index == 0, Margin = new Thickness(0, 0, 6, 0) };
            pill.Clicked += _ =>
            {
                if (sort == index)
                    return;
                sort = index;
                foreach (var p in sortPills)
                    p.On = p == pill;
                Refresh();
            };
            sortPills.Add(pill);
            sortRow.Children.Add(pill);
        }
        var topRow = new Grid();
        topRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 180 });
        topRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        topRow.Children.Add(searchContainer);
        Grid.SetColumn(sortRow, 1);
        topRow.Children.Add(sortRow);

        illustrations = FilterPill("Illustrations", "");
        miniatures = FilterPill("Miniatures", "");
        unlockedOnly = FilterPill("Débloquées", "");
        unlockedOnly.On = true;
        lockedOnly = FilterPill("Verrouillées", "");
        favorites = FilterPill("Seulement mes favoris", "");
        var filterRow = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
        filterRow.Children.Add(illustrations);
        filterRow.Children.Add(miniatures);
        filterRow.Children.Add(Divider());
        filterRow.Children.Add(unlockedOnly);
        filterRow.Children.Add(lockedOnly);
        filterRow.Children.Add(Divider());
        filterRow.Children.Add(favorites);

        var filters = new StackPanel { Margin = new Thickness(24, 2, 20, 6) };
        filters.Children.Add(topRow);
        filters.Children.Add(filterRow);
        if (allTags.Count > 0)
            filters.Children.Add(tagPanel);
        BuildTags();
        #endregion

        #region Grille
        more = new Button { Style = St("HudGhostButton"), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 14, 0, 0), Visibility = Visibility.Collapsed };
        more.Click += (_, _) => ShowMore();
        emptyText = Empty("Rien ici avec ces filtres : essaie d'en retirer un.");
        empty.Children.Add(emptyText);
        var reset = Ghost("Réinitialiser les filtres", ResetFilters);
        reset.HorizontalAlignment = HorizontalAlignment.Center;
        reset.Margin = new Thickness(0, 16, 0, 0);
        empty.Children.Add(reset);

        var gridHost = new StackPanel();
        gridHost.Children.Add(grid);
        gridHost.Children.Add(more);
        gridHost.Children.Add(empty);
        gridHost.SizeChanged += (_, e) => { if (e.WidthChanged) Relayout(e.NewSize.Width); };
        scroll = Scroll(gridHost, new Thickness(18, 4, 14, 20));
        scroll.ScrollChanged += (_, e) =>
        {
            if (shown < filtered.Count && e.ExtentHeight > 0 && e.VerticalOffset + e.ViewportHeight >= e.ExtentHeight - 420)
                ShowMore();
        };
        #endregion

        #region Vue détaillée
        detailLoading = new TextBlock { Text = "Chargement…", Foreground = Res("HudTextMuted"), FontSize = 13, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        RenderOptions.SetBitmapScalingMode(detailImage, BitmapScalingMode.HighQuality);
        detailImage.SizeChanged += (_, e) => detailImage.Clip = new RectangleGeometry(new Rect(e.NewSize), 14, 14);
        detailImage.Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 30, ShadowDepth = 8, Direction = 270, Opacity = 0.45, Color = Colors.Black };
        prevButton = NavButton("", "Photo précédente (←)", () => Step(-1));
        prevButton.HorizontalAlignment = HorizontalAlignment.Left;
        nextButton = NavButton("", "Photo suivante (→)", () => Step(1));
        nextButton.HorizontalAlignment = HorizontalAlignment.Right;
        var imageFrame = new Grid { Margin = new Thickness(56, 8, 56, 8) };
        imageFrame.Children.Add(detailLoading);
        imageFrame.Children.Add(detailImage);
        imageArea.Children.Add(imageFrame);
        imageArea.Children.Add(prevButton);
        imageArea.Children.Add(nextButton);

        detailEyebrow = new TextBlock { Style = St("HudEyebrow") };
        detailTitle = new TextBlock { Style = St("HudTitle"), FontSize = 22, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) };
        detailDate = new TextBlock { FontSize = 12.5, Foreground = Res("HudTextMuted"), Margin = new Thickness(0, 6, 0, 0), TextWrapping = TextWrapping.Wrap };
        detailDescription = new TextBlock { Style = St("HudBodyText"), FontSize = 13.5, Margin = new Thickness(0, 16, 0, 0) };
        detailPosition = new TextBlock { FontFamily = (FontFamily)FindResource("HudMono"), FontSize = 11.5, Foreground = Res("HudTextMuted"), VerticalAlignment = VerticalAlignment.Center };
        detailStar = IconButton("", "Ajouter aux favoris", ToggleCurrentStar);
        var closeDetail = IconButton("", "Fermer (Échap)", CloseDetail);
        var infoHead = new DockPanel { LastChildFill = true };
        var headButtons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(8, -6, -8, 0) };
        headButtons.Children.Add(detailStar);
        headButtons.Children.Add(closeDetail);
        DockPanel.SetDock(headButtons, Dock.Right);
        infoHead.Children.Add(headButtons);
        infoHead.Children.Add(detailEyebrow);

        var infoTop = new StackPanel();
        infoTop.Children.Add(infoHead);
        infoTop.Children.Add(detailTitle);
        infoTop.Children.Add(detailDate);
        infoTop.Children.Add(detailTags);
        infoTop.Children.Add(detailDescription);
        var infoScroll = Scroll(infoTop, new Thickness(0, 0, 6, 0));

        var save = Primary("Enregistrer…", () => { if (current != null) SaveOne(current); });
        save.HorizontalAlignment = HorizontalAlignment.Stretch;
        save.Margin = new Thickness(0, 0, 0, 8);
        var copy = Ghost("Copier l'image", CopyCurrent);
        copy.HorizontalAlignment = HorizontalAlignment.Stretch;
        var actions = new StackPanel { Margin = new Thickness(0, 14, 0, 0) };
        actions.Children.Add(save);
        actions.Children.Add(copy);
        var position = new Grid { Margin = new Thickness(0, 12, 0, 0) };
        position.Children.Add(detailPosition);
        position.Children.Add(new TextBlock { Text = "← → pour naviguer", FontSize = 11.5, Foreground = Res("HudTextMuted"), HorizontalAlignment = HorizontalAlignment.Right });
        actions.Children.Add(position);

        var infoDock = new DockPanel();
        DockPanel.SetDock(actions, Dock.Bottom);
        infoDock.Children.Add(actions);
        infoDock.Children.Add(infoScroll);
        infoPanel = Card(infoDock, new Thickness(20, 16, 20, 18));
        infoPanel.Width = 320;
        infoPanel.Margin = new Thickness(16, 8, 0, 8);

        detailContent.ColumnDefinitions.Add(new ColumnDefinition());
        detailContent.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        detailContent.RowDefinitions.Add(new RowDefinition());
        detailContent.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        detailContent.Children.Add(imageArea);
        detailContent.Children.Add(infoPanel);
        overlay.Children.Add(detailContent);
        overlay.SizeChanged += (_, e) => LayoutDetail(e.NewSize.Width);
        overlay.MouseLeftButtonUp += (_, e) =>
        {
            if (e.OriginalSource == overlay || e.OriginalSource == imageArea || e.OriginalSource == detailContent)
                CloseDetail();
        };
        #endregion

        var main = new Grid();
        main.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        main.RowDefinitions.Add(new RowDefinition());
        main.Children.Add(filters);
        Grid.SetRow(scroll, 1);
        main.Children.Add(scroll);
        var root = new Grid();
        root.Children.Add(main);
        root.Children.Add(overlay);
        Body = root;

        #region Pied : sélection et export
        selectionInfo = new TextBlock { FontSize = 12.5, Foreground = Res("HudTextMuted"), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        selectAll = Ghost("Tout sélectionner", ToggleSelectAll);
        selectAll.Margin = new Thickness(0, 0, 14, 0);
        var left = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        left.Children.Add(selectAll);
        left.Children.Add(selectionInfo);
        var exportAll = Ghost("Exporter toutes les photos débloquées", ExportAllUnlocked);
        exportAll.Margin = new Thickness(0, 0, 8, 0);
        var exportSel = Primary("Exporter la sélection", ExportSelection);
        var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        right.Children.Add(exportAll);
        right.Children.Add(exportSel);
        var footGrid = new Grid();
        footGrid.ColumnDefinitions.Add(new ColumnDefinition());
        footGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        footGrid.Children.Add(left);
        Grid.SetColumn(right, 1);
        footGrid.Children.Add(right);
        var foot = new Border { BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(24, 12, 20, 16), Child = footGrid };
        foot.SetResourceReference(Border.BorderBrushProperty, "HudStroke");
        Footer = foot;
        #endregion

        Activated += (_, _) =>
        {
            // une photo a pu être débloquée ailleurs pendant que la galerie était ouverte
            if (IsLoaded && unlockedAtRefresh >= 0 && MW.Photos.Count(p => p.IsUnlock) != unlockedAtRefresh && overlay.Visibility != Visibility.Visible)
                Refresh();
        };
        Loaded += (_, _) => Refresh();
        Closed += (_, _) =>
        {
            searchDelay.Stop();
            if (instance == this)
                instance = null;
        };
    }

    #region API publique
    /// <summary>Ouvre la galerie (une seule fenêtre à la fois) et la ramène au premier plan</summary>
    public static GalleryWindow Open(MainWindow mw)
    {
        instance ??= new GalleryWindow(mw);
        instance.Present();
        return instance;
    }

    /// <summary>Affiche une photo dans la vue détaillée</summary>
    public void ShowPhoto(Photo p)
    {
        if (!p.IsUnlock)
        {
            Notify("Cette photo n'est pas encore débloquée.");
            return;
        }
        if (!IsLoaded)
        {
            // la grille se remplit au chargement ; la vue détaillée s'ouvre juste après
            RoutedEventHandler? once = null;
            once = (_, _) => { Loaded -= once; Dispatcher.BeginInvoke(() => OpenDetail(p), DispatcherPriority.Background); };
            Loaded += once;
            return;
        }
        OpenDetail(p);
    }
    #endregion

    #region Filtres
    private Pill FilterPill(string label, string glyph)
    {
        var pill = new Pill(label, glyph);
        pill.Clicked += p => { p.On = !p.On; Refresh(); };
        return pill;
    }

    private Border Divider()
    {
        var d = new Border { Width = 1, Height = 18, Margin = new Thickness(6, 0, 12, 6), VerticalAlignment = VerticalAlignment.Center };
        d.SetResourceReference(Border.BackgroundProperty, "HudStroke");
        return d;
    }

    private void BuildTags()
    {
        tagPanel.Children.Clear();
        if (allTags.Count == 0)
            return;
        tagPanel.Children.Add(new TextBlock { Text = "ÉTIQUETTES", Style = St("HudEyebrow"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 6) });
        for (int i = 0; i < allTags.Count; i++)
        {
            var tag = allTags[i];
            if (!tagsExpanded && i >= TagsCollapsed && !tagFilter.Contains(tag))
                continue;
            var pill = new Pill(tag, "", compact: true) { On = tagFilter.Contains(tag) };
            pill.Clicked += p =>
            {
                if (!tagFilter.Remove(tag))
                    tagFilter.Add(tag);
                p.On = tagFilter.Contains(tag);
                Refresh();
            };
            tagPanel.Children.Add(pill);
        }
        if (allTags.Count > TagsCollapsed)
        {
            var toggle = new Button
            {
                Style = St("HudGhostButton"), Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(0, 0, 6, 6), FontSize = 12,
                Content = tagsExpanded ? "Moins d'étiquettes" : $"+{allTags.Count - TagsCollapsed} étiquettes",
            };
            toggle.Click += (_, _) => { tagsExpanded = !tagsExpanded; BuildTags(); };
            tagPanel.Children.Add(toggle);
        }
    }

    private void ResetFilters()
    {
        searchDelay.Stop();
        search.Text = "";
        searchDelay.Stop();
        illustrations.On = miniatures.On = lockedOnly.On = favorites.On = false;
        unlockedOnly.On = true;
        tagFilter.Clear();
        BuildTags();
        Refresh();
    }

    private static bool Contains(string? source, string text) => !string.IsNullOrEmpty(source) && source.Contains(text, StringComparison.OrdinalIgnoreCase);

    private static string TranslatedDescription(Photo p) => string.IsNullOrEmpty(p.Description) ? "" : p.Description.Translate();

    /// <summary>Recalcule la liste filtrée et reconstruit la grille</summary>
    private void Refresh()
    {
        var text = search.Text.Trim();
        // une famille de filtres sans rien de coché = tout
        bool wantIllustration = illustrations.On || !miniatures.On;
        bool wantThumbnail = miniatures.On || !illustrations.On;
        bool wantLocked = lockedOnly.On || !unlockedOnly.On;
        bool wantUnlocked = unlockedOnly.On || !lockedOnly.On;
        bool onlyStars = favorites.On;

        bool Match(Photo p) =>
            (!onlyStars || p.IsStar)
            && (wantIllustration || p.Type != Photo.PhotoType.Illustration)
            && (wantThumbnail || p.Type != Photo.PhotoType.Thumbnail)
            && (tagFilter.Count == 0 || tagFilter.All(t => p.TagsTrans.Contains(t)))
            && (text.Length == 0
                || Contains(p.Name, text)
                || Contains(p.Description, text)
                || Contains(p.TranslateName, text)
                || Contains(TranslatedDescription(p), text));

        var list = new List<Photo>();
        if (wantLocked)
            list.AddRange(MW.Photos.Where(p => !p.IsUnlock && Match(p)));
        if (wantUnlocked)
            list.AddRange(MW.Photos.Where(p => p.IsUnlock && Match(p)));
        list = sort switch
        {
            1 => list.OrderBy(p => p.TranslateName, StringComparer.Create(Fr, true)).ToList(),
            2 => list.OrderByDescending(p => p.PlayerInfo?.UnlockTime ?? DateTime.MinValue).ToList(),
            _ => list,
        };

        filtered = list;
        unlockedAtRefresh = MW.Photos.Count(p => p.IsUnlock);
        grid.Children.Clear();
        cardOf.Clear();
        repaintOf.Clear();
        shown = 0;
        if (MW.Photos.Count == 0)
            emptyText.Text = "Aucune photo installée pour l'instant.";
        else
            emptyText.Text = "Rien ici avec ces filtres : essaie d'en retirer un.";
        empty.Visibility = filtered.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (UiMotion.Enabled)
            grid.BeginAnimation(OpacityProperty, new DoubleAnimation(0.2, 1, TimeSpan.FromMilliseconds(180)));
        ShowMore();
        scroll.ScrollToTop();
        UpdateCounter();
        UpdateFooter();
    }

    private void ShowMore()
    {
        var page = filtered.Skip(shown).Take(PageSize).ToList();
        foreach (var p in page)
        {
            var card = p.IsUnlock ? UnlockedCard(p) : LockedCard(p);
            cardOf[p] = card;
            grid.Children.Add(card);
        }
        shown += page.Count;
        LoadThumbs(page);
        more.Content = $"Afficher plus ({filtered.Count - shown})";
        more.Visibility = shown < filtered.Count ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Colonnes de la grille : cartes d'au moins 210 px, réparties sur toute la largeur</summary>
    private void Relayout(double width)
    {
        if (width <= 0)
            return;
        int columns = Math.Max(1, (int)(width / MinCardWidth));
        double itemWidth = Math.Floor(width / columns);
        grid.ItemWidth = itemWidth;
        grid.ItemHeight = Math.Round((itemWidth - 12 - 14) * 0.75) + 14 + 2 + 58 + 12;
    }

    private void UpdateCounter()
    {
        int total = MW.Photos.Count, unlocked = MW.Photos.Count(p => p.IsUnlock);
        counter.Inlines.Clear();
        var n = new Run(unlocked.ToString("N0", Fr)) { FontFamily = (FontFamily)FindResource("HudDisplay"), FontWeight = FontWeights.SemiBold, FontSize = 15 };
        n.SetResourceReference(TextElement.ForegroundProperty, "HudText");
        var rest = new Run($" / {total.ToString("N0", Fr)} débloquées") { FontSize = 12.5 };
        rest.SetResourceReference(TextElement.ForegroundProperty, "HudTextMuted");
        counter.Inlines.Add(n);
        counter.Inlines.Add(rest);
    }
    #endregion

    #region Cartes
    private static string TypeLabel(Photo p) => p.Type switch
    {
        Photo.PhotoType.Illustration => "Illustration",
        Photo.PhotoType.Thumbnail => "Miniature",
        _ => "Photo",
    };

    private static string FormatPrice(double price)
    {
        if (price < 100000)
            return price.ToString("N0", Fr);
        return (price / 1000).ToString("N0", Fr) + "k";
    }

    /// <summary>Cadre commun : carte arrondie, zone image (avec emplacement) et bandeau titre</summary>
    private (Border card, Grid media, Border thumb, StackPanel strip) Shell(Photo p)
    {
        var placeholderIcon = new TextBlock { Text = "", FontSize = 22, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Opacity = 0.35 };
        placeholderIcon.SetResourceReference(TextBlock.FontFamilyProperty, "HudIcons");
        placeholderIcon.SetResourceReference(TextBlock.ForegroundProperty, "HudTextMuted");
        var placeholder = new Border { CornerRadius = new CornerRadius(11), Child = placeholderIcon };
        placeholder.SetResourceReference(Border.BackgroundProperty, "HudSurfaceHover");
        var thumb = new Border { CornerRadius = new CornerRadius(11), Opacity = 0 };
        var media = new Grid();
        media.Children.Add(placeholder);
        media.Children.Add(thumb);

        var strip = new StackPanel { Margin = new Thickness(6, 10, 6, 2), Height = 46 };
        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition());
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.Children.Add(media);
        Grid.SetRow(strip, 1);
        layout.Children.Add(strip);

        var card = new Border
        {
            CornerRadius = new CornerRadius(16), BorderThickness = new Thickness(1), Padding = new Thickness(6), Margin = new Thickness(6),
            Child = layout, SnapsToDevicePixels = true,
        };
        card.SetResourceReference(Border.BackgroundProperty, "HudSurfaceRaised");
        card.SetResourceReference(Border.BorderBrushProperty, "HudStroke");
        return (card, media, thumb, strip);
    }

    private TextBlock CardTitle(Photo p, bool muted)
    {
        var t = new TextBlock { Text = p.TranslateName, FontSize = 13.5, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
        t.SetResourceReference(TextBlock.ForegroundProperty, muted ? "HudTextMuted" : "HudText");
        return t;
    }

    private TextBlock CardMeta(string text)
    {
        var t = new TextBlock { Text = text, FontSize = 11.5, Margin = new Thickness(0, 3, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
        t.SetResourceReference(TextBlock.ForegroundProperty, "HudTextMuted");
        return t;
    }

    /// <summary>Pastille ronde posée sur l'image (sélection, favori)</summary>
    private static Border Badge(HorizontalAlignment h, string tip)
    {
        var glyph = new TextBlock { FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Foreground = Brushes.White };
        glyph.SetResourceReference(TextBlock.FontFamilyProperty, "HudIcons");
        return new Border
        {
            Width = 28, Height = 28, CornerRadius = new CornerRadius(14), BorderThickness = new Thickness(1),
            HorizontalAlignment = h, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(8), Cursor = Cursors.Hand,
            Child = glyph, ToolTip = tip,
        };
    }

    private static readonly Brush BadgeBack = Frozen(Color.FromArgb(0xA6, 0x12, 0x13, 0x1A));
    private static readonly Brush BadgeStroke = Frozen(Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF));
    private static readonly Brush OnImageText = Frozen(Color.FromArgb(0xEE, 0xF3, 0xF1, 0xF6));

    private static Brush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    private FrameworkElement UnlockedCard(Photo p)
    {
        var (card, media, thumb, strip) = Shell(p);
        strip.Children.Add(CardTitle(p, muted: false));
        var date = p.PlayerInfo?.UnlockTime;
        strip.Children.Add(CardMeta(date is DateTime d && d > DateTime.MinValue ? $"{TypeLabel(p)} · {d.ToString("d MMM yyyy", Fr)}" : TypeLabel(p)));

        var check = Badge(HorizontalAlignment.Left, "Sélectionner pour l'export");
        var star = Badge(HorizontalAlignment.Right, "Favori");
        media.Children.Add(check);
        media.Children.Add(star);

        var description = TranslatedDescription(p);
        card.ToolTip = string.IsNullOrWhiteSpace(description) ? null : description;
        card.Cursor = Cursors.Hand;
        card.Focusable = true;
        card.FocusVisualStyle = null;

        void Paint()
        {
            bool selected = selection.Contains(p), hover = card.IsMouseOver || card.IsKeyboardFocused;
            card.SetResourceReference(Border.BorderBrushProperty, selected ? "HudAccent" : hover ? "HudTextMuted" : "HudStroke");
            var cg = (TextBlock)check.Child;
            cg.Text = selected ? "" : "";
            if (selected)
                check.SetResourceReference(Border.BackgroundProperty, "HudAccent");
            else
                check.Background = BadgeBack;
            check.BorderBrush = selected ? Brushes.Transparent : BadgeStroke;
            check.Opacity = selected || hover ? 1 : 0;
            check.ToolTip = selected ? "Retirer de la sélection" : "Sélectionner pour l'export";

            var sg = (TextBlock)star.Child;
            sg.Text = p.IsStar ? "" : "";
            if (p.IsStar)
                sg.SetResourceReference(TextBlock.ForegroundProperty, "HudAmber");
            else
                sg.Foreground = Brushes.White;
            star.Background = BadgeBack;
            star.BorderBrush = BadgeStroke;
            star.Opacity = p.IsStar || hover ? 1 : 0;
            star.ToolTip = p.IsStar ? "Retirer des favoris" : "Ajouter aux favoris";
        }
        repaintOf[p] = Paint;
        Paint();

        check.MouseLeftButtonDown += (_, e) => e.Handled = true;
        check.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            if (!selection.Remove(p))
                selection.Add(p);
            Paint();
            UpdateFooter();
        };
        star.MouseLeftButtonDown += (_, e) => e.Handled = true;
        star.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            p.IsStar = !p.IsStar;
            Paint();
            if (current == p)
                PaintDetailStar();
        };

        card.MouseEnter += (_, _) => { Paint(); Zoom(thumb, 1.04); };
        card.MouseLeave += (_, _) => { Paint(); Zoom(thumb, 1); };
        card.GotKeyboardFocus += (_, _) => Paint();
        card.LostKeyboardFocus += (_, _) => Paint();
        bool pressed = false;
        card.MouseLeftButtonDown += (_, _) => pressed = true;
        card.MouseLeftButtonUp += (_, _) =>
        {
            if (pressed)
                OpenDetail(p);
            pressed = false;
        };
        card.MouseLeave += (_, _) => pressed = false;
        card.KeyDown += (_, e) =>
        {
            if (e.Key is Key.Enter)
            {
                OpenDetail(p);
                e.Handled = true;
            }
            else if (e.Key is Key.Space)
            {
                if (!selection.Remove(p))
                    selection.Add(p);
                Paint();
                UpdateFooter();
                e.Handled = true;
            }
        };
        card.Tag = thumb;
        return card;
    }

    private FrameworkElement LockedCard(Photo p)
    {
        var (card, media, thumb, strip) = Shell(p);
        strip.Children.Add(CardTitle(p, muted: true));
        strip.Children.Add(CardMeta($"{TypeLabel(p)} · verrouillée"));

        // voile sombre en bas de l'image pour lire la condition
        var shade = new Border
        {
            CornerRadius = new CornerRadius(11),
            Background = new LinearGradientBrush(
                new GradientStopCollection
                {
                    new GradientStop(Color.FromArgb(0x10, 0x0E, 0x0F, 0x14), 0),
                    new GradientStop(Color.FromArgb(0x80, 0x0E, 0x0F, 0x14), 0.4),
                    new GradientStop(Color.FromArgb(0xF0, 0x0E, 0x0F, 0x14), 1),
                }, 90),
        };
        media.Children.Add(shade);

        var lockGlyph = new TextBlock { Text = "", FontSize = 11, Foreground = OnImageText, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
        lockGlyph.SetResourceReference(TextBlock.FontFamilyProperty, "HudIcons");
        var lockRow = new StackPanel { Orientation = Orientation.Horizontal };
        lockRow.Children.Add(lockGlyph);
        lockRow.Children.Add(new TextBlock { Text = "Verrouillée", FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = OnImageText });
        media.Children.Add(new Border
        {
            CornerRadius = new CornerRadius(11), Padding = new Thickness(9, 4, 10, 4), Margin = new Thickness(8),
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
            Background = BadgeBack, BorderBrush = BadgeStroke, BorderThickness = new Thickness(1), Child = lockRow,
        });

        var cond = p.UnlockAble;
        string reason = cond.CheckReason(MW.GameSavesData);
        var bottom = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(12, 0, 12, 12) };
        bottom.Children.Add(new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(reason) ? (cond.SellPrice > 0 ? "À acheter" : "Aucune condition connue") : reason,
            FontSize = 11.5, LineHeight = 16, Foreground = OnImageText, TextWrapping = TextWrapping.Wrap,
            TextTrimming = TextTrimming.CharacterEllipsis, MaxHeight = 48,
        });

        string tip = reason;
        if (cond.SellPrice > 0)
        {
            string price = FormatPrice(cond.SellPrice);
            bool can = !cond.SellBoth || cond.Check(MW.GameSavesData);
            var label = new TextBlock();
            label.Inlines.Add(new Run(can ? "Débloquer · " : "Conditions requises · ") { Foreground = Brushes.White });
            var amount = new Run(price + " $") { FontWeight = FontWeights.SemiBold };
            amount.SetResourceReference(TextElement.ForegroundProperty, "HudAmber");
            label.Inlines.Add(amount);
            var buy = new Button
            {
                Style = St("HudGhostButton"), Content = label, IsEnabled = can, HorizontalAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(12, 5, 12, 5), Margin = new Thickness(0, 8, 0, 0), Background = BadgeBack, FontSize = 12,
                ToolTip = can ? null : $"Remplis d'abord les conditions, puis débloque-la pour {price} $.",
            };
            ToolTipService.SetShowOnDisabled(buy, true);
            buy.Click += (_, _) => Buy(p);
            bottom.Children.Add(buy);
            tip = (cond.SellBoth
                ? $"Coûte {price} $ une fois ces conditions remplies :"
                : $"Coûte {price} $, ou se débloque seule si ces conditions sont remplies :") + "\n" + reason;
        }
        media.Children.Add(bottom);
        card.ToolTip = string.IsNullOrWhiteSpace(tip) ? null : tip.Trim();
        card.MouseEnter += (_, _) => card.SetResourceReference(Border.BorderBrushProperty, "HudTextMuted");
        card.MouseLeave += (_, _) => card.SetResourceReference(Border.BorderBrushProperty, "HudStroke");
        card.Tag = thumb;
        return card;
    }

    private static void Zoom(Border thumb, double to)
    {
        if (thumb.Background is not ImageBrush { RelativeTransform: ScaleTransform s } || !UiMotion.Enabled)
            return;
        var a = new DoubleAnimation(to, TimeSpan.FromMilliseconds(260)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        s.BeginAnimation(ScaleTransform.ScaleXProperty, a);
        s.BeginAnimation(ScaleTransform.ScaleYProperty, a);
    }

    /// <summary>Débloque une photo contre de l'argent (mêmes règles que l'ancienne galerie)</summary>
    private void Buy(Photo p)
    {
        var save = MW.GameSavesData.GameSave;
        int price = p.UnlockAble.SellPrice;
        if (price <= 0 || p.IsUnlock)
            return;
        if (p.UnlockAble.SellBoth && !p.UnlockAble.Check(MW.GameSavesData))
        {
            Notify("Remplis d'abord les conditions de cette photo.", HudToast.Kind.Warning);
            return;
        }
        if (save.Money < price)
        {
            Notify($"Il te manque {FormatPrice(Math.Ceiling(price - save.Money))} $ pour débloquer cette photo.", HudToast.Kind.Warning);
            return;
        }
        save.Money -= price;
        MW.Sandbox?.AfterSpending();
        p.Unlock(MW);
        try { MW.ActivityLogs.Add(new ActivityLog("photo_unlock", p.TranslateName)); } catch { }
        unlockedAtRefresh = MW.Photos.Count(x => x.IsUnlock);

        if (cardOf.TryGetValue(p, out var old))
        {
            int index = grid.Children.IndexOf(old);
            var fresh = UnlockedCard(p);
            cardOf[p] = fresh;
            if (index >= 0)
            {
                grid.Children.RemoveAt(index);
                grid.Children.Insert(index, fresh);
                UiMotion.PopIn(fresh, new Point(0.5, 0.5), 0.96, 240);
            }
            LoadThumbs(new List<Photo> { p });
        }
        UpdateCounter();
        UpdateFooter();
        Notify($"Photo débloquée : {p.TranslateName}", HudToast.Kind.Success);
    }
    #endregion

    #region Miniatures (décodées hors du fil d'interface)
    /// <summary>
    /// Charge les miniatures d'une page : une seule ouverture d'archive par pack, décodage réduit, niveaux de gris
    /// pour les photos verrouillées, puis application sur l'interface (mise en cache pour les filtres suivants).
    /// </summary>
    private void LoadThumbs(List<Photo> photos)
    {
        var jobs = new List<(Photo photo, string? zip, bool gray)>();
        foreach (var p in photos)
        {
            bool gray = !p.IsUnlock;
            var cache = gray ? grayThumbs : colorThumbs;
            if (cache.TryGetValue(p, out var known))
                ApplyThumb(p, known, animate: false);
            else
                jobs.Add((p, MW.FileSources.FindSource(p.Zip + ".zlps") ?? MW.FileSources.FindSource(p.Zip + ".zip"), gray));
        }
        if (jobs.Count == 0)
            return;
        Task.Run(() =>
        {
            foreach (var group in jobs.GroupBy(j => j.zip))
            {
                ZipArchive? archive = null;
                try
                {
                    if (group.Key != null && File.Exists(group.Key))
                        archive = ZipFile.OpenRead(group.Key);
                }
                catch { archive = null; }
                try
                {
                    foreach (var (photo, _, gray) in group)
                    {
                        BitmapSource? bmp = null;
                        try
                        {
                            var entry = archive?.GetEntry(photo.Path);
                            if (entry != null)
                                bmp = DecodeThumb(entry, ThumbPixels, gray);
                        }
                        catch { bmp = null; }
                        var result = bmp;
                        Dispatcher.BeginInvoke(() =>
                        {
                            (gray ? grayThumbs : colorThumbs)[photo] = result;
                            ApplyThumb(photo, result, animate: true);
                        }, DispatcherPriority.Background);
                    }
                }
                finally
                {
                    archive?.Dispose();
                }
            }
        });
    }

    private static BitmapSource DecodeThumb(ZipArchiveEntry entry, int maxPixels, bool gray)
    {
        var ms = new MemoryStream();
        using (var s = entry.Open())
            s.CopyTo(ms);
        ms.Position = 0;
        var frame = BitmapDecoder.Create(ms, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None).Frames[0];
        int w = frame.PixelWidth, h = frame.PixelHeight;
        ms.Position = 0;
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.StreamSource = ms;
        if (w >= h && w > maxPixels)
            bmp.DecodePixelWidth = maxPixels;
        else if (h > w && h > maxPixels)
            bmp.DecodePixelHeight = maxPixels;
        bmp.EndInit();
        bmp.Freeze();
        ms.Dispose();
        if (!gray)
            return bmp;

        // niveaux de gris (même rendu que Photo.ConvertToGrayScale), alpha conservé
        var src = new FormatConvertedBitmap(bmp, PixelFormats.Bgra32, null, 0);
        int pw = src.PixelWidth, ph = src.PixelHeight, stride = pw * 4;
        var px = new byte[stride * ph];
        src.CopyPixels(px, stride, 0);
        for (int i = 0; i < px.Length; i += 4)
        {
            byte g = (byte)((px[i] + px[i + 1] + px[i + 2]) / 3);
            px[i] = px[i + 1] = px[i + 2] = g;
        }
        var result = BitmapSource.Create(pw, ph, 96, 96, PixelFormats.Bgra32, null, px, stride);
        result.Freeze();
        return result;
    }

    private void ApplyThumb(Photo p, BitmapSource? bmp, bool animate)
    {
        if (!cardOf.TryGetValue(p, out var card) || card.Tag is not Border thumb)
            return;
        if (bmp == null)
            return; // l'emplacement (icône image) reste affiché
        thumb.Background = new ImageBrush(bmp)
        {
            Stretch = p.Type == Photo.PhotoType.Thumbnail ? Stretch.Uniform : Stretch.UniformToFill,
            RelativeTransform = new ScaleTransform(1, 1, 0.5, 0.5),
        };
        double target = p.IsUnlock ? 1 : 0.55;
        if (animate && UiMotion.Enabled)
            thumb.BeginAnimation(OpacityProperty, new DoubleAnimation(0, target, TimeSpan.FromMilliseconds(220)));
        else
        {
            thumb.BeginAnimation(OpacityProperty, null);
            thumb.Opacity = target;
        }
    }
    #endregion

    #region Vue détaillée
    private Button NavButton(string glyph, string tip, Action run)
    {
        var b = IconButton(glyph, tip, run);
        b.Width = b.Height = 44;
        b.FontSize = 16;
        b.VerticalAlignment = VerticalAlignment.Center;
        return b;
    }

    private void LayoutDetail(double width)
    {
        bool narrow = width < 820;
        Grid.SetRow(infoPanel, narrow ? 1 : 0);
        Grid.SetColumn(infoPanel, narrow ? 0 : 1);
        infoPanel.Width = narrow ? double.NaN : 320;
        infoPanel.MaxHeight = narrow ? 300 : double.PositiveInfinity;
        infoPanel.Margin = narrow ? new Thickness(0, 12, 0, 0) : new Thickness(16, 8, 0, 8);
    }

    private void OpenDetail(Photo p)
    {
        navList = filtered.Where(x => x.IsUnlock).ToList();
        navIndex = navList.IndexOf(p);
        if (navIndex < 0)
        {
            navList = new List<Photo> { p };
            navIndex = 0;
        }
        var surface = ((SolidColorBrush)Res("HudSurface")).Color;
        overlay.Background = new SolidColorBrush(Color.FromArgb(0xF2, surface.R, surface.G, surface.B));
        bool wasOpen = overlay.Visibility == Visibility.Visible;
        overlay.Visibility = Visibility.Visible;
        if (!wasOpen)
        {
            if (UiMotion.Enabled)
                overlay.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(170)));
            UiMotion.PopIn(detailContent, new Point(0.5, 0.5), 0.97, 220);
        }
        else if (overlay.Opacity < 1)
        {
            // rouverte pendant le fondu de fermeture
            if (UiMotion.Enabled)
                overlay.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(120)));
            else
            {
                overlay.BeginAnimation(OpacityProperty, null);
                overlay.Opacity = 1;
            }
        }
        overlay.Focus();
        Display(p);
    }

    private void Display(Photo p)
    {
        current = p;
        detailEyebrow.Text = TypeLabel(p).ToUpperInvariant();
        detailTitle.Text = p.TranslateName;
        var date = p.PlayerInfo?.UnlockTime;
        detailDate.Text = date is DateTime d && d > DateTime.MinValue
            ? "Débloquée le " + d.ToString("d MMMM yyyy 'à' HH:mm", Fr)
            : "Débloquée";
        var desc = TranslatedDescription(p);
        detailDescription.Text = desc;
        detailDescription.Visibility = string.IsNullOrWhiteSpace(desc) ? Visibility.Collapsed : Visibility.Visible;

        detailTags.Children.Clear();
        foreach (var tag in p.TagsTrans.Where(t => !string.IsNullOrWhiteSpace(t)))
        {
            var t = new TextBlock { Text = tag, FontSize = 11.5 };
            t.SetResourceReference(TextBlock.ForegroundProperty, "HudTextMuted");
            var chip = new Border { CornerRadius = new CornerRadius(10), Padding = new Thickness(9, 3, 9, 3), Margin = new Thickness(0, 0, 6, 6), BorderThickness = new Thickness(1), Child = t };
            chip.SetResourceReference(Border.BorderBrushProperty, "HudStroke");
            detailTags.Children.Add(chip);
        }
        detailTags.Visibility = detailTags.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        bool multi = navList.Count > 1;
        prevButton.Visibility = nextButton.Visibility = multi ? Visibility.Visible : Visibility.Collapsed;
        detailPosition.Text = multi ? $"{navIndex + 1} / {navList.Count}" : "";
        ((FrameworkElement)detailPosition.Parent).Visibility = multi ? Visibility.Visible : Visibility.Collapsed;
        PaintDetailStar();
        LoadDetailImage(p);
    }

    private void LoadDetailImage(Photo p)
    {
        int generation = ++detailGeneration;
        ImageBehavior.SetAnimatedSource(detailImage, null);
        detailImage.Source = null;
        detailImage.Opacity = 0;
        detailLoading.Visibility = Visibility.Visible;
        // les GIF gardent leur flux ouvert (animation) : GetGifImage ; le reste : GetImage, plus économe
        bool gif = p.Path.EndsWith(".gif", StringComparison.OrdinalIgnoreCase);
        Task.Run(() =>
        {
            try { return gif ? p.GetGifImage(MW) : p.GetImage(MW); }
            catch { return null; }
        }).ContinueWith(t => Dispatcher.BeginInvoke(() =>
        {
            if (generation != detailGeneration)
                return;
            var img = t.Result;
            detailLoading.Visibility = Visibility.Collapsed;
            if (img == null)
            {
                detailLoading.Text = "Impossible d'afficher cette photo.";
                detailLoading.Visibility = Visibility.Visible;
                return;
            }
            detailLoading.Text = "Chargement…";
            // les petites images (miniatures) ne sont pas agrandies au-delà de 2×
            detailImage.MaxWidth = p.Type == Photo.PhotoType.Thumbnail ? img.PixelWidth * 2 : double.PositiveInfinity;
            detailImage.MaxHeight = p.Type == Photo.PhotoType.Thumbnail ? img.PixelHeight * 2 : double.PositiveInfinity;
            if (gif)
                ImageBehavior.SetAnimatedSource(detailImage, img);
            else
                detailImage.Source = img;
            if (UiMotion.Enabled)
                detailImage.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200)));
            else
            {
                detailImage.BeginAnimation(OpacityProperty, null);
                detailImage.Opacity = 1;
            }
        }));
    }

    private void Step(int delta)
    {
        if (overlay.Visibility != Visibility.Visible || navList.Count < 2)
            return;
        navIndex = (navIndex + delta + navList.Count) % navList.Count;
        Display(navList[navIndex]);
    }

    private void CloseDetail()
    {
        if (overlay.Visibility != Visibility.Visible)
            return;
        detailGeneration++;
        void Hide()
        {
            overlay.Visibility = Visibility.Collapsed;
            ImageBehavior.SetAnimatedSource(detailImage, null);
            detailImage.Source = null;
            if (current != null && cardOf.TryGetValue(current, out var card))
                card.Focus();
            current = null;
        }
        if (UiMotion.Enabled)
        {
            var a = new DoubleAnimation(0, TimeSpan.FromMilliseconds(130));
            a.Completed += (_, _) =>
            {
                if (overlay.Opacity == 0)
                {
                    overlay.BeginAnimation(OpacityProperty, null);
                    overlay.Opacity = 1;
                    Hide();
                }
            };
            overlay.BeginAnimation(OpacityProperty, a);
        }
        else
            Hide();
    }

    private void PaintDetailStar()
    {
        if (current == null)
            return;
        bool star = current.IsStar;
        detailStar.Content = star ? "" : "";
        detailStar.ToolTip = star ? "Retirer des favoris" : "Ajouter aux favoris";
        if (star)
            detailStar.SetResourceReference(ForegroundProperty, "HudAmber");
        else
            detailStar.ClearValue(ForegroundProperty);
    }

    private void ToggleCurrentStar()
    {
        if (current == null)
            return;
        current.IsStar = !current.IsStar;
        PaintDetailStar();
        if (repaintOf.TryGetValue(current, out var paint))
            paint();
    }

    private void CopyCurrent()
    {
        if (current == null)
            return;
        try
        {
            if (current.CopyImageToClipboard(MW))
                Notify("Image copiée", HudToast.Kind.Success);
            else
                Notify("Impossible de copier cette image.", HudToast.Kind.Warning);
        }
        catch (Exception e)
        {
            Notify("Impossible de copier cette image : " + e.Message, HudToast.Kind.Warning);
        }
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (overlay.Visibility == Visibility.Visible)
        {
            switch (e.Key)
            {
                case Key.Escape:
                    CloseDetail();
                    e.Handled = true;
                    return;
                case Key.Left:
                    Step(-1);
                    e.Handled = true;
                    return;
                case Key.Right:
                    Step(1);
                    e.Handled = true;
                    return;
            }
        }
        else if (e.Key == Key.Escape && e.OriginalSource == search && search.Text.Length > 0)
        {
            search.Text = "";
            e.Handled = true;
            return;
        }
        base.OnPreviewKeyDown(e);
    }
    #endregion

    #region Sélection et export
    private void UpdateFooter()
    {
        int n = selection.Count;
        selectionInfo.Text = n switch
        {
            0 => "Coche des photos pour les exporter.",
            1 => "1 photo sélectionnée",
            _ => $"{n.ToString("N0", Fr)} photos sélectionnées",
        };
        var visible = filtered.Where(p => p.IsUnlock).ToList();
        selectAll.IsEnabled = visible.Count > 0;
        selectAll.Content = visible.Count > 0 && visible.All(selection.Contains) ? "Tout désélectionner" : "Tout sélectionner";
    }

    private void ToggleSelectAll()
    {
        var visible = filtered.Where(p => p.IsUnlock).ToList();
        if (visible.Count == 0)
            return;
        if (visible.All(selection.Contains))
            selection.ExceptWith(visible);
        else
            selection.UnionWith(visible);
        foreach (var paint in repaintOf.Values)
            paint();
        UpdateFooter();
    }

    private void SaveOne(Photo p)
    {
        string ext = p.Path.Split('.').Last();
        var dialog = new SaveFileDialog
        {
            Title = "Enregistrer la photo",
            FileName = p.FilePath(),
            Filter = $"{ext.ToUpperInvariant()}|*.{ext}",
        };
        if (dialog.ShowDialog(this) != true)
            return;
        var path = dialog.FileName;
        Task.Run(() => p.SaveAs(MW, path)).ContinueWith(t => Dispatcher.BeginInvoke(() =>
        {
            if (t.Exception == null)
                Notify("Photo enregistrée : " + System.IO.Path.GetFileName(path), HudToast.Kind.Success);
            else
                Notify("Impossible d'enregistrer la photo : " + t.Exception.InnerException?.Message, HudToast.Kind.Warning);
        }));
    }

    private void ExportMany(List<Photo> photos, string done)
    {
        var dialog = new OpenFolderDialog { Title = "Choisis le dossier d'export" };
        if (dialog.ShowDialog(this) != true)
            return;
        var dir = dialog.FolderName;
        if (photos.Count > 20)
            Notify($"Export de {photos.Count.ToString("N0", Fr)} photos en cours…");
        Task.Run(() =>
        {
            int failed = 0;
            foreach (var p in photos)
            {
                try { p.SaveAs(MW, p.FilePath(dir)); }
                catch { failed++; }
            }
            return failed;
        }).ContinueWith(t => Dispatcher.BeginInvoke(() =>
        {
            int failed = t.IsFaulted ? photos.Count : t.Result;
            if (failed == 0)
                Notify(done, HudToast.Kind.Success);
            else
                Notify($"{failed} photo(s) n'ont pas pu être exportées.", HudToast.Kind.Warning);
        }));
    }

    private void ExportSelection()
    {
        // ordre de la galerie, uniquement des photos débloquées
        var photos = MW.Photos.Where(p => p.IsUnlock && selection.Contains(p)).ToList();
        if (photos.Count == 0)
        {
            Notify("Aucune photo sélectionnée : coche celles à exporter.", HudToast.Kind.Warning);
            return;
        }
        if (photos.Count == 1)
            SaveOne(photos[0]);
        else
            ExportMany(photos, $"{photos.Count.ToString("N0", Fr)} photos exportées");
    }

    private void ExportAllUnlocked()
    {
        var photos = MW.Photos.Where(p => p.IsUnlock).ToList();
        if (photos.Count == 0)
        {
            Notify("Tu n'as encore débloqué aucune photo.", HudToast.Kind.Warning);
            return;
        }
        ExportMany(photos, $"Toutes tes photos débloquées sont exportées ({photos.Count.ToString("N0", Fr)})");
    }
    #endregion

    /// <summary>Pastille à bascule (filtre, tri, étiquette) dans le style HudChip</summary>
    private sealed class Pill : Border
    {
        private bool on;
        public event Action<Pill>? Clicked;

        public Pill(string text, string? glyph = null, bool compact = false)
        {
            CornerRadius = new CornerRadius(14);
            Padding = compact ? new Thickness(10, 4, 11, 4) : new Thickness(12, 5, 13, 5);
            Margin = new Thickness(0, 0, 6, 6);
            BorderThickness = new Thickness(1);
            Cursor = Cursors.Hand;
            Focusable = true;
            FocusVisualStyle = null;
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            if (glyph != null)
            {
                var icon = new TextBlock { Text = glyph, FontSize = compact ? 10 : 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 1, 7, 0), Opacity = 0.85 };
                icon.SetResourceReference(TextBlock.FontFamilyProperty, "HudIcons");
                row.Children.Add(icon);
            }
            var label = new TextBlock { Text = text, FontSize = 12, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
            label.SetResourceReference(TextBlock.FontFamilyProperty, "HudDisplay");
            row.Children.Add(label);
            Child = row;
            MouseEnter += (_, _) => Paint();
            MouseLeave += (_, _) => Paint();
            GotKeyboardFocus += (_, _) => Paint();
            LostKeyboardFocus += (_, _) => Paint();
            MouseLeftButtonDown += (_, e) => e.Handled = true;
            MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                Clicked?.Invoke(this);
            };
            KeyDown += (_, e) =>
            {
                if (e.Key is Key.Space or Key.Enter)
                {
                    e.Handled = true;
                    Clicked?.Invoke(this);
                }
            };
            Paint();
        }

        public bool On
        {
            get => on;
            set { on = value; Paint(); }
        }

        private void Paint()
        {
            bool hover = IsMouseOver || IsKeyboardFocused;
            if (on)
                SetResourceReference(BackgroundProperty, "HudAccentSoft");
            else if (hover)
                SetResourceReference(BackgroundProperty, "HudSurfaceHover");
            else
                Background = Brushes.Transparent;
            SetResourceReference(BorderBrushProperty, on ? "HudAccent" : "HudStroke");
            SetResourceReference(TextElement.ForegroundProperty, on || hover ? "HudText" : "HudTextMuted");
        }
    }
}
