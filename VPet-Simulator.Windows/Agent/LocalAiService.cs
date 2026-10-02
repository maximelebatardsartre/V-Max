using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using VPet_Simulator.Windows.Interface;

namespace VPet_Simulator.Windows.Agent;

/// <summary>
/// V-Max : IA locale native (Llama 3.2 1B via le serveur llama.cpp, compatible OpenAI). Rien n'est embarqué dans
/// l'installeur : au premier passage à « activé », le modèle (~0,8 Go) et le serveur sont téléchargés puis mis en
/// cache. Activé → le serveur tourne (pleine puissance CPU) ; désactivé → le processus est arrêté, zéro ressource.
/// Exposé comme le fournisseur local « maxine-local » et détecté automatiquement par le routeur une fois prêt.
/// </summary>
public sealed class LocalAiService
{
    public const int Port = 8777;
    public const string ProviderId = "maxine-local";
    public const string ModelAlias = "maxine";
    public const string BaseUrl = "http://127.0.0.1:8777/v1";

    // Sources (modifiables si les URLs changent)
    private const string ModelUrl = "https://huggingface.co/bartowski/Llama-3.2-1B-Instruct-GGUF/resolve/main/Llama-3.2-1B-Instruct-Q4_K_M.gguf?download=true";
    private const string ModelFile = "Llama-3.2-1B-Instruct-Q4_K_M.gguf";
    // Les binaires sont sur les builds « bXXXX » (la release « latest » de GitHub n'en a pas) : on parcourt la liste.
    private const string ReleaseApi = "https://api.github.com/repos/ggml-org/llama.cpp/releases?per_page=15";

    private static string Dir => Path.Combine(ExtensionValue.DataDirectory, "local-ai");
    private static string ModelPath => Path.Combine(Dir, ModelFile);
    private static string ServerPath => Path.Combine(Dir, "llama-server.exe");

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(30) };

    private readonly MainWindow mw;
    private Process? server;
    private readonly SemaphoreSlim gate = new(1, 1);

    public LocalAiService(MainWindow mw)
    {
        this.mw = mw;
        Http.DefaultRequestHeaders.UserAgent.TryParseAdd("V-Max");
    }

    /// <summary>Réglage persistant : l'utilisateur veut l'IA locale active</summary>
    public bool Enabled
    {
        get => mw.Set["vmax_localai"][(LinePutScript.gbol)"enabled"];
        set => mw.Set["vmax_localai"][(LinePutScript.gbol)"enabled"] = value;
    }

    public bool ModelReady => File.Exists(ModelPath) && File.Exists(ServerPath);
    public bool Running => server is { HasExited: false };

    /// <summary>Progression (texte, 0..1 ou -1 si indéterminé)</summary>
    public event Action<string, double>? Progress;

    /// <summary>
    /// Active l'IA locale : télécharge ce qui manque, puis démarre le serveur. Renvoie null si OK, sinon un message.
    /// </summary>
    public async Task<string?> EnableAsync(CancellationToken cancel = default)
    {
        await gate.WaitAsync(cancel);
        try
        {
            Enabled = true;
            mw.Save();
            Directory.CreateDirectory(Dir);
            if (!File.Exists(ServerPath))
            {
                var err = await DownloadServerAsync(cancel);
                if (err != null)
                    return err;
            }
            if (!File.Exists(ModelPath))
            {
                Progress?.Invoke("Téléchargement du modèle (~0,8 Go)…", -1);
                var err = await DownloadFileAsync(ModelUrl, ModelPath, "modèle", cancel);
                if (err != null)
                    return err;
            }
            return await StartAsync(cancel);
        }
        catch (OperationCanceledException)
        {
            return "Téléchargement annulé.";
        }
        catch (Exception e)
        {
            return "IA locale indisponible : " + e.Message;
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>Arrête le serveur et coupe l'option</summary>
    public void Disable()
    {
        Enabled = false;
        try { mw.Save(); } catch { }
        Stop();
    }

    /// <summary>Au démarrage de V-Max : relance le serveur si l'option était active et le modèle déjà présent</summary>
    public void ResumeIfEnabled()
    {
        if (Enabled && ModelReady && !Running)
            _ = StartAsync(CancellationToken.None);
    }

    public void Stop()
    {
        try
        {
            if (server is { HasExited: false })
                server.Kill(true);
        }
        catch { }
        server = null;
    }

    private async Task<string?> StartAsync(CancellationToken cancel)
    {
        if (Running)
            return null;
        if (!ModelReady)
            return "Le modèle n'est pas encore téléchargé.";
        Progress?.Invoke("Démarrage de l'IA locale…", -1);
        int threads = Math.Max(1, Environment.ProcessorCount); // pleine puissance CPU, pas de bridage
        var psi = new ProcessStartInfo
        {
            FileName = ServerPath,
            WorkingDirectory = Dir,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var a in new[]
        {
            "-m", ModelPath, "--host", "127.0.0.1", "--port", Port.ToString(),
            "--alias", ModelAlias, "-c", "4096", "-t", threads.ToString(), "-tb", threads.ToString(), "--no-webui",
        })
            psi.ArgumentList.Add(a);
        try
        {
            server = Process.Start(psi);
            if (server == null)
                return "Impossible de démarrer le serveur local.";
            server.EnableRaisingEvents = true;
        }
        catch (Exception e)
        {
            return "Démarrage impossible : " + e.Message;
        }
        // attendre que l'API réponde (jusqu'à ~60 s au premier chargement du modèle)
        for (int i = 0; i < 120; i++)
        {
            if (server.HasExited)
                return "Le serveur local s'est arrêté (voir local-ai). Code " + server.ExitCode + ".";
            if (await PingAsync(cancel))
            {
                Progress?.Invoke("IA locale prête.", 1);
                _ = Providers.ProviderRouter.RefreshLocalAsync();
                return null;
            }
            await Task.Delay(500, cancel);
        }
        return "L'IA locale n'a pas répondu à temps.";
    }

    private static async Task<bool> PingAsync(CancellationToken cancel)
    {
        try
        {
            using var r = await Http.GetAsync($"http://127.0.0.1:{Port}/v1/models", cancel);
            return r.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    // --- téléchargement du serveur llama.cpp (dernière release Windows CPU x64)
    private async Task<string?> DownloadServerAsync(CancellationToken cancel)
    {
        Progress?.Invoke("Recherche du moteur local…", -1);
        string? assetUrl = null;
        try
        {
            var releases = JsonNode.Parse(await Http.GetStringAsync(ReleaseApi, cancel)) as JsonArray;
            // build CPU x64 (compatible partout, pas de GPU requis) le plus récent qui l'a
            foreach (var release in releases ?? [])
            {
                if (release?["assets"] is not JsonArray assets)
                    continue;
                foreach (var pref in new[] { "bin-win-cpu-x64", "bin-win-avx2-x64" })
                {
                    assetUrl = assets.FirstOrDefault(a => (a?["name"]?.GetValue<string>() ?? "").Contains(pref, StringComparison.OrdinalIgnoreCase))
                        ?["browser_download_url"]?.GetValue<string>();
                    if (assetUrl != null)
                        break;
                }
                if (assetUrl != null)
                    break;
            }
        }
        catch (Exception e)
        {
            return "Moteur local introuvable : " + e.Message;
        }
        if (assetUrl == null)
            return "Aucun moteur local compatible trouvé pour Windows.";

        var zip = Path.Combine(Dir, "llama.zip");
        Progress?.Invoke("Téléchargement du moteur local…", -1);
        var err = await DownloadFileAsync(assetUrl, zip, "moteur", cancel);
        if (err != null)
            return err;
        try
        {
            using (var archive = ZipFile.OpenRead(zip))
                foreach (var entry in archive.Entries)
                {
                    if (entry.Length == 0 || entry.FullName.EndsWith("/"))
                        continue;
                    var dest = Path.Combine(Dir, Path.GetFileName(entry.FullName)); // tout à plat dans local-ai
                    entry.ExtractToFile(dest, true);
                }
            File.Delete(zip);
        }
        catch (Exception e)
        {
            return "Extraction du moteur impossible : " + e.Message;
        }
        return File.Exists(ServerPath) ? null : "Le moteur local est incomplet (llama-server.exe absent).";
    }

    private async Task<string?> DownloadFileAsync(string url, string dest, string label, CancellationToken cancel)
    {
        var tmp = dest + ".part";
        try
        {
            using var resp = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancel);
            resp.EnsureSuccessStatusCode();
            long? total = resp.Content.Headers.ContentLength;
            await using var src = await resp.Content.ReadAsStreamAsync(cancel);
            await using (var dst = File.Create(tmp))
            {
                var buffer = new byte[1 << 20];
                long read = 0;
                int n;
                while ((n = await src.ReadAsync(buffer, cancel)) > 0)
                {
                    await dst.WriteAsync(buffer.AsMemory(0, n), cancel);
                    read += n;
                    if (total is > 0)
                        Progress?.Invoke($"Téléchargement du {label} : {read * 100 / total.Value} %", read / (double)total.Value);
                }
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
            return $"Téléchargement du {label} impossible : {e.Message}";
        }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }
}
