using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using VPet_Simulator.Windows.Agent.Providers;
using Xunit;

namespace VMax.Tests;

public class OpenAiStreamParserTests
{
    [Fact]
    public void Texte_au_fil_de_l_eau_puis_fin()
    {
        var p = new OpenAiStreamParser();
        var events = new List<ChatEvent>();
        events.AddRange(p.Feed("data: {\"choices\":[{\"delta\":{\"content\":\"Bon\"}}]}"));
        events.AddRange(p.Feed(""));
        events.AddRange(p.Feed(": commentaire keep-alive"));
        events.AddRange(p.Feed("data: {\"choices\":[{\"delta\":{\"content\":\"jour\"}}]}"));
        events.AddRange(p.Feed("data: [DONE]"));
        events.AddRange(p.Complete());

        Assert.True(p.Done);
        Assert.Equal("Bonjour", string.Concat(events.OfType<TextDelta>().Select(t => t.Text)));
        Assert.Empty(events.OfType<ToolCallsRequested>());
    }

    [Fact]
    public void Appels_d_outils_reconstitues_depuis_des_morceaux()
    {
        var p = new OpenAiStreamParser();
        var lines = new[]
        {
            "data: {\"choices\":[{\"delta\":{\"tool_calls\":[{\"index\":0,\"id\":\"call_a\",\"function\":{\"name\":\"open_app\",\"arguments\":\"{\\\"no\"}}]}}]}",
            "data: {\"choices\":[{\"delta\":{\"tool_calls\":[{\"index\":0,\"function\":{\"arguments\":\"m\\\":\\\"Spotify\\\"}\"}}]}}]}",
            "data: {\"choices\":[{\"delta\":{\"tool_calls\":[{\"index\":1,\"id\":\"call_b\",\"function\":{\"name\":\"system_info\",\"arguments\":\"\"}}]}}]}",
            "data: [DONE]",
        };
        var events = lines.SelectMany(p.Feed).Concat(p.Complete()).ToList();

        var calls = Assert.Single(events.OfType<ToolCallsRequested>()).Calls;
        Assert.Equal(2, calls.Count);
        Assert.Equal("open_app", calls[0].Name);
        Assert.Equal("call_a", calls[0].Id);
        Assert.Equal("Spotify", calls[0].Arguments["nom"]!.GetValue<string>());
        Assert.Equal("system_info", calls[1].Name);
        Assert.Empty(calls[1].Arguments);
    }

    [Fact]
    public void Erreur_dans_le_flux_leve_une_ProviderException()
    {
        var p = new OpenAiStreamParser();
        Assert.Throws<ProviderException>(() => p.Feed("data: {\"error\":{\"message\":\"quota\"}}").ToList());
    }
}

public class ConversionTests
{
    private static List<ChatMessage> Conversation(string producer) =>
    [
        ChatMessage.User("Quelle heure est-il ?"),
        new() { Role = ChatRole.Assistant, Text = "", ProviderId = producer, ToolCalls =
            [new ToolCall("c1", "system_info", new JsonObject(), new JsonObject { ["functionCall"] = new JsonObject { ["name"] = "system_info" }, ["thoughtSignature"] = "SIG" })] },
        new() { Role = ChatRole.Tool, ToolCallId = "c1", ToolName = "system_info", ToolResult = new JsonObject { ["heure"] = "10:00" } },
    ];

    [Fact]
    public void Format_OpenAI_avec_messages_outil()
    {
        var msgs = OpenAiCompatibleProvider.BuildMessages(Conversation("groq"), "système");
        Assert.Equal("system", msgs[0]!["role"]!.GetValue<string>());
        Assert.Equal("assistant", msgs[2]!["role"]!.GetValue<string>());
        Assert.Equal("c1", msgs[2]!["tool_calls"]![0]!["id"]!.GetValue<string>());
        Assert.Equal("tool", msgs[3]!["role"]!.GetValue<string>());
        Assert.Equal("c1", msgs[3]!["tool_call_id"]!.GetValue<string>());
    }

    [Fact]
    public void Gemini_renvoie_la_signature_seulement_pour_ses_propres_appels()
    {
        var own = GeminiProvider.BuildContents(Conversation("gemini"), "gemini");
        Assert.Equal("SIG", own[1]!["parts"]![0]!["thoughtSignature"]!.GetValue<string>());
        Assert.Equal("user", own[2]!["role"]!.GetValue<string>());
        Assert.NotNull(own[2]!["parts"]![0]!["functionResponse"]);

        var other = GeminiProvider.BuildContents(Conversation("groq"), "gemini");
        Assert.Null(other[1]!["parts"]![0]!["thoughtSignature"]);
        Assert.Equal("system_info", other[1]!["parts"]![0]!["functionCall"]!["name"]!.GetValue<string>());
    }
}

public class RouterTests
{
    private sealed class FakeProvider(string id, Func<IAsyncEnumerable<ChatEvent>> stream) : IChatProvider
    {
        public string Id => id;
        public string DisplayName => id;
        public bool IsLocal => false;
        public string Model => "fake";
        public IAsyncEnumerable<ChatEvent> StreamAsync(IReadOnlyList<ChatMessage> h, string s, IReadOnlyList<ToolSpec> t, CancellationToken ct) => stream();
    }

    private static async IAsyncEnumerable<ChatEvent> Fails(ProviderFailure f)
    {
        await Task.Yield();
        throw new ProviderException("échec", f);
#pragma warning disable CS0162
        yield break;
#pragma warning restore CS0162
    }

    private static async IAsyncEnumerable<ChatEvent> Says(string text)
    {
        await Task.Yield();
        yield return new TextDelta(text);
    }

    private static ProviderInfo Info(string id) => new(id, id, "", false, null, null, (_, _) => null!);

    private static async Task<List<(IChatProvider p, ChatEvent e)>> Run(params (ProviderInfo, IChatProvider)[] candidates)
    {
        var list = new List<(IChatProvider, ChatEvent)>();
        await foreach (var x in ProviderRouter.StreamAsync(candidates, [ChatMessage.User("salut")], "s", [], CancellationToken.None))
            list.Add(x);
        return list;
    }

    [Fact]
    public async Task Bascule_sur_le_suivant_quand_le_quota_est_atteint()
    {
        ProviderRouter.Reset();
        var events = await Run(
            (Info("a"), new FakeProvider("a", () => Fails(ProviderFailure.Quota))),
            (Info("b"), new FakeProvider("b", () => Says("bonjour"))));
        var (provider, ev) = Assert.Single(events);
        Assert.Equal("b", provider.Id);
        Assert.Equal("bonjour", ((TextDelta)ev).Text);
    }

    [Fact]
    public async Task Requete_refusee_ne_bascule_pas()
    {
        ProviderRouter.Reset();
        await Assert.ThrowsAsync<ProviderException>(() => Run(
            (Info("a"), new FakeProvider("a", () => Fails(ProviderFailure.Rejected))),
            (Info("b"), new FakeProvider("b", () => Says("non")))));
    }

    [Fact]
    public async Task Tous_en_echec_donne_un_message_par_fournisseur()
    {
        ProviderRouter.Reset();
        var e = await Assert.ThrowsAsync<ProviderException>(() => Run(
            (Info("a"), new FakeProvider("a", () => Fails(ProviderFailure.Unavailable))),
            (Info("b"), new FakeProvider("b", () => Fails(ProviderFailure.Quota)))));
        Assert.Contains("a :", e.Message);
        Assert.Contains("b :", e.Message);
    }
}
