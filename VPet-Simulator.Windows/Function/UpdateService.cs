using System;
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
            }
            catch (Exception e)
            {
                System.Diagnostics.Debug.WriteLine("Mise à jour : vérification impossible — " + e.Message);
            }
        });
    }

    /// <summary>Vérification lancée à la main depuis les paramètres, avec un retour visible dans tous les cas</summary>
    public static void CheckManually(MainWindow mw)
    {
        mw.Toast("Recherche d'une mise à jour…", HUD.HudToast.Kind.Info, 4);
        _ = Task.Run(async () =>
        {
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
                await mw.Dispatcher.InvokeAsync(() => mw.Toast(
                    $"Version {version} prête — elle s'appliquera au prochain démarrage de Maxine.",
                    HUD.HudToast.Kind.Success, 7));
            }
            catch (Exception e)
            {
                await mw.Dispatcher.InvokeAsync(() => mw.Toast("Impossible de vérifier les mises à jour : " + e.Message, HUD.HudToast.Kind.Warning, 7));
            }
        });
    }
}
