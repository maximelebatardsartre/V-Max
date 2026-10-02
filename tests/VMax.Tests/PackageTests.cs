using System.IO;
using System.IO.Compression;
using VPet_Simulator.Windows.Habitat;
using Xunit;

namespace VMax.Tests;

public class HabitatPackageTests : IDisposable
{
    private readonly string dir = Path.Combine(Path.GetTempPath(), "vmax-tests-" + Guid.NewGuid().ToString("N"));

    public HabitatPackageTests() => Directory.CreateDirectory(dir);
    public void Dispose() => Directory.Delete(dir, true);

    private (string image, HabitatMap map) Fixture()
    {
        var image = Path.Combine(dir, "maison.png");
        File.WriteAllBytes(image, new byte[] { 137, 80, 78, 71, 1, 2, 3, 4, 5 });
        var map = new HabitatMap { Image = new HabitatImage { Sha256 = HabitatMap.Sha256Of(image), Width = 100, Height = 50, Name = "Maison", Path = image }, PetHeight = 20 };
        map.Floors.Add(new HabitatFloor { Id = "f1", Y = 45, X1 = 0, X2 = 100 });
        map.Rooms.Add(new HabitatRoom { Id = "r1", Name = "Cuisine", X = 0, Y = 0, Width = 50, Height = 45 });
        return (image, map);
    }

    [Fact]
    public void Export_puis_import_retrouve_l_image_et_la_carte()
    {
        var (image, map) = Fixture();
        var pkg = Path.Combine(dir, "maison.vmaxhome");
        HabitatPackage.Export(image, map, pkg);
        var target = Path.Combine(dir, "import");
        var imported = HabitatPackage.Import(pkg, target);
        Assert.Equal(File.ReadAllBytes(image), File.ReadAllBytes(imported));
        var back = HabitatMap.FromJson(File.ReadAllText(Path.Combine(target, map.Image.Sha256 + ".json")));
        Assert.Equal("Cuisine", back.Rooms[0].Name);
        Assert.Equal(imported, back.Image.Path);
        // aucun chemin local dans le paquet
        using var zip = ZipFile.OpenRead(pkg);
        using var r = new StreamReader(zip.GetEntry("map.json")!.Open());
        Assert.DoesNotContain(dir.Replace("\\", "\\\\"), r.ReadToEnd());
    }

    [Fact]
    public void Image_modifiee_refusee()
    {
        var (image, map) = Fixture();
        var pkg = Path.Combine(dir, "maison.vmaxhome");
        HabitatPackage.Export(image, map, pkg);
        using (var zip = ZipFile.Open(pkg, ZipArchiveMode.Update))
        {
            zip.GetEntry("image.png")!.Delete();
            using var w = zip.CreateEntry("image.png").Open();
            w.Write(new byte[] { 1, 2, 3 });
        }
        Assert.Throws<InvalidDataException>(() => HabitatPackage.Import(pkg, Path.Combine(dir, "import")));
    }

    [Fact]
    public void Chemin_dans_l_archive_refuse_et_fichier_etranger_refuse()
    {
        var evil = Path.Combine(dir, "evil.vmaxhome");
        using (var zip = ZipFile.Open(evil, ZipArchiveMode.Create))
        {
            using (var w = new StreamWriter(zip.CreateEntry("manifest.json").Open()))
                w.Write("{\"format\":\"vmaxhome\",\"version\":1,\"image\":\"..\\\\..\\\\evil.png\"}");
            using (var w = new StreamWriter(zip.CreateEntry("map.json").Open()))
                w.Write("{}");
        }
        Assert.Throws<InvalidDataException>(() => HabitatPackage.Import(evil, Path.Combine(dir, "import")));

        var other = Path.Combine(dir, "other.zip");
        using (var zip = ZipFile.Open(other, ZipArchiveMode.Create))
            zip.CreateEntry("readme.txt");
        Assert.Throws<InvalidDataException>(() => HabitatPackage.Import(other, Path.Combine(dir, "import")));
    }

    [Fact]
    public void Export_refuse_une_image_qui_a_change()
    {
        var (image, map) = Fixture();
        File.WriteAllBytes(image, new byte[] { 9, 9, 9 });
        Assert.Throws<InvalidDataException>(() => HabitatPackage.Export(image, map, Path.Combine(dir, "x.vmaxhome")));
    }
}
