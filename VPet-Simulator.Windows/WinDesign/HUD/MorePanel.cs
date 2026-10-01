using LinePutScript.Localization.WPF;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace VPet_Simulator.Windows.HUD;

/// <summary>
/// V-Max HUD : panneau « Plus ». Construit à chaque ouverture depuis les menus de la barre de VPet,
/// qui restent le modèle : les entrées ajoutées par les plugins et le code DIY apparaissent donc ici automatiquement.
/// Les entrées déjà couvertes par l'anneau (repas, occupations, sommeil) sont masquées.
/// </summary>
public sealed class MorePanel : HudSidePanel
{
    private readonly StackPanel root = new();

    public MorePanel(MainWindow pet) : base(pet, "PLUS", "Tout le reste", 380)
    {
        CloseOnDeactivate = true;
        MaxHeight = 640;
        Body = root;
    }

    protected override void OnOpening()
    {
        root.Children.Clear();
        var tb = Pet.Main.ToolBar;
        if (tb == null)
            return;
        // déjà dans l'anneau ou le garde-manger
        var covered = new HashSet<string>(new[] { "吃饭", "喝水", "收藏", "药品", "礼品", "睡觉", "设置面板" }.Select(k => k.Translate()));
        var hidden = new HashSet<MenuItem> { tb.MenuStudy, tb.MenuPlay, tb.MenuWork };

        AddSection("Interactions", tb.MenuFeed.Items.OfType<MenuItem>().Concat(tb.MenuInteract.Items.OfType<MenuItem>())
            .Where(m => !hidden.Contains(m) && !covered.Contains(HeaderText(m))));
        AddSection("Raccourcis et mods", tb.MenuDIY.Items.OfType<MenuItem>());
        var quit = "退出桌宠".Translate();
        AddSection("Application", tb.MenuSetting.Items.OfType<MenuItem>().Where(m => !covered.Contains(HeaderText(m)))
            .OrderBy(m => HeaderText(m) == quit));
    }

    private void AddSection(string title, IEnumerable<MenuItem> items)
    {
        var list = items.Where(m => m.Visibility == Visibility.Visible && !string.IsNullOrWhiteSpace(HeaderText(m))).ToList();
        if (list.Count == 0)
            return;
        var s = Section(title);
        if (root.Children.Count == 0)
            s.Margin = new Thickness(0, 4, 0, 8);
        root.Children.Add(s);
        foreach (var mi in list)
            root.Children.Add(Row(mi, 0));
    }

    private FrameworkElement Row(MenuItem mi, int depth)
    {
        string text = HeaderText(mi);
        var children = mi.Items.OfType<MenuItem>().Where(c => c.Visibility == Visibility.Visible).ToList();
        bool isQuit = text == "退出桌宠".Translate();

        var icon = new TextBlock
        {
            Text = depth > 0 ? "" : Glyph(text),
            FontFamily = (FontFamily)FindResource("HudIcons"),
            FontSize = depth > 0 ? 10 : 15,
            Width = 22,
            Foreground = Res(isQuit ? "HudAccent" : "HudTextMuted"),
            VerticalAlignment = VerticalAlignment.Center,
        };
        var label = new TextBlock { Text = text, FontSize = 14, Foreground = Res(isQuit ? "HudAccent" : "HudText"), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(10, 0, 0, 0) };
        var chevron = new TextBlock { Text = children.Count > 0 ? "" : "", FontFamily = (FontFamily)FindResource("HudIcons"), FontSize = 10, Foreground = Res("HudTextMuted"), VerticalAlignment = VerticalAlignment.Center };
        var line = new DockPanel();
        DockPanel.SetDock(icon, Dock.Left);
        DockPanel.SetDock(chevron, Dock.Right);
        line.Children.Add(icon);
        line.Children.Add(chevron);
        line.Children.Add(label);
        var row = new Border
        {
            Padding = new Thickness(10, 9, 12, 9),
            Margin = new Thickness(depth * 22, 0, 0, 2),
            CornerRadius = new CornerRadius(12),
            Background = Brushes.Transparent,
            Cursor = Cursors.Hand,
            Child = line,
            Focusable = true,
            ToolTip = mi.ToolTip,
        };
        System.Windows.Automation.AutomationProperties.SetName(row, text);
        row.MouseEnter += (_, _) => row.Background = Res("HudSurfaceHover");
        row.MouseLeave += (_, _) => row.Background = Brushes.Transparent;

        var host = new StackPanel();
        host.Children.Add(row);
        StackPanel? sub = null;
        void Activate()
        {
            if (children.Count > 0)
            {
                // sous-menu déplié sur place
                if (sub == null)
                {
                    sub = new StackPanel();
                    foreach (var c in children)
                        sub.Children.Add(Row(c, depth + 1));
                    host.Children.Add(sub);
                    VPet_Simulator.Core.UiMotion.SlideIn(sub, 6, 160);
                }
                else
                    sub.Visibility = sub.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
                chevron.Text = sub.Visibility == Visibility.Visible ? "" : "";
                return;
            }
            HideAnimated(() =>
            {
                mi.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                if (mi.Command?.CanExecute(mi.CommandParameter) == true)
                    mi.Command.Execute(mi.CommandParameter);
            });
        }
        row.MouseLeftButtonUp += (_, _) => Activate();
        row.KeyDown += (_, e) => { if (e.Key is Key.Enter or Key.Space) Activate(); };
        return host;
    }

    private static string HeaderText(MenuItem mi) => mi.Header switch
    {
        string s => s,
        TextBlock t => t.Text,
        ContentControl c => c.Content?.ToString() ?? "",
        null => "",
        var o => o.ToString() ?? "",
    };

    /// <summary>Icône des entrées connues (les entrées des plugins ont une icône générique)</summary>
    private static string Glyph(string header)
    {
        var map = new Dictionary<string, string>
        {
            ["背包".Translate()] = "",
            ["退出桌宠".Translate()] = "",
            ["开发控制台".Translate()] = "",
            ["照片图库".Translate()] = "",
            ["操作教程".Translate()] = "",
            ["反馈中心".Translate()] = "",
            ["MOD设置".Translate()] = "",
        };
        return map.TryGetValue(header, out var g) ? g : "";
    }
}
