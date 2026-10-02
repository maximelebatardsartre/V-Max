using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using VPet_Simulator.Windows.Interface;

namespace VPet_Simulator.Windows.HUD;

/// <summary>
/// V-Max HUD : le sac (remplace winInventory). Recherche, catégories en pastilles, favoris, tri,
/// « Utiliser » au survol, fiche dépliable avec description, prix et quantité à utiliser.
/// Même logique que l'ancien sac : <see cref="Item.Use"/> appelé N fois, puis la liste est relue.
/// </summary>
public sealed class InventoryPanel : HudSidePanel
{
    private const int PageSize = 40;
    private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

    private enum SortMode { Default, Name, Count, Value }
    private static readonly (SortMode mode, string label)[] Sorts =
    [
        (SortMode.Default, "Par défaut"),
        (SortMode.Name, "Nom"),
        (SortMode.Count, "Quantité"),
        (SortMode.Value, "Valeur"),
    ];

    private readonly TextBox search;
    private readonly WrapPanel chips = new() { Margin = new Thickness(0, 10, 0, 2) };
    private readonly RadioButton favChip;
    private readonly StackPanel sortLinks = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    private readonly Button sortDir;
    private readonly TextBlock countText;
    private readonly StackPanel items = new();
    private readonly TextBlock totalValue;

    private string? category; // null = tout
    private SortMode sort = SortMode.Default;
    private bool ascending = true;
    private List<Item> filtered = new();
    private int shown;
    private Item? expanded;
    private Action? collapseExpanded;

    public InventoryPanel(MainWindow pet) : base(pet, "SAC", "Mes objets", 440, 640)
    {
        totalValue = new TextBlock
        {
            FontFamily = (FontFamily)FindResource("HudDisplay"), FontWeight = FontWeights.SemiBold, FontSize = 15,
            Foreground = Res("HudText"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0),
            ToolTip = "Valeur totale de ton sac",
        };
        HeaderButtons.Children.Insert(0, totalValue);

        // recherche
        search = new TextBox { Style = (Style)FindResource("HudInput"), Tag = "Chercher un objet…" };
        search.TextChanged += (_, _) => Fill();
        var sg = new DockPanel();
        var icon = new TextBlock { Text = "", FontFamily = (FontFamily)FindResource("HudIcons"), FontSize = 13, Foreground = Res("HudTextMuted"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
        DockPanel.SetDock(icon, Dock.Left);
        sg.Children.Add(icon);
        sg.Children.Add(search);
        var searchBox = new Border
        {
            CornerRadius = new CornerRadius(14), Background = Res("HudSurfaceRaised"), BorderBrush = Res("HudStroke"),
            BorderThickness = new Thickness(1), Padding = new Thickness(12, 7, 12, 7), Child = sg,
        };

        // catégories : Tout + chaque type d'objet (y compris ceux ajoutés par des mods)
        AddCategoryChip(null, "Tout", isChecked: true);
        foreach (var type in Item.ItemTypes)
            AddCategoryChip(type, TypeLabel(type), isChecked: false);

        // favoris : pastille à bascule, combinable avec la catégorie (comme l'ancien interrupteur)
        var favContent = new StackPanel { Orientation = Orientation.Horizontal };
        favContent.Children.Add(new TextBlock { Text = "", FontFamily = (FontFamily)FindResource("HudIcons"), FontSize = 11, Foreground = Res("HudAmber"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
        favContent.Children.Add(new TextBlock { Text = "Favoris", VerticalAlignment = VerticalAlignment.Center });
        favChip = new RadioButton { Style = (Style)FindResource("HudChip"), Content = favContent, GroupName = "inventory-fav", Margin = new Thickness(0, 0, 6, 6), ToolTip = "N'afficher que tes favoris" };
        favChip.PreviewMouseLeftButtonDown += (_, e) => { e.Handled = true; favChip.IsChecked = favChip.IsChecked != true; };
        favChip.Checked += (_, _) => Fill();
        favChip.Unchecked += (_, _) => Fill();
        chips.Children.Add(favChip);

        // tri
        foreach (var (mode, label) in Sorts)
        {
            var link = new TextBlock
            {
                Text = label, Tag = mode, FontFamily = (FontFamily)FindResource("HudDisplay"), FontWeight = FontWeights.SemiBold, FontSize = 12,
                Margin = new Thickness(0, 0, 12, 0), Cursor = Cursors.Hand, Background = Brushes.Transparent, VerticalAlignment = VerticalAlignment.Center,
            };
            link.MouseLeftButtonUp += (_, _) => { sort = mode; UpdateSortLinks(); Fill(); };
            link.MouseEnter += (_, _) => link.Foreground = Res("HudText");
            link.MouseLeave += (_, _) => UpdateSortLinks();
            sortLinks.Children.Add(link);
        }
        sortDir = new Button { Style = (Style)FindResource("HudIconButton"), Width = 28, Height = 28, FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
        sortDir.Click += (_, _) => { ascending = !ascending; UpdateSortLinks(); Fill(); };
        countText = new TextBlock { FontSize = 12, Foreground = Res("HudTextMuted"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) };
        var sortLabel = new TextBlock { Text = "TRIER", Style = (Style)FindResource("HudEyebrow"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
        var sortRow = new DockPanel { Margin = new Thickness(0, 4, 0, 10), LastChildFill = true };
        DockPanel.SetDock(sortDir, Dock.Right);
        DockPanel.SetDock(countText, Dock.Right);
        DockPanel.SetDock(sortLabel, Dock.Left);
        sortRow.Children.Add(sortDir);
        sortRow.Children.Add(countText);
        sortRow.Children.Add(sortLabel);
        sortRow.Children.Add(sortLinks);
        UpdateSortLinks();

        var root = new StackPanel();
        root.Children.Add(searchBox);
        root.Children.Add(chips);
        root.Children.Add(sortRow);
        root.Children.Add(items);
        Body = root;
    }

    /// <summary>Nom français d'un type d'objet (les types inconnus, ajoutés par des mods, gardent leur nom)</summary>
    private static string TypeLabel(string type) => type switch
    {
        "Item" => "Objets",
        "Food" => "Nourriture",
        "Tool" => "Outils",
        "Toy" => "Jouets",
        "Mail" => "Courrier",
        _ => type,
    };

    private void AddCategoryChip(string? type, string label, bool isChecked)
    {
        var chip = new RadioButton { Style = (Style)FindResource("HudChip"), Content = label, Tag = type, GroupName = "inventory", Margin = new Thickness(0, 0, 6, 6), IsChecked = isChecked };
        chip.Checked += (_, _) => { category = type; Fill(); };
        chips.Children.Add(chip);
    }

    /// <summary>Ouvre directement sur une catégorie (null = tout)</summary>
    public void ShowCategory(string? itemType)
    {
        category = itemType != null && Item.ItemTypes.Contains(itemType) ? itemType : null;
        foreach (var c in chips.Children.OfType<RadioButton>())
            if (c != favChip && (string?)c.Tag == category)
                c.IsChecked = true;
        if (!IsVisible)
            Toggle();
        else
            Fill();
    }

    protected override void OnOpening()
    {
        search.Text = "";
        expanded = null;
        collapseExpanded = null;
        Fill();
    }

    private void UpdateSortLinks()
    {
        foreach (TextBlock t in sortLinks.Children)
            t.Foreground = Res((SortMode)t.Tag == sort ? "HudText" : "HudTextMuted");
        sortDir.Content = ascending ? "" : "";
        sortDir.ToolTip = ascending ? "Ordre croissant (touche pour inverser)" : "Ordre décroissant (touche pour inverser)";
        sortDir.IsEnabled = sort != SortMode.Default;
        sortDir.Opacity = sort != SortMode.Default ? 1 : 0.35;
    }

    private void RefreshTotal()
    {
        // comme l'ancien sac : la valeur totale compte tous les objets, quel que soit le filtre
        double total = Pet.Items.Sum(x => x.Price * x.Count);
        totalValue.Text = Money(total);
    }

    private static string Money(double v) => v.ToString("#,##0.##", Fr) + " $";

    private void Fill() => Fill(keepScroll: false);

    private void Fill(bool keepScroll)
    {
        RefreshTotal();
        double offset = BodyScroll.VerticalOffset;
        IEnumerable<Item> source = Pet.Items.Where(x => x.Visibility);
        bool bagEmpty = !source.Any();

        var text = search.Text.Trim();
        if (!string.IsNullOrEmpty(text))
            source = source.Where(x => x.TranslateName.Contains(text, StringComparison.CurrentCultureIgnoreCase));
        if (favChip.IsChecked == true)
            source = source.Where(x => x.Star);
        if (category != null)
            source = source.Where(x => x.ItemType == category);

        var cmp = StringComparer.Create(Fr, ignoreCase: true);
        source = sort switch
        {
            SortMode.Name => ascending ? source.OrderBy(x => x.TranslateName, cmp) : source.OrderByDescending(x => x.TranslateName, cmp),
            SortMode.Count => ascending ? source.OrderBy(x => x.Count) : source.OrderByDescending(x => x.Count),
            SortMode.Value => ascending ? source.OrderBy(x => x.Price) : source.OrderByDescending(x => x.Price),
            _ => source,
        };
        filtered = source.ToList();
        if (expanded != null && !filtered.Contains(expanded))
            expanded = null;

        items.Children.Clear();
        collapseExpanded = null;
        shown = 0;
        countText.Text = filtered.Count switch { 0 => "", 1 => "1 objet", var n => $"{n} objets" };
        if (filtered.Count == 0)
        {
            items.Children.Add(new TextBlock
            {
                Text = bagEmpty ? "Ton sac est vide."
                    : favChip.IsChecked == true && string.IsNullOrEmpty(text) ? "Aucun favori ici. Survole un objet et touche l'étoile pour l'épingler."
                    : "Rien ne correspond à cette recherche.",
                Style = (Style)FindResource("HudBodyText"), FontSize = 13, Foreground = Res("HudTextMuted"), Margin = new Thickness(0, 20, 0, 0),
            });
            return;
        }
        // l'objet déplié reste visible même au-delà de la première page
        int needed = expanded != null ? filtered.IndexOf(expanded) + 1 : 0;
        do ShowMore(); while (shown < needed);

        if (keepScroll)
            Dispatcher.BeginInvoke(() => BodyScroll.ScrollToVerticalOffset(offset), DispatcherPriority.Loaded);
        else
            BodyScroll.ScrollToTop();
    }

    private void ShowMore()
    {
        if (items.Children.Count > 0 && items.Children[^1] is Button more)
            items.Children.Remove(more);
        foreach (var it in filtered.Skip(shown).Take(PageSize))
            items.Children.Add(Row(it));
        shown = Math.Min(filtered.Count, shown + PageSize);
        if (shown < filtered.Count)
        {
            var b = new Button { Style = (Style)FindResource("HudGhostButton"), Content = $"Afficher plus ({filtered.Count - shown})", HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 8, 0, 0) };
            b.Click += (_, _) => ShowMore();
            items.Children.Add(b);
        }
    }

    private Border Badge(string text, string? tip = null, string fg = "HudTextMuted") => new()
    {
        CornerRadius = new CornerRadius(8),
        Background = Res("HudSurfaceHover"),
        Padding = new Thickness(7, 1, 7, 2),
        Margin = new Thickness(0, 0, 6, 0),
        ToolTip = tip,
        Child = new TextBlock { Text = text, FontFamily = (FontFamily)FindResource("HudMono"), FontSize = 11, Foreground = Res(fg) },
    };

    private FrameworkElement Row(Item it)
    {
        var img = new Image { Source = it.ImageSource, Width = 40, Height = 40, Stretch = Stretch.Uniform };
        RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
        var thumb = new Border { Width = 50, Height = 50, CornerRadius = new CornerRadius(14), Background = Res("HudSurfaceHover"), Child = img, Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Top };

        var name = new TextBlock { Text = it.TranslateName, FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = Res("HudText"), TextTrimming = TextTrimming.CharacterEllipsis };
        var meta = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
        meta.Children.Add(it.IsSingle
            ? Badge("Ne s'use pas", "Cet objet n'est pas consommé quand tu l'utilises")
            : Badge("×" + it.Count.ToString("N0", Fr), "Quantité dans ton sac", "HudText"));
        if (category == null)
            meta.Children.Add(new TextBlock { Text = TypeLabel(it.ItemType), FontSize = 11, Foreground = Res("HudTextMuted"), VerticalAlignment = VerticalAlignment.Center });
        var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        info.Children.Add(name);
        info.Children.Add(meta);

        var price = new TextBlock
        {
            Text = Money(it.Price), FontFamily = (FontFamily)FindResource("HudDisplay"), FontWeight = FontWeights.SemiBold, FontSize = 14,
            Foreground = Res("HudText"), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right,
            ToolTip = it.IsSingle || it.Count <= 1 ? "Valeur" : $"Valeur à l'unité · {Money(it.Price * it.Count)} au total",
        };
        var use = new Button { Style = (Style)FindResource("HudPrimaryButton"), Content = "Utiliser", Visibility = Visibility.Collapsed, VerticalAlignment = VerticalAlignment.Center };
        use.Click += (_, _) => UseItem(it, 1);
        var star = StarButton(it);
        var right = new Grid { MinWidth = 84 };
        right.Children.Add(price);
        right.Children.Add(use);

        var head = new Grid { Background = Brushes.Transparent, Cursor = Cursors.Hand };
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition());
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.Children.Add(thumb);
        Grid.SetColumn(info, 1);
        head.Children.Add(info);
        Grid.SetColumn(star, 2);
        head.Children.Add(star);
        Grid.SetColumn(right, 3);
        head.Children.Add(right);

        var detailHost = new ContentControl { Visibility = Visibility.Collapsed };
        var stack = new StackPanel();
        stack.Children.Add(head);
        stack.Children.Add(detailHost);

        var row = new Border
        {
            Padding = new Thickness(6, 6, 8, 6),
            Margin = new Thickness(0, 0, 0, 4),
            CornerRadius = new CornerRadius(16),
            Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(1),
            Child = stack,
        };

        bool isOpen = false;
        void Hover(bool on)
        {
            row.Background = isOpen ? Res("HudSurfaceRaised") : on ? Res("HudSurfaceHover") : Brushes.Transparent;
            use.Visibility = on && it.CanUse ? Visibility.Visible : Visibility.Collapsed;
            price.Visibility = on && it.CanUse ? Visibility.Collapsed : Visibility.Visible;
            star.Visibility = on || isOpen || it.Star ? Visibility.Visible : Visibility.Collapsed;
        }
        void SetOpen(bool open)
        {
            isOpen = open;
            row.BorderBrush = open ? Res("HudStroke") : Brushes.Transparent;
            if (open)
            {
                detailHost.Content = Detail(it);
                detailHost.Visibility = Visibility.Visible;
            }
            else
            {
                detailHost.Content = null;
                detailHost.Visibility = Visibility.Collapsed;
            }
            Hover(row.IsMouseOver);
        }

        row.MouseEnter += (_, _) => Hover(true);
        row.MouseLeave += (_, _) => Hover(false);
        head.MouseLeftButtonUp += (_, e) =>
        {
            if (FromButton(e.OriginalSource as DependencyObject, head))
                return;
            if (isOpen)
            {
                SetOpen(false);
                expanded = null;
                collapseExpanded = null;
                return;
            }
            collapseExpanded?.Invoke();
            SetOpen(true);
            expanded = it;
            collapseExpanded = () => SetOpen(false);
            Dispatcher.BeginInvoke(() => row.BringIntoView(), DispatcherPriority.Loaded);
        };

        if (expanded == it)
        {
            SetOpen(true);
            collapseExpanded = () => SetOpen(false);
        }
        else
            Hover(false);
        return row;
    }

    private static bool FromButton(DependencyObject? d, DependencyObject stop)
    {
        while (d != null && d != stop)
        {
            if (d is ButtonBase)
                return true;
            d = d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
        }
        return false;
    }

    private Button StarButton(Item it)
    {
        var star = new Button { Style = (Style)FindResource("HudIconButton"), VerticalAlignment = VerticalAlignment.Center };
        void Paint()
        {
            star.Content = it.Star ? "" : "";
            star.Foreground = Res(it.Star ? "HudAmber" : "HudTextMuted");
            star.ToolTip = it.Star ? "Retirer des favoris" : "Ajouter aux favoris";
        }
        Paint();
        star.Click += (_, _) =>
        {
            it.Star = !it.Star;
            Paint();
        };
        return star;
    }

    /// <summary>Fiche dépliée : description, prix, quantité à utiliser</summary>
    private FrameworkElement Detail(Item it)
    {
        var panel = new StackPanel { Margin = new Thickness(62, 10, 4, 6) };

        panel.Children.Add(new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(it.Description) ? "Pas de description pour cet objet." : it.Description,
            Style = (Style)FindResource("HudBodyText"), FontSize = 13, Foreground = Res(string.IsNullOrWhiteSpace(it.Description) ? "HudTextMuted" : "HudText"),
        });

        var facts = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
        void Fact(string label, string value)
        {
            var s = new StackPanel { Margin = new Thickness(0, 0, 22, 4) };
            s.Children.Add(new TextBlock { Text = label.ToUpperInvariant(), Style = (Style)FindResource("HudEyebrow") });
            s.Children.Add(new TextBlock { Text = value, FontFamily = (FontFamily)FindResource("HudDisplay"), FontWeight = FontWeights.SemiBold, FontSize = 14, Foreground = Res("HudText"), Margin = new Thickness(0, 2, 0, 0) });
            facts.Children.Add(s);
        }
        Fact("Prix", Money(it.Price));
        if (it.IsSingle)
            Fact("Usage", "Illimité");
        else
        {
            Fact("Quantité", it.Count.ToString("N0", Fr));
            if (it.Count > 1)
                Fact("Valeur", Money(it.Price * it.Count));
        }
        panel.Children.Add(facts);

        if (!it.CanUse)
        {
            panel.Children.Add(new TextBlock { Text = "Cet objet ne s'utilise pas depuis le sac.", FontSize = 12, Foreground = Res("HudTextMuted"), Margin = new Thickness(0, 8, 0, 0) });
            return panel;
        }

        int amount = 1;
        int Max() => Math.Max(1, it.Count);
        var useN = new Button { Style = (Style)FindResource("HudPrimaryButton"), Content = "Utiliser", HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        var bar = new DockPanel { Margin = new Thickness(0, 10, 0, 0), LastChildFill = false };
        DockPanel.SetDock(useN, Dock.Right);
        bar.Children.Add(useN);

        if (!it.IsSingle && it.Count > 1)
        {
            var box = new TextBox { Style = (Style)FindResource("HudInput"), Text = "1", Width = 40, TextAlignment = TextAlignment.Center, FontFamily = (FontFamily)FindResource("HudDisplay"), FontWeight = FontWeights.SemiBold };
            void Set(int v)
            {
                amount = Math.Clamp(v, 1, Max());
                if (box.Text != amount.ToString(Fr))
                    box.Text = amount.ToString(Fr);
                useN.Content = amount > 1 ? $"Utiliser ×{amount}" : "Utiliser";
            }
            void ApplyText() => Set(int.TryParse(box.Text.Trim(), NumberStyles.Integer, Fr, out var v) ? v : amount);
            box.PreviewKeyDown += (_, e) => { if (e.Key == Key.Enter) { ApplyText(); e.Handled = true; } };
            box.LostFocus += (_, _) => ApplyText();
            box.PreviewTextInput += (_, e) => e.Handled = !e.Text.All(char.IsDigit);

            var minus = new Button { Style = (Style)FindResource("HudIconButton"), Content = "", Width = 28, Height = 28, FontSize = 11, ToolTip = "Un de moins" };
            minus.Click += (_, _) => { ApplyText(); Set(amount - 1); };
            var plus = new Button { Style = (Style)FindResource("HudIconButton"), Content = "", Width = 28, Height = 28, FontSize = 11, ToolTip = "Un de plus" };
            plus.Click += (_, _) => { ApplyText(); Set(amount + 1); };
            var all = new Button { Style = (Style)FindResource("HudIconButton"), Content = "Max", FontFamily = (FontFamily)FindResource("HudDisplay"), FontSize = 11, Width = 38, Height = 28, ToolTip = $"Tout utiliser ({it.Count})" };
            all.Click += (_, _) => Set(Max());

            var stepper = new Border
            {
                CornerRadius = new CornerRadius(14), Background = Res("HudSurfaceHover"), Padding = new Thickness(2),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new StackPanel { Orientation = Orientation.Horizontal, Children = { minus, box, plus } },
            };
            DockPanel.SetDock(stepper, Dock.Left);
            DockPanel.SetDock(all, Dock.Left);
            bar.Children.Add(stepper);
            all.Margin = new Thickness(4, 0, 0, 0);
            bar.Children.Add(all);
            useN.Click += (_, _) => { ApplyText(); UseItem(it, amount); };
        }
        else
            useN.Click += (_, _) => UseItem(it, 1);

        panel.Children.Add(bar);
        return panel;
    }

    /// <summary>Utilise l'objet N fois (logique de l'ancien sac), puis relit la liste</summary>
    private void UseItem(Item it, int count)
    {
        if (!it.CanUse)
            return;
        count = it.IsSingle ? 1 : Math.Clamp(count, 1, Math.Max(1, it.Count));
        try
        {
            while (count-- > 0)
            {
                // l'objet a pu être consommé entièrement par l'utilisation précédente
                if (!it.IsSingle && (it.Count <= 0 || !Pet.Items.Contains(it)))
                    break;
                it.Use(Pet);
            }
        }
        catch (Exception e)
        {
            HudToast.Show(Pet, $"Impossible d'utiliser {it.TranslateName} : {e.Message}", HudToast.Kind.Warning);
        }
        expanded = null;
        collapseExpanded = null;
        Fill(keepScroll: true);
    }
}
