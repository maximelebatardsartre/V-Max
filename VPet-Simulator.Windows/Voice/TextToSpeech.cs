using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace VPet_Simulator.Windows.Voice;

/// <summary>
/// V-Max voix : synthèse vocale avec les voix de Windows (SAPI), y compris les voix « OneCore » de meilleure qualité
/// (Julie, Paul, Hortense en français) sans téléchargement. Les objets SAPI vivent sur un fil STA dédié ;
/// la lecture est interruptible (l'utilisateur peut couper la parole au compagnon).
/// </summary>
public sealed class TextToSpeech : IDisposable
{
    private const string OneCore = @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Speech_OneCore\Voices";
    private const string Desktop = @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Speech\Voices";

    private readonly BlockingCollection<Action> work = new();
    private readonly Thread thread;
    private dynamic? voice;
    private volatile bool stopRequested;
    private volatile bool speaking;

    /// <summary>Début / fin de lecture (pour l'animation « parle » et la bulle)</summary>
    public event Action<bool>? SpeakingChanged;

    public TextToSpeech()
    {
        thread = new Thread(Loop) { IsBackground = true, Name = "V-Max voix" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
    }

    public bool IsSpeaking => speaking;

    private void Loop()
    {
        foreach (var job in work.GetConsumingEnumerable())
        {
            try { job(); }
            catch { }
        }
    }

    private T Run<T>(Func<T> f)
    {
        T result = default!;
        using var done = new ManualResetEventSlim();
        work.Add(() =>
        {
            try { result = f(); }
            finally { done.Set(); }
        });
        done.Wait(TimeSpan.FromSeconds(10));
        return result;
    }

    private dynamic Voice()
    {
        if (voice == null)
        {
            var t = Type.GetTypeFromProgID("SAPI.SpVoice") ?? throw new InvalidOperationException("La synthèse vocale de Windows n'est pas disponible.");
            voice = Activator.CreateInstance(t)!;
        }
        return voice!;
    }

    /// <summary>Voix installées (OneCore d'abord), françaises en tête</summary>
    public List<string> Voices() => Run(() =>
    {
        var names = new List<string>();
        foreach (var cat in new[] { OneCore, Desktop })
            foreach (var tok in Tokens(cat))
            {
                string d = tok.GetDescription(0);
                if (!names.Contains(d))
                    names.Add(d);
            }
        return names.OrderBy(n => n.Contains("French") || n.Contains("Français") ? 0 : 1).ThenBy(n => n).ToList();
    }) ?? new List<string>(); // Run peut renvoyer null si l'init SAPI dépasse le délai : jamais null pour l'UI

    private static IEnumerable<dynamic> Tokens(string category)
    {
        var list = new List<dynamic>();
        try
        {
            var t = Type.GetTypeFromProgID("SAPI.SpObjectTokenCategory");
            if (t == null)
                return list;
            dynamic cat = Activator.CreateInstance(t)!;
            cat.SetId(category, false);
            dynamic tokens = cat.EnumerateTokens();
            for (int i = 0; i < tokens.Count; i++)
                list.Add(tokens.Item(i));
        }
        catch { }
        return list;
    }

    /// <summary>
    /// Lit un texte (remplace une lecture en cours). <paramref name="voiceName"/> : nom ou partie du nom (« Julie »).
    /// <paramref name="rate"/> : -10 (lent) à 10 (rapide).
    /// </summary>
    public void Speak(string text, string? voiceName, int rate)
    {
        var clean = Clean(text);
        if (clean.Length == 0)
            return;
        Stop();
        work.Add(() =>
        {
            stopRequested = false;
            var v = Voice();
            SelectVoice(v, voiceName);
            v.Rate = Math.Clamp(rate, -10, 10);
            speaking = true;
            SpeakingChanged?.Invoke(true);
            try
            {
                v.Speak(clean, 1 /* SVSFlagsAsync */);
                // attente active courte : permet l'interruption
                while (!v.WaitUntilDone(100))
                    if (stopRequested)
                    {
                        v.Speak("", 1 | 2 /* async + purge */);
                        break;
                    }
            }
            finally
            {
                speaking = false;
                SpeakingChanged?.Invoke(false);
            }
        });
    }

    /// <summary>Synthèse vers un fichier WAV (tests : rien n'est joué sur les haut-parleurs)</summary>
    public void SpeakToFile(string text, string? voiceName, int rate, string path)
    {
        var clean = Clean(text);
        work.Add(() =>
        {
            var v = Voice();
            SelectVoice(v, voiceName);
            v.Rate = Math.Clamp(rate, -10, 10);
            var t = Type.GetTypeFromProgID("SAPI.SpFileStream")!;
            dynamic fs = Activator.CreateInstance(t)!;
            fs.Open(path, 3 /* SSFMCreateForWrite */, false);
            var previous = v.AudioOutputStream;
            v.AudioOutputStream = fs;
            try { v.Speak(clean, 0); }
            finally
            {
                fs.Close();
                v.AudioOutputStream = previous;
            }
        });
    }

    /// <summary>Coupe la lecture en cours</summary>
    public void Stop()
    {
        if (speaking)
            stopRequested = true;
    }

    private static void SelectVoice(dynamic v, string? name)
    {
        IEnumerable<dynamic> all = Tokens(OneCore).Concat(Tokens(Desktop));
        dynamic? pick = null;
        foreach (var t in all)
        {
            string d = t.GetDescription(0);
            if (!string.IsNullOrWhiteSpace(name) && d.Contains(name, StringComparison.OrdinalIgnoreCase)) { pick = t; break; }
            if (pick == null && (d.Contains("French") || d.Contains("Français"))) pick = t;
        }
        if (pick != null)
            v.Voice = pick;
    }

    /// <summary>
    /// Texte prêt à être lu : sans Markdown, sans emoji, liens et blocs de code remplacés par une mention courte
    /// </summary>
    public static string Clean(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "";
        var s = Regex.Replace(text, "```[\\s\\S]*?```", " (voir le code dans la discussion) ");
        s = Regex.Replace(s, @"\[([^\]]+)\]\([^)]+\)", "$1");                 // [texte](lien)
        s = Regex.Replace(s, @"https?://\S+", "le lien");
        s = Regex.Replace(s, @"[*_`#>|~]+", " ");
        var sb = new StringBuilder(s.Length);
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (char.IsSurrogate(c))
                continue;                                                     // emojis (hors plan de base)
            var cat = char.GetUnicodeCategory(c);
            if (cat is System.Globalization.UnicodeCategory.OtherSymbol or System.Globalization.UnicodeCategory.PrivateUse)
                continue;
            sb.Append(c);
        }
        return Regex.Replace(sb.ToString(), @"\s+", " ").Trim();
    }

    public void Dispose()
    {
        Stop();
        work.CompleteAdding();
    }
}
