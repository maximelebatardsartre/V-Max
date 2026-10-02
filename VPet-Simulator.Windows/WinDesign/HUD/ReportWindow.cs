using LinePutScript.Localization.WPF;
using System;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Web;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using VPet_Simulator.Core;
using VPet_Simulator.Windows.Interface;

namespace VPet_Simulator.Windows.HUD;

/// <summary>
/// V-Max : signaler un problème ou proposer une idée (remplace winReport). Le rapport est préparé en Markdown,
/// copié dans le presse-papiers, puis un ticket GitHub s'ouvre. Rien n'est envoyé automatiquement ; la sauvegarde
/// n'est jointe que sur demande, avec les données sensibles masquées.
/// </summary>
public sealed class ReportWindow : HudWindow
{
    private static readonly (string label, string? hint)[] Types =
    [
        ("Erreur dans V-Max", "Que faisais-tu quand l'erreur est apparue ?"),
        ("Calcul incorrect", "Quelle valeur te semble fausse, et qu'attendais-tu ?"),
        ("Équilibrage", "Qu'est-ce qui te paraît trop facile ou trop difficile ?"),
        ("Idée ou suggestion", "Décris ton idée en quelques phrases."),
        ("Ressenti et partage", "Raconte ce que tu as aimé (ou pas)."),
        ("Traductions manquantes", null),
    ];

    private readonly ComboBox type;
    private readonly TextBox description;
    private readonly TextBox details;
    private readonly TextBox contact;
    private readonly CheckBox attach;

    public ReportWindow(MainWindow mw, string? error = null) : base(mw, "report", "AIDE", "Signaler un problème", 720, 680)
    {
        type = new ComboBox { Width = 260, HorizontalAlignment = HorizontalAlignment.Left };
        if (TryFindResource("VMaxComboBox") is Style cs)
            type.Style = cs;
        foreach (var t in Types)
            type.Items.Add(t.label);
        description = Area(5, "Décris le problème : ce qui se passe, ce que tu attendais, comment le reproduire.");
        details = Area(6, "Détails techniques (facultatif) : message d'erreur, étapes…");
        details.FontFamily = (FontFamily)FindResource("HudMono");
        details.FontSize = 12;
        contact = new TextBox { Style = St("HudInput"), Tag = "Pour te répondre (facultatif) : e-mail, pseudo GitHub…" };
        attach = new CheckBox { Content = "Joindre ma sauvegarde et mes réglages (mots de passe, clés et nom d'utilisateur masqués)", Foreground = Res("HudText"), Margin = new Thickness(0, 14, 0, 0) };

        var form = new StackPanel { MaxWidth = 660 };
        form.Children.Add(new TextBlock { Text = "Ton rapport est préparé ici, copié, puis un ticket GitHub s'ouvre pour que tu le colles. Rien n'est envoyé sans toi.", Style = St("HudBodyText"), Foreground = Res("HudTextMuted"), FontSize = 13 });
        form.Children.Add(Section("Type"));
        form.Children.Add(type);
        form.Children.Add(Section("Description"));
        form.Children.Add(Field(description));
        form.Children.Add(Section("Détails"));
        form.Children.Add(Field(details));
        form.Children.Add(Section("Contact"));
        form.Children.Add(Field(contact));
        form.Children.Add(attach);
        Body = Scroll(form);

        var send = Primary("Préparer le rapport", Send);
        var cancel = Ghost("Annuler", FadeClose);
        cancel.Margin = new Thickness(0, 0, 8, 0);
        var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(24, 8, 24, 18) };
        footer.Children.Add(cancel);
        footer.Children.Add(send);
        Footer = footer;

        type.SelectionChanged += (_, _) => OnType();
        if (error != null)
        {
            type.SelectedIndex = 0;
            details.Text = error;
            details.IsReadOnly = true;
        }
        else
            type.SelectedIndex = 3;
    }

    /// <summary>Description pré-remplie (ex. « Erreur de chargement des animations »)</summary>
    public string Description { get => description.Text; set => description.Text = value; }

    private TextBox Area(int lines, string hint) => new()
    {
        Style = St("HudInput"), Tag = hint, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = lines * 18, MaxHeight = 260,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalContentAlignment = VerticalAlignment.Top,
    };

    private Border Field(TextBox tb) => new()
    {
        CornerRadius = new CornerRadius(14), Background = Res("HudSurfaceRaised"), BorderBrush = Res("HudStroke"), BorderThickness = new Thickness(1),
        Padding = new Thickness(12, 8, 12, 8), Child = tb,
    };

    private void OnType()
    {
        var hint = Types[Math.Max(0, type.SelectedIndex)].hint;
        if (hint != null)
            description.Tag = hint;
        // traductions : on joint les textes encore non traduits (mode développeur)
        if (type.SelectedIndex == 5 && !details.IsReadOnly)
        {
            var sb = new StringBuilder();
            foreach (var v in LocalizeCore.StoreTranslationList)
                sb.AppendLine(v.Replace("\n", @"\n").Replace("\r", @"\r"));
            details.Text = sb.Length == 0 ? "Aucun texte non traduit n'a été repéré (active le mode développeur pour les collecter)." : sb.ToString();
            attach.IsChecked = false;
        }
    }

    private void Send()
    {
        if (string.IsNullOrWhiteSpace(description.Text) && type.SelectedIndex != 0 && type.SelectedIndex != 5)
        {
            Notify("Ajoute une description : qu'est-ce qui se passe, ou quelle est ton idée ?", HudToast.Kind.Warning);
            description.Focus();
            return;
        }
        var label = Types[Math.Max(0, type.SelectedIndex)].label;
        var sb = new StringBuilder();
        sb.AppendLine("**Type :** " + label);
        sb.AppendLine("**Version :** " + MW.Version + " (" + LocalizeCore.CurrentCulture + ")");
        if (!string.IsNullOrWhiteSpace(contact.Text))
            sb.AppendLine("**Contact :** " + contact.Text);
        sb.AppendLine();
        sb.AppendLine(description.Text);
        if (!string.IsNullOrWhiteSpace(details.Text))
        {
            sb.AppendLine();
            sb.AppendLine("```");
            sb.AppendLine(details.Text);
            sb.AppendLine("```");
        }
        if (attach.IsChecked == true)
        {
            sb.AppendLine("<details><summary>Sauvegarde et paramètres</summary>");
            sb.AppendLine();
            sb.AppendLine("```");
            sb.AppendLine(Redact(MW.Core.Save!.ToLine().ToString() + MW.Set.ToString()));
            sb.AppendLine("```");
            sb.AppendLine("</details>");
        }
        try { Clipboard.SetText(sb.ToString()); } catch { }
        var first = description.Text.Split('\n')[0].Trim();
        string title = HttpUtility.UrlEncode("[" + label + "] " + (first.Length > 0 ? first[..Math.Min(first.Length, 80)] : "Rapport"));
        ExtensionFunction.StartURL(ExtensionValue.IssueURL + "?title=" + title);
        VDialog.Show(this, "Ton rapport est copié. Colle-le dans le ticket GitHub qui vient de s'ouvrir (Ctrl+V).", "Rapport prêt", MessageBoxButton.OK, Panuon.WPF.UI.MessageBoxIcon.Success);
        FadeClose();
    }

    /// <summary>Masque les valeurs sensibles (clés, jetons, mots de passe) et le nom d'utilisateur Windows</summary>
    private static string Redact(string text)
    {
        var sensitive = new Regex(@"(?i)(api_?key|apikey|token|secret|password|passwd|pwd|auth|bearer|cookie|session)");
        var sb = new StringBuilder();
        foreach (var rawLine in text.Split('\n'))
        {
            var subs = rawLine.Split(":|");
            for (int i = 0; i < subs.Length; i++)
            {
                int sep = subs[i].IndexOf('#');
                if (sep > 0 && sensitive.IsMatch(subs[i][..sep]))
                    subs[i] = subs[i][..sep] + "#***";
            }
            var line = string.Join(":|", subs);
            if (!string.IsNullOrEmpty(Environment.UserName))
                line = line.Replace(Environment.UserName, "<utilisateur>");
            sb.Append(line).Append('\n');
        }
        return sb.ToString();
    }
}
