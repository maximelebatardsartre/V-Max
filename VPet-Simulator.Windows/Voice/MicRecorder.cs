using NAudio.Utils;
using NAudio.Wave;
using System;
using System.IO;

namespace VPet_Simulator.Windows.Voice;

/// <summary>
/// V-Max voix : enregistrement du micro (16 kHz, 16 bits, mono — le format attendu par la reconnaissance vocale).
/// Publie le niveau sonore pour l'indicateur d'écoute ; peut s'arrêter seul après un silence (mode « Hey Max »).
/// </summary>
public sealed class MicRecorder : IDisposable
{
    public static readonly WaveFormat Format = new(16000, 16, 1);
    private const double SpeechLevel = 0.06;   // niveau au-dessus duquel on considère qu'on parle

    private WaveInEvent? input;
    private MemoryStream? pcm;
    private DateTime started, lastVoice;
    private bool heardVoice;
    private bool autoStop;

    /// <summary>Niveau sonore lissé, de 0 à 1 (environ 20 fois par seconde)</summary>
    public event Action<double>? Level;
    /// <summary>Arrêt automatique : silence après la parole, ou rien entendu, ou durée maximale</summary>
    public event Action? AutoStopped;

    public bool Recording => input != null;
    public TimeSpan Duration => Recording ? DateTime.Now - started : TimeSpan.Zero;

    /// <param name="stopOnSilence">s'arrêter seul 1,2 s après la fin de la parole (et après 6 s sans parole)</param>
    public void Start(bool stopOnSilence)
    {
        if (input != null)
            return;
        autoStop = stopOnSilence;
        heardVoice = false;
        pcm = new MemoryStream();
        started = lastVoice = DateTime.Now;
        input = new WaveInEvent { WaveFormat = Format, BufferMilliseconds = 50 };
        input.DataAvailable += OnData;
        input.StartRecording();
    }

    private double smooth;

    private void OnData(object? sender, WaveInEventArgs e)
    {
        pcm?.Write(e.Buffer, 0, e.BytesRecorded);
        double sum = 0;
        int n = e.BytesRecorded / 2;
        for (int i = 0; i < e.BytesRecorded - 1; i += 2)
        {
            short sample = (short)(e.Buffer[i] | e.Buffer[i + 1] << 8);
            double v = sample / 32768.0;
            sum += v * v;
        }
        double rms = n > 0 ? Math.Sqrt(sum / n) : 0;
        double level = Math.Min(1, rms * 4);
        smooth = smooth * 0.6 + level * 0.4;
        Level?.Invoke(smooth);
        var now = DateTime.Now;
        if (level >= SpeechLevel)
        {
            heardVoice = true;
            lastVoice = now;
        }
        bool tooLong = now - started > TimeSpan.FromSeconds(30);
        bool silenceAfterSpeech = autoStop && heardVoice && now - lastVoice > TimeSpan.FromMilliseconds(1200);
        bool nothing = autoStop && !heardVoice && now - started > TimeSpan.FromSeconds(6);
        if (tooLong || silenceAfterSpeech || nothing)
            AutoStopped?.Invoke();
    }

    /// <summary>Arrête et renvoie l'enregistrement au format WAV (null si rien d'exploitable)</summary>
    public byte[]? Stop()
    {
        var i = input;
        var data = pcm;
        input = null;
        pcm = null;
        if (i == null || data == null)
            return null;
        try { i.StopRecording(); } catch { }
        i.DataAvailable -= OnData;
        i.Dispose();
        Level?.Invoke(0);
        if (data.Length < Format.AverageBytesPerSecond / 4 || !heardVoice)
            return null; // moins d'un quart de seconde, ou silence
        return ToWav(data.ToArray());
    }

    public static byte[] ToWav(byte[] pcm16k)
    {
        using var ms = new MemoryStream();
        using (var w = new WaveFileWriter(new IgnoreDisposeStream(ms), Format))
            w.Write(pcm16k, 0, pcm16k.Length);
        return ms.ToArray();
    }

    /// <summary>Un micro est-il branché ?</summary>
    public static bool Available => WaveInEvent.DeviceCount > 0;

    public void Dispose() => Stop();
}
