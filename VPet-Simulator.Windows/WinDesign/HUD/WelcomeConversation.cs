using LinePutScript;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace VPet_Simulator.Windows.HUD;

/// <summary>
/// V-Max : première rencontre avec Maxine (premier lancement). Maxine se présente façon visual novel — elle
/// s'anime pendant qu'elle parle, demande son prénom à l'utilisateur, puis cède la place à la carte de
/// consentement (micro, vision, démarrage). Les mots importants sont en gras dans les bulles.
/// </summary>
public sealed class WelcomeConversation : Window
{
    private readonly MainWindow mw;
    private readonly TalkingMaxine portrait;
    private readonly TextBlock bubble = new() { TextWrapping = TextWrapping.Wrap, FontSize = 16, LineHeight = 24 };
    private readonly Button next;
    private readonly TextBox nameBox;
    private readonly Border nameRow;
    private readonly DispatcherTimer typer = new(DispatcherPriority.Normal);

    private List<Segment> current = [];
    private int typeIndex;
    private int step;
    private string userName = "";

    // un segment de texte : normal ou en gras
    private readonly record struct Segment(string Text, bool Bold);

    public WelcomeConversation(MainWindow mw)
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

        portrait = new TalkingMaxine { Width = 220, Height = 220, VerticalAlignment = VerticalAlignment.Bottom };

        bubble.Foreground = (Brush)FindResource("HudText");
        var bubbleCard = new Border
        {
            CornerRadius = new CornerRadius(20, 20, 20, 6), Background = (Brush)FindResource("HudSurfaceRaised"),
            BorderBrush = (Brush)FindResource("HudStroke"), BorderThickness = new Thickness(1), Padding = new Thickness(20, 16, 20, 16),
            Child = bubble, MinHeight = 108, VerticalAlignment = VerticalAlignment.Top,
        };

        nameBox = new TextBox { Style = (Style)FindResource("HudInput"), MaxLength = 24, FontSize = 15 };
        nameBox.KeyDown += (_, e) => { if (e.Key == Key.Enter) Advance(); };
        nameRow = new Border
        {
            CornerRadius = new CornerRadius(12), Background = (Brush)FindResource("HudSurface"), BorderBrush = (Brush)FindResource("HudStroke"),
            BorderThickness = new Thickness(1), Padding = new Thickness(12, 8, 12, 8), Margin = new Thickness(0, 12, 0, 0), Child = nameBox,
            Visibility = Visibility.Collapsed,
        };

        next = new Button { Style = (Style)FindResource("HudPrimaryButton"), Content = "Suivant  ▸", MinWidth = 130, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        next.Click += (_, _) => Advance();

        var right = new StackPanel { Width = 420, VerticalAlignment = VerticalAlignment.Center };
        right.Children.Add(new TextBlock { Text = "MAXINE", Style = (Style)FindResource("HudEyebrow"), Margin = new Thickness(2, 0, 0, 6) });
        right.Children.Add(bubbleCard);
        right.Children.Add(nameRow);
        right.Children.Add(next);

        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(portrait);
        row.Children.Add(new Border { Width = 10 });
        row.Children.Add(right);

        var card = new Border { Style = (Style)FindResource("HudPanel"), CornerRadius = new CornerRadius(26), Padding = new Thickness(24, 22, 26, 22), Margin = new Thickness(24), Child = row };
        card.MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed && e.OriginalSource is not TextBox) DragMove(); };
        Content = card;

        typer.Interval = TimeSpan.FromMilliseconds(18);
        typer.Tick += (_, _) => TypeTick();
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Enter && nameRow.Visibility != Visibility.Visible) { Advance(); e.Handled = true; } };
        Loaded += (_, _) => { VPet_Simulator.Core.UiMotion.PopIn(card, new Point(0.4, 0.6), 0.94, 260); ShowStep(0); };
        mw.Windows.Add(this);
        Closed += (_, _) => { portrait.Stop(); mw.Windows.Remove(this); };
    }

    // ---- script de la rencontre -------------------------------------------------------------------------
    private void ShowStep(int s)
    {
        step = s;
        nameRow.Visibility = Visibility.Collapsed;
        switch (s)
        {
            case 0:
                portrait.Play("Shining");
                Speak("Bonjour, je suis *Maxine*. D'après ce que j'ai compris, c'est toi mon nouvel *humain*.");
                break;
            case 1:
                portrait.Play("Self");
                Speak("J'ai été imaginée par *Az*, et il m'a confié une mission : *t'être utile*, et te tenir compagnie.");
                break;
            case 2:
                portrait.Play("Serious");
                Speak("Je sais déjà faire plein de choses : *vivre sur ton bureau*, me balader, réagir quand tu m'attrapes, *travailler pour gagner des sous*, suivre mes *routines de vie*… et si tu me prêtes une IA, *discuter avec toi* et même *te répondre à voix haute*.");
                break;
            case 3:
                portrait.Play("Shining");
                Speak("Et ce n'est qu'un début : *Az continue de me faire grandir* et je me mets à jour toute seule. Je deviendrai *de plus en plus maligne* avec le temps.");
                break;
            case 4:
                portrait.Play("Self");
                Speak("Mais avant tout… *comment tu t'appelles ?*", withInput: true);
                break;
            case 5:
                portrait.Play("Shy");
                Speak($"Enchantée, *{userName}* ! Moi c'est *Maxine* — mais tu pourras me renommer quand tu veux.");
                break;
            case 6:
                portrait.Play("Serious");
                Speak("Dernière petite chose : j'ai besoin de ton accord pour quelques réglages. *Promis, rien ne s'active sans toi.*");
                break;
            default:
                Finish();
                break;
        }
    }

    private void Advance()
    {
        if (typer.IsEnabled)
        {// un clic pendant la frappe : affiche la ligne complète d'un coup
            typer.Stop();
            RenderFull();
            AfterLine();
            return;
        }
        if (step == 4)
        {
            var typed = nameBox.Text.Trim();
            userName = string.IsNullOrWhiteSpace(typed) ? "toi" : typed;
            if (userName != "toi")
            {
                try
                {
                    mw.GameSavesData.GameSave.HostName = userName;
                    mw.Save();
                }
                catch { }
            }
        }
        ShowStep(step + 1);
    }

    // ---- bulle avec effet machine à écrire et gras ------------------------------------------------------
    private bool pendingInput;

    private void Speak(string markup, bool withInput = false)
    {
        current = Parse(markup);
        typeIndex = 0;
        pendingInput = withInput;
        bubble.Inlines.Clear();
        next.Content = step >= 6 ? "Continuer  ▸" : "Suivant  ▸";
        next.IsEnabled = true;
        typer.Start();
    }

    private void TypeTick()
    {
        int shown = 0, target = typeIndex + 1;
        bubble.Inlines.Clear();
        foreach (var seg in current)
        {
            if (shown >= target) break;
            int take = Math.Min(seg.Text.Length, target - shown);
            var run = new Run(seg.Text[..take]);
            if (seg.Bold) { run.FontWeight = FontWeights.Bold; run.Foreground = (Brush)FindResource("HudAccent"); }
            bubble.Inlines.Add(run);
            shown += seg.Text.Length;
        }
        typeIndex++;
        if (typeIndex >= current.Sum(s => s.Text.Length))
        {
            typer.Stop();
            AfterLine();
        }
    }

    private void RenderFull()
    {
        bubble.Inlines.Clear();
        foreach (var seg in current)
        {
            var run = new Run(seg.Text);
            if (seg.Bold) { run.FontWeight = FontWeights.Bold; run.Foreground = (Brush)FindResource("HudAccent"); }
            bubble.Inlines.Add(run);
        }
    }

    private void AfterLine()
    {
        if (pendingInput)
        {
            nameRow.Visibility = Visibility.Visible;
            nameBox.Focus();
        }
    }

    // « texte *gras* texte » → segments
    private static List<Segment> Parse(string markup)
    {
        var segs = new List<Segment>();
        foreach (var part in Regex.Split(markup, @"(\*[^*]+\*)"))
        {
            if (part.Length == 0) continue;
            if (part.Length >= 2 && part[0] == '*' && part[^1] == '*')
                segs.Add(new Segment(part[1..^1], true));
            else
                segs.Add(new Segment(part, false));
        }
        return segs;
    }

    private void Finish()
    {
        portrait.Stop();
        if (Content is UIElement root && VPet_Simulator.Core.UiMotion.Enabled)
        {
            var a = new System.Windows.Media.Animation.DoubleAnimation(0, TimeSpan.FromMilliseconds(180));
            a.Completed += (_, _) => { Close(); ShowConsent(); };
            root.BeginAnimation(OpacityProperty, a);
        }
        else { Close(); ShowConsent(); }
    }

    private void ShowConsent()
    {
        var w = new WelcomeWindow(mw) { Topmost = true };
        w.Show();
    }

    /// <summary>Lance la rencontre au premier lancement (une seule fois)</summary>
    public static void ShowIfFirstRun(MainWindow mw)
    {
        if (mw.Set["vmax"][(gbol)"onboarded"])
            return;
        new WelcomeConversation(mw).Show();
    }
}

/// <summary>
/// V-Max : petite animation de Maxine qui « parle », lue depuis mod\0000_core\pet\vup\Say\&lt;variante&gt;
/// (phases A puis B en boucle). Sert uniquement à l'accueil, indépendamment du compagnon affiché.
/// </summary>
public sealed class TalkingMaxine : Image
{
    private readonly DispatcherTimer timer = new(DispatcherPriority.Render);
    private readonly Dictionary<string, List<(BitmapSource img, int ms)>> cache = new();
    private List<(BitmapSource img, int ms)> frames = [];
    private int i;

    public TalkingMaxine()
    {
        Stretch = Stretch.Uniform;
        timer.Tick += (_, _) => Tick();
    }

    private static string SayRoot => Path.Combine(MainWindow.ModPath, "0000_core", "pet", "vup", "Say");

    public void Play(string variant)
    {
        if (!cache.TryGetValue(variant, out var seq))
        {
            seq = Load(variant);
            cache[variant] = seq;
        }
        if (seq.Count == 0)
            return;
        frames = seq;
        i = 0;
        Source = frames[0].img;
        timer.Interval = TimeSpan.FromMilliseconds(Math.Max(40, frames[0].ms));
        timer.Start();
    }

    public void Stop() => timer.Stop();

    private void Tick()
    {
        if (frames.Count == 0)
            return;
        i = (i + 1) % frames.Count;
        Source = frames[i].img;
        timer.Interval = TimeSpan.FromMilliseconds(Math.Max(40, frames[i].ms));
    }

    private static List<(BitmapSource, int)> Load(string variant)
    {
        var list = new List<(BitmapSource, int)>();
        try
        {
            var dir = Path.Combine(SayRoot, variant);
            if (!Directory.Exists(dir))
                return list;
            // phase A (début) puis les phases B (boucle), dans l'ordre
            var phases = new List<string>();
            foreach (var name in new[] { "A" }.Concat(Directory.GetDirectories(dir).Select(Path.GetFileName)
                         .Where(n => n!.StartsWith("B", StringComparison.OrdinalIgnoreCase)).OrderBy(n => n)))
            {
                var p = Path.Combine(dir, name!);
                if (Directory.Exists(p))
                    phases.Add(p);
            }
            foreach (var p in phases)
                foreach (var f in Directory.GetFiles(p, "*.png").OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                {
                    var b = new BitmapImage();
                    b.BeginInit();
                    b.CacheOption = BitmapCacheOption.OnLoad;
                    b.UriSource = new Uri(f);
                    b.DecodePixelWidth = 300;
                    b.EndInit();
                    b.Freeze();
                    var m = Regex.Match(Path.GetFileNameWithoutExtension(f), @"_(\d+)$");
                    list.Add((b, m.Success ? int.Parse(m.Groups[1].Value) : 125));
                }
        }
        catch { }
        return list;
    }
}
