using System;
using System.Speech.Recognition;

namespace VPet_Simulator.Windows.Voice;

/// <summary>
/// V-Max voix : mot d'activation « Hey Max », reconnu localement par Windows (aucun son n'est envoyé sur internet).
/// Petite grammaire fermée : seules quelques formules sont reconnues, avec un seuil de confiance réglable.
/// </summary>
public sealed class WakeWord : IDisposable
{
    public static readonly string[] Phrases = ["hey max", "hé max", "eh max", "dis max", "ok max", "salut max"];

    private SpeechRecognitionEngine? engine;
    private bool listening;

    /// <summary>Seuil de confiance (0,5 = sensible, 0,85 = strict)</summary>
    public double Threshold { get; set; } = 0.7;
    public event Action? Heard;
    /// <summary>Erreur (pas de micro, langue absente…) : le mot d'activation est désactivé</summary>
    public event Action<string>? Failed;

    public bool Running => listening;

    public void Start()
    {
        if (listening)
            return;
        try
        {
            if (engine == null)
            {
                var culture = SpeechToText.OfflineCulture() ?? throw new InvalidOperationException("Aucune reconnaissance vocale installée sur ce PC.");
                engine = new SpeechRecognitionEngine(culture);
                var choices = new Choices(Phrases);
                engine.LoadGrammar(new Grammar(new GrammarBuilder(choices) { Culture = culture }) { Name = "vmax-wake" });
                engine.SetInputToDefaultAudioDevice();
                engine.SpeechRecognized += (_, e) =>
                {
                    if (e.Result.Confidence >= Threshold)
                        Heard?.Invoke();
                };
            }
            engine.RecognizeAsync(RecognizeMode.Multiple);
            listening = true;
        }
        catch (Exception e)
        {
            listening = false;
            Failed?.Invoke(e.Message);
        }
    }

    /// <summary>Pause (pendant l'enregistrement d'une question ou la réponse parlée)</summary>
    public void Pause()
    {
        if (!listening || engine == null)
            return;
        try { engine.RecognizeAsyncCancel(); } catch { }
        listening = false;
    }

    public void Dispose()
    {
        Pause();
        engine?.Dispose();
        engine = null;
    }
}
