using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace VPet_Simulator.Windows.Agent.Providers;

/// <summary>
/// Fournisseur générique compatible avec l'API « chat completions » d'OpenAI :
/// Groq, Mistral, OpenRouter, Cerebras, Ollama, LM Studio…
/// </summary>
public sealed class OpenAiCompatibleProvider : IChatProvider
{
    internal static readonly HttpClient Http = new() { Timeout = Timeout.InfiniteTimeSpan };

    private readonly string baseUrl;
    private readonly string? apiKey;
    private readonly string[] preferredModels;
    private readonly Func<JsonObject, bool>? modelFilter;
    private string? model;

    public string Id { get; }
    public string DisplayName { get; }
    public bool IsLocal { get; }
    public string Model => model ?? "(automatique)";

    /// <param name="model">Modèle imposé, ou null pour un choix automatique parmi <paramref name="preferredModels"/> / la liste du service</param>
    /// <param name="modelFilter">Filtre des modèles candidats lors du choix automatique (ex. modèles gratuits d'OpenRouter)</param>
    public OpenAiCompatibleProvider(string id, string displayName, string baseUrl, string? apiKey, string? model, bool isLocal,
        string[]? preferredModels = null, Func<JsonObject, bool>? modelFilter = null)
    {
        Id = id;
        DisplayName = displayName;
        this.baseUrl = baseUrl.TrimEnd('/') + "/";
        this.apiKey = apiKey;
        this.model = string.IsNullOrWhiteSpace(model) ? null : model;
        IsLocal = isLocal;
        this.preferredModels = preferredModels ?? [];
        this.modelFilter = modelFilter;
    }

    private HttpRequestMessage Request(HttpMethod method, string path)
    {
        var req = new HttpRequestMessage(method, baseUrl + path);
        if (!string.IsNullOrEmpty(apiKey))
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
        req.Headers.Add("X-Title", "V-Max");
        return req;
    }

    /// <summary>
    /// Liste des modèles proposés par le service
    /// </summary>
    public async Task<List<JsonObject>> ListModelsAsync(CancellationToken ct)
    {
        using var req = Request(HttpMethod.Get, "models");
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(IsLocal ? 3 : 15));
        using var resp = await Http.SendAsync(req, cts.Token).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
            throw await ToException(resp).ConfigureAwait(false);
        var node = JsonNode.Parse(await resp.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false));
        return (node?["data"] as JsonArray ?? new JsonArray()).OfType<JsonObject>().ToList();
    }

    private async Task<string> ResolveModelAsync(CancellationToken ct)
    {
        if (model != null)
            return model;
        var models = await ListModelsAsync(ct).ConfigureAwait(false);
        var candidates = models.Where(m => modelFilter?.Invoke(m) ?? true)
            .Select(m => m["id"]?.GetValue<string>()).Where(m => !string.IsNullOrEmpty(m)).Cast<string>().ToList();
        if (candidates.Count == 0)
            throw new ProviderException($"{DisplayName} : aucun modèle disponible.", ProviderFailure.Unavailable);
        model = preferredModels.Select(p => candidates.FirstOrDefault(c => c.Contains(p, StringComparison.OrdinalIgnoreCase)))
                    .FirstOrDefault(c => c != null)
                ?? candidates.Where(c => !c.Contains("embed", StringComparison.OrdinalIgnoreCase)).FirstOrDefault()
                ?? candidates[0];
        return model;
    }

    public async IAsyncEnumerable<ChatEvent> StreamAsync(IReadOnlyList<ChatMessage> history, string systemPrompt, IReadOnlyList<ToolSpec> tools,
        [EnumeratorCancellation] CancellationToken ct)
    {
        string m;
        try
        {
            m = await ResolveModelAsync(ct).ConfigureAwait(false);
        }
        catch (HttpRequestException e)
        {
            throw new ProviderException($"{DisplayName} injoignable : {e.Message}", ProviderFailure.Unavailable);
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new ProviderException($"{DisplayName} ne répond pas.", ProviderFailure.Unavailable);
        }

        var body = new JsonObject
        {
            ["model"] = m,
            ["stream"] = true,
            ["temperature"] = 0.8,
            ["max_tokens"] = 1024,
            ["messages"] = BuildMessages(history, systemPrompt),
        };
        if (tools.Count > 0)
            body["tools"] = new JsonArray(tools.Select(t => (JsonNode)new JsonObject
            {
                ["type"] = "function",
                ["function"] = new JsonObject
                {
                    ["name"] = t.Name,
                    ["description"] = t.Description,
                    ["parameters"] = t.Parameters?.DeepClone() ?? new JsonObject { ["type"] = "object", ["properties"] = new JsonObject() },
                },
            }).ToArray());

        using var req = Request(HttpMethod.Post, "chat/completions");
        req.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        HttpResponseMessage resp;
        try
        {
            resp = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        }
        catch (HttpRequestException e)
        {
            throw new ProviderException($"{DisplayName} injoignable : {e.Message}", ProviderFailure.Unavailable);
        }
        using (resp)
        {
            if (!resp.IsSuccessStatusCode)
                throw await ToException(resp).ConfigureAwait(false);
            using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            var parser = new OpenAiStreamParser();
            while (await reader.ReadLineAsync(ct).ConfigureAwait(false) is string line)
            {
                foreach (var ev in parser.Feed(line))
                    yield return ev;
                if (parser.Done)
                    break;
            }
            foreach (var ev in parser.Complete())
                yield return ev;
        }
    }

    /// <summary>
    /// Convertit l'historique unifié au format « messages » d'OpenAI
    /// </summary>
    internal static JsonArray BuildMessages(IReadOnlyList<ChatMessage> history, string systemPrompt)
    {
        var arr = new JsonArray { new JsonObject { ["role"] = "system", ["content"] = systemPrompt } };
        foreach (var msg in history)
        {
            switch (msg.Role)
            {
                case ChatRole.User:
                    arr.Add(new JsonObject { ["role"] = "user", ["content"] = msg.Text ?? "" });
                    break;
                case ChatRole.Assistant:
                    var a = new JsonObject { ["role"] = "assistant", ["content"] = msg.Text ?? "" };
                    if (msg.ToolCalls is { Count: > 0 })
                        a["tool_calls"] = new JsonArray(msg.ToolCalls.Select(c => (JsonNode)new JsonObject
                        {
                            ["id"] = c.Id,
                            ["type"] = "function",
                            ["function"] = new JsonObject { ["name"] = c.Name, ["arguments"] = c.Arguments.ToJsonString() },
                        }).ToArray());
                    arr.Add(a);
                    break;
                case ChatRole.Tool:
                    arr.Add(new JsonObject
                    {
                        ["role"] = "tool",
                        ["tool_call_id"] = msg.ToolCallId,
                        ["content"] = msg.ToolResult?.ToJsonString() ?? "{}",
                    });
                    break;
            }
        }
        return arr;
    }

    internal static async Task<ProviderException> ToException(HttpResponseMessage resp)
    {
        string detail = "";
        try
        {
            var text = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            var node = JsonNode.Parse(text);
            detail = node?["error"]?["message"]?.GetValue<string>() ?? node?["error"]?.ToString() ?? node?["message"]?.ToString() ?? text;
        }
        catch { }
        if (detail.Length > 240)
            detail = detail[..240] + "…";
        var (failure, msg) = resp.StatusCode switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => (ProviderFailure.Auth, "clé API refusée"),
            HttpStatusCode.PaymentRequired => (ProviderFailure.Quota, "crédits épuisés"),
            HttpStatusCode.TooManyRequests => (ProviderFailure.Quota, "quota atteint"),
            HttpStatusCode.NotFound => (ProviderFailure.Unavailable, "modèle introuvable"),
            HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity => (ProviderFailure.Rejected, "requête refusée"),
            _ => (ProviderFailure.Unavailable, "service indisponible (" + (int)resp.StatusCode + ")"),
        };
        return new ProviderException(msg + (detail.Length > 0 ? " — " + detail : ""), failure);
    }
}

/// <summary>
/// Analyse du flux SSE « chat completions » : texte au fil de l'eau, appels d'outils reconstitués
/// (les arguments arrivent en plusieurs morceaux, indexés par position).
/// </summary>
public sealed class OpenAiStreamParser
{
    private readonly SortedDictionary<int, (string? id, string? name, StringBuilder args)> calls = new();
    public bool Done { get; private set; }

    public IEnumerable<ChatEvent> Feed(string line)
    {
        if (!line.StartsWith("data:", StringComparison.Ordinal))
            yield break;
        var data = line[5..].Trim();
        if (data.Length == 0)
            yield break;
        if (data == "[DONE]")
        {
            Done = true;
            yield break;
        }
        JsonNode? node;
        try { node = JsonNode.Parse(data); }
        catch (JsonException) { yield break; }
        if (node?["error"] is JsonNode err)
            throw new ProviderException("erreur du service — " + (err["message"]?.ToString() ?? err.ToString()), ProviderFailure.Unavailable);
        var delta = node?["choices"]?[0]?["delta"];
        if (delta == null)
            yield break;
        var content = delta["content"];
        if (content is JsonValue v && v.TryGetValue<string>(out var text) && text.Length > 0)
            yield return new TextDelta(text);
        if (delta["tool_calls"] is JsonArray tcs)
        {
            foreach (var tc in tcs)
            {
                int index = tc?["index"]?.GetValue<int>() ?? calls.Count;
                if (!calls.TryGetValue(index, out var acc))
                    acc = (null, null, new StringBuilder());
                acc.id ??= tc?["id"]?.GetValue<string>();
                acc.name ??= tc?["function"]?["name"]?.GetValue<string>();
                var piece = tc?["function"]?["arguments"];
                if (piece is JsonValue pv && pv.TryGetValue<string>(out var s))
                    acc.args.Append(s);
                else if (piece is JsonObject po)
                    acc.args.Append(po.ToJsonString());
                calls[index] = acc;
            }
        }
    }

    /// <summary>
    /// Fin du flux : émet les appels d'outils reconstitués
    /// </summary>
    public IEnumerable<ChatEvent> Complete()
    {
        if (calls.Count == 0)
            yield break;
        var list = new List<ToolCall>();
        foreach (var (index, acc) in calls)
        {
            if (string.IsNullOrEmpty(acc.name))
                continue;
            JsonObject args;
            try { args = JsonNode.Parse(acc.args.Length == 0 ? "{}" : acc.args.ToString()) as JsonObject ?? new JsonObject(); }
            catch (JsonException) { args = new JsonObject(); }
            list.Add(new ToolCall(acc.id ?? "call_" + index, acc.name!, args));
        }
        calls.Clear();
        if (list.Count > 0)
            yield return new ToolCallsRequested(list);
    }
}
