using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using VPet_Simulator.Windows.Interface;

namespace VPet_Simulator.Windows;

/// <summary>
/// V-Max : chronométrage du démarrage. Chaque étape affichée sur l'écran d'accueil est datée depuis le lancement
/// du processus ; quand le compagnon apparaît, le détail est écrit dans %APPDATA%\V-Max\logs\demarrage-temps.log
/// (dernier lancement) et une ligne de synthèse est ajoutée à demarrage-historique.log.
/// </summary>
public static class StartupClock
{
    private static readonly DateTime ProcessStart = SafeStart();
    private static readonly List<(double ms, string step)> marks = [];
    private static bool done;

    private static DateTime SafeStart()
    {
        try { return Process.GetCurrentProcess().StartTime; }
        catch { return DateTime.Now; }
    }

    public static double ElapsedMs => (DateTime.Now - ProcessStart).TotalMilliseconds;

    public static void Mark(string step)
    {
        if (done)
            return;
        lock (marks)
            marks.Add((ElapsedMs, step));
    }

    /// <summary>Ajoute une ligne au relevé du dernier démarrage (après l'apparition du compagnon)</summary>
    public static void Note(string text)
    {
        try
        {
            var line = $"{ElapsedMs,8:0} ms  {text}{Environment.NewLine}";
            File.AppendAllText(Path.Combine(ExtensionValue.DataDirectory, "logs", "demarrage-temps.log"), line);
            File.AppendAllText(Path.Combine(Path.GetTempPath(), "vmax-startup.txt"), line);
        }
        catch { }
    }

    /// <summary>Le compagnon est visible : écrit le relevé (une seule fois par lancement)</summary>
    public static void Finish(int graphCount, int spriteSheetsBuilt)
    {
        if (done)
            return;
        Mark("Compagnon visible");
        done = true;
        try
        {
            var dir = Path.Combine(ExtensionValue.DataDirectory, "logs");
            Directory.CreateDirectory(dir);
            var sb = new StringBuilder();
            sb.AppendLine($"Démarrage du {DateTime.Now:yyyy-MM-dd HH:mm:ss} : {marks[^1].ms:0} ms, {graphCount} animations, {spriteSheetsBuilt} planches générées");
            double prev = 0;
            lock (marks)
                foreach (var (ms, step) in marks)
                {
                    sb.AppendLine($"{ms,8:0} ms  (+{ms - prev,6:0})  {step}");
                    prev = ms;
                }
            File.WriteAllText(Path.Combine(dir, "demarrage-temps.log"), sb.ToString());
            File.AppendAllText(Path.Combine(dir, "demarrage-historique.log"),
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}\t{marks[^1].ms:0} ms\t{graphCount} animations\t{spriteSheetsBuilt} planches{Environment.NewLine}");
            File.WriteAllText(Path.Combine(Path.GetTempPath(), "vmax-startup.txt"), sb.ToString());
        }
        catch { }
    }
}
