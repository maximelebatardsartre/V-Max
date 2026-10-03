using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using VPet_Simulator.Windows.HUD;
using VPet_Simulator.Windows.Interface;
using static VPet_Simulator.Core.WorkTimer;
using Work = VPet_Simulator.Core.GraphHelper.Work;

namespace VPet_Simulator.Windows.Career;

/// <summary>
/// V-Max : progression de carrière de Maxine. Chaque MÉTIER a sa propre barre d'XP (indépendante : changer de job ne
/// réinitialise rien, on reprend où on en était). On choisit une VOIE (famille) qui propose ses métiers ; le JOB
/// ACTIF donne le titre et oriente l'autonomie. Paliers débloquables, salaire croissant, prime, salaire mensuel.
/// Persisté dans %APPDATA%\V-Max\careers.json. Alimenté par l'événement de fin de travail (aucun patch moteur).
/// </summary>
public sealed class CareerState
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private static string PathOf => Path.Combine(ExtensionValue.DataDirectory, "careers.json");

    public static CareerState I { get; private set; } = Load();

    // ----- état sérialisé -----
    public Dictionary<string, double> Xp { get; set; } = new();  // trackId → XP de métier
    public string? Voie { get; set; }                             // famille choisie (domaine)
    public string? ActiveJob { get; set; }                        // métier actif (donne le titre)
    public DateTime LastSalary { get; set; }
    public List<string> Celebrated { get; set; } = new();         // "trackId#tier" déjà fêtés

    public event Action? Changed;

    private static CareerState Load()
    {
        try
        {
            if (File.Exists(PathOf))
                return JsonSerializer.Deserialize<CareerState>(File.ReadAllText(PathOf), Json) ?? new CareerState();
        }
        catch { }
        return new CareerState();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(ExtensionValue.DataDirectory);
            var tmp = PathOf + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(this, Json));
            File.Move(tmp, PathOf, overwrite: true);
        }
        catch { }
    }

    // ----- lecture -----
    public double XpOf(string trackId) => Xp.TryGetValue(trackId, out var v) ? v : 0;

    public int TierIndex(CareerTrack t)
    {
        double x = XpOf(t.Id);
        int idx = 0;
        for (int i = 0; i < t.Tiers.Length; i++)
            if (x >= t.Tiers[i].XpRequired) idx = i;
        return idx;
    }

    public CareerTier CurrentTier(CareerTrack t) => t.Tiers[TierIndex(t)];
    public string TitleOf(CareerTrack t) => CurrentTier(t).Title;
    public CareerTier? NextTier(CareerTrack t)
    {
        int i = TierIndex(t);
        return i + 1 < t.Tiers.Length ? t.Tiers[i + 1] : null;
    }

    [JsonIgnore] public CareerTrack? ActiveJobTrack => CareerTree.Track(ActiveJob);
    [JsonIgnore] public string? CurrentTitle => ActiveJobTrack is { } t ? TitleOf(t) : null;

    /// <summary>Une activité-métier est-elle débloquée ? (hors métier = toujours libre)</summary>
    public bool IsUnlocked(Work w)
    {
        var track = ActivityCatalog.TrackOf(w);
        if (track == null) return true;
        return XpOf(track.Id) >= track.Tiers[Math.Min(ActivityCatalog.TierOf(w), track.Tiers.Length - 1)].XpRequired;
    }

    /// <summary>Choisit un métier précis comme job actif (et aligne la voie sur sa famille).</summary>
    public void ChooseJob(string trackId)
    {
        var t = CareerTree.Track(trackId);
        if (t == null) return;
        ActiveJob = trackId;
        Voie = t.Family;
        Save();
        Changed?.Invoke();
    }

    /// <summary>Choisit une voie (famille) : propose ses métiers et bascule le job actif sur le plus avancé.</summary>
    public void ChooseVoie(string family)
    {
        Voie = family;
        var best = CareerTree.InFamily(family).OrderByDescending(t => XpOf(t.Id)).FirstOrDefault();
        if (best != null && (ActiveJobTrack?.Family != family))
            ActiveJob = best.Id;
        Save();
        Changed?.Invoke();
    }

    // ----- progression -----
    public void CreditWork(FinishWorkInfo info, MainWindow mw)
    {
        var track = ActivityCatalog.TrackOf(info.work);
        if (track == null) { TryMonthly(mw); return; } // loisir / ambiance / inconnu : pas de carrière

        double raw = info.count;
        if (double.IsNaN(raw) || double.IsInfinity(raw)) raw = 0;   // garde-fou (jamais de NaN dans l'XP/le JSON)
        raw = Math.Abs(raw);

        int before = TierIndex(track);
        double gain = Math.Round(raw);                               // pas de « +1 » forcé → pas de farm start/stop
        Xp[track.Id] = XpOf(track.Id) + gain;
        int after = TierIndex(track);

        if (track.Kind == CareerKind.Work)
        {
            double bonus = Math.Round(raw * CurrentTier(track).SalaryBonus);
            if (bonus > 0) { try { mw.Core.Save!.Money += bonus; } catch { } }
        }
        for (int t = before + 1; t <= after; t++) Promote(track, t, mw); // chaque palier franchi est fêté + primé

        Save();
        Changed?.Invoke();
        TryMonthly(mw);
    }

    private void Promote(CareerTrack track, int tier, MainWindow mw)
    {
        string key = track.Id + "#" + tier;
        if (Celebrated.Contains(key)) return;
        Celebrated.Add(key);
        double prime = 150 * tier * (track.Kind == CareerKind.Work ? 1 : 0.2);
        if (prime > 0) { try { mw.Core.Save!.Money += prime; } catch { } }
        string title = track.Tiers[tier].Title;
        try
        {
            mw.Toast($"Promotion ! Maxine est désormais {title} ({track.Name})" + (prime > 0 ? $" — prime +{prime:0} 💰" : ""), HudToast.Kind.Success, 8);
            mw.Main.Say($"Promue {title} ! Merci {mw.Core.Save!.Name}, je monte les échelons 💪", "shining", true);
        }
        catch { }
    }

    public void TryMonthly(MainWindow mw)
    {
        if (ActiveJobTrack is not { } t) return;
        if (LastSalary == default) { LastSalary = DateTime.Now; Save(); return; }
        if ((DateTime.Now - LastSalary).TotalDays < 30) return;
        LastSalary = DateTime.Now;
        double salary = (TierIndex(t) + 1) * 200 * (t.Kind == CareerKind.Work ? 1 : 0.5);
        try { mw.Core.Save!.Money += salary; } catch { }
        Save();
        Changed?.Invoke();
        try { mw.Toast($"Salaire mensuel : +{salary:0} 💰 (métier : {t.Name}, {TitleOf(t)})", HudToast.Kind.Success, 8); } catch { }
    }
}
