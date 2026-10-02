using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using VPet_Simulator.Windows.Interface;

namespace VPet_Simulator.Windows.Habitat;

/// <summary>
/// V-Max : routine de vie définie par l'utilisateur. Une action, un lieu, une plage horaire de départ :
/// chaque jour, l'heure réelle est tirée au hasard dans la plage (12h14 le lundi, 13h40 le mardi…).
/// </summary>
public sealed class LifeRoutine
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public bool Enabled { get; set; } = true;
    /// <summary>« sleep », « eat », « drink », « relax », ou « work:&lt;nom de l'occupation&gt; »</summary>
    public string Action { get; set; } = "relax";
    /// <summary>Nom de la pièce (résolu dans la carte de l'habitat courant ; vide = là où il se trouve)</summary>
    public string? Place { get; set; }
    /// <summary>Début de la plage de départ, « HH:mm »</summary>
    public string From { get; set; } = "12:00";
    /// <summary>Fin de la plage de départ, « HH:mm » (peut passer minuit : 23:00 → 01:30)</summary>
    public string To { get; set; } = "14:00";
    /// <summary>Jours actifs : bit (1 &lt;&lt; DayOfWeek), 127 = tous les jours</summary>
    public int Days { get; set; } = 127;
    /// <summary>Durée minimale de l'action en minutes (0 = durée naturelle de l'animation ou de l'occupation)</summary>
    public int MinMinutes { get; set; }
    /// <summary>Durée maximale en minutes</summary>
    public int MaxMinutes { get; set; }

    public bool OnDay(DayOfWeek d) => (Days & (1 << (int)d)) != 0;

    public static TimeSpan ParseTime(string hhmm) =>
        TimeSpan.TryParse(hhmm, System.Globalization.CultureInfo.InvariantCulture, out var t) && t >= TimeSpan.Zero && t < TimeSpan.FromDays(1) ? t : TimeSpan.Zero;

    public LifeRoutine Clone() => (LifeRoutine)MemberwiseClone();
}

/// <summary>
/// Une occurrence concrète d'une routine pour un jour donné
/// </summary>
public sealed record RoutineOccurrence(LifeRoutine Routine, DateTime Day, DateTime Start, DateTime WindowEnd, TimeSpan Duration)
{
    /// <summary>Clé unique (routine + jour) pour ne jamais déclencher deux fois la même occurrence</summary>
    public string Key => Routine.Id + "@" + Day.ToString("yyyyMMdd");
}

/// <summary>
/// Calcul des heures de déclenchement. Le hasard est <b>déterministe par jour</b> : relancer V-Max ne change pas
/// l'heure choisie pour aujourd'hui, mais chaque jour en a une différente.
/// </summary>
public static class RoutinePlanner
{
    /// <summary>
    /// Occurrence de la routine pour le jour où commence sa plage (null si la routine ne s'applique pas ce jour-là)
    /// </summary>
    public static RoutineOccurrence? Occurrence(LifeRoutine r, DateTime day)
    {
        day = day.Date;
        if (!r.Enabled || !r.OnDay(day.DayOfWeek))
            return null;
        var from = LifeRoutine.ParseTime(r.From);
        var to = LifeRoutine.ParseTime(r.To);
        var windowStart = day + from;
        var windowEnd = day + to;
        if (windowEnd <= windowStart)
            windowEnd = windowEnd.AddDays(1); // la plage passe minuit
        double minutes = (windowEnd - windowStart).TotalMinutes;
        var start = windowStart.AddMinutes(Math.Floor(Unit(r.Id, day, 1) * minutes));
        TimeSpan duration = TimeSpan.Zero;
        if (r.MaxMinutes > 0)
        {
            int min = Math.Max(0, Math.Min(r.MinMinutes, r.MaxMinutes)), max = Math.Max(r.MinMinutes, r.MaxMinutes);
            duration = TimeSpan.FromMinutes(min + Math.Floor(Unit(r.Id, day, 2) * (max - min + 1)));
        }
        return new RoutineOccurrence(r, day, start, windowEnd, duration);
    }

    /// <summary>
    /// Occurrences à lancer maintenant : heure tirée atteinte, plage pas encore terminée, pas déjà jouée.
    /// Si V-Max démarre en cours de plage après l'heure tirée, l'occurrence est rattrapée ; après la plage, elle est sautée.
    /// </summary>
    public static List<RoutineOccurrence> Due(IEnumerable<LifeRoutine> routines, DateTime now, Func<string, bool> alreadyPlayed)
    {
        var due = new List<RoutineOccurrence>();
        foreach (var r in routines)
            foreach (var day in new[] { now.Date.AddDays(-1), now.Date })
                if (Occurrence(r, day) is { } o && now >= o.Start && now < o.WindowEnd && !alreadyPlayed(o.Key))
                    due.Add(o);
        return due.OrderBy(o => o.Start).ToList();
    }

    /// <summary>
    /// Prochaine occurrence à venir (pour l'affichage « aujourd'hui vers 12h37 »)
    /// </summary>
    public static RoutineOccurrence? Next(LifeRoutine r, DateTime now, Func<string, bool> alreadyPlayed)
    {
        for (int i = -1; i < 8; i++)
            if (Occurrence(r, now.Date.AddDays(i)) is { } o && o.WindowEnd > now && !alreadyPlayed(o.Key))
                return o;
        return null;
    }

    /// <summary>Nombre pseudo-aléatoire stable dans [0, 1[ pour (routine, jour, canal)</summary>
    internal static double Unit(string id, DateTime day, int salt)
    {
        // FNV-1a 64 bits : stable d'un lancement à l'autre (string.GetHashCode ne l'est pas)
        ulong h = 14695981039346656037UL;
        foreach (char ch in id + "|" + day.ToString("yyyyMMdd") + "|" + salt)
        {
            h ^= ch;
            h *= 1099511628211UL;
        }
        // mélange final (splitmix64) pour bien répartir les bits
        h ^= h >> 30; h *= 0xBF58476D1CE4E5B9UL;
        h ^= h >> 27; h *= 0x94D049BB133111EBUL;
        h ^= h >> 31;
        return (h >> 11) / (double)(1UL << 53);
    }
}

/// <summary>
/// Stockage des routines : %APPDATA%\V-Max\routines.json (indépendant du décor : les lieux sont des noms de pièces)
/// </summary>
public sealed class RoutineBook
{
    public int Version { get; set; } = 1;
    public bool Enabled { get; set; }
    public List<LifeRoutine> Routines { get; set; } = new();
    /// <summary>Occurrences déjà jouées (clé routine@jour), purgées après quelques jours</summary>
    public List<string> Played { get; set; } = new();

    public static string PathOf => System.IO.Path.Combine(ExtensionValue.DataDirectory, "routines.json");

    public static RoutineBook Load()
    {
        try
        {
            if (File.Exists(PathOf))
                return JsonSerializer.Deserialize<RoutineBook>(File.ReadAllText(PathOf), HabitatMap.Json) ?? Default();
        }
        catch { }
        return Default();
    }

    public void Save()
    {
        // on ne garde que la dernière semaine de l'historique
        var limit = DateTime.Now.Date.AddDays(-7).ToString("yyyyMMdd");
        Played.RemoveAll(k => k.Length < 9 || string.CompareOrdinal(k[(k.IndexOf('@') + 1)..], limit) < 0);
        Directory.CreateDirectory(ExtensionValue.DataDirectory);
        var tmp = PathOf + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(this, HabitatMap.Json));
        File.Move(tmp, PathOf, overwrite: true);
    }

    public bool WasPlayed(string key) => Played.Contains(key);

    /// <summary>Une journée type pour commencer (modifiable)</summary>
    public static RoutineBook Default() => new()
    {
        Routines =
        {
            new LifeRoutine { Action = "eat", Place = "Cuisine", From = "07:30", To = "09:00" },
            new LifeRoutine { Action = "eat", Place = "Cuisine", From = "12:00", To = "14:00" },
            new LifeRoutine { Action = "relax", Place = "Salon", From = "16:00", To = "18:00", MinMinutes = 20, MaxMinutes = 45 },
            new LifeRoutine { Action = "eat", Place = "Cuisine", From = "19:00", To = "20:30" },
            new LifeRoutine { Action = "sleep", Place = "Chambre", From = "23:00", To = "01:30", MinMinutes = 420, MaxMinutes = 510 },
        },
    };
}
