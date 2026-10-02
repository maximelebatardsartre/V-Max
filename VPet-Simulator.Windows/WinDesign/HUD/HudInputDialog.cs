using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace VPet_Simulator.Windows.HUD;

/// <summary>
/// V-Max : demande d'un texte à l'utilisateur (remplace winInputBox). Même contrat que l'ancienne fenêtre :
/// l'action de fin reçoit le texte saisi, ou une chaîne vide si l'utilisateur annule.
/// </summary>
public sealed class HudInputDialog : Window
{
    private readonly TextBox input;
    private bool accepted;

    private HudInputDialog(MainWindow mw, string title, string text, string defaultText, bool multiline, bool center)
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        FontFamily = (FontFamily)FindResource("HudBody");
        Foreground = (Brush)FindResource("HudText");
        Title = string.IsNullOrWhiteSpace(title) ? "V-Max" : title;

        var stack = new StackPanel();
        stack.Children.Add(new TextBlock { Text = Title, Style = (Style)FindResource("HudTitle"), FontSize = 18, TextAlignment = center ? TextAlignment.Center : TextAlignment.Left, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) });
        if (!string.IsNullOrWhiteSpace(text))
            stack.Children.Add(new TextBlock { Text = text, Style = (Style)FindResource("HudBodyText"), FontSize = 14, TextAlignment = center ? TextAlignment.Center : TextAlignment.Left, Margin = new Thickness(0, 0, 0, 14) });
        input = new TextBox
        {
            Style = (Style)FindResource("HudInput"), Text = defaultText, AcceptsReturn = multiline, TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap,
            MinHeight = multiline ? 90 : 0, MaxHeight = 220, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalContentAlignment = multiline ? VerticalAlignment.Top : VerticalAlignment.Center,
        };
        stack.Children.Add(new Border
        {
            CornerRadius = new CornerRadius(14), Background = (Brush)FindResource("HudSurfaceRaised"), BorderBrush = (Brush)FindResource("HudStroke"),
            BorderThickness = new Thickness(1), Padding = new Thickness(12, 8, 12, 8), Child = input,
        });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        var cancel = new Button { Style = (Style)FindResource("HudGhostButton"), Content = "Annuler", MinWidth = 88, Margin = new Thickness(0, 0, 8, 0) };
        cancel.Click += (_, _) => Finish(false);
        var ok = new Button { Style = (Style)FindResource("HudPrimaryButton"), Content = "Valider", MinWidth = 88 };
        ok.Click += (_, _) => Finish(true);
        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);
        stack.Children.Add(buttons);
        var card = new Border { Style = (Style)FindResource("HudPanel"), CornerRadius = new CornerRadius(22), Padding = new Thickness(22, 20, 20, 18), Margin = new Thickness(24), Width = 440, Child = stack };
        card.MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed && e.OriginalSource is not TextBox) DragMove(); };
        Content = card;
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { Finish(false); e.Handled = true; }
            else if (e.Key == Key.Enter && (!multiline || Keyboard.Modifiers.HasFlag(ModifierKeys.Control))) { Finish(true); e.Handled = true; }
        };
        Loaded += (_, _) =>
        {
            VPet_Simulator.Core.UiMotion.PopIn(card, new Point(0.5, 0.5), 0.94, 200);
            input.Focus();
            input.SelectAll();
        };
        mw.Windows.Add(this);
        Closed += (_, _) => mw.Windows.Remove(this);
    }

    private void Finish(bool ok)
    {
        accepted = ok;
        if (Content is UIElement root && VPet_Simulator.Core.UiMotion.Enabled)
        {
            var a = new DoubleAnimation(0, TimeSpan.FromMilliseconds(110));
            a.Completed += (_, _) => Close();
            root.BeginAnimation(OpacityProperty, a);
        }
        else
            Close();
    }

    /// <summary>Affiche la demande (bloquante) puis appelle <paramref name="end"/> avec le texte (vide si annulé)</summary>
    public static void Show(MainWindow mw, string title, string text, string defaultText, Action<string>? end, bool multiline = false, bool center = true)
    {
        var d = new HudInputDialog(mw, title, text, defaultText, multiline, center);
        d.ShowDialog();
        end?.Invoke(d.accepted ? d.input.Text : "");
    }
}
