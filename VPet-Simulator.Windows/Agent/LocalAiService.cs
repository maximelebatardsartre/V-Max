using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using VPet_Simulator.Windows.Interface;

namespace VPet_Simulator.Windows.Agent;

/// <summary>
/// V-Max : IA locale native (Llama 3.2 1B via le serveur llama.cpp, compatible OpenAI). Rien n'est embarqué dans
/// l'installeur : au premier passage à « activé », le modèle (~2 Go) et le serveur sont téléchargés puis mis en
/// cache. Activé → le serveur tourne (pleine puissance CPU) ; désactivé → le processus est arrêté, zéro ressource.
/// Exposé comme le fournisseur local « maxine-local » et détecté automatiquement par le routeur une fois prêt.
/// </summary>
public sealed class LocalAiService
{
    public const int Port = 8777;
    public const string ProviderId = "maxine-local";
    public const string ModelAlias = "maxine";
    public const string BaseUrl = "http://127.0.0.1:8777/v1";

    // Sources (modifiables si les URLs changent). Qwen2.5-3B-Instruct : bien meilleur français et vrai support des
    // outils/fonctions (contrairement au 1B qui vouvoyait, sortait du personnage et produisait des appels d'outils
    // malformés → erreur « format »). ~1,9 Go en Q4_K_M.
    private const string ModelUrl = "https://huggingface.co/bartowski/Qwen2.5-3B-Instruct-GGUF/resolve/main/Qwen2.5-3B-Instruct-Q4_K_M.gguf?download=true";
    private const string ModelFile = "Qwen2.5-3B-Instruct-Q4_K_M.gguf";
    // Les binaires sont sur les builds « bXXXX » (la release « latest » de GitHub n'en a pas) : on parcourt la liste.
    private const string ReleaseApi = "https://api.github.com/repos/ggml-org/llama.cpp/releases?per_page=15";

    // %LOCALAPPDATA%\V-Max\local-ai (pas Roaming) : le modèle ~0,8 Go et llama-server.exe ne doivent pas être
    // synchronisés par OneDrive ni « roamés » entre machines. En mode portable, tout reste à côté de l'exe.
    private static string Dir => ExtensionValue.IsPortable
        ? Path.Combine(ExtensionValue.BaseDirectory, "local-ai")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "V-Max", "local-ai");
    private static string ModelPath => Path.Combine(Dir, ModelFile);
    private static string ServerPath => Path.Combine(Dir, "llama-server.exe");
    private static string ServerLogPath => Path.Combine(Dir, "server.log");

    private static readonly HttpClient Http = new() { Timeout = Timeout.InfiniteTimeSpan };
    /// <summary>Sans le moindre octet reçu pendant ce délai, on considère la connexion perdue.</summary>
    private static readonly TimeSpan IdleTimeout = TimeSpan.FromSeconds(90);

    private readonly MainWindow mw;
    private Process? server;
    private StreamWriter? serverLog;
    private readonly object logLock = new();
    private readonly SemaphoreSlim gate = new(1, 1);

    public LocalAiService(MainWindow mw)
    {
        this.mw = mw;
        Http.DefaultRequestHeaders.UserAgent.TryParseAdd("V-Max");
        // filet de sécurité : si V-Max se ferme (même brutalement), on tente d'arrêter le serveur. Le Job Object
        // ci-dessous est la vraie garantie anti-orphelin ; ceci couvre l'arrêt gracieux.
        AppDomain.CurrentDomain.ProcessExit += (_, _) => Stop();
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
                Progress?.Invoke("Téléchargement du modèle (~2 Go)…", -1);
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
        if (!Enabled || !ModelReady || Running)
            return;
        // passe par le même verrou qu'EnableAsync/Disable pour éviter une course si l'utilisateur (dé)coche l'IA
        // locale pendant la reprise au démarrage.
        _ = Task.Run(async () =>
        {
            await gate.WaitAsync();
            try { if (!Running) await StartAsync(CancellationToken.None); }
            catch { }
            finally { gate.Release(); }
        });
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
        lock (logLock)
        {
            try { serverLog?.Flush(); serverLog?.Dispose(); } catch { }
            serverLog = null;
        }
    }

    private async Task<string?> StartAsync(CancellationToken cancel)
    {
        if (Running)
            return null;
        if (!ModelReady)
            return "Le modèle n'est pas encore téléchargé.";
        // port déjà occupé (orphelin d'une session précédente, ou autre logiciel) : on prévient clairement
        // plutôt que de lancer un processus qui échouera à se lier.
        if (await PingAsync(cancel))
            return "Le port " + Port + " est déjà utilisé (peut-être une ancienne IA locale encore active). "
                 + "Ferme Maxine complètement puis relance, ou redémarre le PC.";
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
            // --jinja : utilise le gabarit de discussion du modèle (Qwen) qui gère correctement les appels d'outils.
            // Sans lui, llama-server rejetait la sortie (« ne correspond pas au format attendu »).
            "--alias", ModelAlias, "--jinja", "-c", "4096", "-t", threads.ToString(), "-tb", threads.ToString(), "--no-webui",
        })
            psi.ArgumentList.Add(a);
        try
        {
            server = Process.Start(psi);
            if (server == null)
                return "Impossible de démarrer le serveur local.";
            server.EnableRaisingEvents = true;
            // anti-orphelin : si V-Max meurt (crash, kill, fermeture de session), l'OS ferme le Job Object et tue
            // automatiquement le serveur. Sinon llama-server survivrait, pleine charge CPU, et squatterait le port.
            JobObject.Assign(server);
            // on draine la sortie en continu (sinon le tampon des pipes se remplit et FIGE le serveur) vers un log
            StartServerLogging(server);
        }
        catch (Exception e)
        {
            return "Démarrage impossible : " + e.Message;
        }
        // attendre que l'API réponde (jusqu'à ~60 s au premier chargement du modèle)
        for (int i = 0; i < 120; i++)
        {
            if (server.HasExited)
                return "Le serveur local s'est arrêté (voir local-ai\\server.log). Code " + server.ExitCode + ".";
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

    /// <summary>Draine stdout/stderr du serveur vers local-ai\server.log (indispensable : pipes non lus = blocage).</summary>
    private void StartServerLogging(Process proc)
    {
        try
        {
            lock (logLock)
            {
                try { serverLog?.Dispose(); } catch { }
                serverLog = new StreamWriter(ServerLogPath, append: false) { AutoFlush = true };
            }
            proc.OutputDataReceived += (_, ev) => WriteServerLog(ev.Data);
            proc.ErrorDataReceived += (_, ev) => WriteServerLog(ev.Data);
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();
        }
        catch { /* le drainage est au pire désactivé ; pas de blocage grâce aux handles redirigés quand même lus */ }
    }

    private void WriteServerLog(string? line)
    {
        if (line == null)
            return;
        lock (logLock)
        {
            try { serverLog?.WriteLine(line); } catch { }
        }
    }

    private static async Task<bool> PingAsync(CancellationToken cancel)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancel);
            cts.CancelAfter(TimeSpan.FromSeconds(3));
            using var r = await Http.GetAsync($"http://127.0.0.1:{Port}/v1/models", cts.Token);
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
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancel);
            cts.CancelAfter(TimeSpan.FromSeconds(30));
            var releases = JsonNode.Parse(await Http.GetStringAsync(ReleaseApi, cts.Token)) as JsonArray;
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
            TryDelete(zip);
            return "Extraction du moteur impossible : " + e.Message;
        }
        return File.Exists(ServerPath) ? null : "Le moteur local est incomplet (llama-server.exe absent).";
    }

    private async Task<string?> DownloadFileAsync(string url, string dest, string label, CancellationToken cancel)
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
                    idle.CancelAfter(IdleTimeout); // réarme le minuteur d'inactivité à chaque lecture
                    try
                    {
                        n = await src.ReadAsync(buffer, idle.Token);
                    }
                    catch (OperationCanceledException) when (!cancel.IsCancellationRequested)
                    {
                        TryDelete(tmp);
                        return $"Connexion perdue pendant le téléchargement du {label}. Vérifie ta connexion puis réessaie.";
                    }
                    if (n <= 0)
                        break;
                    await dst.WriteAsync(buffer.AsMemory(0, n), cancel);
                    read += n;
                    if (total is > 0)
                        Progress?.Invoke($"Téléchargement du {label} : {read * 100 / total.Value} %", read / (double)total.Value);
                }
            }
            // garde-fou anti-corruption : un flux coupé « proprement » laisserait un fichier tronqué sinon accepté
            if (total is > 0 && read < total.Value)
            {
                TryDelete(tmp);
                return $"Téléchargement du {label} incomplet. Réessaie.";
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

    /// <summary>
    /// Job Object Windows partagé : tout processus qui y est assigné est tué automatiquement par l'OS dès que le
    /// handle du Job se ferme — ce qui arrive quand le processus V-Max se termine, y compris sur crash, kill via le
    /// Gestionnaire des tâches, ou fermeture de session. Garantit qu'aucun llama-server n'est laissé orphelin.
    /// </summary>
    private static class JobObject
    {
        private static readonly IntPtr handle = Create();

        public static void Assign(Process p)
        {
            try
            {
                if (handle != IntPtr.Zero)
                    AssignProcessToJobObject(handle, p.Handle);
            }
            catch { }
        }

        private static IntPtr Create()
        {
            try
            {
                var h = CreateJobObject(IntPtr.Zero, null);
                if (h == IntPtr.Zero)
                    return IntPtr.Zero;
                var info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
                info.BasicLimitInformation.LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;
                int len = Marshal.SizeOf(info);
                IntPtr ptr = Marshal.AllocHGlobal(len);
                try
                {
                    Marshal.StructureToPtr(info, ptr, false);
                    SetInformationJobObject(h, JobObjectExtendedLimitInformation, ptr, (uint)len);
                }
                finally { Marshal.FreeHGlobal(ptr); }
                return h;
            }
            catch { return IntPtr.Zero; }
        }

        private const int JobObjectExtendedLimitInformation = 9;
        private const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x2000;

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr CreateJobObject(IntPtr lpJobAttributes, string? lpName);
        [DllImport("kernel32.dll")]
        private static extern bool SetInformationJobObject(IntPtr hJob, int infoClass, IntPtr lpInfo, uint cbInfoLength);
        [DllImport("kernel32.dll")]
        private static extern bool AssignProcessToJobObject(IntPtr hJob, IntPtr hProcess);

        [StructLayout(LayoutKind.Sequential)]
        private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
        {
            public long PerProcessUserTimeLimit;
            public long PerJobUserTimeLimit;
            public uint LimitFlags;
            public UIntPtr MinimumWorkingSetSize;
            public UIntPtr MaximumWorkingSetSize;
            public uint ActiveProcessLimit;
            public UIntPtr Affinity;
            public uint PriorityClass;
            public uint SchedulingClass;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct IO_COUNTERS
        {
            public ulong ReadOperationCount;
            public ulong WriteOperationCount;
            public ulong OtherOperationCount;
            public ulong ReadTransferCount;
            public ulong WriteTransferCount;
            public ulong OtherTransferCount;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
        {
            public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
            public IO_COUNTERS IoInfo;
            public UIntPtr ProcessMemoryLimit;
            public UIntPtr JobMemoryLimit;
            public UIntPtr PeakProcessMemoryUsed;
            public UIntPtr PeakJobMemoryUsed;
        }
    }
}
