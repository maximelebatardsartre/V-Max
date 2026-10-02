using LinePutScript;
using System;
using System.Threading;
using System.Threading.Tasks;
using VPet_Simulator.Windows.Agent.Providers;
using VPet_Simulator.Windows.HUD;

namespace VPet_Simulator.Windows.Voice;

/// <summary>
/// V-Max voix : parler au compagnon et l'entendre répondre.
/// - Touche maintenue (par défaut « ² ») : on parle tant qu'elle est enfoncée ;
/// - « Hey Max » (optionnel, local) : l'écoute démarre seule et s'arrête après un silence ;
/// - la phrase est transcrite (Groq, Gemini ou Windows hors ligne), envoyée à l'agent, et la réponse est lue
///   avec une voix française de Windows. Appuyer pendant qu'il parle lui coupe la parole.
/// Réglages dans Setting.lps, section vmax_voice.
/// </summary>
public sealed class VoiceService : IDisposable
{
    private enum State { Idle, Listening, Transcribing }

    private readonly MainWindow mw;
    private readonly TextToSpeech tts = new();          // voix Windows (repli)
    public readonly PiperTts Piper = new();             // voix premium neurale FR (par défaut une fois téléchargée)
    private readonly MicRecorder mic = new();
    private readonly PushToTalkKey ptt;
    private readonly WakeWord wake = new();
    private ListeningOverlay? overlay;
    private State state;
    private bool speakNext;
    private CancellationTokenSource? cts;

    public VoiceService(MainWindow mw)
    {
        this.mw = mw;
        ptt = new PushToTalkKey(PttKey);
        ptt.Pressed += () => mw.Dispatcher.BeginInvoke(() => Begin(fromWake: false));
        ptt.Released += () => mw.Dispatcher.BeginInvoke(() => _ = EndAsync());
        mic.Level += l => mw.Dispatcher.BeginInvoke(() => overlay?.SetLevel(l));
        mic.AutoStopped += () => mw.Dispatcher.BeginInvoke(() => _ = EndAsync());
        wake.Heard += () => mw.Dispatcher.BeginInvoke(() => Begin(fromWake: true));
        wake.Failed += msg => mw.Dispatcher.BeginInvoke(() =>
        {
            WakeEnabled = false;
            mw.Toast("« Hey Max » est désactivé : " + msg, HudToast.Kind.Warning, 8);
        });
        void OnSpeaking(bool s) => mw.Dispatcher.BeginInvoke(() =>
        {
            mw.Main.PlayingVoice = s;
            if (!s)
                ResumeWake();
        });
        tts.SpeakingChanged += OnSpeaking;
        Piper.SpeakingChanged += OnSpeaking;
        Apply();
    }

    #region Réglages
    private ILine Cfg => mw.Set["vmax_voice"];

    /// <summary>
    /// V-Max : verrou de consentement. Tant qu'il est faux (défaut), aucun micro ni crochet clavier : ni touche
    /// maintenue, ni « Hey Max ». L'utilisateur l'active dans l'accueil ou dans Paramètres › Voix.
    /// </summary>
    public bool Enabled { get => Cfg.GetBool("enabled"); set { Cfg.SetBool("enabled", value); Apply(); } }

    public bool PushToTalk { get => !Cfg.GetBool("ptt_off"); set { Cfg.SetBool("ptt_off", !value); Apply(); } }
    public int PttKey { get => Cfg.GetInt("ptt_key", 0xDE); set { Cfg.SetInt("ptt_key", value); ptt.VirtualKey = value; } }
    /// <summary>« voice » : répond à voix haute quand on lui parle ; « always » ; « never »</summary>
    public string ReplyMode { get => Cfg.GetString("reply", "voice") ?? "voice"; set => Cfg.SetString("reply", value); }
    public string VoiceName { get => Cfg.GetString("voice_name", "Julie") ?? "Julie"; set => Cfg.SetString("voice_name", value); }
    public int Rate { get => Cfg.GetInt("rate", 0); set => Cfg.SetInt("rate", value); }
    /// <summary>
    /// La voix ne quitte jamais le PC (reconnaissance de Windows uniquement). ACTIVÉ PAR DÉFAUT : au premier
    /// lancement, aucun audio n'est envoyé dans le cloud. On stocke l'inverse (« cloud_ok ») pour que l'absence de
    /// réglage = hors-ligne. L'utilisateur peut autoriser une transcription cloud (meilleure qualité) via Paramètres.
    /// </summary>
    public bool OfflineOnly { get => !Cfg.GetBool("cloud_ok"); set => Cfg.SetBool("cloud_ok", !value); }
    public bool WakeEnabled { get => Cfg.GetBool("wake"); set { Cfg.SetBool("wake", value); Apply(); } }
    public double WakeThreshold { get => Cfg.GetFloat("wake_threshold", 0.7); set { Cfg.SetFloat("wake_threshold", value); wake.Threshold = value; } }

    /// <summary>
    /// Voix premium neurale (Piper, « siwis » FR) : ACTIVÉE PAR DÉFAUT. Téléchargée une fois (~85 Mo) à la première
    /// activation de la voix, puis utilisée à la place de la voix Windows (bien plus naturelle). On stocke l'inverse
    /// pour que l'absence de réglage = premium. La désactiver repasse sur les voix Windows (SAPI).
    /// </summary>
    public bool PremiumVoice
    {
        get => !Cfg.GetBool("premium_off");
        set { Cfg.SetBool("premium_off", !value); if (value && Enabled) _ = Piper.EnsureAsync(); }
    }

    public System.Collections.Generic.List<string> Voices() => tts.Voices();

    /// <summary>Active ou coupe la touche et le mot d'activation selon les réglages</summary>
    public void Apply()
    {
        if (!Enabled)
        {// consentement non donné : rien n'écoute (ni touche, ni micro, ni mot d'activation)
            ptt.Disable();
            wake.Pause();
            AbortCapture(); // si on décoche « Voix » en pleine capture, on coupe le micro et la transcription sur-le-champ
            return;
        }
        // voix activée : on prépare la voix premium en arrière-plan (téléchargement unique), sans bloquer.
        if (PremiumVoice && !Piper.Ready)
            _ = Piper.EnsureAsync();
        if (ReplyMode == "always")
            HookAgent();
        if (PushToTalk) ptt.Enable(); else ptt.Disable();
        wake.Threshold = WakeThreshold;
        if (WakeEnabled) wake.Start(); else wake.Pause();
    }
    #endregion

    /// <summary>Fait parler le compagnon (bouton « Écouter un exemple », réponses). Voix premium si prête, sinon Windows.</summary>
    public void Say(string text)
    {
        if (PremiumVoice && Piper.Ready)
            Piper.Speak(text, Rate);
        else
            tts.Speak(text, VoiceName, Rate);
    }

    /// <summary>QA : transcription hors ligne forcée</summary>
    internal bool QaOffline { get; set; }

    /// <summary>QA : synthèse vers un fichier plutôt que vers les haut-parleurs</summary>
    internal string? QaOutputFile { get; set; }

    private bool agentHooked;

    /// <summary>L'agent est créé après la voix : on s'abonne à ses réponses au premier usage</summary>
    private void HookAgent()
    {
        if (agentHooked || mw.AgentPlugin == null)
            return;
        agentHooked = true;
        mw.AgentPlugin.Orchestrator.AssistantFinished += text => mw.Dispatcher.BeginInvoke(() => OnAnswer(text));
    }

    /// <summary>
    /// Coupe immédiatement toute capture/transcription en cours (consentement retiré, ou arrêt forcé). L'audio déjà
    /// capté est jeté sans être transcrit ni envoyé ; une transcription cloud éventuellement en vol est annulée.
    /// </summary>
    private void AbortCapture()
    {
        try { if (state == State.Listening) mic.Stop(); } catch { }
        try { cts?.Cancel(); } catch { }
        try { tts.Stop(); } catch { }
        try { Piper.Stop(); } catch { }
        state = State.Idle;
        try { overlay?.HideAnimated(); } catch { }
    }

    private void Begin(bool fromWake)
    {
        // couper la parole : on parle par-dessus sa réponse
        tts.Stop();
        Piper.Stop();
        if (state != State.Idle)
            return;
        if (!MicRecorder.Available)
        {
            mw.Toast("Aucun micro n'est branché.", HudToast.Kind.Warning);
            return;
        }
        wake.Pause();
        try
        {
            mic.Start(stopOnSilence: fromWake);
        }
        catch (Exception e)
        {
            mw.Toast("Le micro est inaccessible : " + e.Message + " (Paramètres Windows › Confidentialité › Microphone).", HudToast.Kind.Warning, 8);
            ResumeWake();
            return;
        }
        state = State.Listening;
        overlay ??= new ListeningOverlay(mw);
        overlay.ShowListening();
    }

    private async Task EndAsync()
    {
        if (state != State.Listening)
            return;
        var wav = mic.Stop();
        if (wav == null)
        {
            state = State.Idle;
            overlay?.HideAnimated();
            ResumeWake();
            return;
        }
        await HandleAudioAsync(wav);
    }

    /// <summary>Transcrit puis envoie à l'agent (utilisé aussi par les tests avec un fichier WAV)</summary>
    internal async Task HandleAudioAsync(byte[] wav)
    {
        state = State.Transcribing;
        overlay ??= new ListeningOverlay(mw);
        overlay.ShowThinking("Je réfléchis…");
        cts?.Cancel();
        cts = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        SpeechToText.Result result;
        try
        {
            result = await SpeechToText.TranscribeAsync(wav, OfflineOnly || QaOffline, cts.Token);
        }
        catch (Exception e)
        {
            state = State.Idle;
            overlay.HideAnimated();
            mw.Toast("Je n'ai pas pu comprendre : " + e.Message, HudToast.Kind.Warning, 6);
            ResumeWake();
            return;
        }
        state = State.Idle;
        overlay.HideAnimated();
        var text = result.Text.Trim();
        LastTranscript = text;
        if (text.Length == 0)
        {
            mw.Toast("Je n'ai rien entendu. Maintiens la touche pendant que tu parles.", HudToast.Kind.Info);
            ResumeWake();
            return;
        }
        var agent = mw.AgentPlugin?.Orchestrator;
        if (agent == null || !ProviderRouter.HasAnyConfigured())
        {
            mw.Toast("J'ai entendu « " + text + " », mais aucune IA n'est connectée : ouvre la discussion pour en brancher une gratuite.", HudToast.Kind.Info, 8);
            mw.Hud?.OpenChat();
            ResumeWake();
            return;
        }
        if (!OfflineOnly && !Cfg.GetBool("privacy_noted") && result.Engine != "Windows (hors ligne)")
        {
            Cfg.SetBool("privacy_noted", true);
            mw.Toast($"Ta voix a été transcrite par {result.Engine}. Pour qu'elle ne quitte jamais le PC : Paramètres › Voix › hors ligne.", HudToast.Kind.Info, 9);
        }
        HookAgent();
        speakNext = ReplyMode != "never";
        _ = Task.Run(() => agent.RespondAsync(text));
    }

    /// <summary>Dernière phrase comprise (QA)</summary>
    internal string LastTranscript { get; private set; } = "";

    private void OnAnswer(string text)
    {
        bool speak = ReplyMode == "always" || speakNext && ReplyMode == "voice";
        speakNext = false;
        if (!speak || string.IsNullOrWhiteSpace(text))
        {
            ResumeWake();
            return;
        }
        if (QaOutputFile != null)
        {
            if (!(PremiumVoice && Piper.Ready && Piper.SpeakToFile(text, Rate, QaOutputFile)))
                tts.SpeakToFile(text, VoiceName, Rate, QaOutputFile);
            return;
        }
        Say(text);
    }

    private void ResumeWake()
    {
        if (WakeEnabled && state == State.Idle && !tts.IsSpeaking && !Piper.IsSpeaking)
            wake.Start();
    }

    public void Dispose()
    {
        ptt.Dispose();
        wake.Dispose();
        mic.Dispose();
        tts.Dispose();
        Piper.Dispose();
    }
}
