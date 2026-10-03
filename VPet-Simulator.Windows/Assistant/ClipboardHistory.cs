using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Threading;

namespace VPet_Simulator.Windows.Assistant;

/// <summary>
/// Historique du presse-papiers : mémorise les derniers textes copiés pour qu'on puisse les recoller plus tard.
/// Interroge le presse-papiers à faible fréquence sur le thread UI (pas de hook Win32 à maintenir).
/// </summary>
public static class ClipboardHistory
{
    public const int Max = 30;
    private static readonly List<string> items = new();
    private static DispatcherTimer? timer;
    private static string last = "";

    /// <summary>Nouveau texte copié (pour un éventuel retour visuel « j'ai retenu »).</summary>
    public static event Action<string>? Captured;
    /// <summary>La liste a changé (pour rafraîchir le panneau).</summary>
    public static event Action? Changed;

    public static IReadOnlyList<string> Items { get { lock (items) return items.ToList(); } }

    public static void Start()
    {
        if (timer != null)
            return;
        try { last = Clipboard.ContainsText() ? Clipboard.GetText() : ""; } catch { }
        timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
        timer.Tick += (_, _) => Poll();
        timer.Start();
    }

    private static void Poll()
    {
        string cur;
        try { if (!Clipboard.ContainsText()) return; cur = Clipboard.GetText(); } catch { return; }
        if (string.IsNullOrWhiteSpace(cur) || cur == last)
            return;
        last = cur;
        lock (items)
        {
            items.RemoveAll(x => x == cur);
            items.Insert(0, cur);
            while (items.Count > Max) items.RemoveAt(items.Count - 1);
        }
        Changed?.Invoke();
        try { Captured?.Invoke(cur); } catch { }
    }

    /// <summary>Remet un texte de l'historique dans le presse-papiers.</summary>
    public static void CopyBack(string text)
    {
        try { Clipboard.SetText(text); last = text; } catch { }
    }

    public static void Clear()
    {
        lock (items) items.Clear();
        Changed?.Invoke();
    }
}
