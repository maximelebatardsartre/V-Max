using System;
using System.Diagnostics;
using System.IO;
using VPet_Simulator.Windows.Interface;

namespace VPet_Simulator.Windows;

/// <summary>
/// V-Max : migration unique des données utilisateur depuis le dossier d'installation (comportement de VPet)
/// vers <see cref="ExtensionValue.DataDirectory"/> (%APPDATA%\V-Max).
/// Les fichiers sont <b>copiés</b> (jamais supprimés) ; un marqueur évite de recommencer.
/// </summary>
public static class UserDataMigration
{
    private const string MarkerName = ".migrated-from-install-dir";

    public static void Run()
    {
        string from = ExtensionValue.BaseDirectory;
        string to = ExtensionValue.DataDirectory;
        if (string.Equals(Path.GetFullPath(from).TrimEnd('\\'), Path.GetFullPath(to).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
            return; // mode portable
        string marker = Path.Combine(to, MarkerName);
        if (File.Exists(marker))
            return;
        try
        {
            // Si l'utilisateur a déjà des données au nouvel emplacement, on ne touche à rien
            bool alreadyHasData = Directory.GetFiles(to, "Setting*.lps").Length > 0 || Directory.Exists(Path.Combine(to, "Saves"));
            if (!alreadyHasData)
            {
                foreach (var pattern in new[] { "Setting*.lps", "Setting*.bkp", "Save.lps", "Save.bkp", "Logs*.txt", "startup_*" })
                    foreach (var file in Directory.GetFiles(from, pattern))
                        File.Copy(file, Path.Combine(to, Path.GetFileName(file)), false);
                foreach (var dir in new[] { "Saves", "Saves_BKP", "BackUP", "ModData" })
                    CopyDirectory(Path.Combine(from, dir), Path.Combine(to, dir));
            }
            File.WriteAllText(marker, DateTime.Now.ToString("O"));
        }
        catch (Exception e)
        {
            Trace.TraceWarning("Migration des données utilisateur : " + e.Message);
        }
    }

    private static void CopyDirectory(string source, string target)
    {
        if (!Directory.Exists(source))
            return;
        Directory.CreateDirectory(target);
        foreach (var file in Directory.GetFiles(source))
        {
            var dest = Path.Combine(target, Path.GetFileName(file));
            if (!File.Exists(dest))
                File.Copy(file, dest);
        }
        foreach (var dir in Directory.GetDirectories(source))
            CopyDirectory(dir, Path.Combine(target, Path.GetFileName(dir)));
    }
}
