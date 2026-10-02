using LinePutScript;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using VPet_Simulator.Core;

namespace VPet_Simulator.Windows;

/// <summary>
/// V-Max : ajout manuel de mods au catalogue du Studio (glisser-déposer d'un dossier ou d'un .zip).
/// Même lecture et mêmes contrôles que tools/mods/ingest.py (les animations sont classées par le GraphInfo du jeu) ;
/// le mod est copié dans %APPDATA%\V-Max\studio\mods\&lt;id&gt; puis ajouté à catalog.json avec "origin": "manuel"
/// (ingest.py conserve ces entrées quand il régénère le catalogue).
/// </summary>
public static class ModImporter
{
    public sealed record Result(string Id, string Name, string Category, string? Refused);

    private static readonly string[] HiddenCategories = ["Exclu", "Vide", "Erreur"];
    private static readonly string[] LoaderLines = ["pnganimation", "apnganimation", "picture", "foodanimation"];
    private static readonly Dictionary<string, string> TextLines = new(StringComparer.OrdinalIgnoreCase)
    {
        ["clicktext"] = "click", ["lowfoodtext"] = "low", ["lowdrinktext"] = "low", ["selecttext"] = "select",
    };

    static ModImporter() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    /// <summary>
    /// Importe tout ce que contient <paramref name="path"/> : un mod, un dossier de mods ou une archive .zip.
    /// Les mods refusés ne sont pas copiés.
    /// </summary>
    public static List<Result> Import(string path, string studioDir, string coreFoodDir)
    {
        var results = new List<Result>();
        string? temp = null;
        try
        {
            var root = path;
            if (File.Exists(path) && path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                temp = Path.Combine(Path.GetTempPath(), "vmax-import-" + Guid.NewGuid().ToString("N")[..8]);
                ZipFile.ExtractToDirectory(path, temp);
                root = temp;
            }
            if (!Directory.Exists(root))
                return [new Result(Path.GetFileName(path), Path.GetFileName(path), "Erreur", "ni un dossier ni une archive .zip")];

            var modsDir = Path.Combine(studioDir, "mods");
            var catalogPath = Path.Combine(studioDir, "catalog.json");
            var coreFoods = CoreFoodNames(coreFoodDir);
            foreach (var dir in FindModRoots(new DirectoryInfo(root)))
            {
                var id = ModId(dir, temp == null ? null : Path.GetFileNameWithoutExtension(path));
                var check = Build(dir, id, coreFoods);
                var name = check["name"]?.GetValue<string>() ?? dir.Name;
                var category = check["category"]!.GetValue<string>();
                if (HiddenCategories.Contains(category))
                {
                    results.Add(new Result(id, name, category, check["reason"]?.GetValue<string>() ?? "refusé"));
                    continue;
                }
                var dest = new DirectoryInfo(Path.Combine(modsDir, id));
                if (!string.Equals(dir.FullName.TrimEnd('\\'), dest.FullName.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                {
                    if (dest.Exists)
                        dest.Delete(true);// ancienne copie de ce même mod, faite par le Studio
                    CopyDirectory(dir, dest);
                }
                var entry = Build(dest, id, coreFoods);
                entry["origin"] = "manuel";
                AddToCatalog(catalogPath, entry);
                results.Add(new Result(id, name, entry["category"]!.GetValue<string>(), null));
            }
            if (results.Count == 0)
                results.Add(new Result(Path.GetFileName(path), Path.GetFileName(path), "Vide", "aucun fichier info.lps : ce n'est pas un mod VPet"));
        }
        finally
        {
            if (temp != null)
                try { Directory.Delete(temp, true); } catch { }
        }
        return results;
    }

    /// <summary>Dossiers contenant un info.lps : le dossier lui-même, sinon ses sous-dossiers (2 niveaux)</summary>
    private static IEnumerable<DirectoryInfo> FindModRoots(DirectoryInfo d, int depth = 0)
    {
        if (File.Exists(Path.Combine(d.FullName, "info.lps")))
        {
            yield return d;
            yield break;
        }
        if (depth >= 2)
            yield break;
        foreach (var sub in d.EnumerateDirectories())
            foreach (var r in FindModRoots(sub, depth + 1))
                yield return r;
    }

    private static string ModId(DirectoryInfo dir, string? archiveName)
    {
        var item = ReadLps(Path.Combine(dir.FullName, "info.lps")).FirstOrDefault(l => l.Name.Equals("itemid", StringComparison.OrdinalIgnoreCase))?.Info;
        if (item != null && Regex.IsMatch(item.Trim(), @"^\d{6,}$"))
            return item.Trim();
        if (Regex.IsMatch(dir.Name, @"^\d{6,}$"))
            return dir.Name;
        var slug = Regex.Replace((archiveName ?? dir.Name).ToLowerInvariant(), @"[^a-z0-9]+", "-").Trim('-');
        return "manuel-" + (slug.Length > 0 ? slug[..Math.Min(slug.Length, 40)] : Guid.NewGuid().ToString("N")[..8]);
    }

    #region Lecture
    private static string ReadText(string path)
    {
        var bytes = File.ReadAllBytes(path);
        try
        {
            return new UTF8Encoding(false, true).GetString(bytes).TrimStart('﻿');
        }
        catch (DecoderFallbackException)
        {
            return Encoding.GetEncoding("GB18030").GetString(bytes);
        }
    }

    private static List<ILine> ReadLps(string path)
    {
        if (!File.Exists(path))
            return [];
        try
        {
            return new LpsDocument(ReadText(path)).Where(l => !string.IsNullOrWhiteSpace(l.Name) && !l.Name.StartsWith("///")).ToList();
        }
        catch
        {
            return [];
        }
    }

    /// <summary>Fichier d'origine : la sauvegarde *.codex-*.bak laissée par un renommage précédent, si elle existe</summary>
    private static (string path, bool restored) Original(string path)
    {
        var dir = Path.GetDirectoryName(path)!;
        var bak = Directory.Exists(dir) ? Directory.GetFiles(dir, Path.GetFileName(path) + ".codex-*.bak").OrderBy(x => x).FirstOrDefault() : null;
        return bak != null ? (bak, true) : (path, false);
    }

    private static string Get(ILine line, string key)
    {
        foreach (ISub s in line)
            if (string.Equals(s.Name, key, StringComparison.OrdinalIgnoreCase))
                return s.Info ?? "";
        return "";
    }

    private static HashSet<string> CoreFoodNames(string coreFoodDir)
    {
        var set = new HashSet<string>();
        if (Directory.Exists(coreFoodDir))
            foreach (var f in Directory.GetFiles(coreFoodDir, "*.lps"))
                foreach (var l in ReadLps(f))
                    if (l.Name.Equals("food", StringComparison.OrdinalIgnoreCase))
                        set.Add(Get(l, "name"));
        return set;
    }
    #endregion

    #region Construction de l'entrée du catalogue
    private sealed class Strings
    {
        public readonly Dictionary<string, JsonObject> Items = new();

        public void Add(string? key, string kind, string context = "")
        {
            key = key?.Trim();
            if (string.IsNullOrEmpty(key) || Regex.IsMatch(key, @"^[\d\W_]+$"))
                return;
            if (!Items.TryGetValue(key, out var item))
            {
                item = new JsonObject { ["key"] = key, ["kind"] = kind, ["context"] = context, ["hints"] = new JsonObject() };
                if (ModBlocklist.LinePattern.IsMatch(key))
                    item["blocked"] = true;
                Items[key] = item;
            }
            else if (item["kind"]!.GetValue<string>() == "lang" && kind != "lang")
            {
                item["kind"] = kind;
                item["context"] = context;
            }
        }

        public void Hint(string key, string culture, string value)
        {
            key = key.Trim();
            if (key.Length == 0 || string.IsNullOrWhiteSpace(value))
                return;
            Add(key, "lang");
            if (Items.TryGetValue(key, out var item) && item["hints"] is JsonObject h && !h.ContainsKey(culture))
                h[culture] = value;
        }
    }

    private static JsonObject Refused(string id, string source, string category, string reason)
        => new() { ["id"] = id, ["category"] = category, ["reason"] = reason, ["source"] = source };

    public static JsonObject Build(DirectoryInfo d, string id, HashSet<string> coreFoods)
    {
        var (infoPath, infoRestored) = Original(Path.Combine(d.FullName, "info.lps"));
        var info = ReadLps(infoPath);
        var head = info.FirstOrDefault(l => l.Name.Equals("vupmod", StringComparison.OrdinalIgnoreCase));
        string Field(string n) => info.FirstOrDefault(l => l.Name.Equals(n, StringComparison.OrdinalIgnoreCase))?.Info ?? "";
        var name = head?.Info ?? "";
        var intro = Field("intro");
        var itemId = Field("itemid");

        if (ModBlocklist.IsBlocked(d, itemId) || ModBlocklist.IsBlockedId(id) || ModBlocklist.TitlePattern.IsMatch(name + " " + intro))
            return Refused(id, d.FullName, "Exclu", "contenu à caractère sexuel");
        if (info.Count == 0)
            return Refused(id, d.FullName, "Vide", "fiche info.lps illisible ou absente");

        var strings = new Strings();
        strings.Add(name, "mod_name");
        strings.Add(intro, "mod_intro");
        foreach (var l in info.Where(l => l.Name.Equals("lang", StringComparison.OrdinalIgnoreCase) || l.Name.Equals("culturedatas", StringComparison.OrdinalIgnoreCase)))
            foreach (ISub s in l)
                strings.Hint(s.Name, string.IsNullOrEmpty(l.Info) ? "?" : l.Info, s.Info ?? "");
        var restored = new JsonArray();
        if (infoRestored)
            restored.Add(Path.GetRelativePath(d.FullName, infoPath));

        // compagnons, occupations, animations
        var works = new List<(string type, string name, string graph, JsonObject json)>();
        var graphs = new List<JsonObject>();
        var pets = new JsonArray();
        var otherLines = new JsonObject();
        var petDir = new DirectoryInfo(Path.Combine(d.FullName, "pet"));
        var walked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (petDir.Exists)
        {
            foreach (var lps in petDir.GetFiles("*.lps"))
            {
                var (src, r) = Original(lps.FullName);
                if (r)
                    restored.Add(Path.GetRelativePath(d.FullName, src));
                var lines = ReadLps(src);
                var petLine = lines.FirstOrDefault(l => l.Name.Equals("pet", StringComparison.OrdinalIgnoreCase));
                var folderName = petLine != null && Get(petLine, "path").Length > 0 ? Get(petLine, "path") : Path.GetFileNameWithoutExtension(lps.Name);
                var folder = new DirectoryInfo(Path.Combine(petDir.FullName, folderName));
                pets.Add(new JsonObject { ["file"] = Path.GetRelativePath(d.FullName, lps.FullName), ["pet"] = petLine?.Info ?? folderName, ["folder"] = folder.FullName });
                foreach (var l in lines)
                {
                    var n = l.Name.ToLowerInvariant();
                    if (n == "work")
                    {
                        var w = new JsonObject
                        {
                            ["type"] = Get(l, "Type") is { Length: > 0 } t ? t : "Work", ["name"] = Get(l, "Name"), ["graph"] = Get(l, "Graph"),
                            ["money"] = Get(l, "MoneyBase"), ["level"] = Get(l, "LevelLimit"), ["time"] = Get(l, "Time"),
                        };
                        works.Add((w["type"]!.GetValue<string>(), w["name"]!.GetValue<string>(), w["graph"]!.GetValue<string>(), w));
                        strings.Add(w["name"]!.GetValue<string>(), "work", $"occupation ({w["type"]})");
                    }
                    else if (n != "pet")
                        otherLines[n] = (otherLines[n]?.GetValue<int>() ?? 0) + 1;
                }
                if (folder.Exists && walked.Add(folder.FullName))
                    WalkGraphs(folder, folder.FullName, graphs);
            }
            foreach (var folder in petDir.GetDirectories())
                if (walked.Add(folder.FullName))
                {
                    pets.Add(new JsonObject { ["file"] = "", ["pet"] = folder.Name, ["folder"] = folder.FullName });
                    WalkGraphs(folder, folder.FullName, graphs);
                }
        }

        // nourriture
        var foods = new JsonArray();
        var foodDir = Path.Combine(d.FullName, "food");
        if (Directory.Exists(foodDir))
            foreach (var f in Directory.GetFiles(foodDir, "*.lps"))
                foreach (var l in ReadLps(f).Where(l => l.Name.Equals("food", StringComparison.OrdinalIgnoreCase)))
                {
                    var fname = Get(l, "name");
                    foods.Add(new JsonObject { ["name"] = fname, ["type"] = Get(l, "type"), ["price"] = Get(l, "price"), ["desc"] = Get(l, "desc") });
                    strings.Add(fname, "food_name", $"nourriture ({Get(l, "type")})");
                    strings.Add(Get(l, "desc"), "food_desc", $"description de « {fname} »");
                }

        // répliques
        var texts = new JsonObject();
        var textDir = Path.Combine(d.FullName, "text");
        if (Directory.Exists(textDir))
            foreach (var f in Directory.GetFiles(textDir, "*.lps", SearchOption.AllDirectories))
            {
                var (src, r) = Original(f);
                if (r)
                    restored.Add(Path.GetRelativePath(d.FullName, src));
                foreach (var l in ReadLps(src))
                {
                    if (!TextLines.TryGetValue(l.Name, out var kind))
                        continue;
                    texts[kind] = (texts[kind]?.GetValue<int>() ?? 0) + 1;
                    var ctx = Get(l, "Working") is { Length: > 0 } wk ? $"pendant « {wk} »" : Get(l, "State");
                    strings.Add(Get(l, "Text"), kind, ctx);
                    if (kind == "select")
                        strings.Add(Get(l, "Choose"), "select_choice", ctx);
                }
            }

        // traductions fournies par le mod
        var langDir = Path.Combine(d.FullName, "lang");
        if (Directory.Exists(langDir))
            foreach (var culture in Directory.GetDirectories(langDir))
                foreach (var f in Directory.GetFiles(culture, "*.lps"))
                    foreach (var l in ReadLps(f))
                        strings.Hint(l.Name, Path.GetFileName(culture), l.Info ?? "");

        // plugins
        var plugins = new JsonArray();
        var pluginDir = Path.Combine(d.FullName, "plugin");
        if (Directory.Exists(pluginDir))
            foreach (var dll in Directory.GetFiles(pluginDir, "*.dll"))
                plugins.Add(new JsonObject
                {
                    ["file"] = Path.GetFileName(dll), ["bytes"] = new FileInfo(dll).Length,
                    ["sha256"] = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(dll))).ToLowerInvariant(),
                });
        var pluginTexts = new JsonArray(Directory.GetFiles(d.FullName, "*.txt").Select(f => (JsonNode)Path.GetFileName(f)!).ToArray());

        // animations regroupées par (type, nom)
        var animations = new JsonArray();
        foreach (var grp in graphs.GroupBy(g => (g["type"]!.GetValue<string>(), g["name"]!.GetValue<string>())).OrderBy(g => g.Key.Item1).ThenBy(g => g.Key.Item2))
        {
            var first = grp.First();
            var gname = grp.Key.Item2;
            animations.Add(new JsonObject
            {
                ["type"] = grp.Key.Item1, ["name"] = gname,
                ["modes"] = new JsonArray(grp.Select(g => g["mode"]!.GetValue<string>()).Distinct().Order().Select(x => (JsonNode)x!).ToArray()),
                ["phases"] = new JsonArray(grp.Select(g => g["phase"]!.GetValue<string>()).Distinct().Order().Select(x => (JsonNode)x!).ToArray()),
                ["frames"] = grp.Sum(g => g["frames"]!.GetValue<int>()),
                ["duration_ms"] = grp.Sum(g => g["duration_ms"]!.GetValue<int>()),
                ["bytes"] = grp.Sum(g => g["bytes"]!.GetValue<long>()),
                ["preview"] = first["preview"]!.GetValue<string>(),
                ["variants"] = new JsonArray(grp.Select(g => (JsonNode)new JsonObject
                {
                    ["mode"] = g["mode"]!.GetValue<string>(), ["phase"] = g["phase"]!.GetValue<string>(), ["path"] = g["path"]!.GetValue<string>(),
                    ["frames"] = g["frames"]!.GetValue<int>(), ["duration_ms"] = g["duration_ms"]!.GetValue<int>(), ["loader"] = g["loader"]!.GetValue<string>(),
                }).ToArray()),
                ["works"] = new JsonArray(works.Where(w => w.graph.Equals(gname, StringComparison.OrdinalIgnoreCase)).Select(w => (JsonNode)w.name!).ToArray()),
            });
        }

        // contrôle du contenu (noms d'animations et répliques)
        if (graphs.Any(g => ModBlocklist.ContentPattern.IsMatch(g["name"]!.GetValue<string>())) || strings.Items.Keys.Any(k => ModBlocklist.ContentPattern.IsMatch(k)))
            return Refused(id, d.FullName, "Exclu", "contenu à caractère sexuel (repéré dans les animations ou les textes)");

        // catégorie (mêmes règles qu'ingest.py)
        var tags = new SortedSet<string>();
        static string CategoryOf(string type) => type.ToLowerInvariant() switch { "work" => "Travail", "study" => "Études", _ => "Loisir" };
        foreach (var w in works)
            tags.Add(CategoryOf(w.type));
        if (foods.Count > 0)
            tags.Add("Nourriture");
        if (animations.Any(a => a!["type"]!.GetValue<string>() != "Work" && a["works"]!.AsArray().Count == 0))
            tags.Add("Attitude");
        if (plugins.Count > 0)
            tags.Add("Système");
        if (texts.Count > 0 || pluginTexts.Count > 0)
            tags.Add("Dialogues");
        bool overrides = foods.Count > 0 && foods.Count(f => coreFoods.Contains(f!["name"]!.GetValue<string>())) >= 0.8 * foods.Count;
        string category;
        if (overrides)
        {
            tags.Remove("Nourriture");
            tags.Add("Langue");
            category = "Langue";
        }
        else if (foods.Count > works.Count)
            category = "Nourriture";
        else if (works.Count > 0)
            category = works.GroupBy(w => CategoryOf(w.type)).OrderByDescending(g => g.Count()).First().Key;
        else
            category = new[] { "Nourriture", "Attitude", "Système", "Dialogues" }.FirstOrDefault(tags.Contains) ?? (Directory.Exists(langDir) ? "Langue" : "Vide");
        if (category == "Vide")
            return Refused(id, d.FullName, "Vide", "ni animation, ni occupation, ni texte, ni plugin");

        var nameEn = strings.Items.TryGetValue(name, out var nameItem) && nameItem["hints"]?["en"] is JsonNode en ? en.GetValue<string>() : "";
        long size = 0;
        int count = 0;
        foreach (var f in d.EnumerateFiles("*", SearchOption.AllDirectories))
        {
            size += f.Length;
            count++;
        }
        return new JsonObject
        {
            ["id"] = id, ["source"] = d.FullName, ["name"] = name, ["author"] = head != null ? Get(head, "author") : "",
            ["version"] = head != null ? Get(head, "ver") : "", ["game_version"] = head != null ? Get(head, "gamever") : "", ["intro"] = intro,
            ["workshop_url"] = Regex.IsMatch(id, @"^\d+$") ? $"https://steamcommunity.com/sharedfiles/filedetails/?id={id}" : "",
            ["bytes"] = size, ["files"] = count, ["restored"] = restored,
            ["category"] = category, ["tags"] = new JsonArray(tags.Select(t => (JsonNode)t!).ToArray()), ["name_en"] = nameEn,
            ["slug"] = id, ["pets"] = pets, ["works"] = new JsonArray(works.Select(w => (JsonNode)w.json).ToArray()), ["foods"] = foods,
            ["texts"] = texts, ["plugin_texts"] = pluginTexts, ["plugins"] = plugins, ["other_lines"] = otherLines,
            ["animations"] = animations, ["strings"] = new JsonArray(strings.Items.Values.Select(v => (JsonNode)v).ToArray()),
        };
    }

    /// <summary>Parcours des animations, comme PetLoader.LoadGraph ; le classement vient du GraphInfo du jeu</summary>
    private static void WalkGraphs(DirectoryInfo di, string start, List<JsonObject> output)
    {
        var info = Path.Combine(di.FullName, "info.lps");
        if (File.Exists(info))
        {
            foreach (var line in ReadLps(info).Where(l => LoaderLines.Contains(l.Name.ToLowerInvariant())))
            {
                var rel = Get(line, "path");
                var target = rel.Length > 0 ? Path.Combine(di.FullName, rel) : di.FullName;
                line.Add(new Sub("startuppath", start));
                if (Directory.Exists(target))
                    output.Add(Leaf(new DirectoryInfo(target), Directory.GetFiles(target, "*.png").OrderBy(x => x).ToArray(), line));
                else if (File.Exists(target))
                    output.Add(Leaf(new FileInfo(target), [target], line));
            }
            return;
        }
        var subdirs = di.GetDirectories();
        if (subdirs.Length == 0)
        {
            var files = di.GetFiles().Where(f => f.Extension.ToLowerInvariant() is ".png" or ".gif" or ".jpg").Select(f => f.FullName).OrderBy(x => x).ToArray();
            if (files.Length > 0)
                output.Add(Leaf(di, files, new Line(files.Length == 1 ? "picture" : "pnganimation", "", "", new Sub("startuppath", start))));
            return;
        }
        foreach (var d in subdirs.OrderBy(x => x.Name))
            WalkGraphs(d, start, output);
    }

    private static JsonObject Leaf(FileSystemInfo path, string[] files, ILine line)
    {
        var gi = new GraphInfo(path, line);
        int duration = 0;
        foreach (var f in files.Where(f => f.EndsWith(".png", StringComparison.OrdinalIgnoreCase)))
        {
            var m = Regex.Match(Path.GetFileNameWithoutExtension(f), @"_(\d+)$");
            if (m.Success)
                duration += int.Parse(m.Groups[1].Value);
        }
        return new JsonObject
        {
            ["type"] = gi.Type.ToString(), ["name"] = gi.Name, ["mode"] = gi.ModeType.ToString().ToLowerInvariant(), ["phase"] = gi.Animat.ToString(),
            ["path"] = path.FullName, ["frames"] = files.Length, ["duration_ms"] = duration,
            ["bytes"] = files.Sum(f => new FileInfo(f).Length), ["preview"] = files.Length > 0 ? files[files.Length / 2] : "",
            ["loader"] = line.Name.ToLowerInvariant(),
        };
    }
    #endregion

    #region Catalogue et copie
    private static readonly JsonSerializerOptions JsonOut = new() { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private static void AddToCatalog(string catalogPath, JsonObject entry)
    {
        JsonObject catalog;
        try
        {
            catalog = File.Exists(catalogPath) ? JsonNode.Parse(File.ReadAllText(catalogPath))!.AsObject() : new JsonObject();
        }
        catch (JsonException)
        {
            catalog = new JsonObject();
        }
        var mods = catalog["mods"] as JsonArray ?? new JsonArray();
        var id = entry["id"]!.GetValue<string>();
        foreach (var old in mods.Where(m => m?["id"]?.GetValue<string>() == id).ToList())
            mods.Remove(old);
        mods.Add(entry);
        catalog["mods"] = mods;
        catalog["generated"] ??= DateTime.Now.ToString("s");
        var counts = new JsonObject();
        foreach (var g in mods.GroupBy(m => m?["category"]?.GetValue<string>() ?? "?"))
            counts[g.Key] = g.Count();
        catalog["counts"] = counts;
        Directory.CreateDirectory(Path.GetDirectoryName(catalogPath)!);
        File.WriteAllText(catalogPath, catalog.ToJsonString(JsonOut));
    }

    private static void CopyDirectory(DirectoryInfo source, DirectoryInfo dest)
    {
        dest.Create();
        foreach (var f in source.GetFiles())
            f.CopyTo(Path.Combine(dest.FullName, f.Name), true);
        foreach (var d in source.GetDirectories())
            CopyDirectory(d, new DirectoryInfo(Path.Combine(dest.FullName, d.Name)));
    }
    #endregion
}
