using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using VPet_Simulator.Windows.Interface;

namespace VPet_Simulator.Windows.Habitat;

/// <summary>
/// V-Max habitat : carte tracée par l'utilisateur sur une image (sols, échelles, chutes, pièces, emplacements).
/// Toutes les coordonnées sont en pixels de l'image ; la carte est liée à l'image par son empreinte SHA-256.
/// </summary>
public sealed class HabitatMap
{
    public const int CurrentVersion = 1;

    public int Version { get; set; } = CurrentVersion;
    public HabitatImage Image { get; set; } = new();
    /// <summary>Hauteur visible du compagnon debout, en pixels de l'image</summary>
    public double PetHeight { get; set; }
    public List<HabitatFloor> Floors { get; set; } = new();
    public List<HabitatClimb> Climbs { get; set; } = new();
    public List<HabitatDrop> Drops { get; set; } = new();
    public List<HabitatRoom> Rooms { get; set; } = new();
    public List<HabitatSpot> Spots { get; set; } = new();

    /// <summary>Taille par défaut du compagnon : un quart de la hauteur de l'image</summary>
    public static double DefaultPetHeight(double imageHeight) => Math.Round(imageHeight / 4);

    #region Requêtes géométriques
    /// <summary>
    /// Sol sur lequel se trouve un point (pieds), à <paramref name="tolerance"/> pixels près verticalement
    /// </summary>
    public HabitatFloor? FloorAt(double x, double y, double tolerance) =>
        Floors.Where(f => f.Contains(x) && Math.Abs(f.Y - y) <= tolerance)
              .OrderBy(f => Math.Abs(f.Y - y))
              .FirstOrDefault();

    /// <summary>
    /// Premier sol sous un point (le plus haut parmi ceux situés dessous et qui couvrent x)
    /// </summary>
    public HabitatFloor? FloorBelow(double x, double y) =>
        Floors.Where(f => f.Contains(x) && f.Y >= y)
              .OrderBy(f => f.Y)
              .FirstOrDefault();

    /// <summary>
    /// Sol le plus proche d'un point (distance au segment)
    /// </summary>
    public HabitatFloor? NearestFloor(double x, double y) =>
        Floors.OrderBy(f => f.DistanceTo(x, y)).FirstOrDefault();

    /// <summary>
    /// Sol où atterrir quand on lâche le compagnon : d'abord celui juste dessous, sinon le plus proche
    /// </summary>
    public HabitatFloor? LandingFloor(double x, double y) => FloorBelow(x, y) ?? NearestFloor(x, y);

    /// <summary>Sol par défaut (le plus long, puis le plus bas)</summary>
    public HabitatFloor? MainFloor => Floors.OrderByDescending(f => f.Length).ThenByDescending(f => f.Y).FirstOrDefault();

    public HabitatFloor? Floor(string? id) => id == null ? null : Floors.FirstOrDefault(f => f.Id == id);

    /// <summary>Pièce par nom (insensible à la casse et aux accents), puis par type</summary>
    public HabitatRoom? RoomNamed(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;
        string key = Fold(name);
        return Rooms.FirstOrDefault(r => Fold(r.Name) == key) ?? Rooms.FirstOrDefault(r => r.Tag != null && Fold(r.Tag) == key);
    }

    /// <summary>Pièce qui contient un point (la plus petite si elles se chevauchent)</summary>
    public HabitatRoom? RoomAt(double x, double y) =>
        Rooms.Where(r => r.Contains(x, y)).OrderBy(r => r.Width * r.Height).FirstOrDefault();

    /// <summary>
    /// Portions de sols praticables dans une pièce (sol dont la hauteur est dans la pièce, coupé à sa largeur)
    /// </summary>
    public List<(HabitatFloor floor, double left, double right)> FloorsIn(HabitatRoom room, double tolerance = 4)
    {
        var list = new List<(HabitatFloor, double, double)>();
        foreach (var f in Floors)
        {
            if (f.Y < room.Y - tolerance || f.Y > room.Y + room.Height + tolerance)
                continue;
            double l = Math.Max(f.Left, room.X), r = Math.Min(f.Right, room.X + room.Width);
            if (r - l > 1)
                list.Add((f, l, r));
        }
        return list.OrderByDescending(s => s.Item3 - s.Item2).ToList();
    }

    /// <summary>
    /// Point d'arrivée dans une pièce : un emplacement de l'activité voulue, sinon un emplacement de la pièce,
    /// sinon le milieu de la plus grande portion de sol
    /// </summary>
    public (HabitatFloor floor, double x, HabitatSpot? spot)? TargetIn(HabitatRoom room, string? activity, Random? rnd = null)
    {
        var spots = Spots.Where(s => s.Room == room.Id && Floor(s.Floor) != null).ToList();
        var match = spots.Where(s => activity != null && string.Equals(s.Activity, activity, StringComparison.OrdinalIgnoreCase)).ToList();
        var pick = match.Count > 0 ? match : activity == null || activity == "relax" ? spots : new List<HabitatSpot>();
        if (pick.Count > 0)
        {
            var s = pick[(rnd ?? Random.Shared).Next(pick.Count)];
            return (Floor(s.Floor)!, s.X, s);
        }
        var seg = FloorsIn(room).FirstOrDefault();
        if (seg.floor == null)
            return null;
        double margin = Math.Min(PetHeight * 0.3, (seg.right - seg.left) / 3);
        double x = rnd == null ? (seg.left + seg.right) / 2 : seg.left + margin + rnd.NextDouble() * Math.Max(0, seg.right - seg.left - 2 * margin);
        return (seg.floor, x, null);
    }

    /// <summary>Minuscules sans accents, pour comparer des noms de pièces saisis à la main</summary>
    public static string Fold(string s)
    {
        var n = s.Trim().ToLowerInvariant().Normalize(System.Text.NormalizationForm.FormD);
        return new string(n.Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark).ToArray());
    }
    #endregion

    /// <summary>
    /// Identifiant libre de la forme « f1 », « f2 »…
    /// </summary>
    public string NewId(string prefix)
    {
        var used = new HashSet<string>(Floors.Select(f => f.Id)
            .Concat(Climbs.Select(c => c.Id)).Concat(Drops.Select(d => d.Id))
            .Concat(Rooms.Select(r => r.Id)).Concat(Spots.Select(s => s.Id)));
        for (int i = 1; ; i++)
            if (!used.Contains(prefix + i))
                return prefix + i;
    }

    /// <summary>
    /// Problèmes bloquants ou gênants, formulés pour l'utilisateur
    /// </summary>
    public List<string> Validate()
    {
        var issues = new List<string>();
        if (Floors.Count == 0)
            issues.Add("Trace au moins un sol pour que le compagnon puisse marcher.");
        foreach (var f in Floors)
        {
            if (f.Y - PetHeight < 0)
                issues.Add($"Le sol {f.Id} est trop près du haut de l'image : le compagnon n'y tient pas debout.");
            if (f.Length < PetHeight * 0.5)
                issues.Add($"Le sol {f.Id} est très court : le compagnon ne pourra presque pas y marcher.");
        }
        return issues;
    }

    #region Fichier
    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public string ToJson() => JsonSerializer.Serialize(this, Json);

    public static HabitatMap FromJson(string json)
    {
        var map = JsonSerializer.Deserialize<HabitatMap>(json, Json) ?? throw new InvalidDataException("Carte vide.");
        if (map.Version > CurrentVersion)
            throw new InvalidDataException($"Cette carte vient d'une version plus récente de V-Max (format {map.Version}).");
        return map;
    }

    public HabitatMap Clone() => FromJson(ToJson());

    /// <summary>Dossier des cartes : %APPDATA%\V-Max\habitats</summary>
    public static string Directory => Path.Combine(ExtensionValue.DataDirectory, "habitats");

    public static string PathFor(string sha256) => Path.Combine(Directory, sha256 + ".json");

    public static HabitatMap? TryLoad(string sha256)
    {
        var p = PathFor(sha256);
        if (!File.Exists(p))
            return null;
        try
        {
            return FromJson(File.ReadAllText(p));
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Écrit la carte (fichier temporaire puis remplacement, pour ne jamais laisser une carte à moitié écrite)</summary>
    public void Save()
    {
        System.IO.Directory.CreateDirectory(Directory);
        var p = PathFor(Image.Sha256);
        var tmp = p + ".tmp";
        File.WriteAllText(tmp, ToJson());
        File.Move(tmp, p, overwrite: true);
    }

    public static string Sha256Of(string file)
    {
        using var s = File.OpenRead(file);
        return Convert.ToHexString(SHA256.HashData(s)).ToLowerInvariant();
    }
    #endregion
}

public sealed class HabitatImage
{
    public string Sha256 { get; set; } = "";
    public double Width { get; set; }
    public double Height { get; set; }
    public string? Name { get; set; }
    /// <summary>Dernier emplacement connu de l'image (indicatif : la carte suit l'empreinte, pas le chemin)</summary>
    public string? Path { get; set; }
}

/// <summary>Sol : segment horizontal sur lequel le compagnon marche (y = ligne des pieds)</summary>
public sealed class HabitatFloor
{
    public string Id { get; set; } = "";
    public double Y { get; set; }
    public double X1 { get; set; }
    public double X2 { get; set; }

    [JsonIgnore] public double Left => Math.Min(X1, X2);
    [JsonIgnore] public double Right => Math.Max(X1, X2);
    [JsonIgnore] public double Length => Right - Left;

    public bool Contains(double x) => x >= Left && x <= Right;

    public double DistanceTo(double x, double y)
    {
        double dx = x < Left ? Left - x : x > Right ? x - Right : 0;
        double dy = y - Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    public double Clamp(double x) => Math.Clamp(x, Left, Right);
}

/// <summary>Axe d'escalade vertical (échelle, pilier) entre deux sols</summary>
public sealed class HabitatClimb
{
    public string Id { get; set; } = "";
    public double X { get; set; }
    public double Y1 { get; set; }
    public double Y2 { get; set; }
    /// <summary>Côté de la paroi pour l'animation : « left » (climb.left) ou « right » (climb.right)</summary>
    public string Side { get; set; } = "left";
}

/// <summary>Chute volontaire d'un sol vers un sol plus bas</summary>
public sealed class HabitatDrop
{
    public string Id { get; set; } = "";
    public string From { get; set; } = "";
    public string To { get; set; } = "";
    public double X { get; set; }
}

/// <summary>Pièce nommée</summary>
public sealed class HabitatRoom
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    /// <summary>Type de pièce pour les routines : kitchen, bedroom, living, bathroom, office, garden…</summary>
    public string? Tag { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }

    public bool Contains(double x, double y) => x >= X && x <= X + Width && y >= Y && y <= Y + Height;
}

/// <summary>Emplacement où se joue une occupation (l'animation apporte ses meubles)</summary>
public sealed class HabitatSpot
{
    public string Id { get; set; } = "";
    public string? Room { get; set; }
    public string Floor { get; set; } = "";
    public double X { get; set; }
    public string? Activity { get; set; }
}
