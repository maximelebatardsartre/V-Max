using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using VPet_Simulator.Windows.Interface;

namespace VPet_Simulator.Windows.Agent;

/// <summary>
/// Niveau de risque d'un outil (voir docs/VISION.md §4.4)
/// </summary>
public enum ToolRisk
{
    /// <summary>Lecture seule, sans effet</summary>
    ReadOnly,
    /// <summary>Effet limité et réversible : exécution directe, journalisée</summary>
    Low,
    /// <summary>Demande une confirmation (mémorisable)</summary>
    Sensitive,
    /// <summary>Confirmation à chaque fois</summary>
    Dangerous,
}

/// <summary>
/// Contexte fourni aux outils
/// </summary>
public sealed class ToolContext
{
    public required MainWindow MW { get; init; }
    /// <summary>Faire parler le compagnon (rappels, minuteurs…)</summary>
    public required Action<string> Say { get; init; }
}

/// <summary>
/// Outil utilisable par l'agent (function calling)
/// </summary>
public interface IAgentTool
{
    /// <summary>Nom technique (a-z, 0-9, _)</summary>
    string Name { get; }
    /// <summary>Libellé affiché à l'utilisateur</summary>
    string Title { get; }
    /// <summary>Description pour le modèle (en français)</summary>
    string Description { get; }
    /// <summary>Schéma JSON des paramètres (sous-ensemble OpenAPI accepté par Gemini), ou null</summary>
    JsonObject? Parameters { get; }
    ToolRisk Risk { get; }
    /// <summary>Phrase de confirmation montrée à l'utilisateur (outils sensibles)</summary>
    string DescribeAction(JsonObject args) => Title;
    Task<JsonObject> ExecuteAsync(JsonObject args, ToolContext ctx, CancellationToken ct);
}

/// <summary>
/// Outil défini par des délégués
/// </summary>
public sealed class AgentTool : IAgentTool
{
    public required string Name { get; init; }
    public required string Title { get; init; }
    public required string Description { get; init; }
    public JsonObject? Parameters { get; init; }
    public ToolRisk Risk { get; init; }
    public Func<JsonObject, string>? Describe { get; init; }
    public required Func<JsonObject, ToolContext, CancellationToken, Task<JsonObject>> Run { get; init; }
    public string DescribeAction(JsonObject args) => Describe?.Invoke(args) ?? Title;
    public Task<JsonObject> ExecuteAsync(JsonObject args, ToolContext ctx, CancellationToken ct) => Run(args, ctx, ct);
}

/// <summary>
/// Registre des outils de l'agent + outils intégrés de V-Max
/// </summary>
public static class AgentToolRegistry
{
    private static readonly List<IAgentTool> tools = new();

    public static IReadOnlyList<IAgentTool> All => tools;

    /// <summary>
    /// Permet aux plugins d'ajouter des outils (ils restent soumis aux confirmations selon leur risque)
    /// </summary>
    public static void Register(IAgentTool tool)
    {
        if (tools.Any(t => t.Name == tool.Name))
            throw new ArgumentException("Outil déjà enregistré : " + tool.Name);
        tools.Add(tool);
    }

    public static IAgentTool? Find(string name) => tools.FirstOrDefault(t => t.Name == name);

    /// <summary>
    /// Déclarations au format Gemini pour les outils autorisés
    /// </summary>
    public static JsonArray Declarations(Func<IAgentTool, bool> enabled)
    {
        var arr = new JsonArray();
        foreach (var t in tools.Where(enabled))
        {
            var decl = new JsonObject { ["name"] = t.Name, ["description"] = t.Description };
            if (t.Parameters != null)
                decl["parameters"] = t.Parameters.DeepClone();
            arr.Add(decl);
        }
        return arr;
    }

    static AgentToolRegistry()
    {
        RegisterBuiltIns();
    }

    private static JsonObject Obj(params (string name, string type, string desc, string[]? enumValues)[] props)
    {
        var p = new JsonObject();
        var required = new JsonArray();
        foreach (var (name, type, desc, enumValues) in props)
        {
            var o = new JsonObject { ["type"] = type, ["description"] = desc };
            if (enumValues != null)
                o["enum"] = new JsonArray(enumValues.Select(e => (JsonNode)e).ToArray());
            p[name] = o;
            required.Add(name);
        }
        return new JsonObject { ["type"] = "object", ["properties"] = p, ["required"] = required };
    }

    private static JsonObject Ok(string message) => new() { ["ok"] = true, ["message"] = message };
    private static JsonObject Fail(string message) => new() { ["ok"] = false, ["error"] = message };

    private static void RegisterBuiltIns()
    {
        // ---- Lecture seule
        tools.Add(new AgentTool
        {
            Name = "system_info",
            Title = "Lire l'état du PC",
            Description = "Donne la date et l'heure, le niveau de batterie, la mémoire disponible et la durée depuis le démarrage du PC.",
            Risk = ToolRisk.ReadOnly,
            Run = (_, _, _) =>
            {
                var power = System.Windows.Forms.SystemInformation.PowerStatus;
                var mem = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
                GlobalMemoryStatusEx(ref mem);
                var r = new JsonObject
                {
                    ["date_heure"] = DateTime.Now.ToString("dddd d MMMM yyyy, HH:mm", new CultureInfo("fr-FR")),
                    ["memoire_utilisee_pourcent"] = mem.dwMemoryLoad,
                    ["memoire_disponible_go"] = Math.Round(mem.ullAvailPhys / 1073741824.0, 1),
                    ["demarre_depuis_heures"] = Math.Round(Environment.TickCount64 / 3600000.0, 1),
                };
                if (power.BatteryChargeStatus != System.Windows.Forms.BatteryChargeStatus.NoSystemBattery)
                {
                    r["batterie_pourcent"] = (int)(power.BatteryLifePercent * 100);
                    r["sur_secteur"] = power.PowerLineStatus == System.Windows.Forms.PowerLineStatus.Online;
                }
                else
                    r["batterie"] = "aucune (PC fixe)";
                return Task.FromResult(r);
            },
        });

        // ---- Média et volume (touches multimédia)
        tools.Add(new AgentTool
        {
            Name = "media_control",
            Title = "Contrôler la musique",
            Description = "Contrôle la lecture multimédia en cours (Spotify, navigateur, lecteur…) et le volume du PC.",
            Parameters = Obj(("action", "string", "Action à effectuer", ["lecture_pause", "suivant", "precedent", "stop", "volume_plus", "volume_moins", "muet"])),
            Risk = ToolRisk.Low,
            Run = (args, _, _) =>
            {
                string action = args["action"]?.GetValue<string>() ?? "";
                (byte vk, int repeat) = action switch
                {
                    "lecture_pause" => ((byte)0xB3, 1),
                    "suivant" => ((byte)0xB0, 1),
                    "precedent" => ((byte)0xB1, 1),
                    "stop" => ((byte)0xB2, 1),
                    "volume_plus" => ((byte)0xAF, 5),
                    "volume_moins" => ((byte)0xAE, 5),
                    "muet" => ((byte)0xAD, 1),
                    _ => ((byte)0, 0),
                };
                if (vk == 0)
                    return Task.FromResult(Fail("Action inconnue : " + action));
                for (int i = 0; i < repeat; i++)
                {
                    keybd_event(vk, 0, KEYEVENTF_EXTENDEDKEY, UIntPtr.Zero);
                    keybd_event(vk, 0, KEYEVENTF_EXTENDEDKEY | KEYEVENTF_KEYUP, UIntPtr.Zero);
                }
                return Task.FromResult(Ok("Action « " + action + " » envoyée."));
            },
        });

        // ---- Web
        tools.Add(new AgentTool
        {
            Name = "open_url",
            Title = "Ouvrir une page web",
            Description = "Ouvre une adresse web (http ou https uniquement) dans le navigateur par défaut.",
            Parameters = Obj(("url", "string", "Adresse complète commençant par https:// ou http://", null)),
            Risk = ToolRisk.Low,
            Describe = a => "Ouvrir " + a["url"]?.GetValue<string>(),
            Run = (args, _, _) =>
            {
                var url = args["url"]?.GetValue<string>() ?? "";
                if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
                    return Task.FromResult(Fail("Seules les adresses http(s) sont autorisées."));
                ExtensionFunction.StartURL(uri.AbsoluteUri);
                return Task.FromResult(Ok("Page ouverte : " + uri.Host));
            },
        });
        tools.Add(new AgentTool
        {
            Name = "web_search",
            Title = "Rechercher sur le web",
            Description = "Lance une recherche web dans le navigateur de l'utilisateur (le résultat s'affiche à l'écran, tu ne le vois pas).",
            Parameters = Obj(("requete", "string", "Termes de la recherche", null)),
            Risk = ToolRisk.Low,
            Run = (args, _, _) =>
            {
                var q = args["requete"]?.GetValue<string>() ?? "";
                if (string.IsNullOrWhiteSpace(q))
                    return Task.FromResult(Fail("Requête vide."));
                ExtensionFunction.StartURL("https://duckduckgo.com/?q=" + Uri.EscapeDataString(q));
                return Task.FromResult(Ok("Recherche ouverte dans le navigateur."));
            },
        });

        // ---- Applications et dossiers
        tools.Add(new AgentTool
        {
            Name = "open_app",
            Title = "Ouvrir une application",
            Description = "Lance une application installée en cherchant son nom dans le menu Démarrer (ex. « Spotify », « Calculatrice », « Word »).",
            Parameters = Obj(("nom", "string", "Nom de l'application", null)),
            Risk = ToolRisk.Low,
            Describe = a => "Ouvrir l'application « " + a["nom"]?.GetValue<string>() + " »",
            Run = (args, _, _) =>
            {
                var name = args["nom"]?.GetValue<string>() ?? "";
                var (match, suggestions) = StartMenu.Find(name);
                if (match == null)
                    return Task.FromResult(Fail("Application introuvable." + (suggestions.Count > 0 ? " Suggestions : " + string.Join(", ", suggestions) : "")));
                Process.Start(new ProcessStartInfo(match.FullName) { UseShellExecute = true })?.Dispose();
                return Task.FromResult(Ok("Application lancée : " + Path.GetFileNameWithoutExtension(match.Name)));
            },
        });
        tools.Add(new AgentTool
        {
            Name = "open_folder",
            Title = "Ouvrir un dossier",
            Description = "Ouvre un dossier connu de l'utilisateur dans l'Explorateur.",
            Parameters = Obj(("dossier", "string", "Dossier à ouvrir", ["documents", "telechargements", "images", "bureau", "musique", "videos"])),
            Risk = ToolRisk.Low,
            Run = (args, _, _) =>
            {
                string? path = (args["dossier"]?.GetValue<string>()) switch
                {
                    "documents" => Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    "images" => Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
                    "bureau" => Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                    "musique" => Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
                    "videos" => Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
                    "telechargements" => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"),
                    _ => null,
                };
                if (path == null || !Directory.Exists(path))
                    return Task.FromResult(Fail("Dossier inconnu."));
                Process.Start(new ProcessStartInfo("explorer.exe", "\"" + path + "\"") { UseShellExecute = true })?.Dispose();
                return Task.FromResult(Ok("Dossier ouvert."));
            },
        });

        // ---- Minuteur
        tools.Add(new AgentTool
        {
            Name = "set_timer",
            Title = "Lancer un minuteur",
            Description = "Lance un minuteur ; à la fin, le compagnon prévient l'utilisateur à voix haute avec le message donné.",
            Parameters = Obj(("minutes", "number", "Durée en minutes (0,5 à 600)", null), ("message", "string", "Message à dire à la fin", null)),
            Risk = ToolRisk.Low,
            Run = (args, ctx, _) =>
            {
                double minutes = args["minutes"]?.GetValue<double>() ?? 0;
                if (minutes < 0.5 || minutes > 600)
                    return Task.FromResult(Fail("Durée hors limites (0,5 à 600 minutes)."));
                string message = args["message"]?.GetValue<string>() ?? "Le minuteur est terminé !";
                Task.Delay(TimeSpan.FromMinutes(minutes)).ContinueWith(t => ctx.Say("⏰ " + message));
                return Task.FromResult(Ok($"Minuteur lancé pour {minutes:0.#} minute(s)."));
            },
        });

        // ---- Session
        tools.Add(new AgentTool
        {
            Name = "lock_session",
            Title = "Verrouiller la session Windows",
            Description = "Verrouille immédiatement la session Windows (écran de verrouillage).",
            Risk = ToolRisk.Dangerous,
            Describe = _ => "Verrouiller ta session Windows maintenant",
            Run = (_, _, _) => Task.FromResult(LockWorkStation() ? Ok("Session verrouillée.") : Fail("Échec du verrouillage.")),
        });
    }

    #region Win32
    private const uint KEYEVENTF_EXTENDEDKEY = 0x1;
    private const uint KEYEVENTF_KEYUP = 0x2;

    [DllImport("user32.dll")] private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);
    [DllImport("user32.dll")] private static extern bool LockWorkStation();

    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength, dwMemoryLoad;
        public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual;
    }
    [DllImport("kernel32.dll")] private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX buffer);
    #endregion
}

/// <summary>
/// Recherche d'applications dans les raccourcis du menu Démarrer (utilisateur + commun)
/// </summary>
internal static class StartMenu
{
    private static List<FileInfo>? cache;

    private static List<FileInfo> Shortcuts()
    {
        if (cache != null)
            return cache;
        var list = new List<FileInfo>();
        foreach (var root in new[] { Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu) })
        {
            try
            {
                if (Directory.Exists(root))
                    list.AddRange(new DirectoryInfo(root).EnumerateFiles("*.lnk", SearchOption.AllDirectories)
                        .Where(f => !Fold(f.Name).Contains("uninstall") && !Fold(f.Name).Contains("desinstall")));
            }
            catch { }
        }
        return cache = list;
    }

    internal static string Fold(string s)
    {
        var d = s.ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (var c in d)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                sb.Append(c);
        return sb.ToString();
    }

    public static (FileInfo? match, List<string> suggestions) Find(string name)
    {
        var q = Fold(name.Trim());
        if (q.Length == 0)
            return (null, new());
        var all = Shortcuts();
        string Key(FileInfo f) => Fold(Path.GetFileNameWithoutExtension(f.Name));
        var exact = all.FirstOrDefault(f => Key(f) == q);
        if (exact != null)
            return (exact, new());
        var starts = all.Where(f => Key(f).StartsWith(q)).OrderBy(f => f.Name.Length).ToList();
        if (starts.Count > 0)
            return (starts[0], new());
        var contains = all.Where(f => Key(f).Contains(q)).OrderBy(f => f.Name.Length).ToList();
        if (contains.Count == 1)
            return (contains[0], new());
        return (null, contains.Take(5).Select(f => Path.GetFileNameWithoutExtension(f.Name)).Distinct().ToList());
    }
}
