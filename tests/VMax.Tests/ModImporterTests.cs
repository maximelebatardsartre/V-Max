using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json.Nodes;
using VPet_Simulator.Windows;
using Xunit;

namespace VMax.Tests;

public class ModImporterTests : IDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory("vmax-import-test-").FullName;
    private string Studio => Path.Combine(root, "studio");

    public void Dispose()
    {
        try { Directory.Delete(root, true); } catch { }
    }

    private string MakeMod(string folder, string title, string workName = "上学", string extraText = "")
    {
        var dir = Path.Combine(root, "src", folder);
        Directory.CreateDirectory(Path.Combine(dir, "pet", "vup", "WORK", "school", "A"));
        Directory.CreateDirectory(Path.Combine(dir, "pet", "vup", "WORK", "school", "B"));
        File.WriteAllText(Path.Combine(dir, "info.lps"), $"vupmod#{title}:|author#Testeur:|gamever#11000:|ver#100:|\nintro#Un mod de test:|\nitemid#123456789:|\n");
        File.WriteAllText(Path.Combine(dir, "pet", "vup.lps"),
            $"pet#vup:|path#vup:|petname#vup:|\nwork:|Type#Study:|Name#{workName}:|MoneyBase#20:|Graph#school:|LevelLimit#0:|Time#45:|\n");
        foreach (var phase in new[] { "A", "B" })
            for (int i = 0; i < 3; i++)
                File.WriteAllBytes(Path.Combine(dir, "pet", "vup", "WORK", "school", phase, $"f_{i:000}_125.png"), [0x89, 0x50, 0x4E, 0x47]);
        Directory.CreateDirectory(Path.Combine(dir, "text"));
        File.WriteAllText(Path.Combine(dir, "text", "click.lps"), $"clicktext:|Text#好好学习！:|\nclicktext:|Text#{(extraText.Length > 0 ? extraText : "加油~")}:|\n");
        return dir;
    }

    private JsonObject Catalog() => JsonNode.Parse(File.ReadAllText(Path.Combine(Studio, "catalog.json")))!.AsObject();

    [Fact]
    public void Importe_un_dossier_de_mod()
    {
        var src = MakeMod("monmod", "Mon mod");
        var r = Assert.Single(ModImporter.Import(src, Studio, ""));
        Assert.Null(r.Refused);
        Assert.Equal("123456789", r.Id);
        Assert.Equal("Études", r.Category);

        var entry = Catalog()["mods"]!.AsArray().Single()!.AsObject();
        Assert.Equal("manuel", entry["origin"]!.GetValue<string>());
        Assert.StartsWith(Path.Combine(Studio, "mods", "123456789"), entry["source"]!.GetValue<string>());
        var anim = entry["animations"]!.AsArray().Single()!;
        Assert.Equal("Work", anim["type"]!.GetValue<string>());
        Assert.Equal("school", anim["name"]!.GetValue<string>());
        Assert.Equal(["A_Start", "B_Loop"], anim["phases"]!.AsArray().Select(p => p!.GetValue<string>()).ToArray());
        Assert.Equal(6, anim["frames"]!.GetValue<int>());
        Assert.Contains(entry["strings"]!.AsArray(), s => s!["key"]!.GetValue<string>() == "好好学习！" && s["kind"]!.GetValue<string>() == "click");
        Assert.True(File.Exists(Path.Combine(Studio, "mods", "123456789", "pet", "vup.lps")));
    }

    [Fact]
    public void Reimporter_remplace_l_entree()
    {
        var src = MakeMod("monmod", "Mon mod");
        ModImporter.Import(src, Studio, "");
        ModImporter.Import(src, Studio, "");
        Assert.Single(Catalog()["mods"]!.AsArray());
    }

    [Fact]
    public void Refuse_un_mod_sexuel_par_son_titre_sans_le_copier()
    {
        var src = MakeMod("r18", "[R18] quelque chose");
        var r = Assert.Single(ModImporter.Import(src, Studio, ""));
        Assert.Equal("Exclu", r.Category);
        Assert.NotNull(r.Refused);
        Assert.False(Directory.Exists(Path.Combine(Studio, "mods")) && Directory.EnumerateDirectories(Path.Combine(Studio, "mods")).Any());
    }

    [Fact]
    public void Refuse_un_mod_sexuel_par_son_contenu()
    {
        var src = MakeMod("neutre", "Titre neutre", extraText: "好深......❤");
        Assert.Equal("Exclu", Assert.Single(ModImporter.Import(src, Studio, "")).Category);
    }

    [Fact]
    public void Ecarte_une_replique_ambigue_sans_refuser_le_mod()
    {
        var src = MakeMod("chat", "Chat", extraText: "想给主人舔舔！");
        Assert.Null(Assert.Single(ModImporter.Import(src, Studio, "")).Refused);
        var line = Catalog()["mods"]!.AsArray().Single()!["strings"]!.AsArray().Single(s => s!["key"]!.GetValue<string>() == "想给主人舔舔！")!;
        Assert.True(line["blocked"]!.GetValue<bool>());
    }

    [Fact]
    public void Importe_une_archive_zip()
    {
        var src = MakeMod("zipmod", "Mod zippé");
        var zip = Path.Combine(root, "mod.zip");
        ZipFile.CreateFromDirectory(src, zip, CompressionLevel.Fastest, includeBaseDirectory: true);
        var r = Assert.Single(ModImporter.Import(zip, Studio, ""));
        Assert.Null(r.Refused);
        Assert.True(Directory.Exists(Path.Combine(Studio, "mods", r.Id, "pet")));
    }

    [Fact]
    public void Dossier_sans_fiche_n_est_pas_un_mod()
    {
        var dir = Directory.CreateDirectory(Path.Combine(root, "rien")).FullName;
        File.WriteAllText(Path.Combine(dir, "lisez-moi.txt"), "bonjour");
        Assert.NotNull(Assert.Single(ModImporter.Import(dir, Studio, "")).Refused);
    }

    /// <summary>Même résultat que tools/mods/ingest.py sur un vrai mod, s'il est présent sur la machine</summary>
    [Fact]
    public void Meme_classement_que_le_script_python()
    {
        var real = @"D:\VMAX\MOD\1920960\3133902323";
        var catalog = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "V-Max", "studio", "catalog.json");
        if (!Directory.Exists(real) || !File.Exists(catalog))
            return;
        var py = JsonNode.Parse(File.ReadAllText(catalog))!["mods"]!.AsArray().FirstOrDefault(m => m!["id"]!.GetValue<string>() == "3133902323");
        if (py == null)
            return;
        ModImporter.Import(real, Studio, "");
        var cs = Catalog()["mods"]!.AsArray().Single()!;
        string Sig(JsonNode m) => string.Join(";", m["animations"]!.AsArray().Select(a =>
            $"{a!["type"]}/{a["name"]}/{a["frames"]}/{string.Join(",", a["modes"]!.AsArray())}/{string.Join(",", a["phases"]!.AsArray())}"));
        Assert.Equal(Sig(py), Sig(cs));
        Assert.Equal(py["category"]!.GetValue<string>(), cs["category"]!.GetValue<string>());
    }
}
