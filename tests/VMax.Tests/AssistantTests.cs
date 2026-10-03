using System;
using System.Collections.Generic;
using VPet_Simulator.Windows.Assistant;
using Xunit;

namespace VMax.Tests;

public class CommandRouterTests
{
    private sealed class FakeHost : IAssistantHost
    {
        public readonly List<CommandOutcome> Delivered = new();
        public void OnUi(Action action) => action();
        public void Deliver(CommandOutcome outcome) => Delivered.Add(outcome);
        public void OpenPanel(string id) { }
    }

    private static CommandRouter New() => new(new FakeHost());

    [Fact]
    public void Normalise_minuscule_sans_accents()
    {
        Assert.Equal("quelle heure est il", CommandRouter.Normalize("  Quelle   Heure Est-il ?  "));
    }

    [Fact]
    public void Heure_et_date_reconnues()
    {
        Assert.StartsWith("Il est", New().TryHandle("il est quelle heure")!.Speak);
        Assert.StartsWith("On est", New().TryHandle("on est quel jour")!.Speak);
    }

    [Fact]
    public void Minuteur_parse_la_duree()
    {
        var o = New().TryHandle("mets un minuteur de 10 minutes");
        Assert.NotNull(o);
        Assert.Contains("10 minutes", o!.Speak);
    }

    [Fact]
    public void Phrase_inconnue_renvoie_null()
    {
        Assert.Null(New().TryHandle("raconte-moi ta vie en détail"));
    }

    [Fact]
    public void NotUnderstood_delivre_un_message()
    {
        var host = new FakeHost();
        new CommandRouter(host).NotUnderstood();
        Assert.Single(host.Delivered);
    }

    [Fact]
    public void Aide_reconnue_en_plusieurs_formulations()
    {
        foreach (var q in new[] { "qu'est-ce que tu sais faire", "c'est quoi tes commandes", "aide", "a quoi tu sers" })
            Assert.Equal("💡 Commandes", New().TryHandle(q)?.Card);
    }

    [Fact]
    public void Moniteur_de_perfs_ouvre_le_panneau()
    {
        foreach (var q in new[] { "affiche les performances", "ouvre le moniteur", "stats", "l'état de mon pc" })
            Assert.Equal("📊 Moniteur", New().TryHandle(q)?.Card);
    }

    [Fact]
    public void Heure_en_formulations_variees()
    {
        foreach (var q in new[] { "il est quelle heure", "tu as l'heure", "donne-moi l'heure" })
            Assert.StartsWith("Il est", New().TryHandle(q)!.Speak);
    }

    [Fact]
    public void Annuler_minuteur_ne_demarre_pas_un_minuteur()
    {
        Assert.Equal("Annulé", New().TryHandle("annule le minuteur")?.Card);
    }
}

public class WhisperSttTests
{
    [Fact]
    public async System.Threading.Tasks.Task Charge_le_moteur_si_le_modele_est_present()
    {
        if (!VPet_Simulator.Windows.Voice.WhisperStt.ModelPresent)
            return; // modèle pas téléchargé sur cette machine : on saute
        var w = new VPet_Simulator.Windows.Voice.WhisperStt();
        var err = await w.EnsureAsync();
        Assert.Null(err);
        Assert.True(w.Ready);
        w.Dispose();
    }
}
