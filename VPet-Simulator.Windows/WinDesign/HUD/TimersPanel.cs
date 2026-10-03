using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using VPet_Simulator.Windows.Assistant;

namespace VPet_Simulator.Windows.HUD;

/// <summary>V-Max HUD : minuteurs et rappels en cours, avec compte à rebours en direct. « ✕ » pour en annuler un.</summary>
public sealed class TimersPanel : HudSidePanel
{
    private readonly StackPanel list;
    private readonly DispatcherTimer tick;

    public TimersPanel(MainWindow pet) : base(pet, "MINUTEURS", "En cours", 300, 380)
    {
        AddHeaderButton("", "Tout annuler", () => AssistantTimers.CancelAll());
        list = new StackPanel();
        Body = list;
        tick = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
        tick.Tick += (_, _) => Build();
        AssistantTimers.Changed += OnChanged;
        Closed += (_, _) => AssistantTimers.Changed -= OnChanged;
        IsVisibleChanged += (_, _) => { if (IsVisible) { Build(); tick.Start(); } else tick.Stop(); };
    }

    private void OnChanged() => Dispatcher.Invoke(Build);

    private void Build()
    {
        list.Children.Clear();
        var active = AssistantTimers.Active;
        if (active.Count == 0)
        {
            list.Children.Add(new TextBlock
            {
                Text = "Aucun minuteur. Dis « minuteur de 5 minutes » ou « rappelle-moi dans 10 minutes ».",
                Foreground = Res("HudTextMuted"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 8, 0, 0),
            });
            return;
        }
        foreach (var e in active)
            list.Children.Add(Row(e));
    }

    private FrameworkElement Row(AssistantTimers.Entry e)
    {
        var rem = e.Remaining;
        if (rem < TimeSpan.Zero) rem = TimeSpan.Zero;
        string countdown = rem.TotalHours >= 1 ? rem.ToString(@"h\:mm\:ss") : rem.ToString(@"m\:ss");

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var label = new TextBlock
        {
            Text = string.IsNullOrEmpty(e.Label) ? (e.IsReminder ? "Rappel" : "Minuteur") : e.Label,
            Foreground = Res("HudText"),
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        grid.Children.Add(label);

        var time = new TextBlock
        {
            Text = countdown,
            Foreground = Res("HudAccent"),
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 8, 0),
        };
        Grid.SetColumn(time, 1);
        grid.Children.Add(time);

        var cancel = new Button { Style = (Style)FindResource("HudIconButton"), Content = "", ToolTip = "Annuler", FontSize = 12 };
        cancel.Click += (_, _) => AssistantTimers.Cancel(e.Id);
        Grid.SetColumn(cancel, 2);
        grid.Children.Add(cancel);

        return new Border
        {
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(12, 7, 6, 7),
            Margin = new Thickness(0, 0, 0, 4),
            Background = Res("HudSurfaceRaised"),
            Child = grid,
        };
    }
}
