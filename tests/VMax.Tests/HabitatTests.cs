using System.Windows;
using Point = System.Windows.Point;
using VPet_Simulator.Windows.Habitat;
using Xunit;

namespace VMax.Tests;

public class HabitatProjectionTests
{
    [Fact]
    public void Ajuster_garde_l_image_entiere_et_centre_les_bandes()
    {
        // image 16:9 dans une zone carrée : bandes en haut et en bas
        var p = HabitatProjection.Compute(1600, 900, new Rect(100, 50, 800, 800), ImageFit.Fit);
        Assert.Equal(0.5, p.ScaleX, 6);
        Assert.Equal(0.5, p.ScaleY, 6);
        Assert.Equal(new Point(100, 50 + (800 - 450) / 2.0), p.ToScreen(new Point(0, 0)));
        Assert.Equal(new Point(900, 50 + 175 + 450), p.ToScreen(new Point(1600, 900)));
    }

    [Fact]
    public void Remplir_couvre_et_rogne()
    {
        var p = HabitatProjection.Compute(1600, 900, new Rect(0, 0, 800, 800), ImageFit.Fill);
        Assert.Equal(800.0 / 900, p.ScaleY, 6);
        Assert.True(p.OffsetX < 0);
        var b = p.ImageBounds(1600, 900);
        Assert.Equal(400, b.X + b.Width / 2, 6);
        Assert.Equal(800, b.Height, 6);
    }

    [Fact]
    public void Etirer_deforme()
    {
        var p = HabitatProjection.Compute(1000, 500, new Rect(0, 0, 500, 500), ImageFit.Stretch);
        Assert.Equal(0.5, p.ScaleX, 6);
        Assert.Equal(1.0, p.ScaleY, 6);
    }

    [Fact]
    public void Centre_et_mosaique_en_taille_reelle()
    {
        var c = HabitatProjection.Compute(400, 200, new Rect(0, 0, 1000, 1000), ImageFit.Center, pixelToDip: 0.8);
        Assert.Equal(0.8, c.ScaleX, 6);
        Assert.Equal(500 - 160, c.OffsetX, 6);
        var t = HabitatProjection.Compute(400, 200, new Rect(10, 20, 1000, 1000), ImageFit.Tile);
        Assert.Equal(new Point(10, 20), t.ToScreen(new Point(0, 0)));
    }

    [Fact]
    public void Aller_retour_image_ecran()
    {
        var p = HabitatProjection.Compute(3840, 2160, new Rect(-1920, 0, 1920, 1080), ImageFit.Fill);
        var img = new Point(1234.5, 678.25);
        var back = p.ToImage(p.ToScreen(img));
        Assert.Equal(img.X, back.X, 6);
        Assert.Equal(img.Y, back.Y, 6);
    }

    [Theory]
    [InlineData("10", "0", ImageFit.Fill)]
    [InlineData("6", "0", ImageFit.Fit)]
    [InlineData("2", "0", ImageFit.Stretch)]
    [InlineData("0", "0", ImageFit.Center)]
    [InlineData("0", "1", ImageFit.Tile)]
    [InlineData("22", "0", ImageFit.Span)]
    [InlineData(null, null, ImageFit.Fill)]
    public void Modes_du_registre_Windows(string? style, string? tile, ImageFit expected) =>
        Assert.Equal(expected, WallpaperInfo.FitFromRegistry(style, tile));
}

public class HabitatMapTests
{
    private static HabitatMap House()
    {
        var m = new HabitatMap { Image = new HabitatImage { Sha256 = "abc", Width = 1920, Height = 1080 }, PetHeight = 200 };
        m.Floors.Add(new HabitatFloor { Id = "f1", Y = 1000, X1 = 0, X2 = 1920 });   // rez-de-chaussée
        m.Floors.Add(new HabitatFloor { Id = "f2", Y = 550, X1 = 1500, X2 = 300 });  // étage (tracé de droite à gauche)
        return m;
    }

    [Fact]
    public void Sol_sous_le_point_le_plus_haut_d_abord()
    {
        var m = House();
        Assert.Equal("f2", m.FloorBelow(800, 300)?.Id);
        Assert.Equal("f1", m.FloorBelow(800, 600)?.Id);
        Assert.Equal("f1", m.FloorBelow(1700, 300)?.Id); // hors de l'étage : on tombe au rez-de-chaussée
        Assert.Null(m.FloorBelow(800, 1050));
    }

    [Fact]
    public void Atterrissage_sur_le_plus_proche_si_rien_dessous()
    {
        var m = House();
        Assert.Equal("f1", m.LandingFloor(800, 1050)?.Id);
        Assert.Equal("f1", m.LandingFloor(-200, 900)?.Id);
    }

    [Fact]
    public void Sol_courant_avec_tolerance()
    {
        var m = House();
        Assert.Equal("f2", m.FloorAt(400, 553, 5)?.Id);
        Assert.Null(m.FloorAt(200, 553, 5));
        Assert.Null(m.FloorAt(400, 570, 5));
    }

    [Fact]
    public void Sol_principal_et_bornes_normalisees()
    {
        var m = House();
        Assert.Equal("f1", m.MainFloor?.Id);
        var f2 = m.Floor("f2")!;
        Assert.Equal(300, f2.Left);
        Assert.Equal(1500, f2.Right);
        Assert.Equal(1500, f2.Clamp(1700));
    }

    [Fact]
    public void Identifiants_libres()
    {
        var m = House();
        Assert.Equal("f3", m.NewId("f"));
        Assert.Equal("c1", m.NewId("c"));
    }

    [Fact]
    public void Validation_plafond_trop_bas_et_carte_vide()
    {
        var m = House();
        Assert.Empty(m.Validate());
        m.Floors.Add(new HabitatFloor { Id = "f3", Y = 120, X1 = 0, X2 = 900 });
        Assert.Contains(m.Validate(), s => s.Contains("f3") && s.Contains("trop près du haut"));
        Assert.Contains(new HabitatMap().Validate(), s => s.Contains("au moins un sol"));
    }

    [Fact]
    public void Aller_retour_json_et_version_future_refusee()
    {
        var m = House();
        m.Rooms.Add(new HabitatRoom { Id = "r1", Name = "Cuisine", Tag = "kitchen", X = 0, Y = 550, Width = 900, Height = 450 });
        var json = m.ToJson();
        Assert.Contains("\"petHeight\": 200", json);
        Assert.Contains("Cuisine", json);
        var back = HabitatMap.FromJson(json);
        Assert.Equal(2, back.Floors.Count);
        Assert.Equal("kitchen", back.Rooms[0].Tag);
        Assert.Throws<System.IO.InvalidDataException>(() => HabitatMap.FromJson("{\"version\": 99}"));
    }
}
