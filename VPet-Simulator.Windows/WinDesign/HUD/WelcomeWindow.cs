using LinePutScript;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace VPet_Simulator.Windows.HUD;

/// <summary>
/// V-Max : carte d'accueil du premier lancement. Présente Maxine et recueille le consentement pour les options
/// sensibles. Tout ce qui écoute ou regarde (micro, « Hey Max », vision d'écran) reste désactivé tant que la
/// personne ne l'active pas ici ou dans les paramètres.
/// </summary>
public sealed class WelcomeWindow : Window
{
    private readonly MainWindow mw;
    private readonly CheckBox micCheck;
    private readonly CheckBox startupCheck;

    public WelcomeWindow(MainWindow mw)
    {
        this.mw = mw;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = true;
        Topmost = true;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        FontFamily = (FontFamily)FindResource("HudBody");
        Foreground = (Brush)FindResource("HudText");
        Title = "Maxine";

        var stack = new StackPanel { MaxWidth = 460 };
        stack.Children.Add(new TextBlock { Text = "BIENVENUE", Style = (Style)FindResource("HudEyebrow") });
        stack.Children.Add(new TextBlock { Text = "Enchantée, je suis Maxine", Style = (Style)FindResource("HudTitle"), FontSize = 26, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 10) });
        stack.Children.Add(new TextBlock
        {
            Text = "Je suis ta nouvelle compagne de bureau : je vis sur ton écran, je bouge, je réagis quand tu m'attrapes. "
                 + "Si tu me connectes une IA, on pourra aussi discuter.",
            Style = (Style)FindResource("HudBodyText"), FontSize = 14, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 16),
        });

        // bloc vie privée : tout est coupé au départ
        var privacy = new StackPanel();
        privacy.Children.Add(new TextBlock { Text = "RESPECT DE TA VIE PRIVÉE", Style = (Style)FindResource("HudEyebrow"), Margin = new Thickness(0, 0, 0, 8) });
        privacy.Children.Add(PrivacyLine("", "Micro", "coupé — pour parler avec moi"));
        privacy.Children.Add(PrivacyLine("", "« Hey Max »", "coupé — pour m'appeler à la voix"));
        privacy.Children.Add(PrivacyLine("", "Vision de l'écran", "coupée — pour que je commente ce que tu fais"));
        privacy.Children.Add(new TextBlock
        {
            Text = "Rien de tout ça ne s'active sans toi. Tu peux le faire quand tu veux dans Paramètres, et tout reste sur ton PC.",
            Style = (Style)FindResource("HudBodyText"), FontSize = 12.5, Foreground = (Brush)FindResource("HudTextMuted"),
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0),
        });
        stack.Children.Add(new Border
        {
            CornerRadius = new CornerRadius(14), Background = (Brush)FindResource("HudSurfaceRaised"), BorderBrush = (Brush)FindResource("HudStroke"),
            BorderThickness = new Thickness(1), Padding = new Thickness(14, 12, 14, 14), Margin = new Thickness(0, 0, 0, 16), Child = privacy,
        });

        micCheck = Check("Autoriser le micro maintenant pour me parler", false);
        startupCheck = Check("Me lancer au démarrage de Windows", true);
        stack.Children.Add(micCheck);
        stack.Children.Add(startupCheck);

        var go = new Button { Style = (Style)FindResource("HudPrimaryButton"), Content = "C'est parti !", MinWidth = 150, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
        go.Click += (_, _) => Finish();
        stack.Children.Add(go);

        var card = new Border { Style = (Style)FindResource("HudPanel"), CornerRadius = new CornerRadius(24), Padding = new Thickness(26, 22, 24, 20), Margin = new Thickness(24), Width = 512, Child = stack };
        card.MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); };
        Content = card;
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Enter) Finish(); };
        Loaded += (_, _) => VPet_Simulator.Core.UiMotion.PopIn(card, new Point(0.5, 0.5), 0.94, 240);
        mw.Windows.Add(this);
        Closed += (_, _) => mw.Windows.Remove(this);
    }

    private FrameworkElement PrivacyLine(string glyph, string title, string what)
    {
        var dock = new DockPanel { Margin = new Thickness(0, 3, 0, 3) };
        var icon = new TextBlock { Text = glyph, FontFamily = (FontFamily)FindResource("HudIcons"), FontSize = 15, Foreground = (Brush)FindResource("HudTextMuted"), Width = 26, VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(icon, Dock.Left);
        dock.Children.Add(icon);
        var t = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
        t.Inlines.Add(new System.Windows.Documents.Run(title + " ") { FontWeight = FontWeights.SemiBold, Foreground = (Brush)FindResource("HudText") });
        t.Inlines.Add(new System.Windows.Documents.Run(what) { Foreground = (Brush)FindResource("HudTextMuted") });
        dock.Children.Add(t);
        return dock;
    }

    private CheckBox Check(string text, bool on) => new()
    {
        Content = text, IsChecked = on, Foreground = (Brush)FindResource("HudText"), FontSize = 13.5, Margin = new Thickness(0, 8, 0, 0),
    };

    private void Finish()
    {
        mw.Set["vmax"][(gbol)"onboarded"] = true;
        try
        {
            mw.Set.StartUPBoot = startupCheck.IsChecked == true;
            StartupShortcut.Apply(mw);
        }
        catch (Exception e)
        {
            mw.ReportStartupError("Le lancement au démarrage n'a pas pu être réglé.", e.ToString());
        }
        if (micCheck.IsChecked == true && mw.Voice != null)
            mw.Voice.Enabled = true;
        mw.Save();
        if (Content is UIElement root && VPet_Simulator.Core.UiMotion.Enabled)
        {
            var a = new System.Windows.Media.Animation.DoubleAnimation(0, TimeSpan.FromMilliseconds(140));
            a.Completed += (_, _) => Close();
            root.BeginAnimation(OpacityProperty, a);
        }
        else
            Close();
        mw.Toast("Bonjour ! Clic droit sur moi pour le menu, et retrouve tout dans les Paramètres.", HudToast.Kind.Info, 7);
    }

    /// <summary>Affiche l'accueil au premier lancement (une seule fois)</summary>
    public static void ShowIfFirstRun(MainWindow mw)
    {
        if (mw.Set["vmax"][(gbol)"onboarded"])
            return;
        new WelcomeWindow(mw).Show();
    }
}
