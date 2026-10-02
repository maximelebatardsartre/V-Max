using LinePutScript.Localization.WPF;
using Panuon.WPF.UI;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using VPet_Simulator.Windows.Interface;
using static VPet_Simulator.Windows.Interface.Food;

namespace VPet_Simulator.Windows.HUD;

/// <summary>
/// V-Max HUD : garde-manger. Catégories en pastilles, recherche, effets lisibles sur chaque article,
/// « Donner » en un clic (même règles que la boutique de VPet : crédit sous 1 000 $, lassitude, contrôle des objets déséquilibrés).
/// </summary>
public sealed class PantryPanel : HudSidePanel
{
    private static readonly (FoodType type, string label)[] Categories =
    [
        (FoodType.Star, "Favoris"),
        (FoodType.Meal, "Repas"),
        (FoodType.Snack, "En-cas"),
        (FoodType.Drink, "Boissons"),
        (FoodType.Functional, "Fonctionnel"),
        (FoodType.Drug, "Soins"),
        (FoodType.Gift, "Cadeaux"),
    ];
    private const int PageSize = 40;
    private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

    private readonly TextBox search;
    private readonly StackPanel items = new();
    private readonly WrapPanel chips = new() { Margin = new Thickness(0, 10, 0, 4) };
    private readonly TextBlock money;
    private FoodType category = FoodType.Meal;
    private int shown;
    private List<Food> filtered = new();
    private readonly Dictionary<Food, FrameworkElement> rowOf = new();

    public PantryPanel(MainWindow pet) : base(pet, "GARDE-MANGER", "Que mange-t-on ?", 430, 640)
    {
        money = new TextBlock { FontFamily = (FontFamily)FindResource("HudDisplay"), FontWeight = FontWeights.SemiBold, FontSize = 15, Foreground = Res("HudText"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
        HeaderButtons.Children.Insert(0, money);

        search = new TextBox { Style = (Style)FindResource("HudInput"), Tag = "Chercher un aliment, un cadeau…" };
        search.TextChanged += (_, _) => Fill();
        var searchBox = new Border
        {
            CornerRadius = new CornerRadius(14),
            Background = Res("HudSurfaceRaised"),
            BorderBrush = Res("HudStroke"),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(12, 7, 12, 7),
        };
        var sg = new DockPanel();
        var icon = new TextBlock { Text = "", FontFamily = (FontFamily)FindResource("HudIcons"), FontSize = 13, Foreground = Res("HudTextMuted"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
        DockPanel.SetDock(icon, Dock.Left);
        sg.Children.Add(icon);
        sg.Children.Add(search);
        searchBox.Child = sg;

        foreach (var (type, label) in Categories)
        {
            var chip = new RadioButton { Style = (Style)FindResource("HudChip"), Content = label, GroupName = "pantry", Margin = new Thickness(0, 0, 6, 6), IsChecked = type == category };
            chip.Checked += (_, _) => { category = type; Fill(); };
            chips.Children.Add(chip);
        }

        var root = new StackPanel();
        root.Children.Add(searchBox);
        root.Children.Add(chips);
        root.Children.Add(items);
        Body = root;
    }

    /// <summary>Ouvre directement sur une catégorie</summary>
    public void ShowCategory(FoodType type)
    {
        category = type;
        foreach (RadioButton c in chips.Children)
            c.IsChecked = (string)c.Content == Categories.First(x => x.type == type).label;
        if (!IsVisible)
            Toggle();
    }

    protected override void OnOpening()
    {
        search.Text = "";
        PocketMoney();
        RefreshMoney();
        Fill();
    }

    private void RefreshMoney() => money.Text = Pet.Core.Save!.Money.ToString("N0", Fr) + " $";

    private void Fill()
    {
        var text = search.Text.Trim();
        IEnumerable<Food> source = !string.IsNullOrEmpty(text)
            ? Pet.Foods.Where(f => f.TranslateName.Contains(text, StringComparison.CurrentCultureIgnoreCase))
            : category == FoodType.Star ? Pet.Foods.Where(f => f.Star) : Pet.Foods.Where(f => f.Type == category);
        filtered = source.Where(f => f.Visibility).OrderBy(f => f.Price).ToList();
        items.Children.Clear();
        rowOf.Clear();
        shown = 0;
        if (filtered.Count == 0)
        {
            items.Children.Add(new TextBlock
            {
                Text = category == FoodType.Star && string.IsNullOrEmpty(text)
                    ? "Aucun favori pour l'instant. Survole un article et touche l'étoile pour l'épingler ici."
                    : "Rien ne correspond à cette recherche.",
                Style = (Style)FindResource("HudBodyText"), FontSize = 13, Foreground = Res("HudTextMuted"), Margin = new Thickness(0, 20, 0, 0),
            });
            return;
        }
        ShowMore();
        BodyScroll.ScrollToTop();
    }

    private void ShowMore()
    {
        if (items.Children.Count > 0 && items.Children[^1] is Button more)
            items.Children.Remove(more);
        foreach (var f in filtered.Skip(shown).Take(PageSize))
            items.Children.Add(Row(f));
        shown = Math.Min(filtered.Count, shown + PageSize);
        if (shown < filtered.Count)
        {
            var b = new Button { Style = (Style)FindResource("HudGhostButton"), Content = $"Afficher plus ({filtered.Count - shown})", HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 8, 0, 0) };
            b.Click += (_, _) => ShowMore();
            items.Children.Add(b);
        }
    }

    private FrameworkElement Row(Food f)
    {
        var img = new Image { Source = f.ImageSource, Width = 44, Height = 44, Stretch = Stretch.Uniform };
        RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
        var thumb = new Border { Width = 52, Height = 52, CornerRadius = new CornerRadius(14), Background = Res("HudSurfaceHover"), Child = img, Margin = new Thickness(0, 0, 12, 0) };

        var name = new TextBlock { Text = f.TranslateName, FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = Res("HudText"), TextTrimming = TextTrimming.CharacterEllipsis };
        var effects = new TextBlock { FontFamily = (FontFamily)FindResource("HudMono"), FontSize = 11, Foreground = Res("HudTextMuted"), TextWrapping = TextWrapping.Wrap, LineHeight = 15, Margin = new Thickness(0, 2, 0, 0), Text = Effects(f) };
        var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        info.Children.Add(name);
        info.Children.Add(effects);
        var tired = Tiredness(f);
        if (tired != null)
            info.Children.Add(new TextBlock { Text = tired, FontSize = 11, Foreground = Res("HudAmber"), Margin = new Thickness(0, 2, 0, 0) });

        var price = new TextBlock { Text = f.Price.ToString("N0", Fr) + " $", FontFamily = (FontFamily)FindResource("HudDisplay"), FontWeight = FontWeights.SemiBold, FontSize = 14, Foreground = Res("HudText"), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
        var give = new Button { Style = (Style)FindResource("HudPrimaryButton"), Content = f.Type == FoodType.Gift ? "Offrir" : "Donner", Visibility = Visibility.Collapsed, VerticalAlignment = VerticalAlignment.Center };
        give.Click += (_, _) => Give(f);
        var star = new Button { Style = (Style)FindResource("HudIconButton"), Content = f.Star ? "" : "", ToolTip = f.Star ? "Retirer des favoris" : "Ajouter aux favoris", Visibility = f.Star ? Visibility.Visible : Visibility.Collapsed, Foreground = Res(f.Star ? "HudAmber" : "HudTextMuted") };
        star.Click += (_, _) =>
        {
            f.Star = !f.Star;
            star.Content = f.Star ? "" : "";
            star.Foreground = Res(f.Star ? "HudAmber" : "HudTextMuted");
            star.ToolTip = f.Star ? "Retirer des favoris" : "Ajouter aux favoris";
        };
        var right = new Grid { MinWidth = 84 };
        right.Children.Add(price);
        right.Children.Add(give);

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(thumb);
        Grid.SetColumn(info, 1);
        grid.Children.Add(info);
        Grid.SetColumn(star, 2);
        grid.Children.Add(star);
        Grid.SetColumn(right, 3);
        grid.Children.Add(right);

        var row = new Border
        {
            Padding = new Thickness(6, 6, 8, 6),
            Margin = new Thickness(0, 0, 0, 4),
            CornerRadius = new CornerRadius(16),
            Background = Brushes.Transparent,
            Child = grid,
            ToolTip = string.IsNullOrWhiteSpace(f.Description) ? null : f.Description,
        };
        row.MouseEnter += (_, _) =>
        {
            row.Background = Res("HudSurfaceHover");
            give.Visibility = Visibility.Visible;
            price.Visibility = Visibility.Collapsed;
            star.Visibility = Visibility.Visible;
        };
        row.MouseLeave += (_, _) =>
        {
            row.Background = Brushes.Transparent;
            give.Visibility = Visibility.Collapsed;
            price.Visibility = Visibility.Visible;
            star.Visibility = f.Star ? Visibility.Visible : Visibility.Collapsed;
        };
        rowOf[f] = row;
        return row;
    }

    private static string Effects(Food f)
    {
        var parts = new List<string>();
        void Add(double v, string label)
        {
            if (Math.Abs(v) >= 0.5)
                parts.Add((v > 0 ? "+" : "") + Math.Round(v).ToString(Fr) + " " + label);
        }
        Add(f.StrengthFood, "satiété");
        Add(f.StrengthDrink, "soif");
        Add(f.Strength, "énergie");
        Add(f.Feeling, "humeur");
        Add(f.Health, "santé");
        Add(f.Likability, "affinité");
        Add(f.Exp, "XP");
        return string.Join(" · ", parts);
    }

    /// <summary>Lassitude : l'article rapporte moins s'il a été donné récemment</summary>
    private string? Tiredness(Food f)
    {
        var now = DateTime.Now;
        var eat = Pet.GameSavesData["buytime"].GetDateTime(f.Name, now);
        if (eat <= now)
            return null;
        double h = (eat - now).TotalHours;
        double eff = Math.Max(0.5, 1 - h * h * (f.Type == FoodType.Gift ? 0.01 : 0.02));
        return $"Un peu lassé · efficacité {eff.ToString("P0", Fr)} jusqu'à {eat:HH:mm}";
    }

    private void Give(Food item)
    {
        var error = PetCare.Feed(Pet, item, interactive: true);
        if (error != null)
        {
            if (error.Length > 0)
                HudToast.Show(Pet, error, HudToast.Kind.Warning);
            return;
        }
        RefreshMoney();
        // rafraîchit la ligne (lassitude) sans perdre la position
        int index = rowOf.TryGetValue(item, out var old) ? items.Children.IndexOf(old) : -1;
        if (index >= 0)
        {
            items.Children.RemoveAt(index);
            items.Children.Insert(index, Row(item));
        }
    }

    /// <summary>
    /// Argent de poche (tradition de VPet) : à sec, le compagnon dépanne de 1 000 $ ; il les reprend quand on est riche
    /// </summary>
    private void PocketMoney()
    {
        var save = Pet.Core.Save!;
        if (save.Money <= 1)
        {
            if (Pet.GameSavesData[(LinePutScript.gbol)"self"])
                HudToast.Show(Pet, "Ardoise ouverte : tout ce qui coûte moins de 1 000 $ peut être pris à crédit.", HudToast.Kind.Info, 6);
            else
            {
                Pet.GameSavesData[(LinePutScript.gbol)"self"] = true;
                save.Money += 1000;
                HudToast.Show(Pet, $"Te voyant à sec, {save.Name} sort 1 000 $ de ses économies.", HudToast.Kind.Success, 6);
            }
        }
        else if (save.Money >= 11000 && Pet.GameSavesData[(LinePutScript.gbol)"self"])
        {
            save.Money -= 1000;
            Pet.GameSavesData[(LinePutScript.gbol)"self"] = false;
            HudToast.Show(Pet, $"{save.Name} a discrètement remis 1 000 $ de côté.", HudToast.Kind.Info, 6);
        }
    }
}
