using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using NAudio.CoreAudioApi;

namespace VPet_Simulator.Windows.Assistant;

/// <summary>Réglage fin du volume Windows via NAudio (API Core Audio) : valeur exacte, relatif, sourdine.</summary>
internal static class VolumeControl
{
    private static MMDevice? Device()
    {
        try { return new MMDeviceEnumerator().GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia); }
        catch { return null; }
    }

    /// <summary>Règle le volume maître à une valeur 0..1. Renvoie le pourcentage appliqué, ou -1 si échec.</summary>
    public static int SetScalar(float v)
    {
        var d = Device();
        if (d == null) return -1;
        try { v = Math.Clamp(v, 0, 1); d.AudioEndpointVolume.MasterVolumeLevelScalar = v; if (v > 0) d.AudioEndpointVolume.Mute = false; return (int)Math.Round(v * 100); }
        catch { return -1; }
        finally { try { d.Dispose(); } catch { } }
    }

    /// <summary>Ajoute un delta (en 0..1) au volume courant. Renvoie le nouveau pourcentage, ou -1 si échec.</summary>
    public static int Nudge(float delta)
    {
        var d = Device();
        if (d == null) return -1;
        try
        {
            float nv = Math.Clamp(d.AudioEndpointVolume.MasterVolumeLevelScalar + delta, 0, 1);
            d.AudioEndpointVolume.MasterVolumeLevelScalar = nv;
            if (nv > 0) d.AudioEndpointVolume.Mute = false;
            return (int)Math.Round(nv * 100);
        }
        catch { return -1; }
        finally { try { d.Dispose(); } catch { } }
    }

    public static bool SetMute(bool mute)
    {
        var d = Device();
        if (d == null) return false;
        try { d.AudioEndpointVolume.Mute = mute; return true; }
        catch { return false; }
        finally { try { d.Dispose(); } catch { } }
    }
}

/// <summary>Niveau de retour visuel « mode OS » qui accompagne une commande.</summary>
public enum CommandTone { Info, Success, Warning }

/// <summary>
/// Ce que Maxine fait savoir après une commande : une phrase (bulle + voix) et/ou une petite carte « OS »
/// (texte court + ton). Rien d'« IA » : du texte déterministe, écrit ici.
/// </summary>
public sealed record CommandOutcome(string Speak, string? Card = null, CommandTone Tone = CommandTone.Info);

/// <summary>
/// Pont vers le compagnon, fourni par la couche d'intégration (fenêtre principale). Le routeur reste ainsi
/// découplé du moteur d'animation et peut être testé seul.
/// </summary>
public interface IAssistantHost
{
    /// <summary>Exécute une action sur le thread UI.</summary>
    void OnUi(Action action);
    /// <summary>Fait réagir Maxine (bulle + voix + petite carte) — appelé y compris en différé (minuteurs).</summary>
    void Deliver(CommandOutcome outcome);
    /// <summary>Ouvre un panneau HUD persistant (monitor, launcher, clipboard, processes, timers, notes, windows, screenshots).</summary>
    void OpenPanel(string id);
}

/// <summary>
/// V-Max : assistant DÉTERMINISTE. Transforme une phrase (voix ou texte) en action native par reconnaissance de
/// mots-clés — SANS intelligence artificielle. Chaque commande est une règle explicite : ouvrir une app/un site,
/// minuteur, rappel, volume, verrouiller, capture d'écran, infos PC, heure… Si rien ne correspond, renvoie null
/// (la couche appelante dira gentiment qu'elle n'a pas compris).
/// </summary>
public sealed class CommandRouter
{
    private readonly IAssistantHost host;
    private readonly List<Intent> intents;

    /// <summary>
    /// Une intention = un handler + des « signaux » (mots/expressions). On SCORE la phrase contre chaque intention
    /// (corrélation de mots : les expressions pèsent plus qu'un mot isolé) et on exécute la mieux classée. Bien plus
    /// robuste qu'un simple premier-match : gère l'infinité de formulations sans IA.
    /// </summary>
    private sealed record Intent(Func<string, CommandOutcome?> Run, string[] Strong, string[] Weak);

    private static string[] S(params string[] x) => x;

    public CommandRouter(IAssistantHost host)
    {
        this.host = host;
        intents = new()
        {
            // Aide / « qu'est-ce que tu sais faire »
            new(CmdHelp,
                S("aide", "help", "qu est ce que tu sais faire", "que sais tu faire", "que peux tu faire",
                  "qu est ce que tu peux faire", "tes commandes", "liste des commandes", "liste de tes commandes",
                  "quelles commandes", "a quoi tu sers", "comment tu marches", "comment ca marche", "montre moi ce que tu sais faire"),
                S("commandes", "capacites", "fonctions", "possibilites", "secours")),

            // Annuler minuteur/rappel
            new(CmdStopTimers,
                S("annule le minuteur", "arrete le minuteur", "stop le minuteur", "annule la minuterie", "arrete la minuterie",
                  "annule le rappel", "arrete le rappel", "annule le chrono", "arrete le chrono", "annule tout", "arrete tout"),
                S("annule", "annuler", "arrete", "arreter", "stop", "stoppe")),

            // Heure
            new(CmdTime,
                S("quelle heure", "il est quelle heure", "l heure qu il est", "donne moi l heure", "il est quelle heure la",
                  "tu as l heure", "c est quelle heure", "heure", "horloge"),
                S()),

            // Date / jour
            new(CmdDate,
                S("quel jour", "quelle date", "on est quel jour", "la date du jour", "on est le combien", "quel jour on est",
                  "c est quel jour", "on est quelle date", "date", "calendrier"),
                S("jour", "aujourd hui")),

            // Minuteur / chrono
            new(CmdTimer,
                S("minuteur", "minuterie", "compte a rebours", "chrono", "chronometre", "timer", "reveille moi dans",
                  "previens moi dans", "sonne dans", "mets un minuteur", "lance un minuteur", "demarre un chrono", "decompte"),
                S("minute", "minutes", "secondes")),

            // Rappel
            new(CmdReminder,
                S("rappelle moi", "rappelle-moi", "rappel", "n oublie pas", "pense a me rappeler", "previens moi de",
                  "fais moi penser a", "note que je dois"),
                S("rappeler", "souviens", "noter", "memo")),

            // Volume
            new(CmdVolume,
                S("son", "volume", "audio", "mute", "muet", "sourdine", "le son", "le volume", "monte le son", "baisse le son",
                  "coupe le son", "mets le son", "regle le son", "volume a", "augmente le son", "diminue le son", "monte le volume",
                  "baisse le volume", "coupe le volume", "remets le son", "plus fort", "moins fort", "en sourdine"),
                S("silence", "sonore")),

            // Verrouiller
            new(CmdLock,
                S("verrouille l ecran", "verrouille le pc", "verrouille la session", "bloque l ecran", "ecran de verrouillage",
                  "verrouille mon pc", "mets en veille la session"),
                S("verrouille", "verrouiller", "verrou")),

            // Capture d'écran
            new(CmdScreenshot,
                S("capture", "screenshot", "capture d ecran", "prends une capture", "fais une capture",
                  "prends une capture d ecran", "imprime l ecran", "photo de l ecran", "capture l ecran"),
                S("screen")),

            // Stats / moniteur de perfs
            new(CmdStats,
                S("stats", "statistiques", "performances", "perfs", "moniteur", "cpu", "processeur", "ram", "memoire",
                  "batterie", "temperature", "disque", "stockage", "gpu", "etat du pc", "etat de mon pc", "infos pc",
                  "informations sur mon pc", "moniteur de perfs", "affiche les perfs", "ouvre le moniteur", "comment va le pc",
                  "la temperature", "espace disque", "carte graphique", "montre les perfs", "surveille les perfs"),
                S("performance", "perf", "graphique", "chauffe")),

            // Minuteurs/rappels EN COURS (≠ en créer un)
            new(CmdActiveTimers,
                S("mes minuteurs", "mes rappels", "minuteurs en cours", "rappels en cours", "montre mes minuteurs",
                  "montre mes rappels", "affiche mes minuteurs", "liste des minuteurs", "mes chronos", "minuteurs actifs"),
                S()),

            // Lanceur d'applications
            new(CmdLauncher,
                S("lanceur", "ouvre le lanceur", "mes applications", "mes applis", "mes apps", "liste des applications",
                  "liste de mes applications", "toutes mes applications", "quelles applications", "menu des applications",
                  "lanceur d applications", "lanceur d applis"),
                S()),

            // Presse-papiers
            new(CmdClipboard,
                S("presse papier", "presse papiers", "ce que j ai copie", "mon historique de copie", "historique du presse papiers",
                  "ce que tu as retenu", "ce que tu as garde", "mes copies", "historique de copie", "colle ce que j ai copie"),
                S("copie", "copier")),

            // Processus gourmands
            new(CmdProcesses,
                S("qu est ce qui rame", "qu est ce qui tourne", "ce qui rame", "ca rame", "qu est ce qui consomme",
                  "ce qui consomme", "qui consomme", "processus gourmands", "applications gourmandes", "ce qui tourne",
                  "quoi qui rame", "mon pc rame", "pourquoi ca rame"),
                S("processus")),

            // Fenêtres ouvertes
            new(CmdWindows,
                S("mes fenetres", "fenetres ouvertes", "montre mes fenetres", "liste des fenetres", "change de fenetre",
                  "bascule de fenetre", "affiche mes fenetres", "quelles fenetres", "les fenetres ouvertes", "selecteur de fenetres"),
                S()),

            // Captures récentes (galerie)
            new(CmdShots,
                S("mes captures", "galerie de captures", "captures recentes", "montre mes captures", "voir mes captures",
                  "affiche mes captures", "mes screenshots", "mes photos d ecran", "liste des captures"),
                S()),

            // Carrière / métiers
            new(CmdCareer,
                S("ma carriere", "mes metiers", "ma voie", "arbre des metiers", "ma progression de carriere",
                  "mon metier", "montre ma carriere", "ouvre ma carriere", "mon arbre de metiers", "mes statuts"),
                S("carriere", "metier", "metiers")),

            // Bloc-notes (≠ Bloc-notes Windows : « ouvre le bloc-notes » reste Notepad)
            new(CmdNotes,
                S("prends une note", "prendre une note", "mes notes", "ouvre mes notes", "note a moi meme",
                  "note rapide", "ecris une note", "nouvelle note", "ouvre le bloc notes de v max"),
                S()),

            // Musique
            new(CmdMusic,
                S("musique", "mets de la musique", "joue de la musique", "lance la musique", "mets de la zik",
                  "un peu de musique", "mets la musique"),
                S("zik")),

            // Recherche web
            new(CmdSearch,
                S("cherche", "recherche", "chercher", "cherche sur internet", "recherche sur le web", "cherche moi",
                  "recherche moi", "fais une recherche", "cherche sur google", "recherche google", "google moi"),
                S("google")),

            // Ouvrir un site
            new(CmdOpenSite,
                S("va sur", "rends toi sur", "ouvre le site", "ouvre la page", "connecte toi a", "ouvre le site web",
                  "amene moi sur", "emmene moi sur"),
                S("site", "page", "internet")),

            // Ouvrir une application (catch-all en cas d'égalité : défini en dernier)
            new(CmdOpenApp,
                S("ouvre l application", "lance le logiciel", "demarre le programme", "ouvre le logiciel", "lance l appli",
                  "ouvre moi", "lance moi", "demarre moi"),
                S("ouvre", "ouvrir", "lance", "lancer", "demarre", "demarrer", "execute", "application", "appli", "logiciel", "programme")),
        };
    }

    /// <summary>Traite la phrase ET fait réagir Maxine (voix + bulle + carte). Vrai si une commande a été reconnue.</summary>
    public bool Handle(string input)
    {
        var o = TryHandle(input);
        if (o == null)
            return false;
        host.Deliver(o);
        return true;
    }

    /// <summary>Réaction quand aucune commande n'est reconnue (et que l'IA de secours est coupée) : ouvre la doc des commandes.</summary>
    public void NotUnderstood()
    {
        host.OpenPanel("help");
        host.Deliver(new CommandOutcome(
            "Je n'ai pas de commande pour ça — regarde, voici tout ce que je sais faire.",
            "? Commandes", CommandTone.Info));
    }

    /// <summary>Score minimal pour considérer une intention (≈ un mot décisif). Évite les faux positifs sur un indice isolé.</summary>
    private const int MinScore = 4;

    /// <summary>Tente de traiter la phrase. Renvoie l'outcome immédiat (sans le délivrer), ou null si rien ne correspond.</summary>
    public CommandOutcome? TryHandle(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return null;
        var n = Normalize(input);
        // classe les intentions par score de corrélation décroissant (tri stable : l'ordre de définition départage les égalités)
        var ranked = intents
            .Select(i => (intent: i, score: Score(n, i)))
            .Where(x => x.score >= MinScore)
            .OrderByDescending(x => x.score)
            .ToList();
        foreach (var (intent, _) in ranked)
        {
            CommandOutcome? r;
            try { r = intent.Run(n); }
            catch (Exception e) { r = new CommandOutcome("Aïe, ça a coincé : " + e.Message, "Erreur", CommandTone.Warning); }
            if (r != null)
                return r;
        }
        return null;
    }

    /// <summary>QA/diagnostic : classe les intentions pour une phrase (nom du handler + score), sans exécuter.</summary>
    internal (string name, int score)[] Rank(string input)
    {
        var n = Normalize(input);
        return intents.Select(i => (i.Run.Method.Name, Score(n, i)))
            .Where(x => x.Item2 > 0).OrderByDescending(x => x.Item2).ToArray();
    }

    /// <summary>Score de corrélation : expressions fortes = 6, mot fort = 4, indice faible = 1.</summary>
    private static int Score(string n, Intent i)
    {
        int s = 0;
        foreach (var t in i.Strong) if (Word(n, t)) s += t.Contains(' ') ? 6 : 4;
        foreach (var t in i.Weak) if (Word(n, t)) s += 1;
        return s;
    }

    /// <summary>Présence d'un terme avec frontières de mot (« son » ne matche pas dans « raison »).</summary>
    private static bool Word(string n, string term) =>
        Regex.IsMatch(n, "(?<![a-z0-9])" + Regex.Escape(term) + "(?![a-z0-9])");

    #region Normalisation
    /// <summary>minuscule, sans accents, espaces compactés — pour un matching robuste.</summary>
    public static string Normalize(string s)
    {
        var d = s.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(d.Length);
        foreach (var ch in d)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark)
                continue; // accent
            // lettres/chiffres/% gardés ; apostrophes, tirets et ponctuation → espace (« qu'est-ce » → « qu est ce »)
            sb.Append(char.IsLetterOrDigit(ch) || ch == '%' ? ch : ' ');
        }
        return Regex.Replace(sb.ToString(), @"\s+", " ").Trim();
    }

    private static bool Has(string n, params string[] words) => words.Any(w => n.Contains(w));
    #endregion

    #region Aide
    private CommandOutcome? CmdHelp(string n)
    {
        host.OpenPanel("help");
        return new CommandOutcome(
            "Voilà tout ce que je sais faire — clique un exemple pour l'essayer.",
            "💡 Commandes", CommandTone.Success);
    }
    #endregion

    #region Heure & date
    private CommandOutcome? CmdTime(string n)
    {
        var t = DateTime.Now;
        return new CommandOutcome($"Il est {t:HH}h{t:mm}.", $"{t:HH:mm}");
    }

    private CommandOutcome? CmdDate(string n)
    {
        var t = DateTime.Now;
        var fr = new CultureInfo("fr-FR");
        return new CommandOutcome($"On est le {t.ToString("dddd d MMMM yyyy", fr)}.", t.ToString("ddd d MMM", fr));
    }
    #endregion

    #region Minuteur & rappel
    private CommandOutcome? CmdTimer(string n)
    {
        var (secs, label) = ParseDuration(n);
        if (secs <= 0)
            return new CommandOutcome("Dis-moi une durée, par exemple « minuteur de 10 minutes ».", "Durée ?", CommandTone.Warning);
        StartTimer(label, secs, false, "Ton minuteur de " + label + " est terminé !", "Minuteur fini");
        return new CommandOutcome("C'est parti pour " + label + ".", "Minuteur : " + label, CommandTone.Success);
    }

    private CommandOutcome? CmdReminder(string n)
    {
        var (secs, label) = ParseDuration(n);
        // objet du rappel : après « de » … « dans »
        string what = ExtractReminderSubject(n);
        if (secs <= 0)
        {
            secs = 600; // défaut 10 min si aucune durée
            if (label.Length == 0) label = "10 minutes";
        }
        string msg = string.IsNullOrWhiteSpace(what) ? "Petit rappel, comme demandé !" : "Rappel : " + what + " !";
        StartTimer(string.IsNullOrWhiteSpace(what) ? "Rappel" : what, secs, true, msg, "Rappel");
        return new CommandOutcome("D'accord, je te le rappelle dans " + label + ".",
            "Rappel dans " + label, CommandTone.Success);
    }

    private CommandOutcome? CmdStopTimers(string n)
    {
        int c = AssistantTimers.CancelAll();
        return new CommandOutcome(c == 0 ? "Il n'y avait rien en cours." : "C'est annulé.", "Annulé", CommandTone.Info);
    }

    private void StartTimer(string label, int seconds, bool isReminder, string doneSpeak, string doneCard)
    {
        AssistantTimers.Add(label, seconds, isReminder,
            () => host.OnUi(() => host.Deliver(new CommandOutcome(doneSpeak, doneCard, CommandTone.Success))));
    }

    /// <summary>Extrait une durée en secondes + un libellé lisible (« 10 minutes », « 30 secondes », « 1 heure »).</summary>
    private static (int secs, string label) ParseDuration(string n)
    {
        int total = 0;
        var parts = new List<string>();
        foreach (Match m in Regex.Matches(n, @"(\d+)\s*(h(?:eures?)?|min(?:utes?)?|m|sec(?:ondes?)?|s)\b"))
        {
            int v = int.Parse(m.Groups[1].Value);
            string u = m.Groups[2].Value;
            if (u.StartsWith("h")) { total += v * 3600; parts.Add(v + (v > 1 ? " heures" : " heure")); }
            else if (u.StartsWith("s")) { total += v; parts.Add(v + (v > 1 ? " secondes" : " seconde")); }
            else { total += v * 60; parts.Add(v + (v > 1 ? " minutes" : " minute")); }
        }
        // « minuteur de 10 » sans unité → minutes par défaut
        if (total == 0)
        {
            var m = Regex.Match(n, @"\b(\d+)\b");
            if (m.Success) { int v = int.Parse(m.Groups[1].Value); total = v * 60; parts.Add(v + (v > 1 ? " minutes" : " minute")); }
        }
        return (total, string.Join(" et ", parts));
    }

    private static string ExtractReminderSubject(string n)
    {
        var m = Regex.Match(n, @"(?:rappelle[- ]?moi|rappel)\s+(?:de |d'|que |a )?(.+?)(?:\s+dans\b|\s+a \d|$)");
        return m.Success ? m.Groups[1].Value.Trim() : "";
    }
    #endregion

    #region Volume
    private CommandOutcome? CmdVolume(string n)
    {
        // couper / rétablir
        if (Has(n, "remets le son", "remet le son", "retablis le son", "reactive le son", "demute", "unmute", "son remis", "rallume le son"))
        { if (!VolumeControl.SetMute(false)) return VolumeError(); return new CommandOutcome("Son rétabli.", "🔊 Son"); }
        if (Has(n, "coupe", "mute", "muet", "silence", "plus de son", "aucun son", "en sourdine"))
        { if (!VolumeControl.SetMute(true)) return VolumeError(); return new CommandOutcome("Son coupé.", "🔇 Muet"); }

        bool up = Has(n, "monte", "augmente", "plus fort", "plus haut", "hausse", "augmentele", "pousse le son", "a fond");
        bool down = Has(n, "baisse", "diminue", "reduis", "moins fort", "plus bas", "descends", "baissele");
        var num = Regex.Match(n, @"\b(\d{1,3})\b");
        bool hasNum = num.Success;
        int value = hasNum ? Math.Clamp(int.Parse(num.Groups[1].Value), 0, 100) : 0;
        bool relative = Regex.IsMatch(n, @"\bde\s+\d");              // « baisse de 20 »
        bool setWords = Has(n, "mets", "met", "regle", "regles", "passe", "fixe", "a fond", " a ", " sur ", "%");

        // « à fond » = 100 %
        if (Has(n, "a fond", "au maximum", "au max", "a donf"))
        { int r = VolumeControl.SetScalar(1f); return r < 0 ? VolumeError() : new CommandOutcome("Le son à fond !", "🔊 100%", CommandTone.Success); }

        // valeur absolue : un nombre + mot de réglage, OU un nombre sans direction (« le son à 30 »)
        if (hasNum && (setWords || (!up && !down)) && !relative)
        {
            int r = VolumeControl.SetScalar(value / 100f);
            return r < 0 ? VolumeError() : new CommandOutcome($"Volume réglé à {r} %.", $"🔊 {r}%", CommandTone.Success);
        }
        // relatif
        if (up || down)
        {
            float delta = hasNum && relative ? Math.Clamp(value, 1, 100) / 100f : 0.10f;
            int r = VolumeControl.Nudge(down ? -delta : delta);
            return r < 0 ? VolumeError()
                : new CommandOutcome((down ? "Je baisse" : "Je monte") + $" le son à {r} %.", (down ? "🔉 " : "🔊 ") + r + "%", CommandTone.Success);
        }
        return new CommandOutcome("Tu veux monter, baisser, couper, ou le régler à un pourcentage ?", "Volume ?", CommandTone.Warning);
    }

    private static CommandOutcome VolumeError() => new("Je n'arrive pas à régler le son sur ce PC.", "Son indispo", CommandTone.Warning);
    #endregion

    #region Verrouiller / capture
    private CommandOutcome? CmdLock(string n)
    {
        // réponse AVANT le lock (sinon la bulle ne s'affiche pas)
        host.OnUi(() => { try { LockWorkStation(); } catch { } });
        return new CommandOutcome("Je verrouille, à tout de suite !", "🔒 Verrouillé");
    }

    private CommandOutcome? CmdScreenshot(string n)
    {
        string path = SaveScreenshot();
        return new CommandOutcome("Capture enregistrée dans tes images.", "📸 " + Path.GetFileName(path), CommandTone.Success);
    }

    private static string SaveScreenshot()
    {
        var b = System.Windows.Forms.SystemInformation.VirtualScreen;
        using var bmp = new System.Drawing.Bitmap(b.Width, b.Height);
        using (var g = System.Drawing.Graphics.FromImage(bmp))
            g.CopyFromScreen(b.X, b.Y, 0, 0, b.Size);
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "V-Max");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "capture-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".png");
        bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
        return path;
    }
    #endregion

    #region Stats PC
    private CommandOutcome? CmdStats(string n)
    {
        bool temp = Has(n, "temperature", "temperatures", "chaleur", "ca chauffe", "ca chauffele", "degres", "il fait chaud", "chauffe");
        bool cpu = Has(n, "cpu", "processeur", "charge", "utilisation processeur");
        bool ram = Has(n, "ram", "memoire", "memoire vive");
        bool bat = Has(n, "batterie", "charge batterie", "pourcentage batterie");
        bool disk = Has(n, "disque", "stockage", "espace disque", "disque dur", "ssd", "espace libre");
        bool gpu = Has(n, "gpu", "carte graphique", "graphique", "carte video");
        bool monitor = Has(n, "moniteur", "affiche les perfs", "affiche les performances", "affiche les stats", "ouvre le moniteur",
            "montre les perfs", "montre moi les perfs", "affiche le moniteur", "surveille les perfs", "garde les perfs", "perfs en direct", "moniteur de perfs");
        bool all = monitor || Has(n, "stats", "statistiques", "etat du pc", "etat de mon pc", "comment va le pc", "comment va ton", "comment va mon",
            "infos pc", "info pc", "informations pc", "informations sur mon pc", "performance", "performances", "ton etat", "la machine", "le pc va", "les perfs", "perf");
        if (!(temp || cpu || ram || bat || disk || gpu || all))
            return null;

        // Demande large / « moniteur » → on ouvre le panneau PERSISTANT qui se met à jour en direct.
        if (all)
        {
            host.OpenPanel("monitor");
            return new CommandOutcome("Voilà le moniteur de performances, il reste affiché et se met à jour en direct.", "📊 Moniteur", CommandTone.Success);
        }

        var bits = new List<string>();
        if (all || cpu) bits.Add($"processeur à {CpuUsagePercent()} %");
        if (all || temp)
        {
            var t = HardwareInfo.CpuTempC();
            if (t.HasValue) bits.Add($"température {t} °C");
            else if (temp && !all) bits.Add("je n'arrive pas à lire la température sur ce PC");
        }
        if (all || ram) { var (used, total) = RamGb(); bits.Add($"mémoire {used:0.0} Go sur {total:0.0}"); }
        if (all || disk) { var (used, total) = HardwareInfo.SystemDiskGb(); if (total > 0) bits.Add($"disque {used:0} Go utilisés sur {total:0}"); }
        if (all || gpu) { var g = HardwareInfo.GpuName(); if (g != null) bits.Add($"carte graphique {g}"); }
        if (all || bat)
        {
            var ps = System.Windows.Forms.SystemInformation.PowerStatus;
            if (ps.BatteryChargeStatus == System.Windows.Forms.BatteryChargeStatus.NoSystemBattery)
            { if (bat && !all) bits.Add("pas de batterie sur ce PC"); }
            else
            {
                string plugged = ps.PowerLineStatus == System.Windows.Forms.PowerLineStatus.Online ? " (en charge)" : "";
                bits.Add($"batterie à {(int)Math.Round(ps.BatteryLifePercent * 100)} %{plugged}");
            }
        }
        if (all) bits.Add("allumé depuis " + HardwareInfo.Uptime());
        if (bits.Count == 0)
            return null;
        string speak = (all ? "Voilà l'état de ton PC : " : "Là, tout de suite : ") + string.Join(", ", bits) + ".";
        return new CommandOutcome(speak, "📊 " + string.Join(" · ", bits.Take(4).Select(ShortStat)));
    }

    private static string ShortStat(string b)
    {
        if (b.StartsWith("processeur")) return "CPU " + Regex.Match(b, @"\d+").Value + "%";
        if (b.StartsWith("température")) return Regex.Match(b, @"\d+").Value + "°C";
        if (b.StartsWith("mémoire")) return "RAM " + Regex.Match(b, @"[\d.,]+").Value + "Go";
        if (b.StartsWith("disque")) return "💽 " + Regex.Match(b, @"\d+").Value + "Go";
        if (b.StartsWith("batterie")) return "🔋 " + Regex.Match(b, @"\d+").Value + "%";
        if (b.StartsWith("carte graphique")) return "🎮";
        return b;
    }

    private static (double used, double total) RamGb()
    {
        var m = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        if (!GlobalMemoryStatusEx(ref m)) return (0, 0);
        double total = m.ullTotalPhys / 1073741824.0;
        double used = (m.ullTotalPhys - m.ullAvailPhys) / 1073741824.0;
        return (used, total);
    }

    private static int CpuUsagePercent()
    {
        // delta des temps système sur un court intervalle (pas de dépendance externe)
        if (!GetSystemTimes(out var i1, out var k1, out var u1)) return 0;
        System.Threading.Thread.Sleep(200);
        if (!GetSystemTimes(out var i2, out var k2, out var u2)) return 0;
        ulong idle = Sub(i2, i1), kern = Sub(k2, k1), user = Sub(u2, u1);
        ulong busy = kern + user - idle, totalt = kern + user;
        return totalt == 0 ? 0 : (int)Math.Clamp(100.0 * busy / totalt, 0, 100);
    }
    private static ulong Sub(FILETIME a, FILETIME b) => ((ulong)a.dwHighDateTime << 32 | (uint)a.dwLowDateTime) - ((ulong)b.dwHighDateTime << 32 | (uint)b.dwLowDateTime);
    #endregion

    #region Panneaux utilitaires
    private CommandOutcome? CmdActiveTimers(string n)
    {
        host.OpenPanel("timers");
        return new CommandOutcome("Voilà tes minuteurs et rappels en cours.", "⏱ Minuteurs", CommandTone.Success);
    }

    private CommandOutcome? CmdLauncher(string n)
    {
        host.OpenPanel("launcher");
        return new CommandOutcome("Voilà le lanceur, tape le nom d'une application.", "🚀 Lanceur", CommandTone.Success);
    }

    private CommandOutcome? CmdClipboard(string n)
    {
        host.OpenPanel("clipboard");
        return new CommandOutcome("Voici tout ce que tu as copié récemment.", "📋 Presse-papiers", CommandTone.Success);
    }

    private CommandOutcome? CmdProcesses(string n)
    {
        host.OpenPanel("processes");
        return new CommandOutcome("Je te montre ce qui tourne et ce qui consomme le plus.", "⚙ Activité", CommandTone.Success);
    }

    private CommandOutcome? CmdWindows(string n)
    {
        host.OpenPanel("windows");
        return new CommandOutcome("Voilà tes fenêtres ouvertes, clique pour basculer.", "🪟 Fenêtres", CommandTone.Success);
    }

    private CommandOutcome? CmdShots(string n)
    {
        host.OpenPanel("screenshots");
        return new CommandOutcome("Voici tes captures d'écran récentes.", "🖼 Captures", CommandTone.Success);
    }

    private CommandOutcome? CmdNotes(string n)
    {
        host.OpenPanel("notes");
        return new CommandOutcome("J'ouvre ton bloc-notes.", "📝 Notes", CommandTone.Success);
    }

    private CommandOutcome? CmdCareer(string n)
    {
        host.OpenPanel("career");
        return new CommandOutcome("Voici ma progression de carrière et mes métiers.", "💼 Carrière", CommandTone.Success);
    }
    #endregion

    #region Recherche / sites / apps
    private CommandOutcome? CmdMusic(string n)
    {
        Launch("spotify:");
        return new CommandOutcome("Je lance la musique sur Spotify.", "🎵 Spotify", CommandTone.Success);
    }

    private CommandOutcome? CmdSearch(string n)
    {
        var m = Regex.Match(n, @"\b(?:cherche|recherche|google|trouve(?:-| )moi)\b\s+(.+)");
        if (!m.Success)
            return null;
        string q = m.Groups[1].Value.Trim();
        if (q.Length == 0)
            return new CommandOutcome("Je cherche quoi ?", "Recherche ?", CommandTone.Warning);
        OpenUrl("https://www.google.com/search?q=" + Uri.EscapeDataString(q));
        return new CommandOutcome("Je cherche « " + q + " ».", "🔎 " + q, CommandTone.Success);
    }

    private CommandOutcome? CmdOpenSite(string n)
    {
        // « va sur youtube », « ouvre le site leboncoin », une URL explicite
        var m = Regex.Match(n, @"\b(?:va sur|rends? toi sur|ouvre (?:le )?site)\b\s+(.+)");
        if (!m.Success)
        {
            var url = Regex.Match(n, @"\b((?:https?://)?[a-z0-9-]+\.[a-z]{2,}(?:/\S*)?)\b");
            if (!url.Success || !Has(n, "ouvre", "va")) return null;
            m = url;
        }
        string site = m.Groups[1].Value.Trim().TrimEnd('.', ' ');
        string host2 = SiteToUrl(site);
        OpenUrl(host2);
        return new CommandOutcome("J'ouvre " + site + ".", "🌐 " + site, CommandTone.Success);
    }

    private CommandOutcome? CmdOpenApp(string n)
    {
        var m = Regex.Match(n, @"\b(?:ouvre|ouvrir|ouvre moi|lance|lancer|lance moi|demarre|demarrer|demarre moi|execute|affiche|affiche moi)\b\s+(.+)");
        if (!m.Success)
            return null;
        string target = m.Groups[1].Value.Trim().TrimEnd('.', ' ');
        // retire les articles de tête (après normalisation, « l'appli » est devenu « l appli »)
        target = Regex.Replace(target, @"^(l |la |le |les |mon |ma |mes |un |une |du |de |l application |le logiciel |l appli )", "").Trim();
        if (TryLaunchApp(target, out string shown))
            return new CommandOutcome("J'ouvre " + shown + ".", "▶ " + shown, CommandTone.Success);
        // dernier recours : recherche web du terme
        OpenUrl("https://www.google.com/search?q=" + Uri.EscapeDataString(target));
        return new CommandOutcome("Je ne connais pas cette appli, je te la cherche sur le web.", "🔎 " + target, CommandTone.Warning);
    }

    /// <summary>Applications courantes connues (nom parlé → exécutable/URI). Le reste tente un lancement direct.</summary>
    private static readonly Dictionary<string, string> KnownApps = new(StringComparer.OrdinalIgnoreCase)
    {
        ["spotify"] = "spotify:", ["discord"] = "discord", ["chrome"] = "chrome", ["google chrome"] = "chrome",
        ["firefox"] = "firefox", ["edge"] = "msedge", ["explorateur"] = "explorer", ["explorateur de fichiers"] = "explorer",
        ["fichiers"] = "explorer", ["bloc notes"] = "notepad", ["bloc-notes"] = "notepad", ["notepad"] = "notepad",
        ["calculatrice"] = "calc", ["calcul"] = "calc", ["parametres"] = "ms-settings:", ["reglages"] = "ms-settings:",
        ["terminal"] = "wt", ["invite de commande"] = "cmd", ["cmd"] = "cmd", ["powershell"] = "powershell",
        ["paint"] = "mspaint", ["dessin"] = "mspaint", ["steam"] = "steam://open/main", ["word"] = "winword",
        ["excel"] = "excel", ["outlook"] = "outlook", ["gestionnaire des taches"] = "taskmgr",
        ["telegram"] = "telegram", ["vlc"] = "vlc", ["obs"] = "obs64", ["photos"] = "ms-photos:",
        ["teams"] = "msteams:", ["notion"] = "notion", ["code"] = "code", ["vs code"] = "code", ["vscode"] = "code",
        ["epic"] = "com.epicgames.launcher:", ["epic games"] = "com.epicgames.launcher:", ["riot"] = "riotclientservices",
        ["whatsapp"] = "whatsapp:", ["signal"] = "signal", ["github desktop"] = "github", ["twitch"] = "twitch",
        ["corbeille"] = "shell:RecycleBinFolder", ["telechargements"] = "shell:Downloads", ["bureau"] = "shell:Desktop",
    };

    private static bool TryLaunchApp(string target, out string shown)
    {
        shown = target;
        // 1. alias connus (URI protocolaires : spotify:, ms-settings:, shell:…)
        if (KnownApps.TryGetValue(target, out var exe) && Launch(exe))
        {
            shown = Capitalize(target);
            return true;
        }
        // 2. N'IMPORTE quelle app installée (raccourcis du menu Démarrer), par nom approché
        if (AppIndex.Find(target) is { } hit && AppIndex.Launch(hit.path))
        {
            shown = hit.display;
            return true;
        }
        // 3. tentative directe (ex. « code » → code.exe présent dans le PATH)
        return Launch(target);
    }

    private static bool Launch(string what)
    {
        try
        {
            Process.Start(new ProcessStartInfo(what) { UseShellExecute = true });
            return true;
        }
        catch { return false; }
    }

    private static void OpenUrl(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
    }

    private static string SiteToUrl(string site)
    {
        site = site.Replace(" point ", ".").Replace(" ", "");
        if (site.StartsWith("http")) return site;
        if (!site.Contains('.')) site += ".com";
        return "https://" + site;
    }

    private static string Capitalize(string s) => s.Length == 0 ? s : char.ToUpper(s[0]) + s[1..];
    #endregion

    #region Win32
    [DllImport("user32.dll")] private static extern bool LockWorkStation();
    [DllImport("kernel32.dll")] private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);
    [DllImport("kernel32.dll")] private static extern bool GetSystemTimes(out FILETIME idle, out FILETIME kernel, out FILETIME user);

    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength; public uint dwMemoryLoad;
        public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct FILETIME { public int dwLowDateTime; public int dwHighDateTime; }
    #endregion
}
