using Panuon.WPF.UI;
using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace VPet_Simulator.Windows.HUD;

/// <summary>
/// V-Max HUD : boîte de dialogue moderne (verre fumé, fondu, clavier) qui remplace MessageBoxX / MessageBox.
/// Utilisée par <see cref="VPet_Simulator.Core.VDialog"/> pour toute l'application et les plugins.
/// </summary>
public sealed class HudDialog : Window
{
    private MessageBoxResult result = MessageBoxResult.None;
    private readonly MessageBoxResult cancelResult;

    private HudDialog(Window? owner, string text, string caption, MessageBoxButton buttons, MessageBoxIcon icon)
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = owner == null;
        Topmost = true;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = owner != null && owner.IsVisible ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen;
        if (owner != null && owner.IsVisible)
            Owner = owner;
        FontFamily = (FontFamily)FindResource("HudBody");
        Foreground = (Brush)FindResource("HudText");
        Title = string.IsNullOrWhiteSpace(caption) ? "V-Max" : caption;

        var (glyph, brushKey, defaultTitle) = icon switch
        {
            MessageBoxIcon.Error => ("", "HudAccent", "Erreur"),
            MessageBoxIcon.Warning => ("", "HudAmber", "Attention"),
            MessageBoxIcon.Question => ("", "HudSilver", "Question"),
            MessageBoxIcon.Success => ("", "HudSuccess", "C'est fait"),
            _ => ("", "HudSilver", "V-Max"),
        };
        var accent = (Brush)FindResource(brushKey);

        var badge = new Border
        {
            Width = 36, Height = 36, CornerRadius = new CornerRadius(18), Margin = new Thickness(0, 0, 14, 0), VerticalAlignment = VerticalAlignment.Top,
            Background = new SolidColorBrush(((SolidColorBrush)accent).Color) { Opacity = 0.16 },
            Child = new TextBlock { Text = glyph, FontFamily = (FontFamily)FindResource("HudIcons"), FontSize = 17, Foreground = accent, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        };
        var title = new TextBlock { Text = string.IsNullOrWhiteSpace(caption) ? defaultTitle : caption, Style = (Style)FindResource("HudTitle"), FontSize = 18, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
        DockPanel.SetDock(badge, Dock.Left);
        head.Children.Add(badge);
        head.Children.Add(title);

        // texte long ou technique (trace d'erreur) : zone défilante, sélectionnable, police à chasse fixe
        bool technical = text.Length > 420 || text.Contains("   at ") || text.Contains("Exception");
        FrameworkElement body;
        if (technical)
        {
            var box = new TextBox
            {
                Text = text, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Left, BorderThickness = new Thickness(0),
                Background = Brushes.Transparent, Foreground = (Brush)FindResource("HudText"), FontFamily = (FontFamily)FindResource("HudMono"), FontSize = 11.5,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 280,
            };
            body = new Border
            {
                CornerRadius = new CornerRadius(12), Background = (Brush)FindResource("HudSurfaceRaised"), BorderBrush = (Brush)FindResource("HudStroke"),
                BorderThickness = new Thickness(1), Padding = new Thickness(10, 8, 6, 8), Child = box,
            };
        }
        else
            body = new TextBlock { Text = text, Style = (Style)FindResource("HudBodyText"), FontSize = 14, LineHeight = 20, Margin = new Thickness(50, 0, 0, 0) };

        // boutons : l'action affirmative en couleur, les autres discrètes
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
        (string label, MessageBoxResult r, bool primary)[] choices = buttons switch
        {
            MessageBoxButton.YesNo => [("Non", MessageBoxResult.No, false), ("Oui", MessageBoxResult.Yes, true)],
            MessageBoxButton.YesNoCancel => [("Annuler", MessageBoxResult.Cancel, false), ("Non", MessageBoxResult.No, false), ("Oui", MessageBoxResult.Yes, true)],
            MessageBoxButton.OKCancel => [("Annuler", MessageBoxResult.Cancel, false), ("OK", MessageBoxResult.OK, true)],
            _ => [("OK", MessageBoxResult.OK, true)],
        };
        cancelResult = choices.Any(c => c.r == MessageBoxResult.Cancel) ? MessageBoxResult.Cancel
            : choices.Any(c => c.r == MessageBoxResult.No) ? MessageBoxResult.No : MessageBoxResult.OK;
        Button? primaryButton = null;
        foreach (var (label, r, primary) in choices)
        {
            var b = new Button { Style = (Style)FindResource(primary ? "HudPrimaryButton" : "HudGhostButton"), Content = label, MinWidth = 88, Margin = new Thickness(8, 0, 0, 0) };
            b.Click += (_, _) => Close(r);
            row.Children.Add(b);
            if (primary)
                primaryButton = b;
        }
        var footer = new DockPanel();
        if (technical)
        {
            var copy = new Button { Style = (Style)FindResource("HudGhostButton"), Content = "Copier", Margin = new Thickness(0, 18, 0, 0), VerticalAlignment = VerticalAlignment.Bottom };
            copy.Click += (_, _) => { try { Clipboard.SetText(text); copy.Content = "Copié"; } catch { } };
            DockPanel.SetDock(copy, Dock.Left);
            footer.Children.Add(copy);
        }
        footer.Children.Add(row);

        var stack = new StackPanel();
        stack.Children.Add(head);
        stack.Children.Add(body);
        stack.Children.Add(footer);
        var card = new Border
        {
            Style = (Style)FindResource("HudPanel"),
            CornerRadius = new CornerRadius(22),
            Padding = new Thickness(22, 20, 20, 18),
            Margin = new Thickness(24),
            Width = technical ? 560 : 440,
            Child = stack,
        };
        card.MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed && e.OriginalSource is not TextBox) DragMove(); };
        Content = card;

        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Close(cancelResult);
                e.Handled = true;
            }
            else if (e.Key == Key.Enter && e.OriginalSource is not TextBox)
            {
                Close(choices.First(c => c.primary).r);
                e.Handled = true;
            }
        };
        Loaded += (_, _) =>
        {
            VPet_Simulator.Core.UiMotion.PopIn(card, new Point(0.5, 0.5), 0.94, 200);
            primaryButton?.Focus();
        };
    }

    private bool closing;

    private void Close(MessageBoxResult r)
    {
        if (closing)
            return;
        closing = true;
        result = r;
        if (Content is UIElement root && VPet_Simulator.Core.UiMotion.Enabled)
        {
            var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(120));
            fade.Completed += (_, _) => Close();
            root.BeginAnimation(OpacityProperty, fade);
        }
        else
            Close();
    }

    /// <summary>Affiche la boîte de dialogue (bloquante) et renvoie le choix</summary>
    public static MessageBoxResult Show(Window? owner, string text, string caption, MessageBoxButton buttons, MessageBoxIcon icon)
    {
        var d = new HudDialog(owner, text ?? "", caption ?? "", buttons, icon);
        d.ShowDialog();
        return d.result == MessageBoxResult.None ? d.cancelResult : d.result;
    }
}
