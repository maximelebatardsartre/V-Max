using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using NAudio.Wave;

namespace VPet_Simulator.Windows.Voice;

/// <summary>
/// V-Max voix premium : synthèse vocale neurale open-source via Piper (https://github.com/rhasspy/piper). Voix
/// française naturelle « fr_FR-siwis-medium », bien au-dessus des voix Windows de base. Tout tourne en LOCAL et
/// hors-ligne : le moteur (~22 Mo) et la voix (~63 Mo) sont téléchargés une seule fois à la première activation de la
/// voix, puis mis en cache. Lecture interruptible (le compagnon peut être coupé). Repli sur SAPI si Piper n'est pas prêt.
/// </summary>
public sealed class PiperTts : IDisposable
{
    private const string EngineZipUrl = "https://github.com/rhasspy/piper/releases/download/2023.11.14-2/piper_windows_amd64.zip";
    private const string HfBase = "https://huggingface.co/rhasspy/piper-voices/resolve/main/fr/fr_FR/";

    /// <summary>Une voix Piper : nom affiché, fichier de base, et numéro de locuteur pour les modèles multi-voix.</summary>
    public sealed record PiperVoice(string Id, string Label, string File, string Dir, int? Speaker)
    {
        public string OnnxUrl => HfBase + Dir + "/" + File + ".onnx?download=true";
        public string JsonUrl => HfBase + Dir + "/" + File + ".onnx.json?download=true";
    }

    /// <summary>Catalogue des voix françaises proposées (toutes neurales, hors-ligne). La 1re est la voix par défaut.</summary>
    public static readonly PiperVoice[] Catalog =
    [
        new("jessica", "Douce", "fr_FR-upmc-medium", "upmc/medium", 0),
        new("siwis", "Claire", "fr_FR-siwis-medium", "siwis/medium", null),
    ];

    /// <summary>Voix sélectionnée (changer de voix télécharge son modèle si besoin au prochain usage).</summary>
    public PiperVoice Voice { get; set; } = Catalog[0];

    private static string Root => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "V-Max", "tts");
    private static string EnginePath => Path.Combine(Root, "piper", "piper.exe");
    private string VoicePath => Path.Combine(Root, Voice.File + ".onnx");
    private string VoiceJsonPath => Path.Combine(Root, Voice.File + ".onnx.json");

    private static readonly HttpClient Http = new() { Timeout = Timeout.InfiniteTimeSpan };
    private static readonly TimeSpan IdleTimeout = TimeSpan.FromSeconds(90);

    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly object playLock = new();
    private WaveOutEvent? output;
    private Process? proc;
    private volatile bool stopRequested;
    private volatile bool speaking;

    static PiperTts() => Http.DefaultRequestHeaders.UserAgent.TryParseAdd("V-Max");

    /// <summary>Début / fin de lecture (animation « parle » + bulle)</summary>
    public event Action<bool>? SpeakingChanged;

    public bool Ready => File.Exists(EnginePath) && File.Exists(VoicePath) && File.Exists(VoiceJsonPath);
    public bool IsSpeaking => speaking;

    /// <summary>Progression du téléchargement (texte, 0..1 ou -1 si indéterminé)</summary>
    public event Action<string, double>? Progress;

    /// <summary>Télécharge le moteur + la voix si besoin. Renvoie null si OK, sinon un message.</summary>
    public async Task<string?> EnsureAsync(CancellationToken cancel = default)
    {
        if (Ready)
            return null;
        await gate.WaitAsync(cancel);
        try
        {
            if (Ready)
                return null;
            Directory.CreateDirectory(Root);

            if (!File.Exists(EnginePath))
            {
                Progress?.Invoke("Téléchargement de la voix premium…", -1);
                var zip = Path.Combine(Root, "piper.zip");
                var err = await DownloadAsync(EngineZipUrl, zip, cancel);
                if (err != null)
                    return err;
                try
                {
                    var dest = Path.Combine(Root, "piper");
                    if (Directory.Exists(dest))
                        Directory.Delete(dest, true);
                    ZipFile.ExtractToDirectory(zip, Root, overwriteFiles: true); // le zip contient un dossier « piper/ »
                    TryDelete(zip);
                }
                catch (Exception e)
                {
                    TryDelete(zip);
                    return "Installation de la voix impossible : " + e.Message;
                }
                if (!File.Exists(EnginePath))
                    return "Le moteur de voix est incomplet (piper.exe absent).";
            }

            if (!File.Exists(VoiceJsonPath))
            {
                var err = await DownloadAsync(Voice.JsonUrl, VoiceJsonPath, cancel);
                if (err != null)
                    return err;
            }
            if (!File.Exists(VoicePath))
            {
                Progress?.Invoke($"Téléchargement de la voix « {Voice.Label} »…", -1);
                var err = await DownloadAsync(Voice.OnnxUrl, VoicePath, cancel);
                if (err != null)
                    return err;
            }
            Progress?.Invoke("Voix premium prête.", 1);
            return Ready ? null : "La voix premium n'a pas pu être installée. Réessaie.";
        }
        catch (OperationCanceledException)
        {
            return "Téléchargement de la voix annulé.";
        }
        catch (Exception e)
        {
            return "Voix premium indisponible : " + e.Message;
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>Lit un texte (remplace une lecture en cours). rate : -10 (lent) à 10 (rapide).</summary>
    public void Speak(string text, int rate)
    {
        var clean = TextToSpeech.Clean(text);
        if (clean.Length == 0 || !Ready)
            return;
        Stop();
        _ = Task.Run(() =>
        {
            try
            {
                var wav = Synthesize(clean, rate);
                if (wav == null)
                    return;
                try { Play(wav); }
                finally { TryDelete(wav); }
            }
            catch { }
        });
    }

    /// <summary>Synthèse vers un fichier WAV (QA : rien n'est joué)</summary>
    public bool SpeakToFile(string text, int rate, string path)
    {
        var clean = TextToSpeech.Clean(text);
        if (clean.Length == 0 || !Ready)
            return false;
        try
        {
            var wav = Synthesize(clean, rate, path);
            return wav != null;
        }
        catch { return false; }
    }

    /// <summary>Coupe la lecture en cours</summary>
    public void Stop()
    {
        stopRequested = true;
        lock (playLock)
        {
            try { output?.Stop(); } catch { }
            try { if (proc is { HasExited: false }) proc.Kill(true); } catch { }
        }
    }

    private string? Synthesize(string text, int rate, string? outPath = null)
    {
        var wav = outPath ?? Path.Combine(Path.GetTempPath(), "vmax-piper-" + Guid.NewGuid().ToString("N") + ".wav");
        double lengthScale = Math.Clamp(1.0 - rate * 0.04, 0.6, 1.5); // rate +10 => plus rapide, -10 => plus lent
        var psi = new ProcessStartInfo
        {
            FileName = EnginePath,
            WorkingDirectory = Path.GetDirectoryName(EnginePath)!,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var a in new[] { "--model", VoicePath, "--output_file", wav, "--length_scale", lengthScale.ToString(System.Globalization.CultureInfo.InvariantCulture) })
            psi.ArgumentList.Add(a);
        if (Voice.Speaker is int spk) // modèle multi-voix : on choisit le locuteur
        {
            psi.ArgumentList.Add("--speaker");
            psi.ArgumentList.Add(spk.ToString());
        }
        Process p;
        lock (playLock)
        {
            stopRequested = false;
            p = Process.Start(psi) ?? throw new InvalidOperationException("Piper n'a pas démarré.");
            proc = p;
        }
        // on draine les sorties (pipes pleins = blocage) et on envoie le texte
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();
        p.StandardInput.WriteLine(text);
        p.StandardInput.Close();
        if (!p.WaitForExit(20000))
        {
            try { p.Kill(true); } catch { }
            return null;
        }
        lock (playLock) { if (proc == p) proc = null; }
        return File.Exists(wav) && new FileInfo(wav).Length > 44 ? wav : null;
    }

    private void Play(string wav)
    {
        WaveOutEvent dev;
        WaveFileReader reader;
        lock (playLock)
        {
            if (stopRequested)
                return;
            reader = new WaveFileReader(wav);
            dev = new WaveOutEvent();
            dev.Init(reader);
            output = dev;
            speaking = true;
        }
        SpeakingChanged?.Invoke(true);
        try
        {
            dev.Play();
            while (dev.PlaybackState == PlaybackState.Playing)
            {
                if (stopRequested)
                {
                    try { dev.Stop(); } catch { }
                    break;
                }
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

    private async Task<string?> DownloadAsync(string url, string dest, CancellationToken cancel)
    {
        var tmp = dest + ".part";
        try
        {
            using var idle = CancellationTokenSource.CreateLinkedTokenSource(cancel);
            idle.CancelAfter(IdleTimeout);
            using var resp = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, idle.Token);
            resp.EnsureSuccessStatusCode();
            long? total = resp.Content.Headers.ContentLength;
            await using var src = await resp.Content.ReadAsStreamAsync(idle.Token);
            long read = 0;
            await using (var dst = File.Create(tmp))
            {
                var buffer = new byte[1 << 20];
                int n;
                while (true)
                {
                    idle.CancelAfter(IdleTimeout);
                    try { n = await src.ReadAsync(buffer, idle.Token); }
                    catch (OperationCanceledException) when (!cancel.IsCancellationRequested)
                    {
                        TryDelete(tmp);
                        return "Connexion perdue pendant le téléchargement de la voix. Vérifie ta connexion puis réessaie.";
                    }
                    if (n <= 0)
                        break;
                    await dst.WriteAsync(buffer.AsMemory(0, n), cancel);
                    read += n;
                    if (total is > 0)
                        Progress?.Invoke($"Téléchargement de la voix premium… {read * 100 / total.Value} %", read / (double)total.Value);
                }
            }
            if (total is > 0 && read < total.Value)
            {
                TryDelete(tmp);
                return "Téléchargement de la voix incomplet. Réessaie.";
            }
            if (File.Exists(dest))
                File.Delete(dest);
            File.Move(tmp, dest);
            return null;
        }
        catch (OperationCanceledException)
        {
            TryDelete(tmp);
            throw;
        }
        catch (Exception e)
        {
            TryDelete(tmp);
            return "Téléchargement de la voix impossible : " + e.Message;
        }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    public void Dispose()
    {
        Stop();
    }
}
