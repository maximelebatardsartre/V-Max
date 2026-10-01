using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Threading;

namespace VPet_Simulator.Windows.Agent.Providers;

/// <summary>
/// Rôle d'un message de conversation (modèle unifié, indépendant du fournisseur)
/// </summary>
public enum ChatRole { User, Assistant, Tool }

/// <summary>
/// Appel d'outil demandé par le modèle
/// </summary>
/// <param name="Id">Identifiant de l'appel (format OpenAI ; généré pour Gemini)</param>
/// <param name="Name">Nom de l'outil</param>
/// <param name="Arguments">Arguments JSON</param>
/// <param name="ProviderPart">Partie brute propre au fournisseur (ex. Gemini : thoughtSignature à renvoyer à l'identique)</param>
public sealed record ToolCall(string Id, string Name, JsonObject Arguments, JsonObject? ProviderPart = null);

/// <summary>
/// Message de conversation unifié
/// </summary>
public sealed class ChatMessage
{
    public required ChatRole Role { get; init; }
    public string? Text { get; init; }
    /// <summary>Appels d'outils (messages assistant)</summary>
    public IReadOnlyList<ToolCall>? ToolCalls { get; init; }
    /// <summary>Résultat d'outil (messages Tool)</summary>
    public string? ToolCallId { get; init; }
    public string? ToolName { get; init; }
    public JsonObject? ToolResult { get; init; }
    /// <summary>Fournisseur ayant produit ce message (les parties brutes ne sont réutilisées que par lui)</summary>
    public string? ProviderId { get; init; }

    public static ChatMessage User(string text) => new() { Role = ChatRole.User, Text = text };
}

/// <summary>
/// Description d'un outil transmise au modèle
/// </summary>
public sealed record ToolSpec(string Name, string Description, JsonObject? Parameters);

/// <summary>
/// Événement du flux de réponse
/// </summary>
public abstract record ChatEvent;
public sealed record TextDelta(string Text) : ChatEvent;
public sealed record ToolCallsRequested(IReadOnlyList<ToolCall> Calls) : ChatEvent;

/// <summary>
/// Catégorie d'échec, utilisée par le routeur pour basculer vers un autre fournisseur
/// </summary>
public enum ProviderFailure
{
    /// <summary>Clé absente, invalide ou refusée : ne pas réessayer tant que la clé n'a pas changé</summary>
    Auth,
    /// <summary>Quota ou limite de débit atteint : réessayer plus tard</summary>
    Quota,
    /// <summary>Service indisponible, réseau, délai dépassé : réessayer bientôt</summary>
    Unavailable,
    /// <summary>Requête refusée par le modèle (filtre, format) : inutile de basculer</summary>
    Rejected,
}

/// <summary>
/// Erreur d'un fournisseur, avec un message en français prêt à afficher
/// </summary>
public sealed class ProviderException(string message, ProviderFailure failure) : Exception(message)
{
    public ProviderFailure Failure { get; } = failure;
}

/// <summary>
/// Fournisseur de modèle de langage
/// </summary>
public interface IChatProvider
{
    /// <summary>Identifiant stable (« gemini », « groq », « ollama »…)</summary>
    string Id { get; }
    /// <summary>Nom affiché</summary>
    string DisplayName { get; }
    /// <summary>Exécuté sur le PC (aucune donnée envoyée sur Internet)</summary>
    bool IsLocal { get; }
    /// <summary>Modèle effectivement utilisé (connu après la première requête pour les sélections automatiques)</summary>
    string Model { get; }
    IAsyncEnumerable<ChatEvent> StreamAsync(IReadOnlyList<ChatMessage> history, string systemPrompt, IReadOnlyList<ToolSpec> tools, CancellationToken ct);
}
