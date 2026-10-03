using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using VPet_Simulator.Core;
using Velopack;
using Velopack.Sources;

namespace VPet_Simulator.Windows;

/// <summary>
/// V-Max : mise à jour automatique via les Releases GitHub (Velopack). Au lancement, l'application regarde en
/// arrière-plan s'il existe une version plus récente publiée ; si oui, elle la télécharge silencieusement et la
/// prépare. La nouvelle version est appliquée toute seule au prochain démarrage — aucun popup, aucun clic, aucune
/// interruption. L'illusion d'un logiciel entièrement autonome est préservée.
/// N'a d'effet que sur une installation faite par l'installeur (en développement, no-op).
/// </summary>
public static class UpdateService
{
    /// <summary>Dépôt dont on lit les Releases publiées</summary>
    public const string RepositoryUrl = "https://github.com/maximelebatardsartre/V-Max";

    private static bool chec_done;

    /// <summary>
    /// Un SEUL travail de mise à jour à la fois : la vérification auto du démarrage et le « Vérifier maintenant »
    /// manuel partagent ce verrou. Sans lui, les deux se battaient pour le verrou exclusif de Velopack et l'un
    /// échouait avec un message anglais (« Failed to acquire exclusive lock file »).
    /// </summary>
    private static readonly SemaphoreSlim gate = new(1, 1);

    /// <summary>Version déjà téléchargée et prête à s'appliquer au prochain démarrage (pour un retour cohérent).</summary>
    private static string? stagedVersion;

    /// <summary>
    /// À appeler une fois au lancement (hors de l'amorçage Velopack). Vérifie en arrière-plan et, si une mise à
    /// jour existe, la télécharge et la prépare silencieusement : elle sera appliquée automatiquement au prochain
    /// démarrage de Maxine, sans jamais rien demander à l'utilisateur.
    /// </summary>
    public static void CheckInBackground(MainWindow mw)
    {
        if (chec_done)
            return;
        chec_done = true;
        _ = Task.Run(async () =>
        {
            if (!await gate.WaitAsync(0))
                return; // une vérification manuelle est déjà en cours
            try
            {
                var mgr = new UpdateManager(new GithubSource(RepositoryUrl, null, false));
                if (!mgr.IsInstalled)
                    return; // lancé depuis le build de dev, pas depuis l'installeur
                var update = await mgr.CheckForUpdatesAsync();
                if (update == null)
                    return;
                // téléchargement silencieux, puis mise en attente : Velopack appliquera la mise à jour quand Maxine
                // se fermera (silent: true, restart: false). Au prochain lancement, la nouvelle version est déjà là.
                await mgr.DownloadUpdatesAsync(update);
                mgr.WaitExitThenApplyUpdates(update.TargetFullRelease, silent: true, restart: false);
                stagedVersion = update.TargetFullRelease.Version.ToString();
            }
            catch (Exception e)
            {
                System.Diagnostics.Debug.WriteLine("Mise à jour : vérification impossible — " + e.Message);
            }
            finally { gate.Release(); }
        });
    }

    /// <summary>Vérification lancée à la main depuis les paramètres, avec un retour visible dans tous les cas</summary>
    public static void CheckManually(MainWindow mw)
    {
        // une version est déjà prête (téléchargée pendant cette session) : inutile de relancer
        if (stagedVersion != null)
        {
            mw.Toast($"Version {stagedVersion} déjà prête — elle s'appliquera au prochain démarrage de Maxine.", HUD.HudToast.Kind.Success, 7);
            return;
        }
        // un autre travail de mise à jour tourne déjà (la vérif du démarrage) : on l'annonce gentiment, sans erreur
        if (gate.CurrentCount == 0)
        {
            mw.Toast("Une mise à jour est déjà en cours de téléchargement… elle sera prête dans un instant.", HUD.HudToast.Kind.Info, 6);
            return;
        }
        mw.Toast("Recherche d'une mise à jour…", HUD.HudToast.Kind.Info, 4);
        _ = Task.Run(async () =>
        {
            if (!await gate.WaitAsync(0))
            {
                await mw.Dispatcher.InvokeAsync(() => mw.Toast("Une mise à jour est déjà en cours de téléchargement… elle sera prête dans un instant.", HUD.HudToast.Kind.Info, 6));
                return;
            }
            try
            {
                var mgr = new UpdateManager(new GithubSource(RepositoryUrl, null, false));
                if (!mgr.IsInstalled)
                {
                    await mw.Dispatcher.InvokeAsync(() => mw.Toast(
                        "Les mises à jour automatiques fonctionnent sur la version installée (via l'installeur), pas sur ce build de développement.",
                        HUD.HudToast.Kind.Info, 7));
                    return;
                }
                var update = await mgr.CheckForUpdatesAsync();
                if (update == null)
                {
                    await mw.Dispatcher.InvokeAsync(() => mw.Toast("Maxine est à jour. 🎉", HUD.HudToast.Kind.Success, 5));
                    return;
                }
                var version = update.TargetFullRelease.Version.ToString();
                await mw.Dispatcher.InvokeAsync(() => mw.Toast($"Téléchargement de la version {version}…", HUD.HudToast.Kind.Info, 5));
                await mgr.DownloadUpdatesAsync(update);
                mgr.WaitExitThenApplyUpdates(update.TargetFullRelease, silent: true, restart: false);
                stagedVersion = version;
                await mw.Dispatcher.InvokeAsync(() => mw.Toast(
                    $"Version {version} prête — elle s'appliquera au prochain démarrage de Maxine.",
                    HUD.HudToast.Kind.Success, 7));
            }
            catch (Exception e)
            {
                System.Diagnostics.Debug.WriteLine("Mise à jour manuelle : " + e.Message);
                await mw.Dispatcher.InvokeAsync(() => mw.Toast(
                    "La vérification des mises à jour a échoué. Vérifie ta connexion et réessaie dans un moment.",
                    HUD.HudToast.Kind.Warning, 7));
            }
            finally { gate.Release(); }
        });
    }
}
