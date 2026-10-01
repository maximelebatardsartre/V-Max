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

namespace VPet_Simulator.Windows.Agent;

/// <summary>
/// V-Max : client minimal de l'API Gemini (REST, streaming SSE, appel de fonctions).
/// Aucune dépendance externe ; la clé est transmise dans l'en-tête x-goog-api-key (jamais dans l'URL).
/// </summary>
public sealed class GeminiClient
{
    public const string DefaultModel = "gemini-flash-latest";
    /// <summary>
    /// Adresse de l'API. Développement/tests uniquement : surchargeable par la variable d'environnement VMAX_GEMINI_BASEURL.
    /// </summary>
    private static readonly string BaseUrl = Environment.GetEnvironmentVariable("VMAX_GEMINI_BASEURL") is { Length: > 0 } u
        ? u.TrimEnd('/') + "/" : "https://generativelanguage.googleapis.com/v1beta/";
    private static readonly HttpClient Http = new() { Timeout = Timeout.InfiniteTimeSpan };

    private readonly string apiKey;
    public string Model { get; }

    public GeminiClient(string apiKey, string? model = null)
    {
        this.apiKey = apiKey;
        Model = string.IsNullOrWhiteSpace(model) ? DefaultModel : model.Trim();
    }

    /// <summary>
    /// Élément du flux de réponse : du texte, ou un appel de fonction demandé par le modèle
    /// </summary>
    public sealed record Chunk(string? Text, JsonObject? FunctionCall, JsonObject RawPart);

    /// <summary>
    /// Erreur de l'API, avec un message en français prêt à être affiché
    /// </summary>
    public sealed class GeminiException(string message, HttpStatusCode? status = null) : Exception(message)
    {
        public HttpStatusCode? Status { get; } = status;
    }

    /// <summary>
    /// Génère une réponse en streaming. <paramref name="contents"/> : historique au format Gemini.
    /// </summary>
    public async IAsyncEnumerable<Chunk> StreamAsync(JsonArray contents, string systemPrompt, JsonArray? functionDeclarations,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var body = new JsonObject
        {
            ["contents"] = contents.DeepClone(),
            ["systemInstruction"] = new JsonObject { ["parts"] = new JsonArray(new JsonObject { ["text"] = systemPrompt }) },
            ["generationConfig"] = new JsonObject { ["temperature"] = 0.8, ["maxOutputTokens"] = 1024 },
        };
        if (functionDeclarations != null && functionDeclarations.Count > 0)
            body["tools"] = new JsonArray(new JsonObject { ["functionDeclarations"] = functionDeclarations.DeepClone() });

        using var req = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}models/{Uri.EscapeDataString(Model)}:streamGenerateContent?alt=sse")
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        req.Headers.Add("x-goog-api-key", apiKey);

        HttpResponseMessage resp;
        try
        {
            resp = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        }
        catch (HttpRequestException e)
        {
            throw new GeminiException("Impossible de joindre Gemini (connexion Internet ?) : " + e.Message);
        }
        using (resp)
        {
            if (!resp.IsSuccessStatusCode)
                throw await ToException(resp).ConfigureAwait(false);

            using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            while (await reader.ReadLineAsync(ct).ConfigureAwait(false) is string line)
            {
                if (!line.StartsWith("data:", StringComparison.Ordinal))
                    continue;
                var json = line[5..].Trim();
                if (json.Length == 0)
                    continue;
                JsonNode? node;
                try { node = JsonNode.Parse(json); }
                catch (JsonException) { continue; }
                var parts = node?["candidates"]?[0]?["content"]?["parts"] as JsonArray;
                if (parts == null)
                {
                    var blocked = node?["promptFeedback"]?["blockReason"]?.GetValue<string>();
                    if (blocked != null)
                        throw new GeminiException("La demande a été bloquée par les filtres de sécurité de Gemini (" + blocked + ").");
                    continue;
                }
                foreach (var p in parts)
                {
                    if (p is not JsonObject part)
                        continue;
                    var fc = part["functionCall"] as JsonObject;
                    string? text = fc == null && part["thought"]?.GetValue<bool>() != true ? part["text"]?.GetValue<string>() : null;
                    yield return new Chunk(text, fc, (JsonObject)part.DeepClone());
                }
            }
        }
    }

    /// <summary>
    /// Liste les modèles utilisables pour la génération de texte
    /// </summary>
    public static async Task<List<string>> ListModelsAsync(string apiKey, CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, BaseUrl + "models?pageSize=200");
        req.Headers.Add("x-goog-api-key", apiKey);
        using var resp = await Http.SendAsync(req, ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
            throw await ToException(resp).ConfigureAwait(false);
        var node = JsonNode.Parse(await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
        var list = new List<string>();
        foreach (var m in node?["models"] as JsonArray ?? new JsonArray())
        {
            var methods = m?["supportedGenerationMethods"] as JsonArray;
            var name = m?["name"]?.GetValue<string>();
            if (name == null || methods == null || !methods.Any(x => x?.GetValue<string>() == "generateContent"))
                continue;
            name = name.StartsWith("models/") ? name[7..] : name;
            if (name.Contains("gemini", StringComparison.OrdinalIgnoreCase) && !name.Contains("embedding") && !name.Contains("image") && !name.Contains("tts"))
                list.Add(name);
        }
        list.Sort(StringComparer.OrdinalIgnoreCase);
        return list;
    }

    private static async Task<GeminiException> ToException(HttpResponseMessage resp)
    {
        string detail = "";
        try
        {
            var node = JsonNode.Parse(await resp.Content.ReadAsStringAsync().ConfigureAwait(false));
            detail = node?["error"]?["message"]?.GetValue<string>() ?? "";
        }
        catch { }
        string msg = resp.StatusCode switch
        {
            HttpStatusCode.BadRequest when detail.Contains("API key", StringComparison.OrdinalIgnoreCase) => "La clé API Gemini est invalide.",
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => "La clé API Gemini est refusée (invalide ou sans accès à ce modèle).",
            HttpStatusCode.NotFound => "Le modèle Gemini demandé est introuvable. Choisis-en un autre dans les paramètres.",
            HttpStatusCode.TooManyRequests => "Quota Gemini atteint pour le moment. Réessaie un peu plus tard.",
            >= HttpStatusCode.InternalServerError => "Le service Gemini rencontre un problème. Réessaie dans un instant.",
            _ => "Erreur Gemini (" + (int)resp.StatusCode + ")",
        };
        if (!string.IsNullOrEmpty(detail))
            msg += "\n" + (detail.Length > 300 ? detail[..300] + "…" : detail);
        return new GeminiException(msg, resp.StatusCode);
    }
}
