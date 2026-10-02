using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace VPet_Simulator.Windows.Habitat;

/// <summary>
/// V-Max habitat : propose des sols en cherchant les longues ruptures horizontales de l'image
/// (le dessus d'un plancher, d'un sol, d'une étagère). Analyse sur une copie réduite en niveaux de gris :
/// gradient vertical, segments horizontaux continus, fusion des doublons, filtrage des bords du haut.
/// </summary>
public static class FloorDetector
{
    public sealed record Candidate(double Y, double X1, double X2, double Score);

    /// <summary>
    /// Détecte des sols sur une image en niveaux de gris (une valeur 0-255 par pixel, ligne après ligne).
    /// Les coordonnées renvoyées sont celles de cette image.
    /// </summary>
    /// <param name="minLength">longueur minimale d'un sol, en fraction de la largeur</param>
    public static List<Candidate> Detect(byte[] gray, int width, int height, double minLength = 0.12, int max = 8)
    {
        if (width < 8 || height < 8 || gray.Length < width * height)
            return new();
        const int threshold = 26;      // contraste minimal entre la ligne et celle du dessous
        int minRun = Math.Max(4, (int)(width * minLength));
        int gap = Math.Max(1, width / 120); // petites interruptions tolérées (texture, poignée de porte…)
        var raw = new List<Candidate>();
        // on ignore le haut de l'image (ciel, plafond) : un sol a besoin de place au-dessus de lui
        for (int y = (int)(height * 0.12); y < height - 1; y++)
        {
            int start = -1, holes = 0, last = -1;
            double sum = 0;
            for (int x = 0; x <= width; x++)
            {
                int d = x < width ? Math.Abs(gray[(y + 1) * width + x] - gray[y * width + x]) : 0;
                if (d >= threshold && x < width)
                {
                    if (start < 0) { start = x; sum = 0; }
                    sum += d;
                    last = x;
                    holes = 0;
                }
                else if (start >= 0 && (++holes > gap || x == width))
                {
                    if (last - start + 1 >= minRun)
                        raw.Add(new Candidate(y + 1, start, last + 1, sum));
                    start = -1;
                }
            }
        }
        // fusion : deux segments à moins de 2 % de hauteur qui se chevauchent sont le même sol (on garde le plus fort)
        double near = Math.Max(2, height * 0.02);
        var kept = new List<Candidate>();
        foreach (var c in raw.OrderByDescending(c => c.Score))
        {
            bool dup = kept.Any(k => Math.Abs(k.Y - c.Y) <= near && Overlap(k, c) > 0.3 * Math.Min(c.X2 - c.X1, k.X2 - k.X1));
            if (!dup)
                kept.Add(c);
            if (kept.Count >= max * 3)
                break;
        }
        // une planche a deux bords : on marche sur celui du dessus, pas sur le plafond de la pièce du bas
        double plank = Math.Max(3, height * 0.05);
        var floors = new List<Candidate>();
        foreach (var c in kept.OrderBy(c => c.Y))
            if (!floors.Any(f => c.Y > f.Y && c.Y - f.Y <= plank && Overlap(f, c) > 0.5 * Math.Min(c.X2 - c.X1, f.X2 - f.X1)))
                floors.Add(c);
        return floors.OrderByDescending(c => c.Score).Take(max).OrderBy(c => c.Y).ToList();
    }

    private static double Overlap(Candidate a, Candidate b) => Math.Max(0, Math.Min(a.X2, b.X2) - Math.Max(a.X1, b.X1));

    /// <summary>
    /// Propose des sols pour une carte : analyse l'image réduite, remet à l'échelle de l'image,
    /// écarte ce qui double un sol existant et ce qui laisserait trop peu de place au compagnon
    /// </summary>
    public static List<HabitatFloor> Suggest(BitmapSource image, HabitatMap map)
    {
        int w = Math.Min(480, image.PixelWidth);
        double k = (double)image.PixelWidth / w;
        var small = new TransformedBitmap(image, new ScaleTransform(1 / k, 1 / k));
        var gray = new FormatConvertedBitmap(small, PixelFormats.Gray8, null, 0);
        int gw = gray.PixelWidth, gh = gray.PixelHeight;
        var px = new byte[gw * gh];
        gray.CopyPixels(px, gw, 0);
        double sx = map.Image.Width / gw, sy = map.Image.Height / gh;
        var result = new List<HabitatFloor>();
        foreach (var c in Detect(px, gw, gh))
        {
            double y = Math.Round(c.Y * sy), x1 = Math.Round(c.X1 * sx), x2 = Math.Round(c.X2 * sx);
            if (y < map.PetHeight * 0.8)
                continue; // pas la place de tenir debout au-dessus
            bool existing = map.Floors.Concat(result).Any(f => Math.Abs(f.Y - y) < map.Image.Height * 0.03 && Math.Min(f.Right, x2) - Math.Max(f.Left, x1) > 0);
            if (existing)
                continue;
            result.Add(new HabitatFloor { Y = y, X1 = x1, X2 = x2 });
        }
        // identifiants libres dans la carte
        var tmp = map.Clone();
        foreach (var f in result)
        {
            f.Id = tmp.NewId("f");
            tmp.Floors.Add(f);
        }
        return result;
    }
}
