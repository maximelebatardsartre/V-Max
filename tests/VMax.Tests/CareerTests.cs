using VPet_Simulator.Windows.Career;
using Xunit;

namespace VMax.Tests;

public class CareerTests
{
    [Fact]
    public void Le_catalogue_reclasse_correctement()
    {
        Assert.Equal(Bucket.Metier, ActivityCatalog.ByName["文案"].Bucket);        // rédiger = métier
        Assert.Equal("dev", ActivityCatalog.ByName["Coding Lv.20"].Track);
        Assert.Equal(2, ActivityCatalog.ByName["Coding Lv.20"].Tier);
        Assert.Equal("Programmer (senior)", ActivityCatalog.ByName["Coding Lv.20"].Title); // plus de « Lv.20 »
        Assert.Equal(Bucket.Ambiance, ActivityCatalog.ByName["Mopping"].Bucket);    // serpillière = vie, pas un job
        Assert.Equal(Bucket.Ambiance, ActivityCatalog.ByName["Slacking Off"].Bucket); // glander = ambiance
        Assert.Equal(Bucket.Loisir, ActivityCatalog.ByName["玩原神"].Bucket);        // jeu = loisir
        Assert.Equal(Bucket.Loisir, ActivityCatalog.ByName["Genshin Shift Farm"].Bucket); // farm = loisir, pas 107$/min
    }

    [Fact]
    public void Les_paliers_et_titres_progressent_avec_l_xp()
    {
        var s = new CareerState();
        var dev = CareerTree.Track("dev")!;
        Assert.Equal(0, s.TierIndex(dev));
        Assert.Equal("Dev junior", s.TitleOf(dev));
        s.Xp["dev"] = 400;
        Assert.Equal(1, s.TierIndex(dev));
        Assert.Equal("Développeuse", s.TitleOf(dev));
    }

    [Fact]
    public void Le_job_actif_donne_le_titre_et_la_voie()
    {
        var s = new CareerState();
        Assert.Null(s.CurrentTitle);
        s.ActiveJob = "cuisine";          // (sans passer par ChooseJob pour ne rien écrire sur disque)
        Assert.Equal("Commis", s.CurrentTitle);
        Assert.Equal("Restauration", s.ActiveJobTrack!.Family);
        s.Xp["cuisine"] = 300;
        Assert.Equal("Cuisinière", s.CurrentTitle);
    }

    [Fact]
    public void Les_etudes_sont_une_voie_lineaire()
    {
        var e = CareerTree.Track("etudes")!;
        Assert.Equal(CareerKind.Study, e.Kind);
        Assert.Equal("Études", e.Family);
        Assert.Single(CareerTree.InFamily("Études")); // une seule échelle = linéaire
        Assert.True(e.Tiers.Length >= 5);
    }
}
