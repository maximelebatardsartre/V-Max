using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace VPet_Simulator.Windows;

/// <summary>
/// V-Max : mods que V-Max refuse de charger (contenu à caractère sexuel mettant en scène le compagnon).
/// Liste établie par tools/mods/ingest.py (inventaire du 2026-10-02) ; un mod y figure par son identifiant
/// Workshop, qu'il soit installé depuis Steam ou copié à la main dans « mod ».
/// </summary>
public static class ModBlocklist
{
    private static readonly HashSet<string> WorkshopIds =
    [
        "3027004255", "3027542580", "3030945675", "3031981095", "3035399894", "3042568517",
        "3044723043", "3045450089", "3046644833", "3065265367", "3290665653", "3176916830",
    ];

    private static readonly Regex IdInName = new(@"(?<!\d)(\d{9,11})(?!\d)", RegexOptions.Compiled);

    /// <summary>Vrai si le dossier du mod (nom ou « itemid » de sa fiche) correspond à un mod bloqué</summary>
    public static bool IsBlocked(DirectoryInfo modDir, string? itemId)
    {
        if (itemId != null && WorkshopIds.Contains(itemId.Trim()))
            return true;
        foreach (Match m in IdInName.Matches(modDir.Name))
            if (WorkshopIds.Contains(m.Groups[1].Value))
                return true;
        return false;
    }

    /// <summary>Lecture rapide de la ligne « itemid » de info.lps (sans charger le mod)</summary>
    public static bool IsBlocked(DirectoryInfo modDir)
    {
        string? itemId = null;
        try
        {
            foreach (var line in File.ReadLines(Path.Combine(modDir.FullName, "info.lps")))
            {
                if (line.StartsWith("itemid#"))
                {
                    itemId = line.Substring(7).Split(':')[0];
                    break;
                }
            }
        }
        catch (IOException) { }
        return IsBlocked(modDir, itemId);
    }
}
