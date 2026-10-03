using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using KokoroSharp;
using KokoroSharp.Core;
using KokoroSharp.Utilities;
using NAudio.Wave;

namespace VPet_Simulator.Windows.Voice;

/// <summary>
/// V-Max : voix française HAUT DE GAMME via Kokoro (modèle neuronal 82M, hors-ligne, naturel nettement supérieur à
/// Piper). Voix « ff_siwis » (française). Ce n'est PAS une IA conversationnelle : un synthétiseur vocal, comme Piper.
/// Synthèse en WAV puis lecture interruptible via NAudio (même logique que Piper). Modèle téléchargé une fois (~320 Mo).
/// </summary>
public sealed class KokoroTts : IDisposable
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private KokoroWavSynthesizer? synth;
    private KokoroVoice? voice;

    private readonly object playLock = new();
    private WaveOut? output;
    private volatile bool stopRequested;
    private bool speaking;

    public bool Ready => synth != null && voice != null;
    public event Action<bool>? SpeakingChanged;
    public event Action<string>? Progress;

    /// <summary>Le modèle Kokoro est-il déjà téléchargé ? (pour un préchargement au démarrage).</summary>
    public static bool Downloaded
    {
        get { try { return KokoroLoader.IsDownloaded(KModel.float16); } catch { return false; } }
    }

    /// <summary>Charge (et télécharge au besoin) le modèle Kokoro + la voix française. Null si prêt, sinon message d'erreur.</summary>
    public async Task<string?> EnsureAsync(CancellationToken ct = default)
    {
        if (Ready)
            return null;
        await gate.WaitAsync(ct);
        try
        {
            if (Ready)
                return null;
            Progress?.Invoke("Téléchargement de la voix haute qualité (~160 Mo, une seule fois)…");
            // télécharge le modèle fp16 au besoin (la lib gère espeak-ng, bundlé) puis instancie le synthétiseur
            var modelPath = await KokoroLoader.DownloadModelAsync(KModel.float16,
                p => Progress?.Invoke($"Téléchargement de la voix… {p * 100:0} %"));
            synth ??= new KokoroWavSynthesizer(modelPath);
            if (KokoroVoiceManager.Voices.Count == 0)
                KokoroVoiceManager.LoadVoicesFromPath();   // voix françaises bundlées par le NuGet
            voice ??= KokoroVoiceManager.GetVoice("ff_siwis");
            Progress?.Invoke("");
            return Ready ? null : "La voix Kokoro n'a pas pu se charger.";
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception e)
        {
            return "Voix haute qualité indisponible : " + e.Message;
        }
        finally { gate.Release(); }
    }

    /// <summary>Lit un texte. onFailure est appelé si la synthèse échoue (pour retomber sur Piper/Windows).</summary>
    public void Speak(string text, Action? onFailure = null)
    {
        var clean = TextToSpeech.Clean(text);
        if (clean.Length == 0)
            return;
        if (!Ready)
        {
            onFailure?.Invoke();
            return;
        }
        Stop();
        _ = Task.Run(() =>
        {
            try
            {
                var wav = synth!.Synthesize(clean, voice!);
                if (wav == null || wav.Length < 64)
                {
                    onFailure?.Invoke();
                    return;
                }
                Play(wav);
            }
            catch { onFailure?.Invoke(); }
        });
    }

    /// <summary>QA : synthétise en WAV sans jouer (vérifie que la voix FR produit bien de l'audio).</summary>
    internal async Task<byte[]?> QaSynthesize(string text)
    {
        if (!Ready)
            return null;
        return await synth!.SynthesizeAsync(TextToSpeech.Clean(text), voice!);
    }

    public void Stop()
    {
        stopRequested = true;
        lock (playLock)
        {
            try { output?.Stop(); } catch { }
        }
    }

    private void Play(byte[] wav)
    {
        WaveOut dev;
        WaveFileReader reader;
        lock (playLock)
        {
            stopRequested = false;
            reader = new WaveFileReader(new MemoryStream(wav));
            dev = new WaveOut();
            try { dev.Init(reader); }
            catch { try { dev.Dispose(); } catch { } try { reader.Dispose(); } catch { } throw; }
            output = dev;
            speaking = true;
        }
        SpeakingChanged?.Invoke(true);
        try
        {
            dev.Play();
            while (dev.PlaybackState == PlaybackState.Playing)
            {
                if (stopRequested) { try { dev.Stop(); } catch { } break; }
                Thread.Sleep(40);
            }
        }
        finally
        {
            speaking = false;
            SpeakingChanged?.Invoke(false);
            lock (playLock)
            {
                try { dev.Dispose(); } catch { }
                try { reader.Dispose(); } catch { }
                if (output == dev)
                    output = null;
            }
        }
    }

    public void Dispose()
    {
        try { Stop(); } catch { }
        try { synth?.Dispose(); } catch { }
    }
}
