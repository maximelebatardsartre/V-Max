using LinePutScript;
using LinePutScript.Localization.WPF;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using VPet_Simulator.Core;
using VPet_Simulator.Windows.Interface;
using static VPet_Simulator.Windows.Interface.Food;

namespace VPet_Simulator.Windows.HUD;

/// <summary>
/// V-Max HUD : panneau du compagnon (remplace l'ancien winCharacterPanel).
/// Quatre onglets : statistiques en direct, bilan de l'année (affiche à partager), carte d'anniversaire, journal d'activité.
/// La logique (noms des statistiques, calculs du bilan, mise en page de la carte) reprend celle de VPet.
/// </summary>
public sealed class CharacterWindow : HudWindow
{
    public const int TabStats = 0, TabReport = 1, TabBirthday = 2, TabJournal = 3;

    private static CharacterWindow? instance;
    private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

    /// <summary>Ouvre le panneau (une seule instance) sur l'onglet voulu</summary>
    public static CharacterWindow Open(MainWindow mw, int tab = TabStats)
    {
        if (instance == null)
        {
            instance = new CharacterWindow(mw);
            instance.Closed += (_, _) => instance = null;
        }
        instance.Present();
        instance.ShowTab(tab);
        return instance;
    }

    private readonly Statistics stats;

    private CharacterWindow(MainWindow mw)
        : base(mw, "character", string.IsNullOrEmpty(mw.PrefixSave) ? "PANNEAU DU COMPAGNON" : "PANNEAU DU COMPAGNON · " + mw.PrefixSave.Trim(),
              mw.GameSavesData.GameSave.Name, 940, 780)
    {
        stats = mw.GameSavesData.Statistics;
        stats.StatisticChanged += OnStatisticChanged;
        AddTab("Statistiques", BuildStats);
        AddTab("Bilan de l'année", BuildReport);
        AddTab("Carte d'anniversaire", BuildBirthday);
        AddTab("Journal", BuildJournal);
        Closed += (_, _) =>
        {
            stats.StatisticChanged -= OnStatisticChanged;
            MW.ActivityLogs.CollectionChanged -= OnActivityLogsChanged;
        };
    }

    #region Briques
    private FontFamily Display => (FontFamily)FindResource("HudDisplay");
    private FontFamily Mono => (FontFamily)FindResource("HudMono");
    private FontFamily Icons => (FontFamily)FindResource("HudIcons");

    private TextBlock Text(string text, double size = 13.5, string brush = "HudText", bool display = false, FontWeight? weight = null) => new()
    {
        Text = text,
        FontSize = size,
        Foreground = Res(brush),
        FontFamily = display ? Display : (FontFamily)FindResource("HudBody"),
        FontWeight = weight ?? FontWeights.Normal,
        TextWrapping = TextWrapping.Wrap,
    };

    private TextBlock Glyph(string glyph, double size = 14, string brush = "HudTextMuted") => new()
    {
        Text = glyph, FontFamily = Icons, FontSize = size, Foreground = Res(brush), VerticalAlignment = VerticalAlignment.Center,
    };

    /// <summary>Ligne de liste arrondie qui s'éclaire au survol</summary>
    private Border HoverRow(UIElement child)
    {
        var row = new Border { Padding = new Thickness(12, 8, 12, 8), Margin = new Thickness(0, 0, 0, 2), CornerRadius = new CornerRadius(10), Background = Brushes.Transparent, Child = child };
        row.MouseEnter += (_, _) => row.Background = Res("HudSurfaceHover");
        row.MouseLeave += (_, _) => row.Background = Brushes.Transparent;
        return row;
    }

    /// <summary>Champ de saisie dans un cadre arrondi</summary>
    private Border InputFrame(UIElement child) => new()
    {
        CornerRadius = new CornerRadius(12), Background = Res("HudSurfaceRaised"), BorderBrush = Res("HudStroke"),
        BorderThickness = new Thickness(1), Padding = new Thickness(10, 7, 10, 7), Child = child,
    };

    private Button MoreButton(int remaining, Action run)
    {
        var b = Ghost($"Afficher plus ({remaining.ToString("N0", Fr)})", run);
        b.HorizontalAlignment = HorizontalAlignment.Center;
        b.Margin = new Thickness(0, 8, 0, 8);
        return b;
    }

    private static ControlTemplate? switchTemplate;
    private const string SwitchXaml = """
        <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="CheckBox">
            <Grid Background="Transparent">
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="Auto" />
                    <ColumnDefinition />
                </Grid.ColumnDefinitions>
                <Border x:Name="Track" Width="34" Height="18" CornerRadius="9" VerticalAlignment="Center"
                        Background="{DynamicResource HudSurfaceHover}" BorderBrush="{DynamicResource HudStroke}" BorderThickness="1">
                    <Ellipse x:Name="Knob" Width="12" Height="12" Margin="2,0" HorizontalAlignment="Left" Fill="{DynamicResource HudTextMuted}" />
                </Border>
                <ContentPresenter Grid.Column="1" Margin="10,0,0,0" VerticalAlignment="Center" RecognizesAccessKey="False" />
            </Grid>
            <ControlTemplate.Triggers>
                <Trigger Property="IsChecked" Value="True">
                    <Setter TargetName="Track" Property="Background" Value="{DynamicResource HudAccent}" />
                    <Setter TargetName="Track" Property="BorderBrush" Value="{DynamicResource HudAccent}" />
                    <Setter TargetName="Knob" Property="HorizontalAlignment" Value="Right" />
                    <Setter TargetName="Knob" Property="Fill" Value="{DynamicResource HudOnAccent}" />
                </Trigger>
                <Trigger Property="IsEnabled" Value="False">
                    <Setter Property="Opacity" Value="0.45" />
                </Trigger>
            </ControlTemplate.Triggers>
        </ControlTemplate>
        """;

    /// <summary>Interrupteur V-Max (case à cocher en forme de bascule)</summary>
    private CheckBox Switch(string text)
    {
        switchTemplate ??= (ControlTemplate)XamlReader.Parse(SwitchXaml);
        return new CheckBox
        {
            Template = switchTemplate,
            Content = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 13 },
            Foreground = Res("HudText"),
            Cursor = Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Center,
        };
    }

    /// <summary>Enregistre un élément en PNG (rendu net, à l'échelle voulue) après avoir demandé l'emplacement</summary>
    private void SavePng(FrameworkElement el, string defaultName, double scale)
    {
        var dlg = new SaveFileDialog
        {
            FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), defaultName),
            Filter = "Image PNG|*.png",
            DefaultExt = ".png",
            Title = "Enregistrer l'image",
        };
        if (dlg.ShowDialog(this) != true)
            return;
        try
        {
            el.UpdateLayout();
            double w = el.ActualWidth, h = el.ActualHeight;
            if (w <= 0 || h <= 0)
            {
                Notify("L'image n'est pas encore prête, réessaie dans un instant.", HudToast.Kind.Warning);
                return;
            }
            var brush = new VisualBrush(el) { Viewbox = new Rect(0, 0, w, h), ViewboxUnits = BrushMappingMode.Absolute, Stretch = Stretch.Fill };
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
                dc.DrawRectangle(brush, null, new Rect(0, 0, w, h));
            var bmp = new RenderTargetBitmap((int)Math.Ceiling(w * scale), (int)Math.Ceiling(h * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
            bmp.Render(dv);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bmp));
            using (var fs = File.Create(dlg.FileName))
                encoder.Save(fs);
            Notify("Image enregistrée : " + Path.GetFileName(dlg.FileName), HudToast.Kind.Success);
        }
        catch (Exception ex)
        {
            Notify("Impossible d'enregistrer l'image : " + ex.Message, HudToast.Kind.Warning);
        }
    }

    private static string N0(double v) => v.ToString("N0", Fr);
    private static string F1(double v) => v.ToString("0.0", Fr);
    #endregion

    #region Statistiques
    private sealed class StatInfo
    {
        public StatInfo(string id, double value)
        {
            Id = id;
            Name = StatName(id);
            Value = value;
            Group = id.StartsWith("buy_", StringComparison.Ordinal) ? 1 : id.StartsWith("eval_", StringComparison.Ordinal) ? 2 : 0;
        }
        public string Id { get; }
        public string Name { get; }
        public int Group { get; }
        public double Value { get; set; }
        public TextBlock? ValueText { get; set; }
    }

    /// <summary>Nom affiché d'une statistique (même logique que VPet)</summary>
    private static string StatName(string statId)
    {
        if (statId.StartsWith("eval_day_", StringComparison.Ordinal))
            return "eval_day".Translate() + '_' + statId.Substring(9);
        if (statId.StartsWith("eval_month_", StringComparison.Ordinal))
            return "eval_month".Translate() + '_' + statId.Substring(11);
        if (statId.StartsWith("eval_work_project_", StringComparison.Ordinal))
            return "eval_work_project".Translate() + '_' + Uri.UnescapeDataString(statId.Substring(18)).Translate();
        if (statId.StartsWith("eval_study_project_", StringComparison.Ordinal))
            return "eval_study_project".Translate() + '_' + Uri.UnescapeDataString(statId.Substring(19)).Translate();
        if (statId.StartsWith("buy_"))
            return "购买次数".Translate() + '_' + statId.Substring(4).Translate();
        if (statId.StartsWith("stat_"))
            return "统计".Translate() + '_' + statId.Substring(5).Translate();
        return statId.Translate();
    }

    private static string StatValue(double v) => Math.Round(v, 2).ToString("#,##0.##", Fr);

    private static readonly string[] CardKeys = ["stat_total_time", "stat_open_times", "stat_touch_head", "stat_touch_body", "stat_move_length"];
    private static readonly string[] StatGroups = ["Tout", "Général", "Achats", "Évaluations"];
    private const int StatPage = 150;

    private readonly Dictionary<string, StatInfo> statMap = new();
    private List<StatInfo> statFiltered = new();
    private StackPanel? statRows;
    private TextBox? statSearch;
    private TextBlock? statCount;
    private int statShown, statGroup = -1;
    private (TextBlock value, TextBlock sub) cardTime, cardSessions, cardTouches, cardDistance;

    private List<KeyValuePair<string, SetObject?>> StatSnapshot()
    {
        for (int i = 0; i < 3; i++)
        {
            try { return stats.Data.ToList(); }
            catch (InvalidOperationException) { } // modifiée pendant la lecture : on réessaie
        }
        return new();
    }

    private FrameworkElement BuildStats()
    {
        statMap.Clear();
        foreach (var v in StatSnapshot())
            statMap[v.Key] = new StatInfo(v.Key, v.Value?.GetDouble() ?? 0);

        var cards = new UniformGrid { Columns = 4, Margin = new Thickness(18, 2, 18, 0) };
        cardTime = HeadCard(cards, "", "TEMPS PASSÉ");
        cardSessions = HeadCard(cards, "", "SESSIONS");
        cardTouches = HeadCard(cards, "", "CARESSES");
        cardDistance = HeadCard(cards, "", "DISTANCE PARCOURUE");
        RefreshCards();

        statSearch = SearchBox("Chercher une statistique (nom ou identifiant)…", out var searchFrame);
        statSearch.TextChanged += (_, _) => FillStats();
        searchFrame.Width = 300;
        searchFrame.Margin = new Thickness(0, 0, 12, 0);
        var chips = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
        for (int i = 0; i < StatGroups.Length; i++)
        {
            int group = i - 1;
            var chip = new RadioButton { Style = St("HudChip"), Content = StatGroups[i], GroupName = "stat-groups", Margin = new Thickness(0, 3, 6, 3), IsChecked = group == statGroup };
            chip.Checked += (_, _) => { statGroup = group; FillStats(); };
            chips.Children.Add(chip);
        }
        statCount = new TextBlock { FontFamily = Mono, FontSize = 11.5, Foreground = Res("HudTextMuted"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
        var toolbar = new DockPanel { Margin = new Thickness(24, 16, 24, 8) };
        DockPanel.SetDock(searchFrame, Dock.Left);
        DockPanel.SetDock(statCount, Dock.Right);
        toolbar.Children.Add(searchFrame);
        toolbar.Children.Add(statCount);
        toolbar.Children.Add(chips);

        statRows = new StackPanel();
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.Children.Add(cards);
        Grid.SetRow(toolbar, 1);
        root.Children.Add(toolbar);
        var scroll = Scroll(statRows, new Thickness(12, 0, 16, 16));
        Grid.SetRow(scroll, 2);
        root.Children.Add(scroll);
        FillStats();
        return root;
    }

    private (TextBlock, TextBlock) HeadCard(Panel host, string glyph, string label)
    {
        var top = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        var icon = Glyph(glyph, 13, "HudAccent");
        icon.Margin = new Thickness(0, 0, 7, 0);
        DockPanel.SetDock(icon, Dock.Left);
        top.Children.Add(icon);
        top.Children.Add(new TextBlock { Text = label, Style = St("HudEyebrow"), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
        var value = new TextBlock { FontFamily = Display, FontWeight = FontWeights.SemiBold, FontSize = 26, Foreground = Res("HudText"), TextTrimming = TextTrimming.CharacterEllipsis };
        var sub = new TextBlock { FontSize = 11.5, Foreground = Res("HudTextMuted"), TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 2, 0, 0) };
        var stack = new StackPanel();
        stack.Children.Add(top);
        stack.Children.Add(value);
        stack.Children.Add(sub);
        var card = Card(stack, new Thickness(16, 12, 14, 12));
        card.Margin = new Thickness(6);
        host.Children.Add(card);
        return (value, sub);
    }

    private void RefreshCards()
    {
        if (cardTime.value is null)
            return;
        double hours = stats[(gi64)"stat_total_time"] / 3600.0;
        cardTime.value.Text = (hours >= 100 ? N0(hours) : F1(hours)) + " h";
        var birthday = MW.GameSavesData[(gdat)"birthday"];
        double days = birthday == default ? 0 : (DateTime.Now - birthday).TotalDays;
        cardTime.sub.Text = days >= 1 ? $"soit {F1(hours / days)} h par jour en moyenne" : "depuis votre rencontre";

        cardSessions.value.Text = N0(stats[(gint)"stat_open_times"]);
        cardSessions.sub.Text = "ouvertures de V-Max";

        int head = stats[(gint)"stat_touch_head"], body = stats[(gint)"stat_touch_body"];
        cardTouches.value.Text = N0(head + body);
        cardTouches.sub.Text = $"{N0(head)} sur la tête · {N0(body)} sur le corps";

        cardDistance.value.Text = Units.PxToDistance(stats[(gi64)"stat_move_length"]);
        cardDistance.sub.Text = "en balade sur ton bureau";
    }

    private void FillStats()
    {
        if (statRows == null)
            return;
        var q = statSearch?.Text.Trim() ?? "";
        foreach (var s in statMap.Values)
            s.ValueText = null;
        statFiltered = statMap.Values
            .Where(s => statGroup < 0 || s.Group == statGroup)
            .Where(s => q.Length == 0 || s.Name.Contains(q, StringComparison.CurrentCultureIgnoreCase) || s.Id.Contains(q, StringComparison.OrdinalIgnoreCase))
            .OrderBy(s => s.Group)
            .ThenBy(s => s.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        statRows.Children.Clear();
        statShown = 0;
        if (statCount != null)
            statCount.Text = statFiltered.Count == statMap.Count ? $"{N0(statMap.Count)} statistiques" : $"{N0(statFiltered.Count)} sur {N0(statMap.Count)}";
        if (statFiltered.Count == 0)
        {
            statRows.Children.Add(Empty(statMap.Count == 0 ? "Aucune statistique pour l'instant : elles se rempliront au fil de vos journées." : "Rien ne correspond à cette recherche."));
            return;
        }
        MoreStats();
    }

    private void MoreStats()
    {
        if (statRows == null)
            return;
        if (statRows.Children.Count > 0 && statRows.Children[^1] is Button more)
            statRows.Children.Remove(more);
        foreach (var s in statFiltered.Skip(statShown).Take(StatPage))
            statRows.Children.Add(StatRow(s));
        statShown = Math.Min(statFiltered.Count, statShown + StatPage);
        if (statShown < statFiltered.Count)
            statRows.Children.Add(MoreButton(statFiltered.Count - statShown, MoreStats));
    }

    private FrameworkElement StatRow(StatInfo s)
    {
        var name = new TextBlock { Text = s.Name, FontSize = 13.5, Foreground = Res("HudText"), TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        var value = new TextBlock { Text = StatValue(s.Value), FontFamily = Display, FontWeight = FontWeights.SemiBold, FontSize = 14.5, Foreground = Res("HudText"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 0, 0) };
        s.ValueText = value;
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(name);
        Grid.SetColumn(value, 1);
        grid.Children.Add(value);
        var row = HoverRow(grid);
        row.ToolTip = s.Id;
        return row;
    }

    private void OnStatisticChanged(Statistics sender, string name, SetObject? value)
    {
        if (value == null)
            return;
        double v;
        try { v = value.GetDouble(); }
        catch { return; }
        Dispatcher.BeginInvoke(() =>
        {
            try
            {
                if (statRows == null)
                    return; // onglet pas encore construit : il lira les valeurs à jour
                if (statMap.TryGetValue(name, out var s))
                {
                    s.Value = v;
                    if (s.ValueText != null)
                        s.ValueText.Text = StatValue(v);
                }
                else
                {
                    statMap[name] = new StatInfo(name, v);
                    FillStats();
                }
                if (Array.IndexOf(CardKeys, name) >= 0)
                    RefreshCards();
            }
            catch { }
        });
    }
    #endregion

    #region Bilan de l'année
    private sealed class Report
    {
        public string Pet = "", User = "";
        public int Year;
        public DateTime Now, Birthday;
        public double Days, Hours, PerDay;
        public string TimeTier = "", TimeQuip = "";
        public int Level;
        public double TotalExp;
        public int StudyMinutes, BestExp, BestMoney;
        public string LevelTier = "", StudyQuip = "";
        public int WorkMinutes;
        public double WorkShare;
        public string WorkQuip = "";
        public int Purchases, Spent;
        public string? FavItem;
        public FoodType FavType;
        public string FoodQuip = "";
        public int AutoBuy;
        public double AutoShare;
        public string AutoQuip = "";
        public int Mods, ModsOn;
        public string ModQuip = "";
        public double SleepHours;
        public string Distance = "";
        public int Says, Music, Touches, Opens, FullState;
        public double Likability;
    }

    private Button? reportGenerate, reportSave;
    private CheckBox? noCheat;
    private ContentControl? reportHost;
    private FrameworkElement? poster, posterBadge;

    private FrameworkElement BuildReport()
    {
        reportGenerate = Primary("Générer mon bilan", GenerateReport);
        reportGenerate.Margin = new Thickness(0, 0, 8, 0);
        reportSave = Ghost("Enregistrer l'image…", () => { if (poster != null) SavePng(poster, "V-Max_Bilan.png", 2); });
        reportSave.IsEnabled = false;
        noCheat = Switch("Je n'ai jamais triché (pas de mod de triche ni de données modifiées)");
        noCheat.IsEnabled = MW.GameSavesData.HashCheck;
        noCheat.Margin = new Thickness(18, 0, 0, 0);
        noCheat.MaxWidth = 380;
        if (!noCheat.IsEnabled)
        {
            noCheat.ToolTip = "Indisponible : la sauvegarde a été modifiée ou ne peut pas être vérifiée.";
            ToolTipService.SetShowOnDisabled(noCheat, true);
        }
        noCheat.Checked += (_, _) => UpdateBadge();
        noCheat.Unchecked += (_, _) => UpdateBadge();

        var toolbar = new WrapPanel { Margin = new Thickness(24, 4, 24, 12) };
        toolbar.Children.Add(reportGenerate);
        toolbar.Children.Add(reportSave);
        toolbar.Children.Add(noCheat);

        var intro = new StackPanel { MaxWidth = 520, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 30, 0, 0) };
        var spark = Glyph("", 30, "HudAccent");
        spark.HorizontalAlignment = HorizontalAlignment.Center;
        intro.Children.Add(spark);
        var t = Text($"Ton année avec {MW.GameSavesData.GameSave.Name}", 22, "HudText", true, FontWeights.SemiBold);
        t.HorizontalAlignment = HorizontalAlignment.Center;
        t.TextAlignment = TextAlignment.Center;
        t.Margin = new Thickness(0, 12, 0, 6);
        intro.Children.Add(t);
        var d = Text("Une affiche qui résume tout : le temps passé ensemble, les études, le travail, les achats, les câlins… Génère-la, puis enregistre-la en image pour la garder ou la partager.", 13.5, "HudTextMuted");
        d.TextAlignment = TextAlignment.Center;
        intro.Children.Add(d);
        reportHost = new ContentControl { Content = intro };

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.Children.Add(toolbar);
        var scroll = Scroll(reportHost);
        Grid.SetRow(scroll, 1);
        root.Children.Add(scroll);
        return root;
    }

    private void UpdateBadge()
    {
        if (posterBadge != null)
            posterBadge.Visibility = noCheat?.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
    }

    private void GenerateReport()
    {
        Report r;
        try
        {
            MW.Set["v"][(gint)"rank"] = DateTime.Now.Year;
            r = ComputeReport();
        }
        catch (Exception ex)
        {
            Notify("Impossible de préparer le bilan : " + ex.Message, HudToast.Kind.Warning);
            return;
        }
        poster = BuildPoster(r);
        UpdateBadge();
        if (reportHost != null)
        {
            reportHost.Content = new Viewbox { Child = poster, Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, Margin = new Thickness(0, 0, 0, 8) };
            UiMotion.PopIn(poster, new Point(0.5, 0.1), 0.98, 260);
        }
        if (reportSave != null)
            reportSave.IsEnabled = true;
        if (reportGenerate != null)
            reportGenerate.Content = "Actualiser le bilan";
    }

    /// <summary>Mêmes valeurs que GenRank de VPet (paliers compris)</summary>
    private Report ComputeReport()
    {
        var save = MW.GameSavesData.GameSave;
        var r = new Report
        {
            Pet = save.Name,
            User = Environment.UserName,
            Now = DateTime.Now,
            Year = DateTime.Now.Year,
            Birthday = MW.GameSavesData[(gdat)"birthday"],
        };

        int timelength = stats[(gint)"stat_total_time"];
        r.Hours = timelength / 3600.0;
        r.Days = r.Birthday == default ? 0 : Math.Max(0, (r.Now - r.Birthday).TotalDays);
        r.PerDay = r.Hours / Math.Max(1, r.Days);
        (r.TimeTier, r.TimeQuip) = r.PerDay switch
        {
            < 2 => ("un camarade de classe", "« On se croise entre deux cours. »"),
            < 4 => ("un ami", "« Toujours partant pour traîner ensemble. »"),
            < 7 => ("un meilleur ami", "« Même jour, même heure, même bureau. »"),
            < 10 => ("la famille", "« On fait partie des meubles, maintenant. »"),
            _ => ("ta propre ombre", "« À ce stade, on vit carrément ensemble. »"),
        };

        r.Level = save.Level;
        r.TotalExp = save.TotalExpGained();
        r.StudyMinutes = stats[(gint)"stat_study_time"] / 60;
        (r.LevelTier, r.StudyQuip) = r.Level switch
        {
            < 20 => ("niveau école primaire", "« On apprend à lire, tranquillement. »"),
            < 40 => ("niveau lycée", "« Le bac approche, on révise ! »"),
            < 60 => ("niveau fac", "« Étudier, manger, dormir : la vie étudiante. »"),
            < 80 => ("niveau doctorat", "« Ma thèse avance, promis. »"),
            _ => ("niveau expert", "« Plus rien ne m'échappe. »"),
        };
        r.BestExp = stats[(gint)"stat_single_profit_exp"];
        r.BestMoney = stats[(gint)"stat_single_profit_money"];

        r.WorkMinutes = stats[(gint)"stat_work_time"] / 60;
        r.WorkShare = timelength > 0 ? (double)stats[(gint)"stat_work_time"] / timelength : 0;
        r.WorkQuip = r.WorkShare switch
        {
            < 0.25 => "« Un jour je bosse, un jour je me repose. »",
            < 0.55 => "« Horaires de bureau, puis retour à la maison. »",
            < 0.75 => "« Les heures sup', ça me connaît. »",
            _ => "« Patron, on rentre quand ? »",
        };

        r.Purchases = stats[(gint)"stat_buytimes"];
        r.Spent = (int)stats[(gdbe)"stat_betterbuy"];
        foreach (var pair in StatSnapshot().Where(x => x.Key.StartsWith("buy_")).OrderByDescending(x => x.Value?.GetInteger() ?? 0))
        {
            var fn = pair.Key.Substring(4);
            var f = MW.Foods.FirstOrDefault(x => x.Name == fn);
            if (f != null)
            {
                r.FavItem = f.TranslateName;
                r.FavType = f.Type;
                break;
            }
        }
        r.FoodQuip = r.FavItem == null ? $"Rien acheté : {r.Pet} a un petit creux." : r.FavType switch
        {
            FoodType.Meal => "« Un vrai repas, ça ne se refuse pas. »",
            FoodType.Drug => "« Tu as encore oublié l'achat automatique ? »",
            FoodType.Drink => "« Bien s'hydrater, c'est la base. »",
            FoodType.Functional => "« Le fonctionnel, meilleur rapport qualité-prix. »",
            FoodType.Snack => "« Grignoter, c'est bon pour le moral. »",
            FoodType.Gift => "« Autant de cadeaux… tu me gâtes. »",
            _ => "« Manger, c'est important. »",
        };

        r.AutoBuy = stats[(gint)"stat_autobuy"];
        r.AutoShare = r.Purchases > 0 ? (double)r.AutoBuy / r.Purchases : 0;
        r.AutoQuip = r.AutoShare switch
        {
            < 0.25 => "« Tu préfères choisir toi-même, hein ? »",
            < 0.5 => "« Je gère une partie des courses. »",
            < 0.75 => "« Je fais presque toutes les courses. »",
            _ => "« C'est moi qui tiens la maison, maintenant. »",
        };

        var workshop = MW.CoreMODs.FindAll(x => x.Path.FullName.Contains("workshop"));
        r.Mods = workshop.Count;
        r.ModsOn = workshop.FindAll(x => x.IsOnMOD(MW)).Count;
        r.ModQuip = r.Mods == 0 ? "« Le Workshop regorge de mods, va jeter un œil ! »"
            : r.ModsOn == r.Mods ? "« Tous activés : tu es un vrai collectionneur. »"
            : "« Il en reste à essayer, non ? »";

        r.SleepHours = stats[(gint)"stat_sleep_time"] / 3600.0;
        r.Distance = Units.PxToDistance(stats[(gi64)"stat_move_length"]);
        r.Says = stats[(gint)"stat_say_times"];
        r.Music = stats[(gint)"stat_music"];
        r.Touches = stats[(gint)"stat_touch_body"] + stats[(gint)"stat_touch_head"];
        r.Opens = stats[(gint)"stat_open_times"];
        r.FullState = stats[(gint)"stat_100_all"];
        r.Likability = save.Likability;
        return r;
    }

    // Palette fixe de l'affiche : elle reste identique quel que soit le thème (l'image exportée ne change pas)
    private static SolidColorBrush P(uint argb)
    {
        var b = new SolidColorBrush(Color.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb));
        b.Freeze();
        return b;
    }
    private static readonly SolidColorBrush PText = P(0xFFF3F1F6), PMuted = P(0xA6C9CFDA), PSilver = P(0xFFC9CFDA), PAccent = P(0xFFE2455B),
        PAccentSoft = P(0x33E2455B), PCard = P(0xB31C1E28), PStroke = P(0x26C9CFDA), PSuccess = P(0xFF4CC38A);

    private TextBlock PT(string text, double size, Brush fg, bool display = false, FontWeight? weight = null, bool italic = false) => new()
    {
        Text = text,
        FontSize = size,
        Foreground = fg,
        FontFamily = display ? Display : (FontFamily)FindResource("HudBody"),
        FontWeight = weight ?? FontWeights.Normal,
        FontStyle = italic ? FontStyles.Italic : FontStyles.Normal,
        TextWrapping = TextWrapping.Wrap,
    };

    private FrameworkElement BuildPoster(Report r)
    {
        var content = new StackPanel { Margin = new Thickness(44, 40, 44, 34) };

        // en-tête
        content.Children.Add(PT($"V-MAX · BILAN {r.Year}", 12, PAccent, true, FontWeights.SemiBold));
        content.Children.Add(PT($"{r.Pet} & {r.User}", 40, PText, true, FontWeights.SemiBold));
        var since = r.Birthday == default ? "Votre histoire commence à peine."
            : $"Ensemble depuis le {r.Birthday.ToString("d MMMM yyyy", Fr)} — {N0(Math.Floor(r.Days))} jours.";
        var sub = PT(since, 15, PSilver);
        sub.Margin = new Thickness(0, 4, 0, 0);
        content.Children.Add(sub);
        content.Children.Add(new Border { Height = 3, Width = 48, CornerRadius = new CornerRadius(2), Background = PAccent, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 18, 0, 22) });

        // chiffres phares
        var hero = new UniformGrid { Columns = 3 };
        hero.Children.Add(HeroStat(r.Hours >= 100 ? N0(r.Hours) : F1(r.Hours), "heures passées ensemble"));
        hero.Children.Add(HeroStat(F1(r.PerDay), "heures par jour en moyenne"));
        hero.Children.Add(HeroStat(r.Level.ToString(Fr), "niveau atteint"));
        content.Children.Add(hero);
        var tier = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 15, Foreground = PSilver, Margin = new Thickness(0, 14, 0, 20), FontFamily = (FontFamily)FindResource("HudBody") };
        tier.Inlines.Add(new Run("Autant de temps qu'avec "));
        tier.Inlines.Add(new Run(r.TimeTier) { Foreground = PText, FontWeight = FontWeights.SemiBold });
        tier.Inlines.Add(new Run(".  "));
        tier.Inlines.Add(new Run(r.TimeQuip) { FontStyle = FontStyles.Italic, Foreground = PMuted });
        content.Children.Add(tier);

        // sections
        var grid = new UniformGrid { Columns = 2, Margin = new Thickness(-6, 0, -6, 0) };
        grid.Children.Add(PosterCard("ÉTUDES", N0(r.TotalExp), "XP gagnée",
            [$"Niveau {r.Level} · {r.LevelTier}", $"{N0(r.StudyMinutes)} min d'étude", $"Meilleure séance : {N0(r.BestExp)} XP"], r.StudyQuip));
        grid.Children.Add(PosterCard("TRAVAIL", N0(r.WorkMinutes), "min de travail",
            [$"{r.WorkShare.ToString("P1", Fr)} de votre temps", $"Meilleure paie : {N0(r.BestMoney)} $"], r.WorkQuip));
        grid.Children.Add(PosterCard("ACHATS", N0(r.Purchases), "achats",
            [$"{N0(r.Spent)} $ dépensés", r.FavItem == null ? "Pas encore d'article favori" : $"Favori : {r.FavItem} ({TypeName(r.FavType)})"], r.FoodQuip));
        grid.Children.Add(PosterCard("ACHATS AUTOMATIQUES", N0(r.AutoBuy), "achats auto",
            [$"{r.AutoShare.ToString("P1", Fr)} de tous les achats"], r.AutoQuip));
        grid.Children.Add(PosterCard("MODS DU WORKSHOP", r.Mods.ToString(Fr), r.Mods > 1 ? "mods" : "mod",
            [$"{r.ModsOn} activé{(r.ModsOn > 1 ? "s" : "")}"], r.ModQuip));
        grid.Children.Add(PosterCard("AU QUOTIDIEN", F1(r.SleepHours), "h de sommeil",
            [$"{r.Distance} parcourus", $"{N0(r.Says)} phrases · {N0(r.Music)} danses", $"{N0(r.Touches)} caresses"], "« Manger, jouer, dormir : la belle vie. »"));
        content.Children.Add(grid);

        // affection
        content.Children.Add(AffectionBlock(r));

        // signature
        var footer = new Grid { Margin = new Thickness(0, 22, 0, 0) };
        footer.ColumnDefinitions.Add(new ColumnDefinition());
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var sign = new TextBlock { FontFamily = Display, FontSize = 15, Foreground = PSilver, LineHeight = 22 };
        sign.Inlines.Add(new Run("Pour ") { Foreground = PMuted });
        sign.Inlines.Add(new Run(r.Pet) { Foreground = PText, FontWeight = FontWeights.SemiBold });
        sign.Inlines.Add(new LineBreak());
        sign.Inlines.Add(new Run("De ") { Foreground = PMuted });
        sign.Inlines.Add(new Run(r.User) { Foreground = PText, FontWeight = FontWeights.SemiBold });
        footer.Children.Add(sign);
        var right = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom };
        var badgeRow = new StackPanel { Orientation = Orientation.Horizontal };
        badgeRow.Children.Add(new TextBlock { Text = "", FontFamily = Icons, FontSize = 11, Foreground = PSuccess, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
        badgeRow.Children.Add(new TextBlock { Text = "Partie sans triche", FontFamily = Display, FontWeight = FontWeights.SemiBold, FontSize = 11.5, Foreground = PSuccess, VerticalAlignment = VerticalAlignment.Center });
        posterBadge = new Border
        {
            Child = badgeRow, CornerRadius = new CornerRadius(10), Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(0, 0, 0, 8),
            BorderBrush = PSuccess, BorderThickness = new Thickness(1), HorizontalAlignment = HorizontalAlignment.Right,
        };
        right.Children.Add(posterBadge);
        var stamp = PT($"V-Max · {r.Now.ToString("d", Fr)}", 12, PMuted, true);
        stamp.HorizontalAlignment = HorizontalAlignment.Right;
        right.Children.Add(stamp);
        Grid.SetColumn(right, 1);
        footer.Children.Add(right);
        content.Children.Add(footer);

        // fond : encre profonde + halo rubis discret en haut à droite
        var glow = new Border
        {
            CornerRadius = new CornerRadius(28),
            Background = new RadialGradientBrush
            {
                Center = new Point(0.9, 0.0), GradientOrigin = new Point(0.9, 0.0), RadiusX = 0.75, RadiusY = 0.4,
                GradientStops = { new GradientStop(Color.FromArgb(0x40, 0xE2, 0x45, 0x5B), 0), new GradientStop(Color.FromArgb(0, 0xE2, 0x45, 0x5B), 1) },
            },
            Child = content,
        };
        return new Border
        {
            Width = 720,
            MinHeight = 1000,
            CornerRadius = new CornerRadius(28),
            BorderBrush = PStroke,
            BorderThickness = new Thickness(1),
            Background = new LinearGradientBrush(Color.FromRgb(0x1D, 0x1C, 0x29), Color.FromRgb(0x10, 0x11, 0x17), 90),
            Child = glow,
            SnapsToDevicePixels = true,
        };
    }

    private FrameworkElement HeroStat(string number, string label)
    {
        var s = new StackPanel();
        s.Children.Add(PT(number, 54, PText, true, FontWeights.Light));
        var l = PT(label, 13, PMuted);
        l.Margin = new Thickness(2, -4, 8, 0);
        s.Children.Add(l);
        return s;
    }

    private FrameworkElement PosterCard(string eyebrow, string big, string unit, string[] lines, string quip)
    {
        var s = new StackPanel();
        s.Children.Add(PT(eyebrow, 11, PMuted, true, FontWeights.SemiBold));
        var number = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 6) };
        number.Inlines.Add(new Run(big) { FontFamily = Display, FontWeight = FontWeights.SemiBold, FontSize = 32, Foreground = PText });
        number.Inlines.Add(new Run("  " + unit) { FontSize = 13, Foreground = PSilver, FontFamily = (FontFamily)FindResource("HudBody") });
        s.Children.Add(number);
        foreach (var line in lines)
        {
            var t = PT(line, 13, PSilver);
            t.Margin = new Thickness(0, 1, 0, 1);
            s.Children.Add(t);
        }
        var q = PT(quip, 12.5, PMuted, italic: true);
        q.Margin = new Thickness(0, 8, 0, 0);
        s.Children.Add(q);
        return new Border
        {
            Child = s, CornerRadius = new CornerRadius(18), Background = PCard, BorderBrush = PStroke, BorderThickness = new Thickness(1),
            Padding = new Thickness(18, 14, 18, 16), Margin = new Thickness(6),
        };
    }

    private FrameworkElement AffectionBlock(Report r)
    {
        // cœurs : un plein par tranche de 100 d'affinité, un contour pour la demi-tranche restante (comme VPet)
        int like = (int)r.Likability, full = 0, half = 0;
        while (like > 100) { like -= 100; full++; }
        while (like > 50) { like -= 50; half++; }
        var hearts = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 6) };
        void Heart(string g, Brush b) => hearts.Children.Add(new TextBlock { Text = g, FontFamily = Icons, FontSize = 22, Foreground = b, Margin = new Thickness(0, 0, 6, 0) });
        for (int i = 0; i < Math.Min(full, 10); i++)
            Heart("", PAccent);
        if (full > 10)
            hearts.Children.Add(PT("+" + (full - 10), 16, PAccent, true, FontWeights.SemiBold));
        for (int i = 0; i < half; i++)
            Heart("", PAccent);
        if (full == 0 && half == 0)
            Heart("", PMuted);

        var left = new StackPanel();
        left.Children.Add(PT("AFFECTION", 11, PMuted, true, FontWeights.SemiBold));
        left.Children.Add(hearts);
        left.Children.Add(PT($"{r.Pet} t'affectionne à {N0(r.Likability)} points.", 13.5, PSilver));
        var quote = PT("« Je t'adore. On remet ça l'année prochaine ? »", 15, PText, true, italic: false);
        quote.Margin = new Thickness(0, 12, 0, 0);
        left.Children.Add(quote);

        var right = new StackPanel { Margin = new Thickness(24, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        right.Children.Add(MiniStat(N0(r.Opens), "sessions ouvertes"));
        var full100 = MiniStat(N0(r.FullState), "fois en pleine forme");
        full100.Margin = new Thickness(0, 12, 0, 0);
        right.Children.Add(full100);

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(left);
        Grid.SetColumn(right, 1);
        grid.Children.Add(right);
        return new Border
        {
            Child = grid, CornerRadius = new CornerRadius(18), Background = PAccentSoft, BorderBrush = P(0x66E2455B), BorderThickness = new Thickness(1),
            Padding = new Thickness(20, 16, 22, 18), Margin = new Thickness(0, 10, 0, 0),
        };
    }

    private FrameworkElement MiniStat(string number, string label)
    {
        var s = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right };
        var n = PT(number, 28, PText, true, FontWeights.SemiBold);
        n.HorizontalAlignment = HorizontalAlignment.Right;
        s.Children.Add(n);
        var l = PT(label, 12, PMuted);
        l.HorizontalAlignment = HorizontalAlignment.Right;
        s.Children.Add(l);
        return s;
    }

    private static string TypeName(FoodType t) => t switch
    {
        FoodType.Meal => "repas",
        FoodType.Snack => "en-cas",
        FoodType.Drink => "boisson",
        FoodType.Functional => "fonctionnel",
        FoodType.Drug => "soin",
        FoodType.Gift => "cadeau",
        FoodType.Star => "favori",
        _ => "aliment",
    };
    #endregion

    #region Carte d'anniversaire
    private List<(string name, PetLoader loader)>? bdPets;
    private int bdIndex;
    private string bdDefaultText = "";
    private DateTime bdDate = DateTime.Today;
    private Grid? bdCard;
    private Image? bdBackground, bdAvatar;
    private Border? bdLabel, bdTextBorder;
    private TextBlock? bdLabelText, bdText;
    private Viewbox? bdTextBox;
    private CheckBox? bdCustom;
    private TextBox? bdCustomText;

    private const string DefaultAvatar = "pack://application:,,,/Res/player.png";

    private FrameworkElement BuildBirthday()
    {
        // mêmes compagnons et mêmes noms que VPet : ceux qui déclarent une ligne « bday » dans leur configuration
        var petloader = MW.Pets.Find(x => x.Name == MW.Set.PetGraph) ?? MW.Pets.FirstOrDefault();
        var ordname = petloader?.PetName.Translate();
        bdPets = MW.Pets.FindAll(x => x.Config?.Data?.FindLine("bday") != null).Select(x => (x.PetName.Translate(), x)).ToList();
        if (bdPets.Count == 0)
            return Empty("Aucun compagnon installé n'a de carte d'anniversaire pour l'instant.");
        bdIndex = 0;
        for (int i = 0; i < bdPets.Count; i++)
        {
            if (bdPets[i].name == ordname)
            {
                if (petloader == bdPets[i].loader)
                {
                    bdIndex = i;
                    bdPets[i] = (MW.GameSavesData.GameSave.Name, bdPets[i].loader);
                }
                else
                    bdPets[i] = (MW.GameSavesData.GameSave.Name + $" ({bdPets[i].loader.Name.Translate()})", bdPets[i].loader);
            }
        }

        // carte (300 × 200, comme VPet) ; exportée à ×4
        bdAvatar = new Image { Width = 62, Margin = new Thickness(74, 130, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Source = new BitmapImage(new Uri(DefaultAvatar)) };
        bdBackground = new Image { Stretch = Stretch.UniformToFill };
        bdLabelText = new TextBlock { Foreground = Brushes.White, FontSize = 10 };
        bdLabel = new Border { Background = new SolidColorBrush(Color.FromArgb(0xA0, 0, 0, 0)), Padding = new Thickness(2, 1, 2, 1), Child = bdLabelText, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top };
        bdText = new TextBlock { FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.WrapWithOverflow };
        bdTextBorder = new Border { CornerRadius = new CornerRadius(5), Padding = new Thickness(3), MaxWidth = 300, Child = bdText, Background = new SolidColorBrush(Color.FromArgb(0xDD, 0xF9, 0xC0, 0x8A)) };
        bdTextBox = new Viewbox { Margin = new Thickness(140, 100, 5, 10), Child = bdTextBorder };
        bdCard = new Grid { Width = 300, Height = 200, ClipToBounds = true, Background = Brushes.White };
        RenderOptions.SetBitmapScalingMode(bdCard, BitmapScalingMode.HighQuality);
        bdCard.Children.Add(bdAvatar);
        bdCard.Children.Add(bdBackground);
        bdCard.Children.Add(bdLabel);
        bdCard.Children.Add(bdTextBox);

        var preview = new Border
        {
            CornerRadius = new CornerRadius(14), Background = Res("HudSurfaceRaised"), BorderBrush = Res("HudStroke"), BorderThickness = new Thickness(1),
            Padding = new Thickness(14), VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(24, 4, 12, 20),
            Child = new Viewbox { Child = bdCard, Stretch = Stretch.Uniform },
        };

        // réglages
        var side = new StackPanel();
        side.Children.Add(Section("De la part de"));
        if (bdPets.Count > 1)
        {
            var chips = new WrapPanel();
            for (int i = 0; i < bdPets.Count; i++)
            {
                int index = i;
                var chip = new RadioButton { Style = St("HudChip"), Content = bdPets[i].name, GroupName = "bday-pets", Margin = new Thickness(0, 0, 6, 6), IsChecked = i == bdIndex };
                chip.Checked += (_, _) => { bdIndex = index; LoadBirthday(); };
                chips.Children.Add(chip);
            }
            side.Children.Add(chips);
        }
        else
            side.Children.Add(Text(bdPets[0].name, 15, "HudText", true, FontWeights.SemiBold));

        side.Children.Add(Section("Message"));
        bdCustom = Switch("Écrire mon propre message");
        bdCustomText = new TextBox { Style = St("HudInput"), Tag = "Ton message d'anniversaire…", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 64, VerticalContentAlignment = VerticalAlignment.Top };
        bdCustomText.TextChanged += (_, _) => UpdateBirthdayText();
        var customFrame = InputFrame(bdCustomText);
        customFrame.Margin = new Thickness(0, 10, 0, 0);
        customFrame.Visibility = Visibility.Collapsed;
        bdCustom.Checked += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(bdCustomText.Text))
                bdCustomText.Text = bdDefaultText;
            customFrame.Visibility = Visibility.Visible;
            UpdateBirthdayText();
            bdCustomText.Focus();
            bdCustomText.CaretIndex = bdCustomText.Text.Length;
        };
        bdCustom.Unchecked += (_, _) => { customFrame.Visibility = Visibility.Collapsed; UpdateBirthdayText(); };
        side.Children.Add(bdCustom);
        side.Children.Add(customFrame);

        side.Children.Add(Section("Date sur la photo"));
        side.Children.Add(DateField());

        side.Children.Add(Section("Photo de l'invité"));
        var photoRow = new WrapPanel();
        var pick = Ghost("Choisir une image…", PickAvatar);
        pick.Margin = new Thickness(0, 0, 8, 6);
        var reset = Ghost("Par défaut", () => bdAvatar!.Source = new BitmapImage(new Uri(DefaultAvatar)));
        reset.Margin = new Thickness(0, 0, 0, 6);
        photoRow.Children.Add(pick);
        photoRow.Children.Add(reset);
        side.Children.Add(photoRow);

        var save = Primary("Enregistrer l'image…", () => { if (bdCard != null) SavePng(bdCard, "V-Max_Anniversaire.png", 4); });
        save.HorizontalAlignment = HorizontalAlignment.Left;
        save.Margin = new Thickness(0, 24, 0, 0);
        side.Children.Add(save);

        var root = new Grid();
        root.ColumnDefinitions.Add(new ColumnDefinition());
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300) });
        root.Children.Add(preview);
        var sideScroll = Scroll(side, new Thickness(12, 0, 24, 20));
        Grid.SetColumn(sideScroll, 1);
        root.Children.Add(sideScroll);

        SetBirthdayDate(DateTime.Today);
        LoadBirthday();
        return root;
    }

    private FrameworkElement DateField()
    {
        var box = new TextBox { Style = St("HudInput"), Tag = "jj/mm/aaaa", Text = DateTime.Today.ToString("d", Fr) };
        var frame = InputFrame(box);
        box.TextChanged += (_, _) =>
        {
            bool ok = DateTime.TryParse(box.Text, Fr, DateTimeStyles.None, out var d);
            frame.BorderBrush = Res(ok || box.Text.Length == 0 ? "HudStroke" : "HudAmber");
            if (ok)
                SetBirthdayDate(d);
        };

        var cal = new System.Windows.Controls.Calendar { SelectedDate = DateTime.Today, DisplayDate = DateTime.Today };
        var pop = new Popup
        {
            StaysOpen = false, AllowsTransparency = true, Placement = PlacementMode.Bottom,
            Child = new Border { CornerRadius = new CornerRadius(12), Background = Res("HudSurfaceRaised"), BorderBrush = Res("HudStroke"), BorderThickness = new Thickness(1), Padding = new Thickness(6), Child = cal },
        };
        cal.SelectedDatesChanged += (_, _) =>
        {
            if (cal.SelectedDate is DateTime d)
            {
                box.Text = d.ToString("d", Fr);
                pop.IsOpen = false;
            }
        };
        // le calendrier garde la souris capturée après un clic : on la relâche
        cal.PreviewMouseUp += (_, _) => { if (Mouse.Captured is CalendarItem or CalendarDayButton) Mouse.Capture(null); };
        var open = IconButton("", "Choisir dans le calendrier", () =>
        {
            cal.SelectedDate = bdDate;
            cal.DisplayDate = bdDate;
            pop.IsOpen = true;
        });
        pop.PlacementTarget = open;
        var today = Ghost("Aujourd'hui", () => box.Text = DateTime.Today.ToString("d", Fr));
        today.Margin = new Thickness(8, 0, 0, 0);

        var row = new DockPanel();
        DockPanel.SetDock(today, Dock.Right);
        DockPanel.SetDock(open, Dock.Right);
        row.Children.Add(today);
        row.Children.Add(open);
        open.Margin = new Thickness(6, 0, 0, 0);
        row.Children.Add(frame);
        return row;
    }

    private void SetBirthdayDate(DateTime d)
    {
        bdDate = d;
        if (bdLabelText != null)
            bdLabelText.Text = "Photo V-Max · " + d.ToString("d", Fr);
    }

    /// <summary>Applique la mise en page « bday » du compagnon choisi (mêmes paramètres que VPet)</summary>
    private void LoadBirthday()
    {
        if (bdPets == null || bdCard == null || bdIndex < 0 || bdIndex >= bdPets.Count)
            return;
        var pl = bdPets[bdIndex];
        try { bdBackground!.Source = MW.ImageSources.FindImage("bday_" + pl.loader.Name); }
        catch { bdBackground!.Source = null; }
        var host = MW.GameSavesData.GameSave.HostName;
        if (string.IsNullOrWhiteSpace(host))
            host = Environment.UserName;
        var previousDefault = bdDefaultText;
        bdDefaultText = $"{pl.name} souhaite un joyeux anniversaire à {host} !";
        if (bdCustomText != null && bdCustomText.Text == previousDefault)
            bdCustomText.Text = bdDefaultText;

        ILine bdinfo = pl.loader.Config.Data.FindLine("bday")!;
        try
        {
            bdAvatar!.Width = bdinfo[(gdbe)"w"];
            bdAvatar.Margin = new Thickness(bdinfo[(gdbe)"x"], bdinfo[(gdbe)"y"], 0, 0);
        }
        catch { }
        try { bdLabel!.VerticalAlignment = Enum.Parse<VerticalAlignment>(bdinfo[(gstr)"va"]!, true); } catch { bdLabel!.VerticalAlignment = VerticalAlignment.Top; }
        try { bdLabel.HorizontalAlignment = Enum.Parse<HorizontalAlignment>(bdinfo[(gstr)"ha"]!, true); } catch { bdLabel.HorizontalAlignment = HorizontalAlignment.Right; }
        try { bdTextBorder!.Background = new SolidColorBrush(Function.HEXToColor('#' + bdinfo[(gstr)"tb"])); } catch { }
        try { bdText!.Foreground = new SolidColorBrush(Function.HEXToColor('#' + bdinfo[(gstr)"tf"])); } catch { bdText!.Foreground = Brushes.Black; }
        try { bdTextBox!.Margin = new Thickness(bdinfo[(gdbe)"tleft"], bdinfo[(gdbe)"ttop"], bdinfo[(gdbe)"tright"], bdinfo[(gdbe)"tbottom"]); } catch { }
        UpdateBirthdayText();
    }

    private void UpdateBirthdayText()
    {
        if (bdText == null)
            return;
        bdText.Text = bdCustom?.IsChecked == true && bdCustomText != null ? bdCustomText.Text : bdDefaultText;
    }

    private void PickAvatar()
    {
        var dlg = new OpenFileDialog { Filter = "Images|*.png;*.jpg;*.jpeg", Title = "Choisir une image" };
        if (dlg.ShowDialog(this) != true || bdAvatar == null)
            return;
        try
        {
            var img = new BitmapImage();
            img.BeginInit();
            img.CacheOption = BitmapCacheOption.OnLoad; // ne verrouille pas le fichier
            img.UriSource = new Uri(dlg.FileName);
            img.EndInit();
            bdAvatar.Source = img;
        }
        catch (Exception ex)
        {
            Notify("Impossible d'ouvrir cette image : " + ex.Message, HudToast.Kind.Warning);
        }
    }
    #endregion

    #region Journal
    private sealed class LogItem
    {
        public LogItem(ActivityLog log, string text) { Log = log; Text = text; }
        public ActivityLog Log { get; }
        public string Text { get; }
        public FrameworkElement? Row { get; set; }
    }

    private const int LogPage = 200;
    private readonly List<LogItem> logAll = new(); // le plus récent en premier
    private List<LogItem> logFiltered = new();
    private StackPanel? logRows;
    private TextBox? logSearch;
    private TextBlock? logCount;
    private int logShown;
    private bool logSubscribed;

    private bool LogVisible(ActivityLog log) => MW.Set.DeBug || !log.IsDebug;

    private LogItem MakeLogItem(ActivityLog log)
    {
        string text;
        try
        {
            text = log.ToString(MW.Main);
            // « [hh:mm] texte » : l'heure est affichée à part
            if (text.StartsWith('['))
            {
                int end = text.IndexOf("] ", StringComparison.Ordinal);
                if (end > 0)
                    text = text.Substring(end + 2);
            }
        }
        catch
        {
            text = log.Type + " " + log.Description.Replace('|', ' ');
        }
        return new LogItem(log, text);
    }

    private FrameworkElement BuildJournal()
    {
        LoadLogs();
        if (!logSubscribed)
        {
            MW.ActivityLogs.CollectionChanged += OnActivityLogsChanged;
            logSubscribed = true;
        }

        logSearch = SearchBox("Chercher dans le journal…", out var searchFrame);
        logSearch.TextChanged += (_, _) => FillJournal();
        logCount = new TextBlock { FontFamily = Mono, FontSize = 11.5, Foreground = Res("HudTextMuted"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
        var toolbar = new DockPanel { Margin = new Thickness(24, 4, 24, 10) };
        DockPanel.SetDock(logCount, Dock.Right);
        toolbar.Children.Add(logCount);
        toolbar.Children.Add(searchFrame);

        logRows = new StackPanel();
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.Children.Add(toolbar);
        var scroll = Scroll(logRows, new Thickness(12, 0, 16, 16));
        Grid.SetRow(scroll, 1);
        root.Children.Add(scroll);
        FillJournal();
        return root;
    }

    private bool LogMatches(LogItem item)
    {
        var q = logSearch?.Text.Trim() ?? "";
        return q.Length == 0 || item.Text.Contains(q, StringComparison.CurrentCultureIgnoreCase);
    }

    private void FillJournal()
    {
        if (logRows == null)
            return;
        foreach (var item in logAll)
            item.Row = null;
        logFiltered = logAll.Where(LogMatches).ToList();
        logRows.Children.Clear();
        logShown = 0;
        UpdateLogCount();
        if (logFiltered.Count == 0)
        {
            logRows.Children.Add(Empty(logAll.Count == 0
                ? $"Rien pour l'instant : le travail, les études et les petits événements de {MW.GameSavesData.GameSave.Name} s'afficheront ici."
                : "Rien ne correspond à cette recherche."));
            return;
        }
        MoreLogs();
    }

    private void UpdateLogCount()
    {
        if (logCount != null)
            logCount.Text = logFiltered.Count == logAll.Count ? $"{N0(logAll.Count)} entrées" : $"{N0(logFiltered.Count)} sur {N0(logAll.Count)}";
    }

    private void MoreLogs()
    {
        if (logRows == null)
            return;
        if (logRows.Children.Count > 0 && logRows.Children[^1] is Button more)
            logRows.Children.Remove(more);
        foreach (var item in logFiltered.Skip(logShown).Take(LogPage))
            logRows.Children.Add(LogRow(item));
        logShown = Math.Min(logFiltered.Count, logShown + LogPage);
        if (logShown < logFiltered.Count)
            logRows.Children.Add(MoreButton(logFiltered.Count - logShown, MoreLogs));
    }

    private FrameworkElement LogRow(LogItem item)
    {
        var t = item.Log.Time;
        var time = new TextBlock
        {
            Text = t.Date == DateTime.Today ? t.ToString("HH:mm", Fr) : t.ToString("dd/MM HH:mm", Fr),
            FontFamily = Mono, FontSize = 11.5, Foreground = Res("HudTextMuted"), Width = 92, Margin = new Thickness(0, 2, 8, 0),
            ToolTip = t.ToString("F", Fr),
        };
        var text = new TextBlock { Text = item.Text, FontSize = 13.5, Foreground = Res("HudText"), TextWrapping = TextWrapping.Wrap, LineHeight = 19 };
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(time);
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
        if (item.Log.IsDebug)
        {
            var tag = new Border
            {
                CornerRadius = new CornerRadius(8), BorderBrush = Res("HudAmber"), BorderThickness = new Thickness(1), Padding = new Thickness(7, 1, 7, 1),
                Margin = new Thickness(10, 1, 0, 0), VerticalAlignment = VerticalAlignment.Top,
                Child = new TextBlock { Text = "débogage", FontFamily = Display, FontSize = 10.5, Foreground = Res("HudAmber") },
            };
            Grid.SetColumn(tag, 2);
            grid.Children.Add(tag);
        }
        var row = HoverRow(grid);
        item.Row = row;
        return row;
    }

    private void OnActivityLogsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        var added = e.NewItems?.OfType<ActivityLog>().ToList();
        var removed = e.OldItems?.OfType<ActivityLog>().ToList();
        bool reset = e.Action == NotifyCollectionChangedAction.Reset;
        Dispatcher.BeginInvoke(() =>
        {
            try
            {
                if (logRows == null)
                    return;
                if (reset)
                {
                    LoadLogs();
                    FillJournal();
                    return;
                }
                if (removed != null)
                {
                    foreach (var log in removed)
                    {
                        var item = logAll.FirstOrDefault(x => ReferenceEquals(x.Log, log));
                        if (item == null)
                            continue;
                        logAll.Remove(item);
                        if (logFiltered.Remove(item))
                        {
                            if (item.Row != null)
                            {
                                logRows.Children.Remove(item.Row);
                                logShown = Math.Max(0, logShown - 1);
                            }
                        }
                    }
                }
                if (added != null)
                {
                    bool wasEmpty = logFiltered.Count == 0;
                    foreach (var log in added)
                    {
                        if (log == null || !LogVisible(log))
                            continue;
                        var item = MakeLogItem(log);
                        logAll.Insert(0, item);
                        if (!LogMatches(item))
                            continue;
                        logFiltered.Insert(0, item);
                        if (wasEmpty)
                            continue;
                        logRows.Children.Insert(0, LogRow(item));
                        logShown++;
                    }
                    if (wasEmpty && logFiltered.Count > 0)
                    {
                        FillJournal();
                        return;
                    }
                }
                UpdateLogCount();
            }
            catch { }
        });
    }

    /// <summary>Lit tout le journal (le plus récent en premier)</summary>
    private void LoadLogs()
    {
        logAll.Clear();
        ActivityLog[] snapshot = [];
        for (int i = 0; i < 3; i++)
        {
            try { snapshot = MW.ActivityLogs.ToArray(); break; }
            catch (Exception) { } // modifiée pendant la lecture : on réessaie
        }
        for (int i = snapshot.Length - 1; i >= 0; i--)
            if (snapshot[i] != null && LogVisible(snapshot[i]))
                logAll.Add(MakeLogItem(snapshot[i]));
    }
    #endregion
}
