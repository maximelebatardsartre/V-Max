using System;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using VPet_Simulator.Windows.Interface;

namespace VPet_Simulator.Windows;

/// <summary>
/// V-Max : « Web Installer ». L'installeur GitHub ne contient que le cœur de l'application ; les gros assets
/// (animations de base + mods traduits) sont hébergés sur Cloudflare R2 et téléchargés au premier lancement.
/// Ils sont rangés hors du dossier d'installation (%LOCALAPPDATA%\V-Max\assets) pour survivre aux mises à jour.
/// En développement (assets déjà présents à côté de l'exe), ce service ne fait rien.
/// </summary>
public static class AssetService
{
    // Les assets sont rangés sous le préfixe « maxine-assets/ » dans le bucket R2 ; l'URL publique les sert donc
    // depuis https://pub-….r2.dev/maxine-assets/… (voir packaging\upload-assets.ps1, qui téléverse sous ce préfixe).
    private const string DefaultBaseUrl = "https://pub-fe5cb60ed1c14f82a3895d2bc32929d5.r2.dev/maxine-assets";

    /// <summary>Base publique du bucket R2 (lecture seule, aucune clé). Surchargeable par VMAX_ASSETS_BASEURL (tests).</summary>
    public static string BaseUrl =>
        Environment.GetEnvironmentVariable("VMAX_ASSETS_BASEURL") is { Length: > 0 } u ? u : DefaultBaseUrl;
    private const string ManifestName = "assets.json";

    /// <summary>Dossier persistant des assets téléchargés</summary>
    public static string Root => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "V-Max", "assets");

    private static string VersionFile => Path.Combine(Root, "version.txt");
    private static string CoreMarker => Path.Combine(Root, "mod", "0000_core", "info.lps");

    private static readonly HttpClient Http = new() { Timeout = Timeout.InfiniteTimeSpan };

    /// <summary>Sans le moindre octet reçu pendant ce délai, on considère la connexion perdue.</summary>
    private static readonly TimeSpan IdleTimeout = TimeSpan.FromSeconds(120);

    /// <summary>Les assets téléchargés sont présents (0000_core extrait)</summary>
    public static bool Downloaded => File.Exists(CoreMarker);

    static AssetService() => Http.DefaultRequestHeaders.UserAgent.TryParseAdd("V-Max");

    /// <summary>
    /// S'assure que les assets sont présents et à jour avant le chargement du jeu. Met à jour l'écran d'accueil
    /// pendant le téléchargement. Renvoie null si OK, sinon un message d'erreur (à afficher au premier lancement).
    /// Ne lève jamais : tout échec est transformé en message lisible pour l'utilisateur.
    /// </summary>
    public static async Task<string?> EnsureAsync(MainWindow mw, CancellationToken cancel = default)
    {
        // développement : les assets sont déjà à côté de l'exe (dossier mod\0000_core) → rien à faire
        if (Directory.Exists(Path.Combine(ExtensionValue.BaseDirectory, "mod", "0000_core")))
            return null;

        // 1) manifeste (minuscule) : on échoue vite plutôt que de rester figé si R2 ne répond pas
        JsonNode? manifest;
        try
        {
            using var mcts = CancellationTokenSource.CreateLinkedTokenSource(cancel);
            mcts.CancelAfter(TimeSpan.FromSeconds(30));
            var raw = await Http.GetStringAsync(BaseUrl.TrimEnd('/') + "/" + ManifestName, mcts.Token);
            manifest = JsonNode.Parse(raw.TrimStart('﻿'));
        }
        catch
        {
            // pas de réseau : on se rabat sur les assets déjà téléchargés si présents
            if (Downloaded)
                return null;
            return "Maxine a besoin d'Internet pour son tout premier démarrage (téléchargement des animations). "
                 + "Vérifie ta connexion puis relance.";
        }

        // 2) lecture défensive du manifeste (un champ mal typé ne doit pas faire planter le démarrage)
        string remoteVer, zipName; string? sha; long size;
        try
        {
            remoteVer = manifest?["version"]?.ToString() ?? "1";
            zipName = manifest?["zip"]?.ToString() ?? "maxine-assets.zip";
            sha = manifest?["sha256"]?.ToString();
            size = long.TryParse(manifest?["size"]?.ToString(), out var s) ? s : 0;
        }
        catch
        {
            if (Downloaded) return null;
            return "Le fichier de description des animations est illisible. Réessaie un peu plus tard.";
        }

        string localVer = SafeRead(VersionFile);
        if (Downloaded && localVer == remoteVer)
            return null; // déjà à jour

        await mw.Dispatcher.InvokeAsync(() => mw.LoadingStatus = "Préparation du premier lancement…");
        var zipPath = Path.Combine(Root, zipName + ".part");
        try
        {
            Directory.CreateDirectory(Root);
            var err = await DownloadAsync(mw, BaseUrl.TrimEnd('/') + "/" + zipName, zipPath, size, cancel);
            if (err != null)
            {
                TryDelete(zipPath);
                return err;
            }

            if (sha != null)
            {
                await mw.Dispatcher.InvokeAsync(() => mw.LoadingStatus = "Vérification des fichiers…");
                if (!await VerifyAsync(zipPath, sha, cancel))
                {
                    TryDelete(zipPath);
                    return "Les fichiers téléchargés sont corrompus. Relance Maxine pour réessayer.";
                }
            }

            await mw.Dispatcher.InvokeAsync(() => mw.LoadingStatus = "Installation des animations et des mods…");
            // on remplace proprement l'ancien contenu (sauf le .part en cours)
            foreach (var sub in new[] { "mod", "mods-inclus" })
                TryDeleteDir(Path.Combine(Root, sub));
            await Task.Run(() => ZipFile.ExtractToDirectory(zipPath, Root, overwriteFiles: true), cancel);
            TryDelete(zipPath);

            if (!Downloaded)
                return "L'installation des animations a échoué (fichier Core absent après extraction). Relance Maxine.";
            File.WriteAllText(VersionFile, remoteVer); // version écrite seulement une fois l'installation confirmée
            return null;
        }
        catch (OperationCanceledException)
        {
            TryDelete(zipPath);
            return "Téléchargement interrompu. Relance Maxine pour réessayer.";
        }
        catch (Exception e)
        {
            TryDelete(zipPath);
            return "Impossible d'installer les animations : " + e.Message;
        }
    }

    private static async Task<string?> DownloadAsync(MainWindow mw, string url, string dest, long expected, CancellationToken cancel)
    {
        try
        {
            using var idle = CancellationTokenSource.CreateLinkedTokenSource(cancel);
            idle.CancelAfter(IdleTimeout);
            using var resp = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, idle.Token);
            resp.EnsureSuccessStatusCode();
            long total = resp.Content.Headers.ContentLength ?? expected;
            await using var src = await resp.Content.ReadAsStreamAsync(idle.Token);
            await using var dst = File.Create(dest);
            var buffer = new byte[1 << 20];
            long read = 0;
            int lastPct = -1, n;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (true)
            {
                idle.CancelAfter(IdleTimeout); // réarme le minuteur d'inactivité à chaque lecture
                try
                {
                    n = await src.ReadAsync(buffer, idle.Token);
                }
                catch (OperationCanceledException) when (!cancel.IsCancellationRequested)
                {
                    TryDelete(dest);
                    return "Connexion perdue pendant le téléchargement des animations. "
                         + "Vérifie ta connexion Internet puis relance Maxine.";
                }
                if (n <= 0)
                    break;
                await dst.WriteAsync(buffer.AsMemory(0, n), cancel);
                read += n;
                int pct = total > 0 ? (int)(read * 100 / total) : -1;
                if (pct != lastPct)
                {
                    lastPct = pct;
                    double mb = read / 1_048_576.0, totalMb = total / 1_048_576.0;
                    double speed = sw.Elapsed.TotalSeconds > 0 ? mb / sw.Elapsed.TotalSeconds : 0;
                    string text = total > 0
                        ? $"Téléchargement des animations et des mods… {pct} %  ({mb:0} / {totalMb:0} Mo, {speed:0} Mo/s)"
                        : $"Téléchargement… {mb:0} Mo";
                    await mw.Dispatcher.InvokeAsync(() => mw.LoadingStatus = text);
                }
            }
            if (total > 0 && read < total)
            {
                TryDelete(dest);
                return "Téléchargement incomplet des animations. Relance Maxine pour réessayer.";
            }
            return null;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception e)
        {
            TryDelete(dest);
            return "Téléchargement impossible : " + e.Message;
        }
    }

    private static async Task<bool> VerifyAsync(string path, string expectedSha, CancellationToken cancel)
    {
        try
        {
            await using var fs = File.OpenRead(path);
            var hash = await SHA256.HashDataAsync(fs, cancel);
            return Convert.ToHexString(hash).Equals(expectedSha, StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    private static string SafeRead(string path)
    {
        try { return File.Exists(path) ? File.ReadAllText(path).Trim() : ""; }
        catch { return ""; }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    private static void TryDeleteDir(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, true); } catch { }
    }
}
