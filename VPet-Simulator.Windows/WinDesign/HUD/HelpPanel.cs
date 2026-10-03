using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using VPet_Simulator.Windows.Assistant;

namespace VPet_Simulator.Windows.HUD;

/// <summary>
/// V-Max HUD : la « documentation » des commandes. Liste tout ce que Maxine sait faire, par thème, avec des exemples
/// cliquables (clic = exécute la commande). C'est ce qu'elle propose d'ouvrir quand elle ne comprend pas une phrase.
/// </summary>
public sealed class HelpPanel : HudSidePanel
{
    public HelpPanel(MainWindow pet) : base(pet, "COMMANDES", "Ce que je sais faire", 340, 540)
    {
        var root = new StackPanel();
        root.Children.Add(new TextBlock
        {
            Text = "Dis-le à l'oral (bouton micro) ou écris-le. Pas besoin d'IA : clique un exemple pour l'essayer.",
            Style = (Style)FindResource("HudBodyText"),
            Foreground = Res("HudTextMuted"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 6),
        });
        foreach (var g in CommandCatalog.Groups)
        {
            root.Children.Add(Section(g.Icon + "  " + g.Title));
            var wrap = new WrapPanel();
            foreach (var ex in g.Examples)
                wrap.Children.Add(Chip(ex));
            root.Children.Add(wrap);
        }
        Body = root;
    }

    private FrameworkElement Chip(string text)
    {
        var tb = new TextBlock { Text = "« " + text + " »", Foreground = Res("HudText"), FontSize = 12.5 };
        var b = new Border
        {
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(11, 7, 11, 7),
            Margin = new Thickness(0, 0, 7, 7),
            Background = Res("HudSurfaceRaised"),
            BorderBrush = Res("HudStroke"),
            BorderThickness = new Thickness(1),
            Cursor = Cursors.Hand,
            Child = tb,
        };
        b.MouseEnter += (_, _) => b.BorderBrush = Res("HudAccent");
        b.MouseLeave += (_, _) => b.BorderBrush = Res("HudStroke");
        b.MouseLeftButtonUp += (_, _) => { try { Pet.Assistant?.Handle(text); } catch { } HideAnimated(); };
        return b;
    }
}
