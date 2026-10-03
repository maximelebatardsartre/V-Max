using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using VPet_Simulator.Windows.Assistant;

namespace VPet_Simulator.Windows.HUD;

/// <summary>V-Max HUD : lanceur d'applications. Tape pour filtrer tes logiciels installés, clic pour lancer.</summary>
public sealed class AppLauncherPanel : HudSidePanel
{
    private readonly TextBox search;
    private readonly StackPanel list;
    private readonly IReadOnlyList<(string display, string path)> all;

    public AppLauncherPanel(MainWindow pet) : base(pet, "LANCEUR", "Applications", 320, 470)
    {
        all = AppIndex.AllApps();
        search = new TextBox
        {
            Style = TryFindResource("HudTextBox") as Style,
            Tag = "Rechercher une application…",
            Margin = new Thickness(0, 0, 0, 10),
            Foreground = (Brush)FindResource("HudText"),
        };
        search.TextChanged += (_, _) => Refresh();
        search.PreviewKeyDown += (_, e) => { if (e.Key == Key.Enter) LaunchFirst(); };
        list = new StackPanel();
        var root = new StackPanel();
        root.Children.Add(search);
        root.Children.Add(list);
        Body = root;
        Refresh();
        Loaded += (_, _) => search.Focus();
    }

    private void LaunchFirst()
    {
        var q = CommandRouter.Normalize(search.Text);
        var first = all.FirstOrDefault(a => q.Length == 0 || CommandRouter.Normalize(a.display).Contains(q));
        if (first.path != null)
            Open(first.path);
    }

    private void Refresh()
    {
        list.Children.Clear();
        var q = CommandRouter.Normalize(search.Text);
        var items = all.Where(a => q.Length == 0 || CommandRouter.Normalize(a.display).Contains(q)).Take(40).ToList();
        if (items.Count == 0)
            list.Children.Add(new TextBlock { Text = "Aucune application trouvée.", Foreground = Res("HudTextMuted"), Margin = new Thickness(2, 6, 0, 0) });
        foreach (var a in items)
            list.Children.Add(Row(a.display, a.path));
    }

    private FrameworkElement Row(string display, string path)
    {
        var tb = new TextBlock { Text = display, Foreground = (Brush)FindResource("HudText"), FontSize = 14, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        var b = new Border
        {
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(12, 9, 12, 9),
            Margin = new Thickness(0, 0, 0, 4),
            Background = Brushes.Transparent,
            Cursor = Cursors.Hand,
            Child = tb,
        };
        b.MouseEnter += (_, _) => b.Background = (Brush)FindResource("HudSurfaceRaised");
        b.MouseLeave += (_, _) => b.Background = Brushes.Transparent;
        b.MouseLeftButtonUp += (_, _) => Open(path);
        return b;
    }

    private void Open(string path)
    {
        AppIndex.Launch(path);
        HideAnimated();
    }
}
