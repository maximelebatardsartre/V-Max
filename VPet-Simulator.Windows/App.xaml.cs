using LinePutScript.Localization.WPF;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using VPet_Simulator.Windows.Interface;
using VPet_Simulator.Core;

namespace VPet_Simulator.Windows
{
    /// <summary>
    /// App.xaml 的交互逻辑
    /// </summary>
    public partial class App : Application
    {
        public App() : base()
        {
            // V-Max : amorçage de la mise à jour automatique. DOIT rester la première instruction : Velopack
            // intercepte ici les étapes d'installation et de mise à jour (et peut fermer le processus).
            Velopack.VelopackApp.Build().Run();
            Environment.CurrentDirectory =
                Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
#if !DEBUG
            base.DispatcherUnhandledException += (s, e) => { e.Handled = true; UnhandledException(e.Exception, false); };
            AppDomain.CurrentDomain.UnhandledException += (s, e) => { UnhandledException((e.ExceptionObject as Exception)!, true); };
#endif
            //AppDomain.CurrentDomain.AssemblyResolve += CurrentDomain_AssemblyResolve;
        }
        public static string[] Args { get; set; } = [];
        /// <summary>
        /// 多存档系统名称
        /// </summary>
        public static List<string> MutiSaves { get; set; } = new List<string>();

        public static List<MainWindow> MainWindows { get; set; } = new List<MainWindow>();

        public static HashSet<string> MODType { get; set; } = new HashSet<string>();

        /// <summary>V-Max : marqueur d'instance unique (conservé pour toute la vie du processus)</summary>
        private static Mutex? singleInstance;

        protected override void OnStartup(StartupEventArgs e)
        {
            Args = e.Args;

            // V-Max : une seule instance de Maxine à la fois. Deux processus écriraient la même sauvegarde et se
            // disputeraient le téléchargement des assets au premier lancement (corruption / conflit). Le redémarrage
            // interne (Restart) relance un processus marqué « vmax-restart » : celui-ci attend que l'ancien libère le
            // verrou plutôt que de ressortir. Un simple double-clic en trop ressort silencieusement.
            if (!EnsureSingleInstance())
                return;

            // V-Max : toutes les boîtes de dialogue (application, mods, plugins) passent par la fenêtre V-Max
            VDialog.Handler = (owner, text, caption, buttons, icon) =>
                Dispatcher.CheckAccess()
                    ? HUD.HudDialog.Show(owner, text, caption, buttons, icon)
                    : Dispatcher.Invoke(() => HUD.HudDialog.Show(owner, text, caption, buttons, icon));

            // V-Max : données utilisateur dans %APPDATA%\V-Max (copie unique depuis le dossier d'installation)
            UserDataMigration.Run();

            //旧版本多开bug修复
            if (File.Exists(ExtensionValue.DataDirectory + @"\Setting-.lps"))
                File.Delete(ExtensionValue.DataDirectory + @"\Setting-.lps");

            foreach (var mss in new DirectoryInfo(ExtensionValue.DataDirectory).GetFiles("Setting*.lps"))
            {
                var n = mss.Name.Substring(7).Trim('-');
                MutiSaves.Add(n.Substring(0, n.Length - 4));
            }

            if (MutiSaves.Count == 0)
            {
                MutiSaves.Add("");
            }
            if (!Args.Any(x => x.Contains("prefix")))
            {
                var file = new DirectoryInfo(ExtensionValue.DataDirectory).GetFiles("startup_*").FirstOrDefault();
                if (file != null)
                {
                    var su = file.Name.Substring(8);
                    var al = Args.ToList();
                    al.Add($"prefix#{su}:|");
                    Args = al.ToArray();
                }
            }

        }

        /// <summary>
        /// V-Max : garantit l'unicité du processus. Renvoie false si une autre instance tourne déjà (l'appelant
        /// doit alors sortir sans rien faire). Ne bloque jamais le lancement si le verrou ne peut pas être créé.
        /// </summary>
        private bool EnsureSingleInstance()
        {
            bool isRestart = Args.Any(a => a.Contains("vmax-restart"));
            try
            {
                singleInstance = new Mutex(false, @"VMax-Maxine-SingleInstance");
                bool acquired;
                try
                {
                    // un redémarrage interne attend que l'ancien processus libère le verrou ; sinon, pas d'attente
                    acquired = singleInstance.WaitOne(isRestart ? TimeSpan.FromSeconds(15) : TimeSpan.Zero);
                }
                catch (AbandonedMutexException) { acquired = true; } // l'instance précédente est partie : on reprend
                if (!acquired)
                {
                    singleInstance = null;
                    Shutdown();
                    return false;
                }
            }
            catch { /* impossible de créer le verrou (droits, etc.) : on ne bloque pas le lancement */ }
            return true;
        }

        HashSet<string> ErrorReport = new HashSet<string>();
        private void UnhandledException(Exception e, bool isFatality)
        {
            var expt = e.ToString();
            if (ErrorReport.Contains(expt))
                return;//防止重复报错
            ErrorReport.Add(expt);
            if (expt.Contains("MainWindow.Close") || expt.Contains("System.Windows.Window.DragMove") ||
                expt.Contains("winConsole"))
                return;
            else if ((!isFatality && MainWindow != null && ((MainWindow)MainWindow).GameSavesData?.GameSave != null &&
                (((MainWindow)MainWindow).GameSavesData.GameSave.Money > int.MaxValue || ((MainWindow)MainWindow).GameSavesData.GameSave.Exp > int.MaxValue)
                ) && ((expt.ToLowerInvariant().Contains("value") && expt.ToLowerInvariant().Contains("nan")) ||
                expt.Contains("System.OverflowException") || expt.Contains("System.DivideByZeroException")))
            {
                VDialog.Show("Des données du jeu ont débordé : ta sauvegarde pourrait être affectée.\n"
                    + "Évite les mods qui trichent trop sur les montants (argent, expérience).");
                return;
            }
            else if (expt.Contains("System.IO.FileNotFoundException") && expt.Contains("cache"))
            {
                VDialog.Show("Le cache des animations a été supprimé par un autre logiciel.\n"
                    + "Relance Maxine pour le régénérer.");
                return;
            }
            else if (expt.Contains("0x80070008"))
            {
                VDialog.Show("Mémoire insuffisante. Baisse la résolution de rendu dans les paramètres "
                    + "pour réduire l'utilisation mémoire.");
                return;
            }
            else if (expt.Contains("UnauthorizedAccessException"))
            {
                VDialog.Show("Maxine n'a pas pu écrire ses fichiers (sauvegarde et paramètres).\n"
                    + "Vérifie qu'aucun autre logiciel ne les bloque et que le dossier est accessible en écriture.");
                return;
            }
            else if (expt.Contains("VPet.Plugin"))
            {
                var exptin = expt.Split('\n').First(x => x.Contains("VPet.Plugin"));
                exptin = exptin.Substring(exptin.IndexOf("VPet.Plugin") + 12).Split('.')[0];
                VDialog.Show("Une erreur est survenue, probablement causée par le mod « " + exptin + " ».\n"
                    + "Si tu peux, envoie une capture de l'erreur et ce que tu faisais juste avant à l'auteur du mod.\n\n"
                     + expt, "Erreur — mod « " + exptin + " »");
                return;
            }

            foreach (var modname in MODType)
            {
                if (expt.Contains(modname))
                {
                    var exptin = modname.Split('.').Last();
                    VDialog.Show("Une erreur est survenue, probablement causée par le mod « " + modname + " ».\n"
                        + "Si tu peux, envoie une capture de l'erreur et ce que tu faisais juste avant à l'auteur du mod.\n\n"
                         + expt, "Erreur — mod « " + exptin + " »");
                    return;
                }
            }


            string errstr = "Une erreur est survenue dans Maxine"
                + (string.IsNullOrWhiteSpace(CoreMOD.NowLoading) ? "" : $" (pendant le chargement du mod « {CoreMOD.NowLoading} »)")
                + ".\n\n" + expt;
            if (isFatality || MainWindow == null)
            {
                VDialog.Show(errstr, "Erreur critique");
                return;
            }
            else
            {
                ((MainWindow)MainWindow).ShowReport(errstr);
                return;
            }
        }
    }
}
