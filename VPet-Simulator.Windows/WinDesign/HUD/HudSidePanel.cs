using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace VPet_Simulator.Windows.HUD;

/// <summary>
/// V-Max HUD : panneau latéral ancré à côté du compagnon (état, garde-manger, occupations, plus…).
/// Fournit l'en-tête (surtitre, titre, boutons), le corps défilant et le placement du côté où l'écran a de la place.
/// </summary>
public abstract class HudSidePanel : HudOverlay
{
    protected readonly TextBlock EyebrowText;
    protected readonly TextBlock TitleText;
    protected readonly StackPanel HeaderButtons;
    protected readonly ScrollViewer BodyScroll;
    private readonly ContentControl body = new();
    private readonly ContentControl footer = new();

    /// <summary>Se ferme quand l'utilisateur clique ailleurs (cartes éphémères)</summary>
    protected bool CloseOnDeactivate { get; init; }

    /// <summary>Déclenché à l'ouverture (le contrôleur ferme alors les autres panneaux)</summary>
    public event Action<HudSidePanel>? Opening;

    protected HudSidePanel(MainWindow pet, string eyebrow, string title, double width, double? height = null) : base(pet, activates: true)
    {
        Width = width;
        if (height is double h)
        {
            SizeToContent = SizeToContent.Manual;
            Height = h;
        }
        else
            SizeToContent = SizeToContent.Height;

        EyebrowText = new TextBlock { Text = eyebrow, Style = (Style)FindResource("HudEyebrow") };
        TitleText = new TextBlock { Text = title, Style = (Style)FindResource("HudTitle"), FontSize = 20, TextTrimming = TextTrimming.CharacterEllipsis };
        var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        titles.Children.Add(EyebrowText);
        titles.Children.Add(TitleText);
        HeaderButtons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top };
        var header = new Grid { Margin = new Thickness(20, 16, 12, 8), Background = Brushes.Transparent };
        header.ColumnDefinitions.Add(new ColumnDefinition());
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(titles);
        Grid.SetColumn(HeaderButtons, 1);
        header.Children.Add(HeaderButtons);
        header.MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); };

        BodyScroll = new ScrollViewer
        {
            Content = body,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Padding = new Thickness(20, 0, 14, 16),
            Resources = { [typeof(ScrollBar)] = new Style(typeof(ScrollBar), (Style)FindResource("HudScrollBar")) },
        };

        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        // en étoile : prend la hauteur du contenu, puis défile quand MaxHeight (ou la hauteur fixe) est atteinte
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.Children.Add(header);
        Grid.SetRow(BodyScroll, 1);
        layout.Children.Add(BodyScroll);
        Grid.SetRow(footer, 2);
        layout.Children.Add(footer);
        Content = new Border { Style = (Style)FindResource("HudPanel"), CornerRadius = new CornerRadius(24), Margin = new Thickness(14), Child = layout };

        AddHeaderButton("", "Fermer (Échap)", () => HideAnimated());
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) HideAnimated(); };
        Deactivated += (_, _) => { if (CloseOnDeactivate) HideAnimated(); };
    }

    protected UIElement? Body { get => body.Content as UIElement; set => body.Content = value; }
    protected UIElement? Footer { get => footer.Content as UIElement; set => footer.Content = value; }

    /// <summary>Ajoute un bouton d'en-tête (avant le bouton Fermer)</summary>
    protected Button AddHeaderButton(string glyph, string tip, Action run)
    {
        var b = new Button { Style = (Style)FindResource("HudIconButton"), Content = glyph, ToolTip = tip };
        b.Click += (_, _) => run();
        HeaderButtons.Children.Insert(Math.Max(0, HeaderButtons.Children.Count - 1), b);
        return b;
    }

    /// <summary>Ouvre, ramène au premier plan ou ferme</summary>
    public void Toggle()
    {
        if (IsVisible)
        {
            if (IsActive)
                HideAnimated();
            else
                Activate();
            return;
        }
        Opening?.Invoke(this);
        OnOpening();
        ShowAnimated(new Point(0.5, 0.5));
    }

    /// <summary>Rafraîchit le contenu juste avant l'affichage</summary>
    protected virtual void OnOpening() { }

    protected override void Reposition()
    {
        if (ActualWidth < 1)
            return;
        var pet = PetRect;
        var area = WorkArea;
        // à droite du compagnon s'il y a la place, sinon à gauche ; centré verticalement sur lui
        double right = pet.Right - pet.Width * 0.12;
        double left = pet.Left + pet.Width * 0.12 - ActualWidth;
        double x = area.Right - right >= ActualWidth ? right : left;
        double y = pet.Top + pet.Height / 2 - ActualHeight / 2;
        var pos = Clamp(new Point(x, y), new Size(ActualWidth, ActualHeight), area, 4);
        Left = pos.X;
        Top = pos.Y;
    }

    #region Briques communes
    protected TextBlock Section(string text) => new()
    {
        Text = text.ToUpperInvariant(),
        Style = (Style)FindResource("HudEyebrow"),
        Margin = new Thickness(0, 16, 0, 8),
    };

    protected Brush Res(string key) => (Brush)FindResource(key);
    #endregion
}
