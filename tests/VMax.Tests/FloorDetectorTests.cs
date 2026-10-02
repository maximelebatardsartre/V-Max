using VPet_Simulator.Windows.Habitat;
using Xunit;

namespace VMax.Tests;

public class FloorDetectorTests
{
    /// <summary>Image synthétique : ciel clair, un plancher sombre à y=120 sur la moitié gauche, le sol à y=180</summary>
    private static byte[] House(int w, int h)
    {
        var px = new byte[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                byte v = 220;                                    // fond clair
                if (y >= 120 && y < 126 && x < w / 2) v = 90;    // plancher de l'étage
                if (y >= 180) v = 60;                            // sol
                px[y * w + x] = v;
            }
        return px;
    }

    [Fact]
    public void Trouve_le_sol_et_l_etage()
    {
        var c = FloorDetector.Detect(House(240, 200), 240, 200);
        Assert.Contains(c, f => f.Y == 180 && f.X1 <= 1 && f.X2 >= 239);
        Assert.Contains(c, f => f.Y == 120 && f.X1 <= 1 && f.X2 is >= 118 and <= 122);
        // le dessous du plancher (y=126) est une rupture aussi, mais trop proche : fusionnée
        Assert.DoesNotContain(c, f => f.Y == 126);
    }

    [Fact]
    public void Ignore_les_traits_courts_et_le_haut_de_l_image()
    {
        int w = 240, h = 200;
        var px = new byte[w * h];
        for (int i = 0; i < px.Length; i++) px[i] = 200;
        for (int x = 10; x < 25; x++) px[100 * w + x] = 0;   // trait trop court
        for (int x = 0; x < w; x++) px[10 * w + x] = 0;      // tout en haut
        Assert.Empty(FloorDetector.Detect(px, w, h));
    }

    [Fact]
    public void Image_unie_ou_minuscule()
    {
        Assert.Empty(FloorDetector.Detect(new byte[100 * 100], 100, 100));
        Assert.Empty(FloorDetector.Detect(new byte[4], 2, 2));
    }
}

public class HabitatVisionTests
{
    private static HabitatMap Map() => new() { Image = new HabitatImage { Width = 2000, Height = 1000 }, PetHeight = 200 };

    [Fact]
    public void Reponse_json_avec_bloc_et_texte_autour()
    {
        var text = "Voici :\n```json\n{\"pieces\":[{\"nom\":\"Cuisine\",\"type\":\"kitchen\",\"x\":100,\"y\":500,\"largeur\":300,\"hauteur\":400},"
                 + "{\"nom\":\"Grenier\",\"type\":\"attic\",\"x\":\"200\",\"y\":50,\"largeur\":600,\"hauteur\":150}]}\n```";
        var rooms = VPet_Simulator.Windows.Habitat.HabitatVision.Parse(text, Map());
        Assert.Equal(2, rooms.Count);
        Assert.Equal(200, rooms[0].X);
        Assert.Equal(500, rooms[0].Y);
        Assert.Equal(600, rooms[0].Width);
        Assert.Equal("kitchen", rooms[0].Tag);
        Assert.Equal("other", rooms[1].Tag);   // type inconnu
        Assert.Equal(400, rooms[1].X);         // nombre donné en texte
    }

    [Fact]
    public void Reponses_invalides_ou_cadres_minuscules()
    {
        Assert.Empty(VPet_Simulator.Windows.Habitat.HabitatVision.Parse("désolé, je ne peux pas", Map()));
        Assert.Empty(VPet_Simulator.Windows.Habitat.HabitatVision.Parse("{\"pieces\":[{\"nom\":\"X\",\"x\":10,\"y\":10,\"largeur\":3,\"hauteur\":3}]}", Map()));
        var clamped = VPet_Simulator.Windows.Habitat.HabitatVision.Parse("{\"rooms\":[{\"name\":\"Jardin\",\"x\":900,\"y\":0,\"width\":400,\"height\":2000}]}", Map());
        Assert.Equal(200, clamped[0].Width);   // ramené dans l'image
        Assert.Equal(1000, clamped[0].Height);
    }
}
