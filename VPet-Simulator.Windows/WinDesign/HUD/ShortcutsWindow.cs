using LinePutScript;
using LinePutScript.Localization.WPF;
using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using VPet_Simulator.Core;
using VPet_Simulator.Windows.Interface;

namespace VPet_Simulator.Windows.HUD;

/// <summary>
/// V-Max : raccourcis personnalisés (remplace l'onglet « 自定 » des anciens paramètres et ses lignes DIYViewer).
/// Chaque raccourci est un bouton du panneau « Plus » : nom affiché + contenu (lien, programme ou touches SendKeys).
/// Même stockage que VPet (<c>Set["diy"]</c>, un Sub par raccourci) et même rechargement (<see cref="MainWindow.LoadDIY"/>).
/// </summary>
public sealed class ShortcutsWindow : HudWindow
{
    private static ShortcutsWindow? instance;

    private sealed class Row
    {
        public required TextBox Label;
        public required TextBox Content;
        public required Border Card;
        public required TextBlock KindGlyph;
        public required TextBlock KindText;
        public required Button Capture;
        public required TextBlock CaptureHint;
        public required Button Up;
        public required Button Down;
        public bool ReadKeyPress;
    }

    private readonly List<Row> rows = new();
    private readonly StackPanel cards = new();
    private readonly ScrollViewer scroll;
    private readonly TextBlock status;
    private readonly Button save;
    private bool dirty;

    static ShortcutsWindow()
    {
        // Échap : passe avant le gestionnaire de HudWindow (les gestionnaires de classe sont appelés en premier)
        EventManager.RegisterClassHandler(typeof(ShortcutsWindow), PreviewKeyDownEvent, new KeyEventHandler((s, e) =>
        {
            if (s is ShortcutsWindow w && e.Key == Key.Escape && e.OriginalSource is not TextBox)
            {
                e.Handled = true;
                w.TryClose();
            }
        }));
    }

    private ShortcutsWindow(MainWindow mw) : base(mw, "shortcuts", "EXTENSIONS", "Raccourcis personnalisés", 860, 660)
    {
        // le bouton Fermer vérifie d'abord les modifications non enregistrées
        HeaderButtons.Children.RemoveAt(HeaderButtons.Children.Count - 1);
        HeaderButtons.Children.Add(IconButton("\uE711", "Fermer (Échap)", TryClose));

        // en-tête : explication, exemples, aide
        var intro = new StackPanel { Margin = new Thickness(24, 0, 24, 14) };
        intro.Children.Add(new TextBlock
        {
            Text = "Des boutons à toi dans le panneau « Plus » : ouvrir un site, un programme ou envoyer une combinaison de touches.",
            Style = St("HudBodyText"), Foreground = Res("HudTextMuted"), FontSize = 13.5,
        });
        var help = new DockPanel { Margin = new Thickness(0, 6, 0, 0) };
        var link = LinkButton("\uE897", "Aide sur la syntaxe des touches", OpenSyntaxHelp);
        DockPanel.SetDock(link, Dock.Right);
        help.Children.Add(link);
        help.Children.Add(new TextBlock
        {
            Text = "^c = Ctrl+C   ·   %{F4} = Alt+F4   ·   +a = Maj+A   ·   {ENTER}",
            FontFamily = (FontFamily)FindResource("HudMono"), FontSize = 12, Foreground = Res("HudTextMuted"),
            VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
        });
        intro.Children.Add(help);

        scroll = Scroll(cards);
        var body = new DockPanel();
        DockPanel.SetDock(intro, Dock.Top);
        body.Children.Add(intro);
        body.Children.Add(scroll);
        Body = body;

        // pied : ajouter, état, enregistrer
        var add = GlyphButton("\uE710", "Ajouter un raccourci", () =>
        {
            var row = AddRow("", "");
            Layout();
            SetDirty();
            row.Label.Focus();
            row.Card.BringIntoView();
        });
        status = new TextBlock { FontSize = 12.5, Foreground = Res("HudAmber"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 12, 0), HorizontalAlignment = HorizontalAlignment.Right };
        save = Primary("Enregistrer", Save);
        var foot = new DockPanel();
        DockPanel.SetDock(add, Dock.Left);
        DockPanel.SetDock(save, Dock.Right);
        foot.Children.Add(add);
        foot.Children.Add(save);
        foot.Children.Add(status);
        Footer = new Border { BorderBrush = Res("HudStroke"), BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(24, 12, 24, 16), Child = foot };

        foreach (ISub sub in MW.Set["diy"])
            AddRow(sub.Name, sub.Info);
        Layout();
        UpdateState();

        Closing += (_, e) =>
        {
            if (!dirty)
                return;
            // fermeture par un autre chemin (Alt+F4, barre des tâches…) : on propose d'enregistrer
            bool fading = Content is UIElement root && root.Opacity < 1;
            var r = VDialog.Show(this, "Tu as des modifications non enregistrées.\nLes enregistrer avant de fermer ?", "Raccourcis personnalisés",
                fading ? MessageBoxButton.YesNo : MessageBoxButton.YesNoCancel, Panuon.WPF.UI.MessageBoxIcon.Warning);
            if (r == MessageBoxResult.Yes)
                Save();
            else if (r == MessageBoxResult.Cancel)
                e.Cancel = true;
        };
        Closed += (_, _) => instance = null;
    }

    /// <summary>Ouvre (ou ramène) l'éditeur de raccourcis</summary>
    public static ShortcutsWindow Open(MainWindow mw)
    {
        instance ??= new ShortcutsWindow(mw);
        instance.Present();
        return instance;
    }

    #region Enregistrement et fermeture
    private void SetDirty()
    {
        if (dirty)
            return;
        dirty = true;
        UpdateState();
    }

    private void UpdateState()
    {
        status.Text = dirty ? "Modifications non enregistrées" : "";
        save.IsEnabled = dirty;
    }

    private void Save()
    {
        MW.Set["diy"].Clear();
        foreach (var row in rows)
        {
            if (string.IsNullOrWhiteSpace(row.Label.Text) && string.IsNullOrWhiteSpace(row.Content.Text))
                continue; // ligne laissée vide
            MW.Set["diy"].Add(new Sub(row.Label.Text, row.Content.Text));
        }
        MW.LoadDIY();
        dirty = false;
        UpdateState();
        Notify("Raccourcis enregistrés", HudToast.Kind.Success);
    }

    private void TryClose()
    {
        if (!dirty)
        {
            FadeClose();
            return;
        }
        var r = VDialog.Show(this, "Tu as des modifications non enregistrées.\nLes enregistrer avant de fermer ?", "Raccourcis personnalisés",
            MessageBoxButton.YesNoCancel, Panuon.WPF.UI.MessageBoxIcon.Warning);
        if (r == MessageBoxResult.Yes)
            Save();
        else if (r == MessageBoxResult.No)
            dirty = false;
        else
            return;
        FadeClose();
    }

    private static void OpenSyntaxHelp()
    {
        if (LocalizeCore.CurrentCulture.StartsWith("zh"))
            ExtensionFunction.StartURL("https://www.exlb.net/SendKeys");
        else if (LocalizeCore.CurrentCulture == "null")
            ExtensionFunction.StartURL("https://learn.microsoft.com/en-us/dotnet/api/system.windows.forms.sendkeys?view=windowsdesktop-7.0#remarks");
        else
            ExtensionFunction.StartURL($"https://learn.microsoft.com/{LocalizeCore.CurrentCulture}/dotnet/api/system.windows.forms.sendkeys?view=windowsdesktop-7.0#remarks");
    }
    #endregion

    #region Cartes
    private void Layout()
    {
        cards.Children.Clear();
        for (int i = 0; i < rows.Count; i++)
        {
            rows[i].Up.IsEnabled = i > 0;
            rows[i].Down.IsEnabled = i < rows.Count - 1;
            cards.Children.Add(rows[i].Card);
        }
        if (rows.Count == 0)
            cards.Children.Add(Empty("Aucun raccourci pour l'instant. Ajoute-en un : il apparaîtra dans le panneau « Plus »."));
    }

    private void Move(Row row, int delta)
    {
        int i = rows.IndexOf(row), j = i + delta;
        if (i < 0 || j < 0 || j >= rows.Count)
            return;
        rows.RemoveAt(i);
        rows.Insert(j, row);
        Layout();
        SetDirty();
        row.Card.BringIntoView();
    }

    private void Remove(Row row)
    {
        rows.Remove(row);
        Layout();
        SetDirty();
    }

    private Row AddRow(string label, string content)
    {
        var labelBox = new TextBox { Style = St("HudInput"), Tag = "Ex. : Mon site préféré", Text = label };
        var contentBox = new TextBox { Style = St("HudInput"), Tag = "https://…, C:\\…\\programme.exe ou ^c", Text = content, FontFamily = (FontFamily)FindResource("HudMono"), FontSize = 13 };
        var kindGlyph = new TextBlock { FontFamily = (FontFamily)FindResource("HudIcons"), FontSize = 13, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 1, 8, 0) };
        var kindText = new TextBlock { FontFamily = (FontFamily)FindResource("HudDisplay"), FontWeight = FontWeights.SemiBold, FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
        var captureHint = new TextBlock
        {
            Text = "Appuie sur les touches à envoyer, puis clique sur « Arrêter la capture ».",
            FontSize = 12, Foreground = Res("HudAccent"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0), Visibility = Visibility.Collapsed,
        };

        Row? row = null;
        var up = IconButton("\uE70E", "Monter", () => Move(row!, -1));
        var down = IconButton("\uE70D", "Descendre", () => Move(row!, +1));
        var delete = IconButton("\uE74D", "Supprimer", () => Remove(row!));
        var pick = GlyphButton("\uE8E5", "Choisir un programme…", () => PickFile(row!));
        var capture = GlyphButton("\uE765", "Capturer des touches", () => ToggleCapture(row!));
        pick.Padding = capture.Padding = new Thickness(10, 5, 10, 6);
        pick.FontSize = capture.FontSize = 12.5;

        // ligne du haut : type détecté + déplacer / supprimer
        var kind = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        kind.Children.Add(kindGlyph);
        kind.Children.Add(kindText);
        var tools = new StackPanel { Orientation = Orientation.Horizontal };
        tools.Children.Add(up);
        tools.Children.Add(down);
        tools.Children.Add(delete);
        var top = new DockPanel { Margin = new Thickness(0, 0, -6, 6) };
        DockPanel.SetDock(tools, Dock.Right);
        top.Children.Add(tools);
        top.Children.Add(kind);

        // champs : nom (3) et contenu (8), comme l'ancienne ligne
        var fields = new Grid();
        fields.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star) });
        fields.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8, GridUnitType.Star) });
        var labelField = Field("Nom du bouton", labelBox);
        labelField.Margin = new Thickness(0, 0, 10, 0);
        var contentField = Field("Lien, programme ou touches", contentBox);
        Grid.SetColumn(contentField, 1);
        fields.Children.Add(labelField);
        fields.Children.Add(contentField);

        var actions = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
        pick.Margin = new Thickness(0, 0, 8, 0);
        actions.Children.Add(pick);
        actions.Children.Add(capture);

        var sp = new StackPanel();
        sp.Children.Add(top);
        sp.Children.Add(fields);
        sp.Children.Add(actions);
        sp.Children.Add(captureHint);
        var card = Card(sp, new Thickness(16, 10, 16, 14));
        card.Margin = new Thickness(0, 0, 0, 10);

        row = new Row
        {
            Label = labelBox, Content = contentBox, Card = card, KindGlyph = kindGlyph, KindText = kindText,
            Capture = capture, CaptureHint = captureHint, Up = up, Down = down,
        };
        labelBox.TextChanged += (_, _) => SetDirty();
        contentBox.TextChanged += (_, _) =>
        {
            UpdateKind(row);
            SetDirty();
        };
        contentBox.PreviewKeyDown += (_, e) => CaptureKey(row, e);
        UpdateKind(row);
        rows.Add(row);
        return row;
    }

    private Border Field(string caption, TextBox box)
    {
        var sp = new StackPanel();
        sp.Children.Add(new TextBlock { Text = caption, FontSize = 11.5, Foreground = Res("HudTextMuted"), Margin = new Thickness(2, 0, 0, 4) });
        sp.Children.Add(new Border
        {
            CornerRadius = new CornerRadius(10), Background = Res("HudSurface"), BorderBrush = Res("HudStroke"), BorderThickness = new Thickness(1),
            Padding = new Thickness(10, 6, 10, 6), Child = box,
        });
        return new Border { Child = sp };
    }

    /// <summary>Type deviné avec les règles de MainWindow.RunDIY : « :\ » programme, « :// » site, sinon touches</summary>
    private void UpdateKind(Row row)
    {
        string c = row.Content.Text;
        (string glyph, string text, string brush) k;
        if (string.IsNullOrWhiteSpace(c))
            k = ("\uE70F", "À compléter", "HudTextMuted");
        else if (c.Contains(@":\"))
            k = File.Exists(c) || Directory.Exists(c) ? ("\uECAA", "Programme", "HudSilver") : ("\uE7BA", "Programme · introuvable", "HudAmber");
        else if (c.Contains("://"))
            k = ("\uE774", "Site web", "HudSilver");
        else
            k = ("\uE765", "Touches", "HudSilver");
        row.KindGlyph.Text = k.glyph;
        row.KindText.Text = k.text.ToUpperInvariant();
        row.KindGlyph.Foreground = row.KindText.Foreground = Res(k.brush);
    }

    private void PickFile(Row row)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "Programmes (*.exe)|*.exe|Tous les fichiers (*.*)|*.*", Title = "Choisir un programme" };
        if (dlg.ShowDialog(this) != true)
            return;
        StopCapture(row);
        row.Content.Text = dlg.FileName;
        if (string.IsNullOrWhiteSpace(row.Label.Text))
            row.Label.Text = Path.GetFileNameWithoutExtension(dlg.FileName);
    }
    #endregion

    #region Capture des touches (reprise de DIYViewer)
    private void ToggleCapture(Row row)
    {
        if (row.ReadKeyPress)
        {
            StopCapture(row);
            return;
        }
        foreach (var other in rows)
            StopCapture(other);
        SetCapture(row, true);
        row.Content.Focus();
        row.Content.CaretIndex = row.Content.Text.Length;
    }

    private void StopCapture(Row row)
    {
        if (row.ReadKeyPress)
            SetCapture(row, false);
    }

    private void SetCapture(Row row, bool on)
    {
        row.ReadKeyPress = on;
        row.Content.AcceptsReturn = on;
        row.Content.AcceptsTab = on;
        row.CaptureHint.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        var label = (StackPanel)row.Capture.Content;
        ((TextBlock)label.Children[1]).Text = on ? "Arrêter la capture" : "Capturer des touches";
        row.Capture.Foreground = on ? Res("HudAccent") : Res("HudText");
        row.Capture.Background = on ? Res("HudAccentSoft") : Brushes.Transparent;
    }

    /// <summary>Transforme la touche pressée en syntaxe SendKeys (logique identique à DIYViewer.TextBox_PreviewKeyDown)</summary>
    private static void CaptureKey(Row row, KeyEventArgs e)
    {
        if (!row.ReadKeyPress)
            return;
        var TextContent = row.Content;
        bool isshift = false;
        string startxt = "";
        if (TextContent.Text.EndsWith(")"))
        {
            isshift = true;
            TextContent.Text = TextContent.Text.Substring(0, TextContent.Text.Length - 1);
            startxt = TextContent.Text.Split('(')[0];
        }
        switch (e.Key)
        {
            case Key.Back:
                TextContent.AppendText("{BS}");
                break;
            case Key.CapsLock:
            case Key.Delete:
            case Key.Down:
            case Key.Left:
            case Key.Right:
            case Key.Space:
            case Key.Up:
            case Key.End:
            case Key.Enter:
            case Key.Help:
            case Key.Home:
            case Key.Insert:
            case Key.PageUp:
            case Key.PageDown:
            case Key.NumLock:
            case Key.Tab:
            case Key.F1:
            case Key.F2:
            case Key.F3:
            case Key.F4:
            case Key.F5:
            case Key.F6:
            case Key.F7:
            case Key.F8:
            case Key.F9:
            case Key.F10:
            case Key.F11:
            case Key.F12:
            case Key.F13:
            case Key.F14:
            case Key.F15:
            case Key.F16:
            case Key.Add:
            case Key.Subtract:
            case Key.Multiply:
            case Key.Divide:
                TextContent.AppendText($"{{{e.Key.ToString().ToUpperInvariant()}}}");
                break;
            case Key.Escape:
                TextContent.AppendText("{ESC}");
                break;
            case Key.PrintScreen:
                TextContent.AppendText("{PRTSC}");
                break;
            case Key.LeftCtrl:
            case Key.RightCtrl:
                if (!startxt.Contains("^"))
                    startxt += "^";
                break;
            case Key.LeftAlt:
            case Key.RightAlt:
            case Key.System:
                if (!startxt.Contains("%"))
                    startxt += "%";
                break;
            case Key.RightShift:
            case Key.LeftShift:
                if (!startxt.Contains("+"))
                    startxt += "+";
                break;
            case Key.OemComma:
                TextContent.AppendText(",");
                break;
            case Key.OemPeriod:
                TextContent.AppendText(".");
                break;
            case Key.OemQuestion:
                TextContent.AppendText("/");
                break;
            case Key.OemMinus:
                TextContent.AppendText("-");
                break;
            case Key.OemPlus:
                TextContent.AppendText("+");
                break;
            case Key.Oem3:
                TextContent.AppendText("`");
                break;
            case Key.Oem5:
                TextContent.AppendText("|");
                break;
            case Key.LWin:
            case Key.RWin:
                break;
            case Key.D1:
            case Key.D2:
            case Key.D3:
            case Key.D4:
            case Key.D5:
            case Key.D6:
            case Key.D7:
            case Key.D8:
            case Key.D9:
            case Key.D0:
                TextContent.AppendText(e.Key.ToString().Substring(1));
                break;
            default:
                TextContent.AppendText(e.Key.ToString());
                break;
        }
        if (isshift)
        {
            TextContent.Text = startxt + '(' + TextContent.Text.Split('(')[1] + ')';
        }
        else if (startxt.Length != 0)
        {
            TextContent.Text = startxt + '(' + TextContent.Text + ')';
        }
        TextContent.CaretIndex = TextContent.Text.Length;
        e.Handled = true;
    }
    #endregion

    #region Boutons
    private Button GlyphButton(string glyph, string text, Action run)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        content.Children.Add(new TextBlock { Text = glyph, FontFamily = (FontFamily)FindResource("HudIcons"), FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 1, 8, 0) });
        content.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });
        var b = new Button { Style = St("HudGhostButton"), Content = content };
        b.Click += (_, _) => run();
        return b;
    }

    /// <summary>Lien discret (texte argenté, sans cadre)</summary>
    private Button LinkButton(string glyph, string text, Action run)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(10, 5, 10, 5) };
        content.Children.Add(new TextBlock { Text = glyph, FontFamily = (FontFamily)FindResource("HudIcons"), FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 1, 6, 0) });
        content.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center, TextDecorations = TextDecorations.Underline });
        var b = new Button { Style = St("HudIconButton"), Content = content, Width = double.NaN, Height = double.NaN, FontFamily = (FontFamily)FindResource("HudBody"), FontSize = 12.5, ToolTip = "S'ouvre dans ton navigateur" };
        b.Click += (_, _) => run();
        return b;
    }
    #endregion
}
