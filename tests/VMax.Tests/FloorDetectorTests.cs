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
