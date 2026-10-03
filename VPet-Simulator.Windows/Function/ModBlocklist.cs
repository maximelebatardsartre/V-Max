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

    // Mêmes motifs que tools/mods/ingest.py (import manuel dans le Studio)
    /// <summary>Titre ou description d'un mod à caractère sexuel</summary>
    public static readonly Regex TitlePattern = new(@"r-?18|18\+|nsfw|porn|hentai|lewd|色情|裸|自慰|女上位|奈子|看光光|诱惑|誘惑|打屁股|涩涩|瑟瑟|成人", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    /// <summary>Noms d'animations ou répliques à caractère sexuel : le mod entier est refusé</summary>
    public static readonly Regex ContentPattern = new(@"\bsex\b|肉棒|好深|要坏掉|要壞掉|高潮|做爱|做愛|内射|內射|小穴|乳头|乳頭|脱衣|脫衣|nude|naked", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    /// <summary>Réplique ambiguë dans un mod par ailleurs anodin : seule la ligne est écartée</summary>
    public static readonly Regex LinePattern = new(@"舔|色眯眯|变态|變態|❤", RegexOptions.Compiled);

    public static bool IsBlockedId(string id) => WorkshopIds.Contains(id.Trim());

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
        catch { }
        return IsBlocked(modDir, itemId);
    }

    /// <summary>
    /// Contrôle de CONTENU : refuse un mod dont la fiche info.lps (titre/description) est à caractère sexuel, même
    /// si son identifiant n'est pas dans la liste connue. Complète IsBlocked pour les mods copiés à la main sous un
    /// nom non numérique. Lecture seule de info.lps (léger) ; en cas de doute on NE bloque pas (évite les faux
    /// positifs au démarrage). Le veto « jamais chargé » tient ainsi aussi hors des 12 IDs connus et de l'import Studio.
    /// </summary>
    public static bool IsSexualContent(DirectoryInfo modDir)
    {
        try
        {
            var info = Path.Combine(modDir.FullName, "info.lps");
            if (!File.Exists(info))
                return false;
            var text = File.ReadAllText(info);
            return TitlePattern.IsMatch(text) || ContentPattern.IsMatch(text);
        }
        catch { return false; }
    }
}
