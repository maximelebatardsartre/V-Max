using System;
using System.Linq;
using VPet_Simulator.Windows.Assistant;
using Xunit;
using Xunit.Abstractions;

namespace VMax.Tests;

public class RouteDebugTests
{
    private readonly ITestOutputHelper o;
    public RouteDebugTests(ITestOutputHelper o) => this.o = o;

    private sealed class FakeHost : IAssistantHost
    {
        public void OnUi(Action a) => a();
        public void Deliver(CommandOutcome x) { }
        public void OpenPanel(string id) { }
    }

    [Theory]
    [InlineData("ouvre spotify")]
    [InlineData("lance discord")]
    [InlineData("ouvre steam")]
    [InlineData("démarre chrome")]
    [InlineData("ouvre le bloc-notes")]
    public void Route(string phrase)
    {
        var r = new CommandRouter(new FakeHost());
        var rank = r.Rank(phrase);
        o.WriteLine(phrase + "  ->  " + string.Join(", ", rank.Select(x => x.name + ":" + x.score)));
        Assert.True(rank.Length > 0, "aucun intent pour: " + phrase);
        Assert.Equal("CmdOpenApp", rank[0].name);
    }

    [Theory]
    [InlineData("mes minuteurs", "CmdActiveTimers")]
    [InlineData("montre mes rappels", "CmdActiveTimers")]
    [InlineData("ouvre le lanceur", "CmdLauncher")]
    [InlineData("mes applications", "CmdLauncher")]
    [InlineData("ce que j'ai copié", "CmdClipboard")]
    [InlineData("mon historique de copie", "CmdClipboard")]
    [InlineData("qu'est-ce qui rame", "CmdProcesses")]
    [InlineData("ce qui consomme", "CmdProcesses")]
    [InlineData("mes fenêtres ouvertes", "CmdWindows")]
    [InlineData("montre mes fenêtres", "CmdWindows")]
    [InlineData("mes captures", "CmdShots")]
    [InlineData("prends une note", "CmdNotes")]
    [InlineData("mes notes", "CmdNotes")]
    public void RoutePanels(string phrase, string expected)
    {
        var r = new CommandRouter(new FakeHost());
        var rank = r.Rank(phrase);
        o.WriteLine(phrase + "  ->  " + string.Join(", ", rank.Select(x => x.name + ":" + x.score)));
        Assert.True(rank.Length > 0, "aucun intent pour: " + phrase);
        Assert.Equal(expected, rank[0].name);
    }

    // Garde-fou : créer un minuteur reste distinct d'afficher les minuteurs en cours.
    [Theory]
    [InlineData("minuteur de 5 minutes", "CmdTimer")]
    [InlineData("rappelle-moi dans 10 minutes de boire", "CmdReminder")]
    [InlineData("prends une capture d'écran", "CmdScreenshot")]
    public void RouteNotStolenByPanels(string phrase, string expected)
    {
        var r = new CommandRouter(new FakeHost());
        var rank = r.Rank(phrase);
        o.WriteLine(phrase + "  ->  " + string.Join(", ", rank.Select(x => x.name + ":" + x.score)));
        Assert.Equal(expected, rank[0].name);
    }
}
