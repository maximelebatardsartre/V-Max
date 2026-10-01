using Microsoft.Win32;
using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;

namespace VPet_Simulator.Windows.Habitat;

/// <summary>
/// Façon dont une image remplit sa zone (mêmes modes que le fond d'écran Windows)
/// </summary>
public enum ImageFit
{
    /// <summary>Remplir : couvre toute la zone, l'excédent est rogné</summary>
    Fill,
    /// <summary>Ajuster : image entière, bandes sur les côtés</summary>
    Fit,
    /// <summary>Étirer : déformée pour remplir</summary>
    Stretch,
    /// <summary>Centré : taille réelle au centre</summary>
    Center,
    /// <summary>Mosaïque : taille réelle depuis le coin (la carte s'applique à la première tuile)</summary>
    Tile,
    /// <summary>Étendu sur tous les écrans : comme Remplir, sur le bureau virtuel</summary>
    Span,
}

/// <summary>
/// V-Max habitat : passage des coordonnées de l'image (pixels) aux coordonnées de l'écran (unités WPF) et inversement.
/// </summary>
public readonly record struct HabitatProjection(double ScaleX, double ScaleY, double OffsetX, double OffsetY)
{
    public static readonly HabitatProjection Identity = new(1, 1, 0, 0);

    /// <summary>
    /// Calcule la projection d'une image <paramref name="imageWidth"/>×<paramref name="imageHeight"/> dans <paramref name="target"/>.
    /// <paramref name="pixelToDip"/> sert aux modes « taille réelle » (centré, mosaïque) : 1 pixel d'image = 1 pixel d'écran.
    /// </summary>
    public static HabitatProjection Compute(double imageWidth, double imageHeight, Rect target, ImageFit fit, double pixelToDip = 1)
    {
        if (imageWidth <= 0 || imageHeight <= 0 || target.Width <= 0 || target.Height <= 0)
            return Identity with { OffsetX = target.X, OffsetY = target.Y };
        double sx = target.Width / imageWidth, sy = target.Height / imageHeight;
        double s;
        switch (fit)
        {
            case ImageFit.Stretch:
                return new(sx, sy, target.X, target.Y);
            case ImageFit.Fit:
                s = Math.Min(sx, sy);
                break;
            case ImageFit.Center:
                s = pixelToDip;
                break;
            case ImageFit.Tile:
                return new(pixelToDip, pixelToDip, target.X, target.Y);
            default: // Fill, Span
                s = Math.Max(sx, sy);
                break;
        }
        return new(s, s, target.X + (target.Width - imageWidth * s) / 2, target.Y + (target.Height - imageHeight * s) / 2);
    }

    public Point ToScreen(Point image) => new(OffsetX + image.X * ScaleX, OffsetY + image.Y * ScaleY);
    public Point ToImage(Point screen) => new((screen.X - OffsetX) / ScaleX, (screen.Y - OffsetY) / ScaleY);
    public double ToScreenX(double x) => OffsetX + x * ScaleX;
    public double ToScreenY(double y) => OffsetY + y * ScaleY;
    public double ToImageX(double x) => (x - OffsetX) / ScaleX;
    public double ToImageY(double y) => (y - OffsetY) / ScaleY;

    /// <summary>Rectangle de l'image entière à l'écran</summary>
    public Rect ImageBounds(double imageWidth, double imageHeight) => new(OffsetX, OffsetY, imageWidth * ScaleX, imageHeight * ScaleY);
}

/// <summary>
/// Fond d'écran actuel de Windows (chemin et mode d'ajustement)
/// </summary>
public static class WallpaperInfo
{
    /// <summary>Chemin du fond d'écran (null si couleur unie ou introuvable)</summary>
    public static string? CurrentPath()
    {
        var sb = new StringBuilder(1024);
        if (SystemParametersInfo(SPI_GETDESKWALLPAPER, (uint)sb.Capacity, sb, 0) && sb.Length > 0 && System.IO.File.Exists(sb.ToString()))
            return sb.ToString();
        return null;
    }

    /// <summary>Mode d'ajustement choisi dans les paramètres de Windows</summary>
    public static ImageFit CurrentFit()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop");
            var style = key?.GetValue("WallpaperStyle") as string;
            var tile = key?.GetValue("TileWallpaper") as string;
            return FitFromRegistry(style, tile);
        }
        catch
        {
            return ImageFit.Fill;
        }
    }

    /// <summary>Correspondance des valeurs du registre (WallpaperStyle, TileWallpaper)</summary>
    public static ImageFit FitFromRegistry(string? style, string? tile) => style switch
    {
        "0" => tile == "1" ? ImageFit.Tile : ImageFit.Center,
        "2" => ImageFit.Stretch,
        "6" => ImageFit.Fit,
        "22" => ImageFit.Span,
        _ => ImageFit.Fill, // "10" et inconnu
    };

    private const uint SPI_GETDESKWALLPAPER = 0x0073;
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SystemParametersInfo(uint uiAction, uint uiParam, StringBuilder pvParam, uint fWinIni);
}
