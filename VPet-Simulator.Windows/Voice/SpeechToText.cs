using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Speech.Recognition;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using VPet_Simulator.Windows.Agent;
using VPet_Simulator.Windows.Agent.Providers;

namespace VPet_Simulator.Windows.Voice;

/// <summary>
/// V-Max voix : transcription de ce que dit l'utilisateur.
/// 1. Groq (Whisper, gratuit, très précis) si une clé Groq est enregistrée ;
/// 2. Gemini (audio) si une clé Gemini est enregistrée ;
/// 3. reconnaissance de Windows, hors ligne (moins précise), toujours disponible si la langue est installée.
/// En mode « hors ligne uniquement », seule la 3e est utilisée : la voix ne quitte jamais le PC.
/// </summary>
public static class SpeechToText
{
    public sealed record Result(string Text, string Engine);

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(40) };

    /// <summary>Adresse de Groq (surchargeable pour les tests : VMAX_GROQ_BASEURL)</summary>
    private static string GroqBase => Environment.GetEnvironmentVariable("VMAX_GROQ_BASEURL") is { Length: > 0 } u
        ? u.TrimEnd('/') : "https://api.groq.com/openai/v1";

    public static async Task<Result> TranscribeAsync(byte[] wav, bool offlineOnly, CancellationToken ct)
    {
        if (!offlineOnly)
        {
            var groq = ProviderRouter.Catalog.First(p => p.Id == "groq");
            if (ProviderRouter.KeyFor(groq) is { Length: > 0 } groqKey)
            {
                try { return new Result(await GroqAsync(wav, groqKey, ct), "Groq Whisper"); }
                catch (OperationCanceledException) { throw; }
                catch { }
            }
            var gemini = ProviderRouter.Catalog.First(p => p.Id == "gemini");
            if (ProviderRouter.KeyFor(gemini) is { Length: > 0 } geminiKey)
            {
                try { return new Result(await GeminiAsync(wav, geminiKey, ct), "Gemini"); }
                catch (OperationCanceledException) { throw; }
                catch { }
            }
        }
        return new Result(await Task.Run(() => Offline(wav), ct), "Windows (hors ligne)");
    }

    private static async Task<string> GroqAsync(byte[] wav, string key, CancellationToken ct)
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(wav);
        file.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
        form.Add(file, "file", "voix.wav");
        form.Add(new StringContent("whisper-large-v3-turbo"), "model");
        form.Add(new StringContent("fr"), "language");
        form.Add(new StringContent("json"), "response_format");
        using var req = new HttpRequestMessage(HttpMethod.Post, GroqBase + "/audio/transcriptions") { Content = form };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        using var res = await Http.SendAsync(req, ct);
        var body = await res.Content.ReadAsStringAsync(ct);
        if (!res.IsSuccessStatusCode)
            throw new HttpRequestException($"Groq : {(int)res.StatusCode}");
        return (JsonNode.Parse(body)?["text"]?.GetValue<string>() ?? "").Trim();
    }

    private static async Task<string> GeminiAsync(byte[] wav, string key, CancellationToken ct)
    {
        var contents = new JsonArray(new JsonObject
        {
            ["role"] = "user",
            ["parts"] = new JsonArray(
                new JsonObject { ["text"] = "Transcris mot pour mot ce que dit la personne (en français). Réponds uniquement par la transcription, sans guillemets. Si rien n'est compréhensible, réponds par une chaîne vide." },
                new JsonObject { ["inline_data"] = new JsonObject { ["mime_type"] = "audio/wav", ["data"] = Convert.ToBase64String(wav) } }),
        });
        var sb = new StringBuilder();
        await foreach (var chunk in new GeminiClient(key).StreamAsync(contents, "Tu es un service de transcription.", null, ct))
            sb.Append(chunk.Text);
        return sb.ToString().Trim().Trim('"', '«', '»').Trim();
    }

    /// <summary>Reconnaissance de Windows (hors ligne) sur l'enregistrement</summary>
    public static string Offline(byte[] wav)
    {
        var culture = OfflineCulture() ?? throw new InvalidOperationException(
            "Aucune reconnaissance vocale française n'est installée sur ce PC (Paramètres Windows › Heure et langue › Voix).");
        using var engine = new SpeechRecognitionEngine(culture);
        engine.LoadGrammar(new DictationGrammar());
        using var ms = new MemoryStream(wav);
        engine.SetInputToWaveStream(ms);
        var sb = new StringBuilder();
        // la dictée découpe en phrases : on lit jusqu'à la fin du flux
        while (true)
        {
            RecognitionResult? r;
            try
            {
                r = engine.Recognize();
            }
            catch (InvalidOperationException)
            {
                break; // fin du flux : Windows lève une exception au lieu de renvoyer null
            }
            if (r == null)
                break;
            sb.Append(r.Text).Append(' ');
        }
        return sb.ToString().Trim();
    }

    /// <summary>Langue de reconnaissance hors ligne disponible (français de préférence)</summary>
    public static CultureInfo? OfflineCulture()
    {
        try
        {
            var all = SpeechRecognitionEngine.InstalledRecognizers();
            return (all.FirstOrDefault(r => r.Culture.TwoLetterISOLanguageName == "fr") ?? all.FirstOrDefault())?.Culture;
        }
        catch
        {
            return null;
        }
    }
}
