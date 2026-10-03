using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using VPet_Simulator.Windows.Agent;
using VPet_Simulator.Windows.Agent.Providers;
using VPet_Simulator.Windows.Interface;

namespace VPet_Simulator.Windows.HUD;

/// <summary>
/// V-Max HUD : panneau de discussion avec l'agent, ancré à côté du compagnon.
/// Contient l'accueil « choisis le cerveau » (IA gratuites, clé collée et vérifiée sur place),
/// les messages en streaming, les cartes d'actions et de confirmation.
/// </summary>
public sealed class ChatPanel : HudOverlay
{
    private readonly AgentOrchestrator agent;
    private readonly StackPanel list = new() { Margin = new Thickness(18, 6, 14, 6) };
    private readonly ScrollViewer scroll;
    private readonly TextBox input;
    private readonly Button send;
    private readonly TextBlock statusText;
    private readonly Ellipse statusDot;
    private TextBlock? streaming;
    private FrameworkElement? typingRow;
    private bool sendOnEnter = true;

    public ChatPanel(MainWindow pet, AgentOrchestrator agent) : base(pet, activates: true)
    {
        this.agent = agent;
        SizeToContent = SizeToContent.Manual;
        Width = 420;
        Height = 620;

        // ---- En-tête
        var title = new TextBlock { Text = pet.Core.Save?.Name ?? "V-Max", Style = (Style)FindResource("HudTitle"), FontSize = 19 };
        statusDot = new Ellipse { Width = 7, Height = 7, Margin = new Thickness(0, 1, 6, 0), VerticalAlignment = VerticalAlignment.Center };
        statusText = new TextBlock { FontFamily = (FontFamily)FindResource("HudMono"), FontSize = 11, Foreground = (Brush)FindResource("HudTextMuted"), TextTrimming = TextTrimming.CharacterEllipsis };
        var status = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 0) };
        status.Children.Add(statusDot);
        status.Children.Add(statusText);
        var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        titles.Children.Add(title);
        titles.Children.Add(status);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        buttons.Children.Add(IconButton("", "Nouvelle conversation", () => { agent.ResetConversation(); Rebuild(); }));
        buttons.Children.Add(IconButton("", "Réglages de l'IA", () => Pet.ShowSetting("ia")));
        buttons.Children.Add(IconButton("", "Fermer (Échap)", () => HideAnimated()));
        var header = new Grid { Margin = new Thickness(20, 16, 12, 10), Background = Brushes.Transparent };
        header.ColumnDefinitions.Add(new ColumnDefinition());
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(titles);
        Grid.SetColumn(buttons, 1);
        header.Children.Add(buttons);
        // la fenêtre se déplace par l'en-tête
        header.MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); };

        // ---- Fil de discussion
        scroll = new ScrollViewer
        {
            Content = list,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Resources = { [typeof(ScrollBar)] = new Style(typeof(ScrollBar), (Style)FindResource("HudScrollBar")) },
        };

        // ---- Saisie
        input = new TextBox
        {
            Style = (Style)FindResource("HudInput"),
            Tag = "Écris à " + (pet.Core.Save?.Name ?? "V-Max") + "…",
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MaxHeight = 120,
            MinHeight = 22,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Margin = new Thickness(4, 0, 8, 0),
        };
        input.PreviewKeyDown += Input_PreviewKeyDown;
        send = new Button
        {
            Style = (Style)FindResource("HudPrimaryButton"),
            Content = new TextBlock { Text = "", FontFamily = (FontFamily)FindResource("HudIcons"), FontSize = 14 },
            Width = 38, Height = 38, Padding = new Thickness(0),
            ToolTip = "Envoyer (Entrée)",
            VerticalAlignment = VerticalAlignment.Bottom,
        };
        send.Click += (_, _) => SendOrStop();
        // Bouton micro : dicter une commande à la voix (clic = écoute, s'arrête au silence). Rend le vocal VISIBLE et testable.
        var micBtn = new Button
        {
            Style = (Style)FindResource("HudIconButton"),
            Content = new TextBlock { Text = "", FontFamily = (FontFamily)FindResource("HudIcons"), FontSize = 15 },
            Width = 38, Height = 38, Padding = new Thickness(0),
            ToolTip = "Parler : clique et dicte ta commande (ex. « ouvre Spotify »)",
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 4, 0),
        };
        micBtn.Click += (_, _) => { try { Pet.Voice?.ListenNow(); } catch { } };
        var composerGrid = new Grid();
        composerGrid.ColumnDefinitions.Add(new ColumnDefinition());
        composerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        composerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        composerGrid.Children.Add(input);
        Grid.SetColumn(micBtn, 1);
        composerGrid.Children.Add(micBtn);
        Grid.SetColumn(send, 2);
        composerGrid.Children.Add(send);
        var composer = new Border
        {
            Margin = new Thickness(14, 6, 14, 14),
            Padding = new Thickness(12, 6, 6, 6),
            CornerRadius = new CornerRadius(22),
            Background = (Brush)FindResource("HudSurfaceRaised"),
            BorderBrush = (Brush)FindResource("HudStroke"),
            BorderThickness = new Thickness(1),
            Child = composerGrid,
        };
        composer.MouseLeftButtonDown += (_, _) => input.Focus();

        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition());
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.Children.Add(header);
        Grid.SetRow(scroll, 1);
        layout.Children.Add(scroll);
        Grid.SetRow(composer, 2);
        layout.Children.Add(composer);
        Content = new Border { Style = (Style)FindResource("HudPanel"), CornerRadius = new CornerRadius(24), Margin = new Thickness(14), Child = layout };

        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) HideAnimated(); };
        IsVisibleChanged += (_, _) =>
        {
            agent.PanelVisible = IsVisible;
            if (IsVisible)
                input.Focus();
        };

        Action<ChatMessage> onMessage = m => Dispatcher.BeginInvoke(() => { if (m.Role == ChatRole.User) AddUser(m.Text ?? ""); });
        Action onStarted = () => Dispatcher.BeginInvoke(StartAssistant);
        Action<string> onDelta = d => Dispatcher.BeginInvoke(() => AppendAssistant(d));
        Action<string> onFinished = _ => Dispatcher.BeginInvoke(() => { streaming = null; RefreshStatus(); });
        Action<AgentState> onState = s => Dispatcher.BeginInvoke(() => OnState(s));
        Action<ToolActivity> onTool = t => Dispatcher.BeginInvoke(() => AddTool(t));
        Action<string, bool> onError = (msg, setup) => Dispatcher.BeginInvoke(() => AddError(msg, setup));
        Action<IChatProvider?> onProvider = _ => Dispatcher.BeginInvoke(RefreshStatus);
        agent.MessageAdded += onMessage;
        agent.AssistantStarted += onStarted;
        agent.AssistantDelta += onDelta;
        agent.AssistantFinished += onFinished;
        agent.StateChanged += onState;
        agent.ToolActivityChanged += onTool;
        agent.ErrorRaised += onError;
        agent.ConfirmHandler = (tool, description) => Dispatcher.Invoke(() => AskConfirmation(tool, description));
        ProviderRouter.CurrentChanged += onProvider;
        // fermeture définitive (changement de thème) : on se désabonne de l'agent
        Closed += (_, _) =>
        {
            agent.MessageAdded -= onMessage;
            agent.AssistantStarted -= onStarted;
            agent.AssistantDelta -= onDelta;
            agent.AssistantFinished -= onFinished;
            agent.StateChanged -= onState;
            agent.ToolActivityChanged -= onTool;
            agent.ErrorRaised -= onError;
            agent.ConfirmHandler = null;
            agent.PanelVisible = false;
            ProviderRouter.CurrentChanged -= onProvider;
        };

        Rebuild();
    }

    public void ToggleOpen()
    {
        if (IsVisible)
        {
            // déjà ouvert en arrière-plan : on le ramène au premier plan au lieu de le fermer
            if (IsActive)
                HideAnimated();
            else
            {
                Activate();
                input.Focus();
            }
            return;
        }
        RefreshStatus();
        if (!ProviderRouter.HasAnyConfigured() && list.Children.Count == 0)
            Rebuild();
        ShowAnimated(new Point(0.5, 0.5));
        _ = ProviderRouter.RefreshLocalAsync().ContinueWith(_ => Dispatcher.BeginInvoke(RefreshStatus));
    }

    #region Construction du fil
    private void Rebuild()
    {
        list.Children.Clear();
        streaming = null;
        typingRow = null;
        RefreshStatus();
        if (!ProviderRouter.HasAnyConfigured())
        {
            ShowOnboarding(force: false);
            return;
        }
        var history = agent.History;
        if (history.Count == 0)
        {
            AddWelcome();
            return;
        }
        foreach (var m in history)
        {
            if (m.Role == ChatRole.User)
                AddUser(m.Text ?? "");
            else if (m.Role == ChatRole.Assistant && !string.IsNullOrWhiteSpace(m.Text))
            {
                StartAssistant();
                AppendAssistant(m.Text!);
                streaming = null;
            }
        }
    }

    private void AddWelcome()
    {
        var box = new StackPanel { Margin = new Thickness(0, 24, 0, 8) };
        box.Children.Add(new TextBlock { Text = "Salut !", FontFamily = (FontFamily)FindResource("HudDisplay"), FontWeight = FontWeights.SemiBold, FontSize = 30, Foreground = (Brush)FindResource("HudText") });
        box.Children.Add(new TextBlock { Text = "Dis-moi ce que tu veux — pas besoin d'IA. Clique un exemple ou écris-le :", Style = (Style)FindResource("HudBodyText"), Foreground = (Brush)FindResource("HudTextMuted"), Margin = new Thickness(0, 6, 0, 10) });
        // toutes les catégories de commandes (2-3 exemples chacune), depuis la source unique
        foreach (var g in VPet_Simulator.Windows.Assistant.CommandCatalog.Groups)
        {
            box.Children.Add(new TextBlock
            {
                Text = g.Icon + "  " + g.Title.ToUpperInvariant(),
                Style = (Style)FindResource("HudEyebrow"),
                Margin = new Thickness(0, 12, 0, 6),
            });
            var chips = new WrapPanel();
            foreach (var s in g.Examples.Take(3))
            {
                var chip = new Button { Style = (Style)FindResource("HudGhostButton"), Content = "« " + s + " »", Margin = new Thickness(0, 0, 8, 8), FontSize = 12 };
                chip.Click += (_, _) => Send(s);
                chips.Children.Add(chip);
            }
            box.Children.Add(chips);
        }
        list.Children.Add(box);
    }

    private void AddUser(string text)
    {
        RemoveWelcome();
        var bubble = new Border
        {
            Background = (Brush)FindResource("HudAccentSoft"),
            BorderBrush = (Brush)FindResource("HudAccent"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(18, 18, 4, 18),
            Padding = new Thickness(14, 9, 14, 10),
            HorizontalAlignment = HorizontalAlignment.Right,
            MaxWidth = 300,
            Margin = new Thickness(40, 10, 0, 4),
            Child = new TextBlock { Text = text, Style = (Style)FindResource("HudBodyText") },
        };
        Add(bubble);
    }

    private void StartAssistant()
    {
        RemoveTyping();
        var name = new TextBlock { Text = (Pet.Core.Save?.Name ?? "V-Max").ToUpperInvariant(), Style = (Style)FindResource("HudEyebrow"), Foreground = (Brush)FindResource("HudAccent"), Margin = new Thickness(0, 14, 0, 4) };
        streaming = new TextBlock { Style = (Style)FindResource("HudBodyText"), FontSize = 14.5 };
        var copy = new MenuItem { Header = "Copier" };
        var tb = streaming;
        copy.Click += (_, _) => { try { Clipboard.SetText(tb.Text); } catch { } };
        streaming.ContextMenu = new ContextMenu { Items = { copy } };
        var box = new StackPanel { Margin = new Thickness(0, 0, 30, 4) };
        box.Children.Add(name);
        box.Children.Add(streaming);
        Add(box);
    }

    private void AppendAssistant(string delta)
    {
        if (streaming == null)
            StartAssistant();
        streaming!.Text += delta;
        scroll.ScrollToEnd();
    }

    private void AddTool(ToolActivity t)
    {
        RemoveTyping();
        string icon = !t.Done ? "" : t.Ok ? "" : "";
        var brush = (Brush)FindResource(!t.Done ? "HudAmber" : t.Ok ? "HudSuccess" : "HudTextMuted");
        var row = new Border
        {
            Margin = new Thickness(0, 8, 30, 2),
            Padding = new Thickness(12, 8, 12, 8),
            CornerRadius = new CornerRadius(12),
            BorderBrush = (Brush)FindResource("HudStroke"),
            BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        sp.Children.Add(new TextBlock { Text = icon, FontFamily = (FontFamily)FindResource("HudIcons"), FontSize = 13, Foreground = brush, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
        var txt = new TextBlock { FontSize = 12.5, Foreground = (Brush)FindResource("HudTextMuted"), TextWrapping = TextWrapping.Wrap, MaxWidth = 300 };
        txt.Inlines.Add(new System.Windows.Documents.Run(t.Title) { Foreground = (Brush)FindResource("HudText") });
        if (!string.IsNullOrEmpty(t.Detail))
            txt.Inlines.Add(new System.Windows.Documents.Run(" · " + t.Detail));
        sp.Children.Add(txt);
        row.Child = sp;
        // une action terminée remplace sa carte « en cours »
        if (t.Done && list.Children.Count > 0 && list.Children[^1] is Border last && Equals(last.Tag, t.ToolName))
            list.Children.RemoveAt(list.Children.Count - 1);
        row.Tag = t.Done ? null : t.ToolName;
        Add(row);
    }

    private void AddError(string message, bool needsSetup)
    {
        RemoveTyping();
        var bar = new Border { Width = 3, CornerRadius = new CornerRadius(2), Background = (Brush)FindResource("HudAmber"), Margin = new Thickness(0, 0, 12, 0) };
        var text = new StackPanel();
        text.Children.Add(new TextBlock { Text = message, Style = (Style)FindResource("HudBodyText"), FontSize = 13 });
        if (needsSetup)
        {
            var b = new Button { Style = (Style)FindResource("HudPrimaryButton"), Content = "Connecter une IA gratuite", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 10, 0, 0) };
            b.Click += (_, _) => ShowOnboarding(force: true);
            text.Children.Add(b);
        }
        var row = new DockPanel { Margin = new Thickness(0, 12, 20, 4) };
        DockPanel.SetDock(bar, Dock.Left);
        row.Children.Add(bar);
        row.Children.Add(text);
        Add(row);
        RefreshStatus();
    }

    private Task<Decision> AskConfirmation(IAgentTool tool, string description)
    {
        RemoveTyping();
        var tcs = new TaskCompletionSource<Decision>();
        var card = new Border
        {
            Margin = new Thickness(0, 10, 20, 4),
            Padding = new Thickness(16, 12, 16, 14),
            CornerRadius = new CornerRadius(16),
            Background = (Brush)FindResource("HudSurfaceRaised"),
            BorderBrush = (Brush)FindResource("HudAccent"),
            BorderThickness = new Thickness(1),
        };
        var sp = new StackPanel();
        sp.Children.Add(new TextBlock { Text = "AUTORISATION", Style = (Style)FindResource("HudEyebrow"), Foreground = (Brush)FindResource("HudAccent") });
        sp.Children.Add(new TextBlock { Text = description, Style = (Style)FindResource("HudBodyText"), FontSize = 15, Margin = new Thickness(0, 4, 0, 12) });
        var actions = new StackPanel { Orientation = Orientation.Horizontal };
        void Done(Decision d, string label)
        {
            tcs.TrySetResult(d);
            sp.Children.Remove(actions);
            sp.Children.Add(new TextBlock { Text = label, Style = (Style)FindResource("HudEyebrow") });
        }
        var allow = new Button { Style = (Style)FindResource("HudPrimaryButton"), Content = "Autoriser", Margin = new Thickness(0, 0, 8, 0) };
        allow.Click += (_, _) => Done(Decision.Once, "AUTORISÉ");
        actions.Children.Add(allow);
        if (tool.Risk == ToolRisk.Sensitive)
        {
            var always = new Button { Style = (Style)FindResource("HudGhostButton"), Content = "Toujours", Margin = new Thickness(0, 0, 8, 0) };
            always.Click += (_, _) => Done(Decision.Always, "AUTORISÉ DÉFINITIVEMENT");
            actions.Children.Add(always);
        }
        var refuse = new Button { Style = (Style)FindResource("HudGhostButton"), Content = "Refuser" };
        refuse.Click += (_, _) => Done(Decision.Refused, "REFUSÉ");
        actions.Children.Add(refuse);
        sp.Children.Add(actions);
        card.Child = sp;
        Add(card);
        return tcs.Task;
    }

    private void OnState(AgentState s)
    {
        RefreshStatus();
        send.Content = new TextBlock
        {
            Text = s is AgentState.Thinking or AgentState.Acting or AgentState.Speaking or AgentState.AwaitingConfirmation ? "" : "",
            FontFamily = (FontFamily)FindResource("HudIcons"),
            FontSize = 14,
        };
        if (s == AgentState.Thinking && typingRow == null)
            AddTyping();
        if (s is AgentState.Idle or AgentState.Error)
            RemoveTyping();
    }

    private void AddTyping()
    {
        var dots = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 14, 0, 4) };
        for (int i = 0; i < 3; i++)
        {
            var d = new Ellipse { Width = 7, Height = 7, Margin = new Thickness(0, 0, 5, 0), Fill = (Brush)FindResource("HudSilver") };
            var a = new DoubleAnimation(0.25, 1, TimeSpan.FromMilliseconds(420)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, BeginTime = TimeSpan.FromMilliseconds(i * 140) };
            d.BeginAnimation(OpacityProperty, a);
            dots.Children.Add(d);
        }
        typingRow = dots;
        Add(dots);
    }

    private void RemoveTyping()
    {
        if (typingRow != null)
            list.Children.Remove(typingRow);
        typingRow = null;
    }

    private void RemoveWelcome()
    {
        if (list.Children.Count > 0 && list.Children[0] is StackPanel sp && sp.Children.Count > 0 && sp.Children[0] is TextBlock { Text: "Salut !" })
            list.Children.RemoveAt(0);
        if (list.Children.Count > 0 && list.Children[0] is FrameworkElement fe && Equals(fe.Tag, "onboarding"))
            list.Children.RemoveAt(0);
    }

    private void Add(FrameworkElement el)
    {
        list.Children.Add(el);
        UiMotionSlide(el);
        scroll.ScrollToEnd();
    }

    private static void UiMotionSlide(FrameworkElement el) => VPet_Simulator.Core.UiMotion.SlideIn(el, 8, 180);
    #endregion

    #region Accueil : choisir le cerveau
    // V-Max : la discussion ne liste plus les connecteurs (ça vit dans Paramètres › IA) ; elle invite juste à s'y rendre.
    private void ShowOnboarding(bool force)
    {
        if (force)
            list.Children.Clear();
        var name = Pet.Core.Save?.Name ?? "Maxine";
        var box = new StackPanel { Margin = new Thickness(0, 10, 0, 8), Tag = "onboarding" };
        box.Children.Add(new TextBlock { Text = "Donne un cerveau à " + name, FontFamily = (FontFamily)FindResource("HudDisplay"), FontWeight = FontWeights.SemiBold, FontSize = 24, TextWrapping = TextWrapping.Wrap, Foreground = (Brush)FindResource("HudText") });
        box.Children.Add(new TextBlock
        {
            Text = "Pour que je puisse te répondre, connecte-moi une IA — gratuite en 30 secondes, ou l'IA locale qui tourne sur ton PC. Tout ça se règle dans les paramètres.",
            Style = (Style)FindResource("HudBodyText"), FontSize = 13, Foreground = (Brush)FindResource("HudTextMuted"), Margin = new Thickness(0, 6, 0, 14),
        });
        var b = new Button { Style = (Style)FindResource("HudPrimaryButton"), Content = "Ouvrir les réglages IA", HorizontalAlignment = HorizontalAlignment.Left };
        b.Click += (_, _) => Pet.ShowSetting("ia");
        box.Children.Add(b);
        list.Children.Add(box);
        UiMotionSlide(box);
        if (force)
            scroll.ScrollToTop();
    }

    private FrameworkElement ProviderRow(ProviderInfo p)
    {
        bool connected = p.IsLocal ? ProviderRouter.IsLocalReachable(p.Id) : !string.IsNullOrEmpty(ProviderRouter.KeyFor(p));
        var (ok, statusLabel) = ProviderRouter.StatusOf(p);
        var name = new TextBlock { Text = p.Name, FontFamily = (FontFamily)FindResource("HudDisplay"), FontWeight = FontWeights.SemiBold, FontSize = 15, Foreground = (Brush)FindResource("HudText") };
        var tagline = new TextBlock { Text = p.Tagline, FontSize = 12, Foreground = (Brush)FindResource("HudTextMuted"), TextWrapping = TextWrapping.Wrap };
        var state = new TextBlock
        {
            Text = statusLabel.ToUpperInvariant(),
            Style = (Style)FindResource("HudEyebrow"),
            Foreground = (Brush)FindResource(ok ? "HudSuccess" : connected ? "HudAmber" : "HudTextMuted"),
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition());
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var names = new StackPanel { Margin = new Thickness(0, 0, 10, 0) };
        names.Children.Add(name);
        names.Children.Add(tagline);
        head.Children.Add(names);
        var side = new StackPanel { VerticalAlignment = VerticalAlignment.Top };
        side.Children.Add(state);
        if (!p.IsLocal && p.SignupUrl != null)
            side.Children.Add(LinkButton(connected ? "Gérer" : "Obtenir une clé", p.SignupUrl));
        Grid.SetColumn(side, 1);
        head.Children.Add(side);

        var body = new StackPanel();
        body.Children.Add(head);
        var actions = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 10, 0, 0) };
        var feedback = new TextBlock { FontSize = 12, Foreground = (Brush)FindResource("HudTextMuted"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0), Visibility = Visibility.Collapsed };

        if (p.IsLocal)
        {
            var retry = new Button { Style = (Style)FindResource("HudGhostButton"), Content = "Détecter", Margin = new Thickness(0, 0, 8, 0) };
            retry.Click += async (_, _) =>
            {
                retry.IsEnabled = false;
                await ProviderRouter.RefreshLocalAsync();
                ProviderRouter.Reset(p.Id);
                retry.IsEnabled = true;
                Refresh();
            };
            DockPanel.SetDock(retry, Dock.Left);
            actions.Children.Add(retry);
            if (!connected && p.SignupUrl != null)
            {
                var install = LinkButton("Installer", p.SignupUrl);
                install.VerticalAlignment = VerticalAlignment.Center;
                install.HorizontalAlignment = HorizontalAlignment.Left;
                actions.Children.Add(install);
            }
            else
                actions.Children.Add(new Border());
        }
        else
        {
            var keyBox = new PasswordBox
            {
                Height = 34, Padding = new Thickness(10, 7, 10, 7), VerticalContentAlignment = VerticalAlignment.Center,
                Background = Brushes.Transparent, BorderThickness = new Thickness(0),
                Foreground = (Brush)FindResource("HudText"), CaretBrush = (Brush)FindResource("HudAccent"), FontFamily = (FontFamily)FindResource("HudMono"),
                ToolTip = "Colle ta clé API ici",
            };
            // le bouton ne prend la couleur d'action qu'une fois une clé collée
            var connect = new Button { Style = (Style)FindResource("HudGhostButton"), Content = connected ? "Remplacer" : "Connecter", Margin = new Thickness(8, 0, 0, 0) };
            // indication dans le champ tant qu'il est vide
            var hint = new TextBlock { Text = connected ? "Nouvelle clé…" : "Colle ta clé ici", IsHitTestVisible = false, Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, FontSize = 12.5, Foreground = (Brush)FindResource("HudTextMuted") };
            keyBox.PasswordChanged += (_, _) =>
            {
                bool empty = keyBox.Password.Length == 0;
                hint.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
                connect.Style = (Style)FindResource(empty ? "HudGhostButton" : "HudPrimaryButton");
            };
            keyBox.KeyDown += (_, e) => { if (e.Key == Key.Enter) connect.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent)); };
            var fieldGrid = new Grid();
            fieldGrid.Children.Add(keyBox);
            fieldGrid.Children.Add(hint);
            var field = new Border
            {
                CornerRadius = new CornerRadius(12),
                Background = (Brush)FindResource("HudSurface"),
                BorderBrush = (Brush)FindResource("HudStroke"),
                BorderThickness = new Thickness(1),
                Child = fieldGrid,
            };
            keyBox.GotKeyboardFocus += (_, _) => field.BorderBrush = (Brush)FindResource("HudAccent");
            keyBox.LostKeyboardFocus += (_, _) => field.BorderBrush = (Brush)FindResource("HudStroke");
            connect.Click += async (_, _) =>
            {
                var key = keyBox.Password.Trim();
                if (key.Length < 10)
                {
                    Feedback(feedback, "Colle d'abord la clé copiée sur le site du service.", false);
                    return;
                }
                connect.IsEnabled = false;
                Feedback(feedback, "Vérification de la clé…", null);
                var error = await ProviderRouter.ValidateKeyAsync(p, key);
                connect.IsEnabled = true;
                if (error != null)
                {
                    Feedback(feedback, "Cette clé ne fonctionne pas : " + error, false);
                    return;
                }
                SecretStore.Set(p.SecretName!, key);
                ProviderRouter.Reset(p.Id);
                keyBox.Clear();
                Feedback(feedback, "Connecté ! Tu peux discuter.", true);
                ActivateAgent();
                RefreshStatus();
                await Task.Delay(900);
                Rebuild();
            };
            DockPanel.SetDock(connect, Dock.Right);
            actions.Children.Add(connect);
            actions.Children.Add(field);
        }
        body.Children.Add(actions);
        body.Children.Add(feedback);
        return new Border
        {
            Margin = new Thickness(0, 0, 0, 10),
            Padding = new Thickness(14, 12, 14, 12),
            CornerRadius = new CornerRadius(16),
            Background = (Brush)FindResource("HudSurfaceRaised"),
            BorderBrush = (Brush)FindResource(connected ? "HudSuccess" : "HudStroke"),
            BorderThickness = new Thickness(1),
            Child = body,
        };

        void Refresh()
        {
            RefreshStatus();
            if (list.Children.Count > 0 && list.Children[0] is FrameworkElement fe && Equals(fe.Tag, "onboarding"))
                ShowOnboarding(force: true);
        }
    }

    private void Feedback(TextBlock fb, string text, bool? ok)
    {
        fb.Text = text;
        fb.Foreground = (Brush)FindResource(ok == true ? "HudSuccess" : ok == false ? "HudAmber" : "HudTextMuted");
        fb.Visibility = Visibility.Visible;
    }

    private Button LinkButton(string text, string url)
    {
        var label = new TextBlock { FontSize = 12.5, Foreground = (Brush)FindResource("HudAccent") };
        label.Inlines.Add(new System.Windows.Documents.Run(text + " "));
        label.Inlines.Add(new System.Windows.Documents.Run("") { FontFamily = (FontFamily)FindResource("HudIcons"), FontSize = 10 });
        var b = new Button { Content = label, Cursor = Cursors.Hand, ToolTip = url, Margin = new Thickness(0, 4, 0, 0), HorizontalAlignment = HorizontalAlignment.Right };
        b.Template = new ControlTemplate(typeof(Button)) { VisualTree = new FrameworkElementFactory(typeof(ContentPresenter)) };
        b.MouseEnter += (_, _) => label.TextDecorations = TextDecorations.Underline;
        b.MouseLeave += (_, _) => label.TextDecorations = null;
        b.Click += (_, _) => ExtensionFunction.StartURL(url);
        return b;
    }


    private void ActivateAgent()
    {
        if (!VMaxAgentPlugin.IsActive(Pet))
            VMaxAgentPlugin.Activate(Pet, true);
    }
    #endregion

    #region Envoi
    private void Input_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && sendOnEnter && !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            e.Handled = true;
            SendOrStop();
        }
    }

    private void SendOrStop()
    {
        if (agent.State is AgentState.Thinking or AgentState.Acting or AgentState.Speaking or AgentState.AwaitingConfirmation)
        {
            agent.Cancel();
            return;
        }
        Send(input.Text);
    }

    /// <summary>Envoie un message comme si l'utilisateur l'avait tapé</summary>
    public void Send(string text)
    {
        text = text.Trim();
        if (text.Length == 0)
            return;
        input.Clear();
        // Assistant utilitaire DÉTERMINISTE d'abord : commande reconnue → on exécute et on répond ici, sans IA.
        var outcome = Pet.Assistant?.TryHandle(text);
        if (outcome != null)
        {
            AddUser(text);
            AddAssistantMessage(outcome.Speak);
            return;
        }
        // IA coupée par défaut : on ne connaît pas la commande → on ouvre la liste des commandes (la « doc »).
        if (!Pet.AssistantAiEnabled)
        {
            AddUser(text);
            AddAssistantMessage("Je n'ai pas de commande pour ça. Je t'ouvre la liste de tout ce que je sais faire 👇");
            Pet.Hud?.OpenPanel("help");
            return;
        }
        // IA de secours (seulement si activée dans les réglages) — l'agent affiche lui-même le message utilisateur.
        ActivateAgent();
        _ = Task.Run(() => agent.RespondAsync(text));
    }

    /// <summary>Affiche une réponse de Maxine (assistant déterministe) dans le fil, en un bloc.</summary>
    private void AddAssistantMessage(string text)
    {
        StartAssistant();
        streaming!.Text = text;
        streaming = null;
        scroll.ScrollToEnd();
    }
    #endregion

    private void RefreshStatus()
    {
        // Mode par défaut : assistant utilitaire déterministe (sans IA). On n'affiche PAS de fournisseur IA.
        if (!Pet.AssistantAiEnabled)
        {
            statusDot.Fill = (Brush)FindResource("HudSuccess");
            statusText.Text = "assistant · commandes";
            return;
        }
        var current = ProviderRouter.Current;
        bool any = ProviderRouter.HasAnyConfigured();
        statusDot.Fill = (Brush)FindResource(any ? "HudSuccess" : "HudTextMuted");
        statusText.Text = agent.State switch
        {
            AgentState.Thinking => "réfléchit…",
            AgentState.Acting => "agit sur ton PC…",
            AgentState.AwaitingConfirmation => "attend ton accord",
            _ when !any => "aucune IA connectée",
            _ when current != null => (current.IsLocal ? "local · " : "en ligne · ") + current.DisplayName + " · " + current.Model,
            _ => "prête · " + string.Join(", ", ProviderRouter.Available().Select(a => a.info.Name)),
        };
    }

    private Button IconButton(string glyph, string tip, Action run)
    {
        var b = new Button { Style = (Style)FindResource("HudIconButton"), Content = glyph, ToolTip = tip };
        b.Click += (_, _) => run();
        return b;
    }

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
}
