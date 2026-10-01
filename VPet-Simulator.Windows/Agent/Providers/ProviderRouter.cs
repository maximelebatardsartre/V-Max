using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace VPet_Simulator.Windows.Agent.Providers;

/// <summary>
/// Description d'un fournisseur connu de V-Max (catalogue affiché dans l'accueil de la discussion)
/// </summary>
public sealed record ProviderInfo(
    string Id,
    string Name,
    string Tagline,
    bool IsLocal,
    string? SignupUrl,
    string? SecretName,
    Func<string?, string?, IChatProvider> Create);

/// <summary>
/// V-Max : catalogue des fournisseurs gratuits et routeur avec bascule automatique.
/// Ordre : clés cloud configurées (dans l'ordre de préférence), puis IA locales détectées.
/// Quand un fournisseur échoue (quota, panne, clé refusée), il est mis en pause et le suivant prend le relais.
/// </summary>
public static class ProviderRouter
{
    public static readonly IReadOnlyList<ProviderInfo> Catalog =
    [
        new("gemini", "Google Gemini", "Le plus polyvalent · 1 500 requêtes/jour gratuites", false,
            "https://aistudio.google.com/app/apikey", "GeminiApiKey",
            (key, model) => new GeminiProvider(key!, model)),
        new("groq", "Groq", "Ultra-rapide · ~1 000 requêtes/jour gratuites", false,
            "https://console.groq.com/keys", "GroqApiKey",
            (key, model) => new OpenAiCompatibleProvider("groq", "Groq", "https://api.groq.com/openai/v1", key, model, false,
                ["llama-3.3-70b-versatile", "llama-4", "qwen", "llama"])),
        new("mistral", "Mistral AI", "Français · quota gratuit quotidien", false,
            "https://console.mistral.ai/api-keys", "MistralApiKey",
            (key, model) => new OpenAiCompatibleProvider("mistral", "Mistral AI", "https://api.mistral.ai/v1", key, model ?? "mistral-small-latest", false)),
        new("cerebras", "Cerebras", "Très rapide · offre gratuite", false,
            "https://cloud.cerebras.ai", "CerebrasApiKey",
            (key, model) => new OpenAiCompatibleProvider("cerebras", "Cerebras", "https://api.cerebras.ai/v1", key, model, false,
                ["llama-3.3-70b", "qwen-3", "gpt-oss", "llama"])),
        new("openrouter", "OpenRouter", "Modèles gratuits variés", false,
            "https://openrouter.ai/settings/keys", "OpenRouterApiKey",
            (key, model) => new OpenAiCompatibleProvider("openrouter", "OpenRouter", "https://openrouter.ai/api/v1", key, model, false,
                ["llama-3.3-70b", "gemini", "deepseek", "qwen"],
                m => (m["id"]?.GetValue<string>() ?? "").EndsWith(":free")
                     && (m["supported_parameters"] as JsonArray)?.Any(p => p?.GetValue<string>() == "tools") == true)),
        new("ollama", "Ollama (local)", "Sur ton PC · hors ligne · illimité", true,
            "https://ollama.com/download", null,
            (_, model) => new OpenAiCompatibleProvider("ollama", "Ollama", "http://127.0.0.1:11434/v1", null, model, true,
                ["qwen3", "qwen2.5", "llama3.2", "llama3.1", "mistral", "gemma"])),
        new("lmstudio", "LM Studio (local)", "Sur ton PC · hors ligne · illimité", true,
            "https://lmstudio.ai", null,
            (_, model) => new OpenAiCompatibleProvider("lmstudio", "LM Studio", "http://127.0.0.1:1234/v1", null, model, true)),
    ];

    /// <summary>Fournisseurs mis en pause jusqu'à une date (quota, panne)</summary>
    private static readonly ConcurrentDictionary<string, DateTime> cooldown = new();
    /// <summary>Clés refusées (jusqu'à ce que la clé change)</summary>
    private static readonly ConcurrentDictionary<string, string> rejectedKeys = new();
    private static readonly ConcurrentDictionary<string, bool> localReachable = new();
    private static readonly ConcurrentDictionary<string, IChatProvider> instances = new();

    /// <summary>Modèle choisi par l'utilisateur pour un fournisseur (null = automatique)</summary>
    public static Func<string, string?> ModelFor { get; set; } = _ => null;

    /// <summary>Fournisseur utilisé pour la dernière réponse réussie</summary>
    public static IChatProvider? Current { get; private set; }

    /// <summary>Déclenché quand le fournisseur actif change (affichage de l'état dans la discussion)</summary>
    public static event Action<IChatProvider?>? CurrentChanged;

    public static string? KeyFor(ProviderInfo p) =>
        p.SecretName == null ? null : SecretStore.Get(p.SecretName)
            ?? (p.Id == "gemini" ? Environment.GetEnvironmentVariable("VMAX_GEMINI_APIKEY") : null);

    /// <summary>
    /// Détecte les IA locales (Ollama, LM Studio) — rapide, sans bloquer
    /// </summary>
    public static async Task RefreshLocalAsync()
    {
        foreach (var p in Catalog.Where(c => c.IsLocal))
        {
            try
            {
                var provider = (OpenAiCompatibleProvider)p.Create(null, ModelFor(p.Id));
                var models = await provider.ListModelsAsync(CancellationToken.None);
                localReachable[p.Id] = models.Count > 0;
            }
            catch
            {
                localReachable[p.Id] = false;
            }
        }
    }

    public static bool IsLocalReachable(string id) => localReachable.TryGetValue(id, out var ok) && ok;

    /// <summary>
    /// Fournisseurs utilisables actuellement, dans l'ordre d'essai
    /// </summary>
    public static List<(ProviderInfo info, IChatProvider provider)> Available()
    {
        var list = new List<(ProviderInfo, IChatProvider)>();
        foreach (var p in Catalog)
        {
            string? key = KeyFor(p);
            if (!p.IsLocal && string.IsNullOrEmpty(key))
                continue;
            if (p.IsLocal && !IsLocalReachable(p.Id))
                continue;
            if (key != null && rejectedKeys.TryGetValue(p.Id, out var bad) && bad == key)
                continue;
            if (cooldown.TryGetValue(p.Id, out var until) && until > DateTime.Now)
                continue;
            string cacheKey = p.Id + "|" + (key?.GetHashCode() ?? 0) + "|" + ModelFor(p.Id);
            var provider = instances.GetOrAdd(cacheKey, _ => p.Create(key, ModelFor(p.Id)));
            list.Add((p, provider));
        }
        return list;
    }

    /// <summary>
    /// Au moins un fournisseur est-il configuré (clé ou IA locale) ?
    /// </summary>
    public static bool HasAnyConfigured() =>
        Catalog.Any(p => p.IsLocal ? IsLocalReachable(p.Id) : !string.IsNullOrEmpty(KeyFor(p)));

    /// <summary>
    /// Oublie les pauses et refus (après ajout ou changement de clé)
    /// </summary>
    public static void Reset(string? providerId = null)
    {
        if (providerId == null)
        {
            cooldown.Clear();
            rejectedKeys.Clear();
            instances.Clear();
            return;
        }
        cooldown.TryRemove(providerId, out _);
        rejectedKeys.TryRemove(providerId, out _);
        foreach (var k in instances.Keys.Where(k => k.StartsWith(providerId + "|")).ToList())
            instances.TryRemove(k, out _);
    }

    /// <summary>
    /// Génère une réponse avec le premier fournisseur disponible ; bascule sur le suivant en cas d'échec
    /// AVANT que du contenu n'ait été transmis (une réponse commencée n'est jamais mélangée entre deux modèles).
    /// </summary>
    public static IAsyncEnumerable<(IChatProvider provider, ChatEvent ev)> StreamAsync(
        IReadOnlyList<ChatMessage> history, string systemPrompt, IReadOnlyList<ToolSpec> tools, CancellationToken ct)
        => StreamAsync(Available(), history, systemPrompt, tools, ct);

    /// <summary>
    /// Cœur du routage, sur une liste explicite de fournisseurs (testable)
    /// </summary>
    internal static async IAsyncEnumerable<(IChatProvider provider, ChatEvent ev)> StreamAsync(
        IEnumerable<(ProviderInfo info, IChatProvider provider)> candidates,
        IReadOnlyList<ChatMessage> history, string systemPrompt, IReadOnlyList<ToolSpec> tools,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var errors = new List<string>();
        foreach (var (info, provider) in candidates)
        {
            bool started = false;
            var enumerator = provider.StreamAsync(history, systemPrompt, tools, ct).GetAsyncEnumerator(ct);
            ProviderException? failure = null;
            try
            {
                while (true)
                {
                    ChatEvent ev;
                    try
                    {
                        if (!await enumerator.MoveNextAsync())
                            break;
                        ev = enumerator.Current;
                    }
                    catch (ProviderException e) when (!started)
                    {
                        failure = e;
                        break;
                    }
                    catch (HttpRequestException e) when (!started)
                    {
                        failure = new ProviderException(e.Message, ProviderFailure.Unavailable);
                        break;
                    }
                    if (!started)
                    {
                        started = true;
                        if (!ReferenceEquals(Current, provider))
                        {
                            Current = provider;
                            CurrentChanged?.Invoke(provider);
                        }
                    }
                    yield return (provider, ev);
                }
            }
            finally
            {
                await enumerator.DisposeAsync();
            }
            if (failure == null)
                yield break;

            errors.Add($"{info.Name} : {failure.Message}");
            switch (failure.Failure)
            {
                case ProviderFailure.Auth:
                    if (KeyFor(info) is string k)
                        rejectedKeys[info.Id] = k;
                    break;
                case ProviderFailure.Quota:
                    cooldown[info.Id] = DateTime.Now.AddMinutes(2);
                    break;
                case ProviderFailure.Unavailable:
                    cooldown[info.Id] = DateTime.Now.AddSeconds(30);
                    if (info.IsLocal)
                        localReachable[info.Id] = false;
                    break;
                case ProviderFailure.Rejected:
                    throw failure;
            }
        }
        if (errors.Count == 0)
            throw new ProviderException("Aucune IA n'est configurée.", ProviderFailure.Auth);
        throw new ProviderException(string.Join("\n", errors), ProviderFailure.Unavailable);
    }
}
