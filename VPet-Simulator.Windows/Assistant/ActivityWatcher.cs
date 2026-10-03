using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows.Threading;

namespace VPet_Simulator.Windows.Assistant;

/// <summary>
/// Surveille ce que tu LANCES sur ton PC (jeux, grosses applis) en comparant la liste des processus toutes les
/// quelques secondes. Émet <see cref="Detected"/> à la première apparition d'un programme connu — pour que Maxine
/// réagisse (« Oh, tu lances … ! »). On ne réagit qu'à une liste curatée : zéro bruit sur les processus inconnus.
/// </summary>
public sealed class ActivityWatcher
{
    public sealed record Launch(string Display, bool IsGame);
    public event Action<Launch>? Detected;

    private readonly DispatcherTimer timer;
    private HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);

    public ActivityWatcher()
    {
        timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(4) };
        timer.Tick += (_, _) => Scan();
    }

    public void Start()
    {
        seen = Current(); // on mémorise ce qui tourne DÉJÀ pour ne pas l'annoncer au démarrage
        timer.Start();
    }

    public void Stop() => timer.Stop();

    private void Scan()
    {
        var now = Current();
        foreach (var name in now)
            if (!seen.Contains(name) && Known.TryGetValue(name, out var hit))
                try { Detected?.Invoke(hit); } catch { }
        seen = now;
    }

    private static HashSet<string> Current()
    {
        var s = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in Process.GetProcesses())
        {
            try { s.Add(p.ProcessName); } catch { }
            finally { try { p.Dispose(); } catch { } }
        }
        return s;
    }

    /// <summary>Processus connus (sans « .exe ») → nom affichable + est-ce un jeu. Curaté : on ne réagit qu'à ça.</summary>
    private static readonly Dictionary<string, Launch> Known = new(StringComparer.OrdinalIgnoreCase)
    {
        // Jeux
        ["LeagueClient"] = new("League of Legends", true),
        ["League of Legends"] = new("League of Legends", true),
        ["VALORANT-Win64-Shipping"] = new("Valorant", true),
        ["cs2"] = new("Counter-Strike 2", true),
        ["csgo"] = new("Counter-Strike", true),
        ["FortniteClient-Win64-Shipping"] = new("Fortnite", true),
        ["RocketLeague"] = new("Rocket League", true),
        ["GTA5"] = new("GTA V", true),
        ["GTA5_Enhanced"] = new("GTA V", true),
        ["RDR2"] = new("Red Dead Redemption 2", true),
        ["Cyberpunk2077"] = new("Cyberpunk 2077", true),
        ["eldenring"] = new("Elden Ring", true),
        ["Overwatch"] = new("Overwatch", true),
        ["destiny2"] = new("Destiny 2", true),
        ["bg3"] = new("Baldur's Gate 3", true),
        ["bg3_dx11"] = new("Baldur's Gate 3", true),
        ["Hades2"] = new("Hades II", true),
        ["Terraria"] = new("Terraria", true),
        ["factorio"] = new("Factorio", true),
        ["StardewValley"] = new("Stardew Valley", true),
        ["EscapeFromTarkov"] = new("Escape from Tarkov", true),
        ["RainbowSix"] = new("Rainbow Six", true),
        ["PalworldClient-Win64-Shipping"] = new("Palworld", true),
        ["deadlock"] = new("Deadlock", true),
        ["Dota2"] = new("Dota 2", true),
        ["Warframe.x64"] = new("Warframe", true),
        ["gw2-64"] = new("Guild Wars 2", true),
        // Grosses applis (réactions plus discrètes)
        ["Discord"] = new("Discord", false),
        ["Spotify"] = new("Spotify", false),
        ["obs64"] = new("OBS", false),
        ["Code"] = new("VS Code", false),
        ["blender"] = new("Blender", false),
        ["Photoshop"] = new("Photoshop", false),
        ["Premiere Pro"] = new("Premiere Pro", false),
    };
}
