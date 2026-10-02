using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace VPet_Simulator.Windows.Habitat;

/// <summary>
/// V-Max habitat : paquet de partage « .vmaxhome » (archive zip) contenant l'image du décor, sa carte et un manifeste.
/// À l'import, l'image est copiée dans %APPDATA%\V-Max\habitats\images et sa carte enregistrée à côté des autres.
/// </summary>
public static class HabitatPackage
{
    public const string Extension = ".vmaxhome";
    private const string Format = "vmaxhome";

    /// <summary>Crée le paquet à partir de l'image et de sa carte</summary>
    public static void Export(string imagePath, HabitatMap map, string destination, string? author = null)
    {
        var sha = HabitatMap.Sha256Of(imagePath);
        if (!string.Equals(sha, map.Image.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("L'image a changé depuis le tracé de la carte : rouvre l'éditeur avant d'exporter.");
        string ext = Path.GetExtension(imagePath).ToLowerInvariant();
        if (ext is not (".png" or ".jpg" or ".jpeg" or ".bmp" or ".webp"))
            ext = ".png";
        var exported = map.Clone();
        exported.Image.Path = null; // pas de chemin local dans un fichier partagé
        var tmp = destination + ".tmp";
        using (var zip = ZipFile.Open(tmp, ZipArchiveMode.Create))
        {
            var manifest = new JsonObject
            {
                ["format"] = Format,
                ["version"] = 1,
                ["name"] = map.Image.Name ?? Path.GetFileNameWithoutExtension(imagePath),
                ["image"] = "image" + ext,
                ["sha256"] = sha,
                ["created"] = DateTime.UtcNow.ToString("o"),
                ["author"] = author,
                ["app"] = "V-Max",
            };
            Write(zip, "manifest.json", manifest.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            Write(zip, "map.json", exported.ToJson());
            zip.CreateEntryFromFile(imagePath, "image" + ext, CompressionLevel.NoCompression);
        }
        File.Move(tmp, destination, overwrite: true);
    }

    /// <summary>
    /// Importe un paquet : vérifie l'image (empreinte), la copie dans le dossier des décors et enregistre la carte.
    /// Retourne le chemin de l'image importée.
    /// </summary>
    public static string Import(string package, string? imagesDirectory = null)
    {
        using var zip = ZipFile.OpenRead(package);
        var manifestEntry = zip.GetEntry("manifest.json") ?? throw new InvalidDataException("Ce fichier n'est pas un habitat V-Max (manifeste absent).");
        var manifest = JsonNode.Parse(Read(manifestEntry)) as JsonObject ?? throw new InvalidDataException("Manifeste illisible.");
        if (manifest["format"]?.GetValue<string>() != Format)
            throw new InvalidDataException("Ce fichier n'est pas un habitat V-Max.");
        if ((manifest["version"]?.GetValue<int>() ?? 0) > 1)
            throw new InvalidDataException("Ce paquet vient d'une version plus récente de V-Max.");
        string imageName = manifest["image"]?.GetValue<string>() ?? "";
        // jamais de chemin dans l'archive : seulement un nom simple
        if (imageName.Length == 0 || imageName != Path.GetFileName(imageName))
            throw new InvalidDataException("Nom d'image invalide dans le paquet.");
        var imageEntry = zip.GetEntry(imageName) ?? throw new InvalidDataException("Image absente du paquet.");
        var mapEntry = zip.GetEntry("map.json") ?? throw new InvalidDataException("Carte absente du paquet.");
        if (imageEntry.Length > 64L * 1024 * 1024)
            throw new InvalidDataException("Image trop volumineuse (plus de 64 Mo).");
        var map = HabitatMap.FromJson(Read(mapEntry));

        string dir = imagesDirectory ?? Path.Combine(HabitatMap.Directory, "images");
        Directory.CreateDirectory(dir);
        string tmp = Path.Combine(dir, Guid.NewGuid().ToString("N") + ".tmp");
        imageEntry.ExtractToFile(tmp, overwrite: true);
        string sha = HabitatMap.Sha256Of(tmp);
        if (!string.Equals(sha, map.Image.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(tmp);
            throw new InvalidDataException("L'image ne correspond pas à sa carte (paquet abîmé ou modifié).");
        }
        string ext = Path.GetExtension(imageName).ToLowerInvariant();
        string final = Path.Combine(dir, sha + ext);
        File.Move(tmp, final, overwrite: true);
        map.Image.Path = final;
        map.Image.Name ??= manifest["name"]?.GetValue<string>();
        if (imagesDirectory == null)
            map.Save();
        else
            File.WriteAllText(Path.Combine(imagesDirectory, sha + ".json"), map.ToJson());
        return final;
    }

    /// <summary>Nom proposé pour le fichier d'export</summary>
    public static string SuggestedName(HabitatMap map)
    {
        var name = string.Concat((map.Image.Name ?? "habitat").Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        return name + Extension;
    }

    private static void Write(ZipArchive zip, string name, string text)
    {
        using var w = new StreamWriter(zip.CreateEntry(name, CompressionLevel.Optimal).Open());
        w.Write(text);
    }

    private static string Read(ZipArchiveEntry e)
    {
        using var r = new StreamReader(e.Open());
        return r.ReadToEnd();
    }
}
