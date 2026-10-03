using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace VPet_Simulator.Windows.Assistant;

/// <summary>
/// Index des applications INSTALLÉES (raccourcis du menu Démarrer). Permet de lancer n'importe quel logiciel par son
/// nom — pas seulement une liste codée en dur. Lancer le .lnk via le shell démarre l'application comme un double-clic.
/// </summary>
internal static class AppIndex
{
    private static List<(string norm, string display, string path)>? apps;

    private static List<(string norm, string display, string path)> All()
    {
        if (apps != null)
            return apps;
        var list = new List<(string, string, string)>();
        var roots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
            Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
        };
        // RecurseSubdirectories + IgnoreInaccessible : sinon une seule sous-arborescence illisible interrompait TOUT
        // le scan (EnumerateFiles classique lève à la première erreur → on ne récupérait qu'un raccourci).
        var opts = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = 0 };
        foreach (var root in roots)
        {
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
                continue;
            try
            {
                foreach (var f in Directory.EnumerateFiles(root, "*.lnk", opts))
                {
                    var name = Path.GetFileNameWithoutExtension(f);
                    if (IsNoise(name))
                        continue;
                    list.Add((Norm(name), name, f));
                }
            }
            catch { }
        }
        // dédoublonnage par nom normalisé (on garde le premier rencontré)
        apps = list.GroupBy(x => x.Item1).Select(g => g.First()).ToList();
        return apps;
    }

    /// <summary>Permet de reconstruire l'index (ex. après installation d'une nouvelle app).</summary>
    public static void Invalidate() => apps = null;

    /// <summary>Toutes les applications indexées (nom affichable + chemin du raccourci), triées par nom.</summary>
    public static IReadOnlyList<(string display, string path)> AllApps() =>
        All().OrderBy(a => a.display, StringComparer.CurrentCultureIgnoreCase).Select(a => (a.display, a.path)).ToList();

    private static bool IsNoise(string name)
    {
        var n = name.ToLowerInvariant();
        return n.Contains("uninstall") || n.Contains("desinstall") || n.Contains("désinstall")
            || n.Contains("readme") || n.Contains("documentation") || n.Contains("lisez-moi")
            || n.Contains("website") || n.Contains("site web") || n.Contains("report a bug");
    }

    /// <summary>Meilleure correspondance pour un nom parlé, ou null. Essaie exact, puis inclusion, puis tous les mots.</summary>
    public static (string display, string path)? Find(string query)
    {
        var q = Norm(query);
        if (q.Length == 0)
            return null;
        var all = All();

        var exact = all.FirstOrDefault(a => a.norm == q);
        if (exact.path != null)
            return (exact.display, exact.path);

        // l'un contient l'autre → on prend le nom le plus court (le plus « central »)
        var contains = all.Where(a => a.norm.Contains(q) || q.Contains(a.norm)).OrderBy(a => a.norm.Length).FirstOrDefault();
        if (contains.path != null)
            return (contains.display, contains.path);

        // tous les mots de la requête présents dans le nom de l'app
        var words = q.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length > 0)
        {
            var tok = all.Where(a => words.All(w => a.norm.Contains(w))).OrderBy(a => a.norm.Length).FirstOrDefault();
            if (tok.path != null)
                return (tok.display, tok.path);
        }
        return null;
    }

    public static bool Launch(string path)
    {
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); return true; }
        catch { return false; }
    }

    private static string Norm(string s)
    {
        var d = s.ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(d.Length);
        foreach (var ch in d)
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
                sb.Append(ch);
        return Regex.Replace(sb.ToString(), @"\s+", " ").Trim();
    }
}
