using System;
using System.IO;
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace VPet_Simulator.Windows.Habitat;

/// <summary>
/// V-Max habitat : proportions du compagnon dans sa fenêtre carrée, mesurées sur une image « debout » du personnage.
/// Sert à poser les pieds sur un sol et à convertir la taille voulue en zoom.
/// </summary>
public sealed record PetMetrics(double FootRatio, double HeadRatio, double CenterRatio, double HalfWidthRatio, BitmapSource? Standing)
{
    /// <summary>Valeurs du personnage par défaut (mesurées sur Default/Nomal)</summary>
    public static readonly PetMetrics Default = new(0.97, 0.034, 0.515, 0.18, null);

    /// <summary>Hauteur visible du personnage debout, en fraction de la fenêtre</summary>
    public double StandingRatio => Math.Max(0.1, FootRatio - HeadRatio);

    /// <summary>Taille de la fenêtre carrée (unités WPF) pour que le personnage mesure <paramref name="visibleHeight"/></summary>
    public double WindowSizeFor(double visibleHeight) => visibleHeight / StandingRatio;

    /// <summary>
    /// Mesure le personnage à partir d'une image de son animation au repos (cherchée dans « Default »)
    /// </summary>
    public static PetMetrics Measure(MainWindow mw)
    {
        try
        {
            var loader = mw.Pets.Find(x => x.Name == mw.Set.PetGraph);
            var root = loader?.path.FirstOrDefault(Directory.Exists);
            if (root == null)
                return Default;
            var dir = new[] { "Default", "default", "IDEL", "Idel" }.Select(d => Path.Combine(root, d)).FirstOrDefault(Directory.Exists) ?? root;
            var file = Directory.EnumerateFiles(dir, "*.png", SearchOption.AllDirectories)
                .OrderBy(f => f.Contains("Nomal", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ThenBy(f => f, StringComparer.Ordinal)
                .FirstOrDefault();
            return file == null ? Default : FromImage(file);
        }
        catch
        {
            return Default;
        }
    }

    /// <summary>
    /// Boîte englobante des pixels visibles (alpha &gt; 24) d'une image carrée du personnage
    /// </summary>
    public static PetMetrics FromImage(string file)
    {
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.UriSource = new Uri(file);
        bmp.EndInit();
        bmp.Freeze();
        var conv = new FormatConvertedBitmap(bmp, PixelFormats.Bgra32, null, 0);
        int w = conv.PixelWidth, h = conv.PixelHeight, stride = w * 4;
        var px = new byte[stride * h];
        conv.CopyPixels(px, stride, 0);
        int minX = w, maxX = -1, minY = h, maxY = -1;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                if (px[y * stride + x * 4 + 3] > 24)
                {
                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                }
        if (maxX < 0)
            return Default with { Standing = bmp };
        return new PetMetrics((maxY + 1) / (double)h, minY / (double)h, (minX + maxX + 1) / 2.0 / w, (maxX + 1 - minX) / 2.0 / w, bmp);
    }
}
