using System;
using VPet_Simulator.Windows.Assistant;

namespace VPet_Simulator.Windows.HUD;

/// <summary>
/// V-Max : Maxine RÉAGIT à ce que tu fais sur ton PC, sans qu'on lui demande. Elle « absorbe » ce que tu copies et
/// réagit quand tu lances un jeu ou une grosse appli. Tout passe par une petite bulle animée. Débrayable
/// (vmax_assistant/reactions_off) et fortement throttlé pour ne jamais être envahissante.
/// </summary>
public sealed class ActivityReactions
{
    private readonly MainWindow mw;
    private readonly ActivityWatcher watcher = new();
    private DateTime lastReaction = DateTime.MinValue;
    private static readonly Random rng = new();

    public ActivityReactions(MainWindow mw)
    {
        this.mw = mw;
        ClipboardHistory.Captured += OnCopied;
        watcher.Detected += OnLaunched;
        try { watcher.Start(); } catch { }
        mw.Closed += (_, _) => { ClipboardHistory.Captured -= OnCopied; try { watcher.Stop(); } catch { } };
    }

    /// <summary>Réactions activées (défaut oui). Se règle dans Paramètres › IA.</summary>
    private bool Enabled => !mw.Set["vmax_assistant"].GetBool("reactions_off");

    /// <summary>Vrai (et consomme le crédit) si on a le droit de réagir maintenant : activé + assez de temps écoulé.</summary>
    private bool Allow(TimeSpan min)
    {
        if (!Enabled || DateTime.UtcNow - lastReaction < min)
            return false;
        lastReaction = DateTime.UtcNow;
        return true;
    }

    private void OnCopied(string text)
    {
        if (!Allow(TimeSpan.FromSeconds(20)))
            return;
        React(Pick(ClipLines));
    }

    private void OnLaunched(ActivityWatcher.Launch l)
    {
        if (!Allow(TimeSpan.FromSeconds(12)))
            return;
        React(l.IsGame ? Pick(GameLines).Replace("{jeu}", l.Display) : Pick(AppLines).Replace("{app}", l.Display));
    }

    private void React(string line)
    {
        // "shining" = animation ravie/pétillante pendant qu'elle parle ; repli sur Say simple si le graphe manque
        try { mw.Main.Say(line, "shining", true); }
        catch { try { mw.Main.Say(line); } catch { } }
    }

    private static string Pick(string[] a) => a[rng.Next(a.Length)];

    private static readonly string[] ClipLines =
    {
        "Hop, copié ! J'avale ça 🧠",
        "Ctrl+C repéré — je le garde au chaud 📋",
        "Mmh, je mémorise ce que tu viens de copier.",
        "Copié, c'est dans mon cache. Dis « ce que j'ai copié » pour le ressortir.",
        "Je te garde ça sous le coude, au cas où.",
        "Absorbé ! J'ai une sacrée mémoire, moi.",
    };

    private static readonly string[] GameLines =
    {
        "Oh, {jeu} ! Je regarde par-dessus ton épaule 👀",
        "Session {jeu} qui commence ? Montre-moi ce que tu vaux !",
        "{jeu} ! Préviens-moi si tu rages, je suis là 😌",
        "Allez, {jeu} ! Je parie que tu win cette fois.",
        "{jeu} lancé — je surveille ton CPU pendant que tu joues.",
    };

    private static readonly string[] AppLines =
    {
        "Tiens, {app} qui s'ouvre.",
        "{app} lancé — c'est noté.",
        "Ah, {app} ! On passe aux choses sérieuses ?",
    };
}
