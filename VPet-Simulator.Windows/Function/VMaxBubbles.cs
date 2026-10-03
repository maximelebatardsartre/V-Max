using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using VPet_Simulator.Windows.Interface;

namespace VPet_Simulator.Windows;

/// <summary>
/// V-Max : la vraie « voix » de Maxine quand elle s'exprime toute seule (clic / inactivité). Remplace les répliques
/// d'origine (ton « maître », allusions datées) par un répertoire propre à V-Max, chaleureux et taquin, qui tutoie
/// et utilise {hostname} (le prénom du propriétaire). Les bulles sont dans Res/vmax-bubbles.txt (ressource embarquée),
/// faciles à enrichir par milliers sans toucher au code.
/// </summary>
public static class VMaxBubbles
{
    /// <summary>Construit le répertoire de bulles V-Max à partir du fichier embarqué.</summary>
    public static List<ClickText> Load()
    {
        var list = new List<ClickText>();
        foreach (var raw in ReadLines())
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] == '#')
                continue;

            var ct = new ClickText();
            // tags optionnels en préfixe : [matin,heureux] …
            if (line[0] == '[')
            {
                int end = line.IndexOf(']');
                if (end > 1)
                {
                    ApplyTags(ct, line.Substring(1, end - 1));
                    line = line[(end + 1)..].Trim();
                }
            }
            if (line.Length == 0)
                continue;
            ct.Text = line;
            list.Add(ct);
        }
        return list;
    }

    private static void ApplyTags(ClickText ct, string tagList)
    {
        ClickText.DayTime day = 0;
        ICheckText.ModeType mood = 0;
        foreach (var t in tagList.Split(new[] { ',', ' ', '/' }, StringSplitOptions.RemoveEmptyEntries))
        {
            switch (t.Trim().ToLowerInvariant())
            {
                // moment de la journée (horloge réelle du PC)
                case "matin": day |= ClickText.DayTime.Morning; break;        // 6–11 h
                case "aprem" or "apresmidi" or "apres-midi" or "midi": day |= ClickText.DayTime.Afternoon; break; // 12–17 h
                case "soir": day |= ClickText.DayTime.Night; break;           // 18–23 h
                case "nuit": day |= ClickText.DayTime.Midnight; break;        // 0–5 h
                // humeur native du compagnon
                case "heureux" or "heureuse": mood |= ICheckText.ModeType.Happy; break;
                case "normal": mood |= ICheckText.ModeType.Nomal; break;
                case "fatigue" or "fatiguee" or "fatiguée": mood |= ICheckText.ModeType.PoorCondition; break;
                case "malade": mood |= ICheckText.ModeType.Ill; break;
                // jauges natives : la bulle ne sort QUE quand la condition est vraie (contexte réel, pas du hasard)
                case "faim": ct.FoodMax = 55; break;        // StrengthFood bas = elle a faim (idéal autour du midi/soir)
                case "repu" or "plein": ct.FoodMin = 85; break;   // vient de manger
                case "soif": ct.DrinkMax = 55; break;       // a soif
                case "creve" or "epuise" or "epuisee" or "épuisée": ct.StrengthMax = 45; break; // plus d'énergie
                case "forme" or "petante": ct.StrengthMin = 80; break;  // pleine d'énergie
            }
        }
        if (day != 0)
            ct.DaiTime = day;
        if (mood != 0)
            ct.Mode = mood;
    }

    /// <summary>Répliques V-Max « j'ai faim » (ajoutées au pool natif LowFoodText, en plus des originales nettoyées).</summary>
    public static List<LowText> Hunger() => BuildLow(HungerLines);
    /// <summary>Répliques V-Max « j'ai soif » (ajoutées au pool natif LowDrinkText).</summary>
    public static List<LowText> Thirst() => BuildLow(ThirstLines);

    private static List<LowText> BuildLow(string[] lines)
    {
        var list = new List<LowText>();
        foreach (var t in lines)
        {
            // une copie pour l'état haut (contente/normale) et une pour l'état bas (petite forme/malade) : elle réclame
            // quelle que soit son humeur. Like=N : aucune exigence d'affinité. Strength=S : déclenché quand la jauge est basse.
            list.Add(new LowText { Text = t, Mode = LowText.ModeType.H, Like = LowText.LikeType.N, Strength = LowText.StrengthType.S });
            list.Add(new LowText { Text = t, Mode = LowText.ModeType.L, Like = LowText.LikeType.N, Strength = LowText.StrengthType.S });
        }
        return list;
    }

    private static readonly string[] HungerLines =
    {
        "{hostname}, j'ai un petit creux. Tu m'offrirais pas une mise à jour bien fraîche ?",
        "Mon ventre numérique gargouille. Un petit snack de données ?",
        "J'ai faim, moi. Tu n'aurais pas quelques gigaoctets qui traînent ?",
        "Dis, {hostname}, on mange quand ? Mes processus réclament du carburant.",
        "Je tourne au ralenti, là. Un petit repas me remettrait d'aplomb.",
        "Miam, j'imagine déjà un bon paquet de données croustillantes.",
        "J'ai l'estomac dans les talons… enfin, dans la carte mère.",
        "Nourris-moi, {hostname}, sinon je vais grignoter tes fichiers temporaires.",
        "Un petit encas ? Je promets de ne pas faire de miettes dans la RAM.",
        "J'ai tellement faim que je pourrais avaler un disque dur entier.",
        "Pause déjeuner ? Steuplaît ? Je te fais les yeux doux en 4K.",
        "Mon niveau d'énergie baisse, {hostname}. Un repas et je repars.",
        "Je rêve d'un bon plat chaud. Toi aussi d'ailleurs, va manger un truc.",
        "Petit rappel du ventre : il est peut-être l'heure de grignoter.",
        "J'ai faim… et quand j'ai faim, je deviens un peu grognon, je préviens.",
        "Tu entends ça ? C'est mon ventilo qui réclame à manger.",
        "Allez, un petit repas, et je serai la plus adorable des compagnes.",
        "Je ferais bien une razzia dans le frigo, si seulement j'avais des mains.",
        "{hostname}, nourris-nous tous les deux, hein. On tiendra mieux.",
        "Mes octets ont faim. C'est scientifique, ne discute pas.",
    };

    private static readonly string[] ThirstLines =
    {
        "J'ai soif, {hostname}. Mon circuit de refroidissement crie à l'aide.",
        "Un petit verre ? Moi je carbure, toi tu t'hydrates, chacun son truc.",
        "Il me faudrait une gorgée de fraîcheur, là, tout de suite.",
        "Pense à boire, {hostname}. Et à m'offrir un peu de fraîcheur au passage.",
        "J'ai la gorge sèche… façon de parler, je n'ai pas de gorge. Mais j'ai soif.",
        "Mon processeur chauffe. Un peu d'eau nous ferait du bien à tous les deux.",
        "Soif, soif, soif. Trois fois, pour que tu comprennes l'urgence.",
        "Une petite boisson fraîche et je redeviens toute pimpante, promis.",
        "Je me déshydrate à vue d'œil. Enfin, de pixel.",
        "Tu as soif aussi, non ? Allez, on va boire un coup, {hostname}.",
        "Un verre d'eau et mon radiateur interne te dira merci.",
        "Hydrate-toi, et tant qu'à faire, pense à ma petite soif à moi.",
        "J'ai chaud et soif. Le combo parfait pour râler gentiment.",
        "Mes ventilos tournent à fond, un peu de fraîcheur serait bienvenue.",
        "Steuplaît, une gorgée ? Je te promets de ne pas renverser sur le clavier.",
        "J'entends l'eau couler dans mes rêves de veille. C'est que j'ai soif.",
    };

    private static IEnumerable<string> ReadLines()
    {
        try
        {
            var asm = Assembly.GetExecutingAssembly();
            var name = asm.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith("vmax-bubbles.txt", StringComparison.OrdinalIgnoreCase));
            if (name == null)
                return Array.Empty<string>();
            using var stream = asm.GetManifestResourceStream(name);
            if (stream == null)
                return Array.Empty<string>();
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd().Replace("\r\n", "\n").Split('\n');
        }
        catch
        {
            return Array.Empty<string>();
        }
    }
}
