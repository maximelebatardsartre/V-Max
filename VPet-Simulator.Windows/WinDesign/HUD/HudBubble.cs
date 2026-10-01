using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using VPet_Simulator.Core;

namespace VPet_Simulator.Windows.HUD;

/// <summary>
/// V-Max HUD : bulle de parole du compagnon (remplace la MessageBar de VPet via l'interface IMassageBar).
/// Fenêtre de verre fumé ancrée au-dessus de la tête (ou dessous si l'écran manque de place), avec une pointe
/// vers le compagnon, texte écrit au fil de l'eau, pause au survol, fermeture en fondu.
/// </summary>
public sealed class HudBubble : HudOverlay, IMassageBar
{
    private readonly Main main;
    private readonly TextBlock nameText;
    private readonly Run textRun;
    private readonly Run caret;
    private readonly ContentControl contentHost;
    private readonly Path tailDown;
    private readonly Path tailUp;
    private readonly Border card;
    private readonly Button closeButton;
    private readonly DispatcherTimer typeTimer = new() { Interval = TimeSpan.FromMilliseconds(28) };
    private readonly DispatcherTimer endTimer = new();
    private readonly DispatcherTimer caretTimer = new() { Interval = TimeSpan.FromMilliseconds(530) };
    private string pending = "";
    private string shown = "";
    private string? graphName;
    private bool typing;
    private SayInfoWithStream? stream;
    private readonly Control placeholder = new() { Visibility = Visibility.Collapsed };

    public HudBubble(MainWindow pet) : base(pet, activates: false)
    {
        main = pet.Main;

        nameText = new TextBlock { Style = (Style)FindResource("HudEyebrow"), Foreground = (Brush)FindResource("HudAccent"), Margin = new Thickness(0, 0, 0, 4) };
        closeButton = new Button { Style = (Style)FindResource("HudIconButton"), Content = "", Width = 24, Height = 24, FontSize = 10, Opacity = 0, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, -6, -10, 0), ToolTip = "Fermer" };
        closeButton.Click += (_, _) => ForceClose();
        textRun = new Run();
        caret = new Run("▍") { Foreground = (Brush)FindResource("HudAccent") };
        var text = new TextBlock { Style = (Style)FindResource("HudBodyText"), FontSize = 15, LineHeight = 21 };
        text.Inlines.Add(textRun);
        text.Inlines.Add(caret);
        contentHost = new ContentControl { Margin = new Thickness(0, 8, 0, 0), Visibility = Visibility.Collapsed };

        var header = new Grid();
        header.Children.Add(nameText);
        header.Children.Add(closeButton);
        var stack = new StackPanel();
        stack.Children.Add(header);
        stack.Children.Add(new ScrollViewer
        {
            Content = text,
            MaxHeight = 260,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Resources = { [typeof(System.Windows.Controls.Primitives.ScrollBar)] = new Style(typeof(System.Windows.Controls.Primitives.ScrollBar), (Style)FindResource("HudScrollBar")) },
        });
        stack.Children.Add(contentHost);
        card = new Border
        {
            Style = (Style)FindResource("HudPanel"),
            CornerRadius = new CornerRadius(18),
            Padding = new Thickness(16, 11, 16, 13),
            MinWidth = 120,
            MaxWidth = 360,
            Child = stack,
        };

        Path Tail(bool down) => new()
        {
            Data = Geometry.Parse(down ? "M0,0 L18,0 L9,10 Z" : "M0,10 L18,10 L9,0 Z"),
            Fill = (Brush)FindResource("HudSurface"),
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = down ? new Thickness(0, -1, 0, 0) : new Thickness(0, 0, 0, -1),
        };
        tailDown = Tail(true);
        tailUp = Tail(false);
        var root = new StackPanel { Margin = new Thickness(14, 6, 14, 14) };
        root.Children.Add(tailUp);
        root.Children.Add(card);
        root.Children.Add(tailDown);
        Content = root;

        root.MouseEnter += (_, _) =>
        {
            endTimer.Stop();
            closeButton.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(120)));
        };
        root.MouseLeave += (_, _) =>
        {
            closeButton.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(200)));
            if (!typing && stream == null)
                endTimer.Start();
        };
        root.MouseRightButtonUp += (_, e) =>
        {
            var menu = new ContextMenu();
            var copy = new MenuItem { Header = "Copier le texte" };
            copy.Click += (_, _) => { try { Clipboard.SetText(shown); } catch { } };
            var close = new MenuItem { Header = "Fermer" };
            close.Click += (_, _) => ForceClose();
            menu.Items.Add(copy);
            menu.Items.Add(close);
            menu.IsOpen = true;
            e.Handled = true;
        };

        typeTimer.Tick += (_, _) => TypeNext();
        endTimer.Tick += (_, _) => { endTimer.Stop(); FadeOut(); };
        caretTimer.Tick += (_, _) => caret.Text = caret.Text.Length == 0 ? "▍" : "";
    }

    /// <summary>Texte actuellement affiché (QA, copie)</summary>
    public string Text => shown;

    public Control This => placeholder;

    public event Action? EndAction;

    private void OnUi(Action a)
    {
        if (Dispatcher.CheckAccess()) a();
        else Dispatcher.Invoke(a);
    }

    #region IMassageBar
    public void Show(string name, string text, string? graphName = null, UIElement? msgContent = null)
    {
        OnUi(() =>
        {
            DetachStream();
            Prepare(name, graphName, msgContent);
            pending = text;
            StartTyping();
        });
    }

    public void Show(string name, SayInfoWithStream sayInfoWithStream)
    {
        OnUi(() =>
        {
            DetachStream();
            var content = sayInfoWithStream.MsgContent ?? (string.IsNullOrWhiteSpace(sayInfoWithStream.Desc) ? null
                : new TextBlock { Text = sayInfoWithStream.Desc, Style = (Style)FindResource("HudEyebrow"), HorizontalAlignment = HorizontalAlignment.Right });
            Prepare(name, sayInfoWithStream.GraphName, content);
            stream = sayInfoWithStream;
            pending = sayInfoWithStream.CurrentText.ToString();
            StartTyping();
            sayInfoWithStream.Event_Update += OnStreamUpdate;
            if (sayInfoWithStream.IsFinishGen)
                OnStreamFinish(sayInfoWithStream.CurrentText.ToString());
            else
                sayInfoWithStream.Event_Finish += OnStreamFinish;
        });
    }

    public void ForceClose()
    {
        OnUi(() =>
        {
            DetachStream();
            typeTimer.Stop();
            endTimer.Stop();
            caretTimer.Stop();
            Hide();
            contentHost.Content = null;
            EndSpeakingAnimation();
            EndAction?.Invoke();
        });
    }

    /// <summary>La bulle est toujours placée hors du compagnon dans V-Max</summary>
    public void SetPlaceIN() { }
    public void SetPlaceOUT() { }

    public void Dispose()
    {
        OnUi(() =>
        {
            typeTimer.Stop();
            endTimer.Stop();
            caretTimer.Stop();
            Close();
        });
    }
    #endregion

    private void Prepare(string name, string? graph, UIElement? msgContent)
    {
        graphName = graph;
        nameText.Text = name.ToUpperInvariant();
        shown = "";
        textRun.Text = "";
        contentHost.Content = msgContent;
        contentHost.Visibility = msgContent == null ? Visibility.Collapsed : Visibility.Visible;
        endTimer.Stop();
        bool wasHidden = !IsVisible;
        if (wasHidden)
            ShowAnimated(new Point(0.5, 1));
        else
            Reposition();
    }

    private void StartTyping()
    {
        typing = true;
        caret.Text = "▍";
        caretTimer.Start();
        typeTimer.Start();
    }

    private void TypeNext()
    {
        if (shown.Length < pending.Length)
        {
            // accélère pour les textes longs ou quand le flux a pris de l'avance
            int step = Math.Max(1, (pending.Length - shown.Length) / 60);
            shown = pending[..Math.Min(pending.Length, shown.Length + step)];
            textRun.Text = shown;
            return;
        }
        if (stream != null)
            return; // on attend la suite du flux
        typeTimer.Stop();
        FinishTyping(pending);
    }

    private void FinishTyping(string full)
    {
        typing = false;
        caretTimer.Stop();
        caret.Text = "";
        _ = WaitVoiceThenEnd(full);
    }

    private async Task WaitVoiceThenEnd(string full)
    {
        // laisse la voix (si un plugin TTS parle) se terminer, 30 s au plus
        for (int i = 0; i < 300 && main.PlayingVoice; i++)
            await Task.Delay(100);
        EndSpeakingAnimation();
        endTimer.Interval = TimeSpan.FromSeconds(Math.Clamp(Function.ComCheck(full) * 2 + 4, 4, 30));
        if (!(Content as UIElement)!.IsMouseOver)
            endTimer.Start();
    }

    private void OnStreamUpdate((string fullText, string changedText) data) =>
        Dispatcher.BeginInvoke(() =>
        {
            pending = data.fullText;
            if (!typeTimer.IsEnabled)
                typeTimer.Start();
        });

    private void OnStreamFinish(string fullText) =>
        Dispatcher.BeginInvoke(() =>
        {
            pending = fullText;
            DetachStream();
            if (!typeTimer.IsEnabled)
                typeTimer.Start();
        });

    private void DetachStream()
    {
        if (stream == null)
            return;
        stream.Event_Update -= OnStreamUpdate;
        stream.Event_Finish -= OnStreamFinish;
        stream = null;
    }

    private void EndSpeakingAnimation()
    {
        if ((main.DisplayType.Name == graphName || main.DisplayType.Type == GraphInfo.GraphType.Say)
            && main.DisplayType.Animat != GraphInfo.AnimatType.C_End)
            main.DisplayCEndtoNomal(main.DisplayType.Name);
    }

    private void FadeOut()
    {
        HideAnimated(() =>
        {
            contentHost.Content = null;
            EndAction?.Invoke();
        });
    }

    protected override void Reposition()
    {
        if (ActualWidth < 1)
            return;
        var pet = PetRect;
        var area = WorkArea;
        double anchorX = pet.Left + pet.Width / 2;
        double headY = pet.Top + pet.Height * 0.12;
        bool above = headY - ActualHeight >= area.Top;
        tailDown.Visibility = above ? Visibility.Visible : Visibility.Collapsed;
        tailUp.Visibility = above ? Visibility.Collapsed : Visibility.Visible;
        double top = above ? headY - ActualHeight + 8 : pet.Bottom - pet.Height * 0.08;
        var pos = Clamp(new Point(anchorX - ActualWidth / 2, top), new Size(ActualWidth, ActualHeight), area, 4);
        Left = pos.X;
        Top = pos.Y;
        // la pointe reste dirigée vers le compagnon même quand la bulle est décalée par le bord de l'écran
        double tailX = Math.Clamp(anchorX - pos.X - 14 - 9, 18, Math.Max(18, card.ActualWidth - 36));
        tailDown.Margin = new Thickness(tailX, -1, 0, 0);
        tailUp.Margin = new Thickness(tailX, 0, 0, -1);
    }
}
