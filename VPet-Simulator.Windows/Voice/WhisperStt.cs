using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Whisper.net;

namespace VPet_Simulator.Windows.Voice;

/// <summary>
/// V-Max : transcription vocale HORS-LIGNE de qualité via Whisper (modèle « small », multilingue). Remplace l'ancienne
/// reconnaissance de Windows (System.Speech), très médiocre en français. Ce n'est PAS une IA conversationnelle :
/// juste un moteur de reconnaissance de la parole, comme Piper l'est pour la voix. Modèle téléchargé une seule fois
/// (~470 Mo) dans %LOCALAPPDATA%\V-Max\stt, puis tout tourne en local — la voix ne quitte jamais le PC.
/// </summary>
public sealed class WhisperStt : IDisposable
{
    private static readonly string Root = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "V-Max", "stt");
    private static string ModelPath => Path.Combine(Root, "ggml-small.bin");
    // modèle Whisper « small » multilingue (officiel whisper.cpp)
    private const string ModelUrl = "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-small.bin";

    private readonly SemaphoreSlim gate = new(1, 1);
    private WhisperFactory? factory;

    /// <summary>Le modèle est présent sur le disque (taille plausible).</summary>
    public static bool ModelPresent => File.Exists(ModelPath) && new FileInfo(ModelPath).Length > 100_000_000;

    /// <summary>Le moteur est chargé et prêt à transcrire.</summary>
    public bool Ready => factory != null;

    public event Action<string>? Progress;

    /// <summary>
    /// Télécharge le modèle si besoin et charge le moteur. Renvoie null si tout est prêt, sinon un message d'erreur.
    /// </summary>
    public async Task<string?> EnsureAsync(CancellationToken ct = default)
    {
        if (factory != null)
            return null;
        await gate.WaitAsync(ct);
        try
        {
            if (factory != null)
                return null;
            Directory.CreateDirectory(Root);
            if (!ModelPresent)
            {
                Progress?.Invoke("Téléchargement du moteur de transcription (~470 Mo, une seule fois)…");
                var tmp = ModelPath + ".part";
                try
                {
                    using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
                    using var resp = await http.GetAsync(ModelUrl, HttpCompletionOption.ResponseHeadersRead, ct);
                    resp.EnsureSuccessStatusCode();
                    await using (var src = await resp.Content.ReadAsStreamAsync(ct))
                    await using (var file = File.Create(tmp))
                        await src.CopyToAsync(file, 1 << 20, ct);
                    if (new FileInfo(tmp).Length < 100_000_000)
                        throw new Exception("téléchargement incomplet");
                    File.Move(tmp, ModelPath, true);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception e)
                {
                    try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
                    return "Téléchargement de la transcription échoué : " + e.Message;
                }
            }
            try { factory = WhisperFactory.FromPath(ModelPath); }
            catch (Exception e) { return "Moteur de transcription indisponible : " + e.Message; }
            Progress?.Invoke("");
            return null;
        }
        finally { gate.Release(); }
    }

    /// <summary>Transcrit un WAV 16 kHz mono. Renvoie le texte, ou null si le moteur n'est pas prêt.</summary>
    public async Task<string?> TranscribeAsync(byte[] wav, CancellationToken ct = default)
    {
        var f = factory;
        if (f == null)
            return null;
        var sb = new StringBuilder();
        using var processor = f.CreateBuilder().WithLanguage("fr").Build();
        using var ms = new MemoryStream(wav);
        await foreach (var seg in processor.ProcessAsync(ms, ct))
            sb.Append(seg.Text);
        return sb.ToString().Trim();
    }

    public void Dispose()
    {
        try { factory?.Dispose(); } catch { }
        factory = null;
    }
}
