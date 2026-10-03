using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using VPet_Simulator.Windows.Assistant;

namespace VPet_Simulator.Windows.HUD;

/// <summary>V-Max HUD : historique du presse-papiers. Clic sur une entrée = la recopier pour la recoller où tu veux.</summary>
public sealed class ClipboardPanel : HudSidePanel
{
    private readonly StackPanel list;

    public ClipboardPanel(MainWindow pet) : base(pet, "PRESSE-PAPIERS", "Ce que j'ai retenu", 320, 460)
    {
        AddHeaderButton("", "Vider l'historique", () => { ClipboardHistory.Clear(); });
        list = new StackPanel();
        Body = list;
        ClipboardHistory.Start();
        ClipboardHistory.Changed += OnChanged;
        Closed += (_, _) => ClipboardHistory.Changed -= OnChanged;
        Build();
    }

    private void OnChanged() => Dispatcher.Invoke(Build);
    protected override void OnOpening() => Build();

    private void Build()
    {
        list.Children.Clear();
        var items = ClipboardHistory.Items;
        if (items.Count == 0)
        {
            list.Children.Add(new TextBlock
            {
                Text = "Copie quelque chose (Ctrl+C) et je le garde ici pour toi.",
                Foreground = Res("HudTextMuted"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 8, 0, 0),
            });
            return;
        }
        foreach (var it in items)
            list.Children.Add(Row(it));
    }

    private FrameworkElement Row(string text)
    {
        var preview = text.Replace("\r", " ").Replace("\n", " ").Trim();
        if (preview.Length > 140) preview = preview[..140] + "…";
        var tb = new TextBlock
        {
            Text = preview,
            Foreground = Res("HudText"),
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
            MaxHeight = 60,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        var hint = new TextBlock { Text = "Copié", FontSize = 11, Foreground = Res("HudSuccess"), Opacity = 0, Margin = new Thickness(0, 4, 0, 0) };
        var stack = new StackPanel();
        stack.Children.Add(tb);
        stack.Children.Add(hint);
        var b = new Border
        {
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(12, 9, 12, 9),
            Margin = new Thickness(0, 0, 0, 6),
            Background = Res("HudSurfaceRaised"),
            Cursor = Cursors.Hand,
            Child = stack,
        };
        b.MouseLeftButtonUp += (_, _) =>
        {
            ClipboardHistory.CopyBack(text);
            hint.Opacity = 1;
        };
        return b;
    }
}
