using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using VPet_Simulator.Core;

namespace VPet_Simulator.Windows.HUD;

/// <summary>
/// V-Max HUD : carte d'état du compagnon. Quatre jauges en arc (énergie, satiété, soif, humeur),
/// santé et affinité, progression et argent. Mise à jour en direct tant qu'elle est ouverte.
/// </summary>
public sealed class StatusCard : HudSidePanel
{
    private sealed record Gauge(ArcGauge Arc, TextBlock Number, TextBlock Rate);
    private readonly Gauge energy, food, drink, mood;
    private readonly (Border fill, TextBlock value) health, likability;
    private readonly Border expFill;
    private readonly Grid expTrack;
    private readonly TextBlock expText, moneyText, modeNote;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

    public StatusCard(MainWindow pet) : base(pet, "ÉTAT", pet.Core.Save?.Name ?? "V-Max", 380)
    {
        CloseOnDeactivate = true;
        AddHeaderButton("", "Statistiques détaillées", () => { HideAnimated(); Pet.MWController.ShowPanel(); });

        var root = new StackPanel();
        modeNote = new TextBlock { Style = (Style)FindResource("HudBodyText"), FontSize = 13, Foreground = Res("HudTextMuted"), Margin = new Thickness(0, 0, 0, 6) };
        root.Children.Add(modeNote);

        var gauges = new UniformGrid { Columns = 4, Margin = new Thickness(-4, 6, 0, 0) };
        energy = AddGauge(gauges, "Énergie");
        food = AddGauge(gauges, "Satiété");
        drink = AddGauge(gauges, "Soif");
        mood = AddGauge(gauges, "Humeur");
        root.Children.Add(gauges);

        root.Children.Add(Section("Santé et lien"));
        health = AddBar(root, "Santé");
        likability = AddBar(root, "Affinité");

        root.Children.Add(Section("Progression"));
        expTrack = new Grid { Height = 6, Margin = new Thickness(0, 2, 0, 6) };
        expTrack.Children.Add(new Border { CornerRadius = new CornerRadius(3), Background = Res("HudStroke") });
        expFill = new Border { CornerRadius = new CornerRadius(3), Background = Res("HudAccent"), HorizontalAlignment = HorizontalAlignment.Left, Width = 0 };
        expTrack.Children.Add(expFill);
        expTrack.SizeChanged += (_, _) => Refresh();
        expText = new TextBlock { FontFamily = (FontFamily)FindResource("HudMono"), FontSize = 11.5, Foreground = Res("HudTextMuted") };
        root.Children.Add(expTrack);
        root.Children.Add(expText);

        moneyText = new TextBlock { FontFamily = (FontFamily)FindResource("HudDisplay"), FontWeight = FontWeights.SemiBold, FontSize = 28, Foreground = Res("HudText"), Margin = new Thickness(0, 14, 0, 0) };
        var moneyLabel = new TextBlock { Text = "ARGENT", Style = (Style)FindResource("HudEyebrow") };
        root.Children.Add(moneyText);
        root.Children.Add(moneyLabel);

        Body = root;
        timer.Tick += (_, _) => Refresh();
        IsVisibleChanged += (_, _) => { if (IsVisible) timer.Start(); else timer.Stop(); };
    }

    protected override void OnOpening() => Refresh();

    private Gauge AddGauge(Panel host, string label)
    {
        var arc = new ArcGauge
        {
            Width = 66, Height = 66, Thickness = 5,
            Track = Res("HudStroke"), Fill = Res("HudSilver"),
            ReserveFill = new SolidColorBrush(((SolidColorBrush)Res("HudSilver")).Color) { Opacity = 0.35 },
        };
        var number = new TextBlock
        {
            FontFamily = (FontFamily)FindResource("HudDisplay"), FontWeight = FontWeights.SemiBold, FontSize = 19,
            Foreground = Res("HudText"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        };
        var dial = new Grid { Width = 66, Height = 66 };
        dial.Children.Add(arc);
        dial.Children.Add(number);
        var rate = new TextBlock { FontFamily = (FontFamily)FindResource("HudMono"), FontSize = 10, Foreground = Res("HudTextMuted"), HorizontalAlignment = HorizontalAlignment.Center };
        var cell = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        cell.Children.Add(dial);
        cell.Children.Add(new TextBlock { Text = label, FontSize = 12.5, Foreground = Res("HudText"), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 2, 0, 0) });
        cell.Children.Add(rate);
        host.Children.Add(cell);
        return new Gauge(arc, number, rate);
    }

    private (Border, TextBlock) AddBar(Panel host, string label)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(78) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) });
        grid.Children.Add(new TextBlock { Text = label, FontSize = 13, Foreground = Res("HudText"), VerticalAlignment = VerticalAlignment.Center });
        var track = new Grid { Height = 6, VerticalAlignment = VerticalAlignment.Center };
        track.Children.Add(new Border { CornerRadius = new CornerRadius(3), Background = Res("HudStroke") });
        var fill = new Border { CornerRadius = new CornerRadius(3), Background = Res("HudSilver"), HorizontalAlignment = HorizontalAlignment.Left, Width = 0 };
        track.Children.Add(fill);
        track.SizeChanged += (_, _) => Refresh();
        Grid.SetColumn(track, 1);
        grid.Children.Add(track);
        var value = new TextBlock { FontFamily = (FontFamily)FindResource("HudMono"), FontSize = 12, Foreground = Res("HudTextMuted"), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(value, 2);
        grid.Children.Add(value);
        host.Children.Add(grid);
        return (fill, value);
    }

    private void Refresh()
    {
        var s = Pet.Core.Save;
        if (s == null)
            return;
        TitleText.Text = s.Name;
        EyebrowText.Text = $"NIVEAU {s.Level}";
        modeNote.Text = !Pet.Core.Controller.EnableFunction ? "Les besoins sont en pause (mode sans gestion)."
            : s.Mode switch
            {
                IGameSave.ModeType.Happy => "En pleine forme.",
                IGameSave.ModeType.Nomal => "Tout va bien.",
                IGameSave.ModeType.PoorCondition => "Pas au mieux : un repas ou un peu d'attention aideraient.",
                IGameSave.ModeType.Ill => "Malade : il faut manger, boire et se reposer.",
                _ => "",
            };
        modeNote.Foreground = Res(s.Mode is IGameSave.ModeType.Ill or IGameSave.ModeType.PoorCondition && Pet.Core.Controller.EnableFunction ? "HudAmber" : "HudTextMuted");

        SetGauge(energy, s.Strength, 0, s.StrengthMax, s.ChangeStrength);
        SetGauge(food, s.StrengthFood, s.StoreStrengthFood, s.StrengthMax, s.ChangeStrengthFood);
        SetGauge(drink, s.StrengthDrink, s.StoreStrengthDrink, s.StrengthMax, s.ChangeStrengthDrink);
        SetGauge(mood, s.Feeling, 0, s.FeelingMax, s.ChangeFeeling);

        SetBar(health, s.Health, 100);
        SetBar(likability, s.Likability, s.LikabilityMax);

        double need = Math.Max(1, s.LevelUpNeed());
        double expRatio = Math.Clamp(s.Exp / need, 0, 1);
        if (expTrack.ActualWidth > 0)
            expFill.Width = expTrack.ActualWidth * expRatio;
        expText.Text = $"{s.Exp.ToString("N0", Fr)} / {need.ToString("N0", Fr)} XP · bonus ×{s.ExpBonus.ToString("0.00", Fr)}";
        moneyText.Text = Pet.Sandbox?.MoneyText(Fr, "N2") ?? s.Money.ToString("N2", Fr) + " $";
    }

    private void SetGauge(Gauge g, double value, double store, double max, double change)
    {
        max = Math.Max(1, max);
        double ratio = value / max;
        g.Arc.Reserve = (value + store) / max;
        if (Math.Abs(g.Arc.Value - ratio) > 0.001)
        {
            var anim = new DoubleAnimation(ratio, TimeSpan.FromMilliseconds(UiMotionMs(400))) { EasingFunction = (IEasingFunction)FindResource("HudEase") };
            g.Arc.BeginAnimation(ArcGauge.ValueProperty, anim);
        }
        bool low = ratio < 0.25;
        g.Arc.Fill = Res(low ? "HudAmber" : "HudSilver");
        g.Number.Text = Math.Round(ratio * 100).ToString(Fr);
        g.Number.Foreground = Res(low ? "HudAmber" : "HudText");
        g.Rate.Text = Math.Abs(change) < 0.005 ? "stable" : (change > 0 ? "+" : "") + change.ToString(Math.Abs(change) > 1 ? "0.0" : "0.00", Fr) + "/t";
    }

    private void SetBar((Border fill, TextBlock value) bar, double value, double max)
    {
        max = Math.Max(1, max);
        double ratio = Math.Clamp(value / max, 0, 1);
        if (bar.fill.Parent is Grid track && track.ActualWidth > 0)
            bar.fill.Width = track.ActualWidth * ratio;
        bar.fill.Background = Res(ratio < 0.25 ? "HudAmber" : "HudSilver");
        bar.value.Text = Math.Round(value).ToString(Fr);
    }

    private static int UiMotionMs(int ms) => VPet_Simulator.Core.UiMotion.Enabled ? ms : 0;
}
