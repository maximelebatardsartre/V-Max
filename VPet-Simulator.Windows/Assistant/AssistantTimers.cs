using System;
using System.Collections.Generic;
using System.Linq;

namespace VPet_Simulator.Windows.Assistant;

/// <summary>
/// Registre des minuteurs et rappels ACTIFS (partagé entre le routeur de commandes et le panneau « Minuteurs »).
/// Possède les timers réels ; notifie <see cref="Changed"/> quand la liste évolue (pour rafraîchir l'affichage).
/// </summary>
public static class AssistantTimers
{
    public sealed class Entry
    {
        public string Id = "";
        public string Label = "";
        public bool IsReminder;
        public DateTime EndUtc;
        internal System.Timers.Timer? Timer;
        public TimeSpan Remaining => EndUtc - DateTime.UtcNow;
    }

    private static readonly List<Entry> list = new();
    public static event Action? Changed;

    public static IReadOnlyList<Entry> Active
    {
        get { lock (list) return list.OrderBy(e => e.EndUtc).ToList(); }
    }

    /// <summary>Crée un minuteur/rappel. <paramref name="onElapsed"/> est appelé à l'échéance (sur un thread de fond).</summary>
    public static string Add(string label, double seconds, bool isReminder, Action onElapsed)
    {
        var e = new Entry
        {
            Id = Guid.NewGuid().ToString("N")[..6],
            Label = label,
            IsReminder = isReminder,
            EndUtc = DateTime.UtcNow.AddSeconds(seconds),
        };
        e.Timer = new System.Timers.Timer(Math.Max(1, seconds) * 1000) { AutoReset = false };
        e.Timer.Elapsed += (_, _) =>
        {
            Drop(e.Id);
            try { onElapsed(); } catch { }
        };
        lock (list) list.Add(e);
        e.Timer.Start();
        Changed?.Invoke();
        return e.Id;
    }

    public static void Cancel(string id)
    {
        Entry? e;
        lock (list) { e = list.FirstOrDefault(x => x.Id == id); if (e != null) list.Remove(e); }
        if (e != null) { try { e.Timer?.Stop(); e.Timer?.Dispose(); } catch { } Changed?.Invoke(); }
    }

    public static int CancelAll()
    {
        List<Entry> copy;
        lock (list) { copy = list.ToList(); list.Clear(); }
        foreach (var e in copy) { try { e.Timer?.Stop(); e.Timer?.Dispose(); } catch { } }
        if (copy.Count > 0) Changed?.Invoke();
        return copy.Count;
    }

    private static void Drop(string id)
    {
        lock (list) list.RemoveAll(x => x.Id == id);
        Changed?.Invoke();
    }
}
