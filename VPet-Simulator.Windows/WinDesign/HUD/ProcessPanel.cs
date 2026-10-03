using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace VPet_Simulator.Windows.HUD;

/// <summary>
/// V-Max HUD : les applications les plus gourmandes (processeur + mémoire), en direct. Clic sur « ✕ » pour forcer
/// la fermeture (double-clic de confirmation). Un mini gestionnaire des tâches, pour « qu'est-ce qui rame ? ».
/// </summary>
public sealed class ProcessPanel : HudSidePanel
{
    private readonly StackPanel list;
    private readonly DispatcherTimer timer;
    private readonly Dictionary<int, (TimeSpan cpu, DateTime at)> prev = new();

    public ProcessPanel(MainWindow pet) : base(pet, "ACTIVITÉ", "Ce qui tourne", 320, 470)
    {
        AddHeaderButton("", "Rafraîchir", Sample);
        list = new StackPanel();
        Body = list;
        timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(2) };
        timer.Tick += (_, _) => Sample();
        IsVisibleChanged += (_, _) => { if (IsVisible) { Sample(); timer.Start(); } else timer.Stop(); };
    }

    private void Sample()
    {
        var now = DateTime.UtcNow;
        int cpuCount = Math.Max(1, Environment.ProcessorCount);
        var rows = new List<(int pid, string name, double cpu, long ram)>();
        foreach (var p in Process.GetProcesses())
        {
            try
            {
                if (p.Id == 0 || string.IsNullOrEmpty(p.ProcessName))
                    continue;
                long ram = p.WorkingSet64;
                double cpu = 0;
                var cur = p.TotalProcessorTime;
                if (prev.TryGetValue(p.Id, out var pr))
                {
                    double secs = (now - pr.at).TotalSeconds;
                    if (secs > 0)
                        cpu = Math.Clamp((cur - pr.cpu).TotalSeconds / (secs * cpuCount) * 100.0, 0, 100);
                }
                prev[p.Id] = (cur, now);
                rows.Add((p.Id, p.ProcessName, cpu, ram));
            }
            catch { }
            finally { try { p.Dispose(); } catch { } }
        }

        // on garde les 14 plus gourmands (processeur d'abord, puis mémoire)
        var top = rows
            .GroupBy(r => r.name)
            .Select(g => (name: g.Key, cpu: g.Sum(x => x.cpu), ram: g.Sum(x => x.ram), pid: g.OrderByDescending(x => x.ram).First().pid))
            .OrderByDescending(r => r.cpu)
            .ThenByDescending(r => r.ram)
            .Take(14)
            .ToList();

        list.Children.Clear();
        foreach (var r in top)
            list.Children.Add(Row(r.pid, r.name, r.cpu, r.ram));
    }

    private FrameworkElement Row(int pid, string name, double cpu, long ram)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var title = new TextBlock { Text = name, Foreground = Res("HudText"), FontSize = 13, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        grid.Children.Add(title);

        var stats = new TextBlock
        {
            Text = $"{cpu:0} %   ·   {ram / 1048576.0:0} Mo",
            Foreground = Res("HudTextMuted"),
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 10, 0),
        };
        Grid.SetColumn(stats, 1);
        grid.Children.Add(stats);

        var kill = new Button { Style = (Style)FindResource("HudIconButton"), Content = "", ToolTip = "Forcer la fermeture", FontSize = 12 };
        bool armed = false;
        kill.Click += (_, _) =>
        {
            if (!armed)
            {
                armed = true;
                kill.Foreground = new SolidColorBrush(Color.FromRgb(0xE5, 0x5A, 0x5A));
                kill.ToolTip = "Clique encore pour confirmer";
                return;
            }
            try { using var p = Process.GetProcessById(pid); p.Kill(); } catch { }
            Sample();
        };
        Grid.SetColumn(kill, 2);
        grid.Children.Add(kill);

        return new Border
        {
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(12, 6, 6, 6),
            Margin = new Thickness(0, 0, 0, 3),
            Background = Res("HudSurfaceRaised"),
            Child = grid,
        };
    }
}
