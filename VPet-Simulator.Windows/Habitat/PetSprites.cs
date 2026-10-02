using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Windows.Media.Imaging;

namespace VPet_Simulator.Windows.Habitat;

/// <summary>
/// V-Max habitat : image représentative d'une animation du personnage (pour les fantômes de l'éditeur).
/// Cherche le dossier de l'animation dans le personnage courant et prend une image de sa boucle « normale ».
/// </summary>
public static class PetSprites
{
    private static readonly ConcurrentDictionary<string, BitmapSource?> cache = new();

    /// <summary>Nom du dossier d'animation pour une activité (« sleep », « eat », « work:Study »…)</summary>
    public static string? GraphFor(MainWindow mw, string activity)
    {
        switch (activity)
        {
            case "sleep": return "Sleep";
            case "eat": return "Eat";
            case "drink": return "Drink";
            case "relax": return null;
        }
        if (activity.StartsWith("work:"))
        {
            mw.Main.WorkList(out var ws, out var ss, out var ps);
            var w = ws.Concat(ss).Concat(ps).FirstOrDefault(x => string.Equals(x.Name, activity[5..], StringComparison.OrdinalIgnoreCase));
            return w == null ? null : string.IsNullOrEmpty(w.Graph) ? w.Name : w.Graph;
        }
        return null;
    }

    /// <summary>Image de l'activité (null si introuvable : l'appelant affiche alors le personnage debout)</summary>
    public static BitmapSource? Frame(MainWindow mw, string activity)
    {
        var graph = GraphFor(mw, activity);
        if (graph == null)
            return null;
        return cache.GetOrAdd(mw.Set.PetGraph + "|" + graph, _ => Load(mw, graph));
    }

    private static BitmapSource? Load(MainWindow mw, string graph)
    {
        try
        {
            var root = mw.Pets.Find(x => x.Name == mw.Set.PetGraph)?.path.FirstOrDefault(Directory.Exists);
            if (root == null)
                return null;
            var dir = Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories)
                .FirstOrDefault(d => string.Equals(Path.GetFileName(d), graph, StringComparison.OrdinalIgnoreCase));
            if (dir == null)
                return null;
            // boucle (B) de l'humeur normale d'abord, sinon n'importe quelle image
            var sub = Directory.EnumerateDirectories(dir, "*", SearchOption.AllDirectories)
                .Select(d => (d, name: Path.GetFileName(d)))
                .OrderBy(x => x.name.StartsWith("B", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ThenBy(x => x.name.Contains("Nomal", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .Select(x => x.d)
                .Append(dir);
            var file = sub.Select(d => Directory.EnumerateFiles(d, "*.png").OrderBy(f => f, StringComparer.Ordinal).FirstOrDefault())
                .FirstOrDefault(f => f != null);
            if (file == null)
                return null;
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.DecodePixelWidth = 500;
            bmp.UriSource = new Uri(file);
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch
        {
            return null;
        }
    }
}
