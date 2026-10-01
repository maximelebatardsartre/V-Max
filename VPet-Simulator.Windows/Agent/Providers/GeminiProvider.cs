using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using System.Threading;

namespace VPet_Simulator.Windows.Agent.Providers;

/// <summary>
/// Gemini (Google AI Studio) adapté au modèle de conversation unifié
/// </summary>
public sealed class GeminiProvider : IChatProvider
{
    private readonly GeminiClient client;

    public GeminiProvider(string apiKey, string? model) => client = new GeminiClient(apiKey, model);

    public string Id => "gemini";
    public string DisplayName => "Google Gemini";
    public bool IsLocal => false;
    public string Model => client.Model;

    public async IAsyncEnumerable<ChatEvent> StreamAsync(IReadOnlyList<ChatMessage> history, string systemPrompt, IReadOnlyList<ToolSpec> tools,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var contents = BuildContents(history, Id);
        JsonArray? decls = null;
        if (tools.Count > 0)
        {
            decls = new JsonArray();
            foreach (var t in tools)
            {
                var d = new JsonObject { ["name"] = t.Name, ["description"] = t.Description };
                if (t.Parameters != null)
                    d["parameters"] = t.Parameters.DeepClone();
                decls.Add(d);
            }
        }

        var calls = new List<ToolCall>();
        var e = client.StreamAsync(contents, systemPrompt, decls, ct).GetAsyncEnumerator(ct);
        try
        {
            while (true)
            {
                GeminiClient.Chunk chunk;
                try
                {
                    if (!await e.MoveNextAsync())
                        break;
                    chunk = e.Current;
                }
                catch (GeminiClient.GeminiException ex)
                {
                    throw new ProviderException(ex.Message, ex.Status switch
                    {
                        HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => ProviderFailure.Auth,
                        HttpStatusCode.BadRequest when ex.Message.Contains("clé", StringComparison.OrdinalIgnoreCase) => ProviderFailure.Auth,
                        HttpStatusCode.TooManyRequests => ProviderFailure.Quota,
                        HttpStatusCode.BadRequest => ProviderFailure.Rejected,
                        _ => ProviderFailure.Unavailable,
                    });
                }
                if (chunk.FunctionCall != null)
                {
                    var name = chunk.FunctionCall["name"]?.GetValue<string>() ?? "";
                    var args = chunk.FunctionCall["args"] as JsonObject ?? new JsonObject();
                    calls.Add(new ToolCall("gemini_" + Guid.NewGuid().ToString("N")[..8], name, (JsonObject)args.DeepClone(), chunk.RawPart));
                }
                else if (!string.IsNullOrEmpty(chunk.Text))
                    yield return new TextDelta(chunk.Text);
            }
        }
        finally
        {
            await e.DisposeAsync();
        }
        if (calls.Count > 0)
            yield return new ToolCallsRequested(calls);
    }

    /// <summary>
    /// Convertit l'historique unifié au format « contents » de Gemini.
    /// Les appels d'outils produits par Gemini sont renvoyés tels quels (thoughtSignature).
    /// </summary>
    internal static JsonArray BuildContents(IReadOnlyList<ChatMessage> history, string providerId)
    {
        var contents = new JsonArray();
        JsonObject? pendingUser = null;
        foreach (var msg in history)
        {
            switch (msg.Role)
            {
                case ChatRole.User:
                    pendingUser = null;
                    contents.Add(new JsonObject { ["role"] = "user", ["parts"] = new JsonArray(new JsonObject { ["text"] = msg.Text ?? "" }) });
                    break;
                case ChatRole.Assistant:
                    pendingUser = null;
                    var parts = new JsonArray();
                    if (!string.IsNullOrEmpty(msg.Text))
                        parts.Add(new JsonObject { ["text"] = msg.Text });
                    foreach (var c in msg.ToolCalls ?? [])
                        parts.Add(c.ProviderPart != null && msg.ProviderId == providerId
                            ? c.ProviderPart.DeepClone()
                            : new JsonObject { ["functionCall"] = new JsonObject { ["name"] = c.Name, ["args"] = c.Arguments.DeepClone() } });
                    if (parts.Count > 0)
                        contents.Add(new JsonObject { ["role"] = "model", ["parts"] = parts });
                    break;
                case ChatRole.Tool:
                    // Les réponses d'outils consécutives sont regroupées dans un seul message utilisateur
                    if (pendingUser == null)
                    {
                        pendingUser = new JsonObject { ["role"] = "user", ["parts"] = new JsonArray() };
                        contents.Add(pendingUser);
                    }
                    ((JsonArray)pendingUser["parts"]!).Add(new JsonObject
                    {
                        ["functionResponse"] = new JsonObject { ["name"] = msg.ToolName, ["response"] = msg.ToolResult?.DeepClone() ?? new JsonObject() },
                    });
                    break;
            }
        }
        return contents;
    }
}
