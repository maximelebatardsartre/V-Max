using System.IO;
using VPet_Simulator.Windows;
using Xunit;

namespace VMax.Tests;

public class ModBlocklistTests
{
    [Theory]
    [InlineData("3065265367", null, true)]          // dossier Workshop
    [InlineData("2001_3065265367_copie", null, true)] // copié à la main dans « mod »
    [InlineData("1234_mon-mod", "3290665653", true)] // repéré par sa fiche
    [InlineData("3133902323", "3133902323", false)]  // mod autorisé
    [InlineData("0000_core", null, false)]
    [InlineData("30652653670", null, false)]          // identifiant plus long : pas de faux positif
    public void Detecte_les_mods_bloques(string folder, string? itemId, bool expected)
    {
        Assert.Equal(expected, ModBlocklist.IsBlocked(new DirectoryInfo(Path.Combine(Path.GetTempPath(), folder)), itemId));
    }

    [Fact]
    public void Lit_l_itemid_de_la_fiche()
    {
        var dir = Directory.CreateTempSubdirectory("vmax-mod-");
        try
        {
            File.WriteAllText(Path.Combine(dir.FullName, "info.lps"), "vupmod#Test:|author#x:|gamever#1:|ver#1:|\nitemid#3042568517:|\n");
            Assert.True(ModBlocklist.IsBlocked(dir));
            File.WriteAllText(Path.Combine(dir.FullName, "info.lps"), "vupmod#Test:|author#x:|gamever#1:|ver#1:|\nitemid#3133902323:|\n");
            Assert.False(ModBlocklist.IsBlocked(dir));
        }
        finally
        {
            dir.Delete(true);
        }
    }
}
