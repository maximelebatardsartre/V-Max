using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using VPet_Simulator.Windows.Agent;
using VPet_Simulator.Windows.Agent.Providers;

namespace VPet_Simulator.Windows.Habitat;

/// <summary>
/// V-Max habitat : suggestion des pièces par l'IA (vision Gemini). L'image du décor, réduite, est envoyée
/// avec la liste des sols ; la réponse JSON (coordonnées en millièmes) devient des pièces à valider dans l'éditeur.
/// L'envoi n'a lieu qu'après l'accord explicite de l'utilisateur.
/// </summary>
public static class HabitatVision
{
    private static readonly string[] Tags = ["kitchen", "bedroom", "living", "bathroom", "office", "garden", "other"];

    private static ProviderInfo Gemini => ProviderRouter.Catalog.First(p => p.Id == "gemini");

    /// <summary>Une clé Gemini est-elle disponible (seul fournisseur gratuit avec la vision dans V-Max) ?</summary>
    public static bool Available => !string.IsNullOrEmpty(ProviderRouter.KeyFor(Gemini));

    public static async Task<List<HabitatRoom>> SuggestRoomsAsync(BitmapSource image, HabitatMap map, string? model, CancellationToken ct)
    {
        var key = ProviderRouter.KeyFor(Gemini) ?? throw new InvalidOperationException("Ajoute une clé Gemini (gratuite) dans Paramètres › Intelligence artificielle.");
        string jpeg = Convert.ToBase64String(Jpeg(image, 1024));
        var floors = string.Join(", ", map.Floors.Select(f => $"{f.Id}: y={Thousandths(f.Y, map.Image.Height)}, x={Thousandths(f.Left, map.Image.Width)}→{Thousandths(f.Right, map.Image.Width)}"));
        string prompt =
            "Voici un décor 2D (souvent une maison vue en coupe) dans lequel vit un petit compagnon. " +
            "Repère les pièces ou zones distinctes où il pourrait vivre : cuisine, chambre, salon, salle de bain, bureau, jardin… " +
            "Réponds UNIQUEMENT avec du JSON de la forme {\"pieces\":[{\"nom\":\"Cuisine\",\"type\":\"kitchen\",\"x\":0,\"y\":0,\"largeur\":0,\"hauteur\":0}]}. " +
            "Coordonnées en millièmes de la largeur et de la hauteur de l'image, origine en haut à gauche ; chaque cadre va du plafond au sol de la pièce. " +
            "Types possibles : " + string.Join(", ", Tags) + ". Noms courts en français. " +
            (floors.Length > 0 ? "Sols déjà tracés (millièmes) : " + floors + "." : "");
        var contents = new JsonArray(new JsonObject
        {
            ["role"] = "user",
            ["parts"] = new JsonArray(
                new JsonObject { ["text"] = prompt },
                new JsonObject { ["inline_data"] = new JsonObject { ["mime_type"] = "image/jpeg", ["data"] = jpeg } }),
        });
        var client = new GeminiClient(key, model);
        var sb = new StringBuilder();
        await foreach (var chunk in client.StreamAsync(contents, "Tu analyses des images de décor et tu réponds en JSON strict.", null, ct))
            sb.Append(chunk.Text);
        return Parse(sb.ToString(), map);
    }

    /// <summary>
    /// Transforme la réponse du modèle en pièces (pixels de l'image). Tolère les blocs ```json et le texte autour.
    /// </summary>
    public static List<HabitatRoom> Parse(string text, HabitatMap map)
    {
        var rooms = new List<HabitatRoom>();
        int a = text.IndexOf('{'), b = text.LastIndexOf('}');
        if (a < 0 || b <= a)
            return rooms;
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(text[a..(b + 1)]);
        }
        catch
        {
            return rooms;
        }
        var list = root?["pieces"] as JsonArray ?? root?["rooms"] as JsonArray;
        if (list == null)
            return rooms;
        double W = map.Image.Width, H = map.Image.Height;
        foreach (var n in list.OfType<JsonObject>())
        {
            string name = (n["nom"] ?? n["name"])?.ToString().Trim() ?? "";
            string tag = (n["type"] ?? n["tag"])?.ToString().Trim().ToLowerInvariant() ?? "other";
            double x = Num(n["x"]), y = Num(n["y"]), w = Num(n["largeur"] ?? n["width"]), h = Num(n["hauteur"] ?? n["height"]);
            if (name.Length == 0 || w <= 10 || h <= 10)
                continue;
            x = Math.Clamp(x, 0, 1000); y = Math.Clamp(y, 0, 1000);
            w = Math.Min(w, 1000 - x); h = Math.Min(h, 1000 - y);
            if (w <= 10 || h <= 10)
                continue;
            if (!Tags.Contains(tag))
                tag = "other";
            rooms.Add(new HabitatRoom
            {
                Name = name.Length > 40 ? name[..40] : name,
                Tag = tag,
                X = Math.Round(x / 1000 * W), Y = Math.Round(y / 1000 * H),
                Width = Math.Round(w / 1000 * W), Height = Math.Round(h / 1000 * H),
            });
        }
        return rooms;
    }

    private static double Num(JsonNode? n)
    {
        if (n is null) return 0;
        try { return n.GetValue<double>(); } catch { }
        return double.TryParse(n.ToString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : 0;
    }

    private static int Thousandths(double v, double total) => (int)Math.Round(v / Math.Max(1, total) * 1000);

    private static byte[] Jpeg(BitmapSource image, int maxWidth)
    {
        BitmapSource src = image;
        if (image.PixelWidth > maxWidth)
        {
            double k = (double)maxWidth / image.PixelWidth;
            src = new TransformedBitmap(image, new ScaleTransform(k, k));
        }
        var enc = new JpegBitmapEncoder { QualityLevel = 82 };
        enc.Frames.Add(BitmapFrame.Create(new FormatConvertedBitmap(src, PixelFormats.Bgr24, null, 0)));
        using var ms = new MemoryStream();
        enc.Save(ms);
        return ms.ToArray();
    }
}
