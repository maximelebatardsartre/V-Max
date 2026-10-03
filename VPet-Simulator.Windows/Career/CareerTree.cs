using System.Collections.Generic;
using System.Linq;

namespace VPet_Simulator.Windows.Career;

/// <summary>Un métier est de type Travail (gagne de l'argent) ou Études (gagne de l'XP/savoir, progression linéaire).</summary>
public enum CareerKind { Work, Study }

/// <summary>Un palier d'un métier : titre/statut débloqué, seuil d'XP de métier (cumulé) et bonus de salaire (fraction).</summary>
public sealed record CareerTier(string Title, double XpRequired, double SalaryBonus);

/// <summary>
/// Un métier (ou sous-métier) : une voie de carrière avec sa PROPRE barre d'XP, regroupée dans une « famille »
/// (= la voie que l'utilisateur choisit, qui propose ses métiers). Les activités qui le composent sont déclarées
/// dans <see cref="ActivityCatalog"/> (clé = nom de l'activité, car le graphe d'animation n'est pas unique).
/// </summary>
public sealed record CareerTrack(string Id, string Family, string Name, string Icon, CareerKind Kind, CareerTier[] Tiers)
{
    public double MaxXp => Tiers[^1].XpRequired;
}

/// <summary>
/// V-Max : l'ARBRE DES MÉTIERS. Familles (= voies) → métiers → paliers (titres + salaire). Le Travail propose
/// plusieurs métiers par voie ; les Études sont une progression LINÉAIRE (un seul parcours). Les loisirs et les
/// activités d'ambiance ne sont pas ici (voir <see cref="ActivityCatalog"/>).
/// </summary>
public static class CareerTree
{
    public static readonly CareerTrack[] Tracks =
    {
        // ───── Voie NUMÉRIQUE ─────
        new("redaction", "Numérique", "Rédaction", "✍️", CareerKind.Work, new[]
        {
            new CareerTier("Pigiste", 0, 0.00), new CareerTier("Rédactrice", 300, 0.10),
            new CareerTier("Rédac' en chef", 900, 0.25), new CareerTier("Plume d'or", 2000, 0.50),
        }),
        new("stream", "Numérique", "Streaming & création", "🎥", CareerKind.Work, new[]
        {
            new CareerTier("Débutante", 0, 0.00), new CareerTier("Streameuse", 350, 0.15),
            new CareerTier("Créatrice", 1000, 0.30), new CareerTier("Star du live", 2200, 0.60),
        }),
        new("dev", "Numérique", "Développement", "💻", CareerKind.Work, new[]
        {
            new CareerTier("Dev junior", 0, 0.00), new CareerTier("Développeuse", 400, 0.15),
            new CareerTier("Lead dev", 1200, 0.35),
        }),
        new("maintenance", "Numérique", "Maintenance info", "🛠️", CareerKind.Work, new[]
        {
            new CareerTier("Technicienne", 0, 0.00), new CareerTier("Dépanneuse", 300, 0.10),
            new CareerTier("Experte matériel", 800, 0.25), new CareerTier("Ingénieure", 1800, 0.50),
        }),

        // ───── Voie RESTAURATION ─────
        new("cuisine", "Restauration", "Cuisine", "🍳", CareerKind.Work, new[]
        {
            new CareerTier("Commis", 0, 0.00), new CareerTier("Cuisinière", 300, 0.10),
            new CareerTier("Cheffe", 900, 0.30), new CareerTier("Cheffe étoilée", 2000, 0.55),
        }),

        // ───── Voie INDÉPENDANTE (freelance / jobs d'appoint) ─────
        new("freelance", "Indépendante", "Travail à distance", "🏠", CareerKind.Work, new[]
        {
            new CareerTier("Petites missions", 0, 0.00), new CareerTier("Missions régulières", 400, 0.10),
            new CareerTier("Gros contrats", 1200, 0.30), new CareerTier("Contrats premium", 2800, 0.60),
        }),
        new("appoint", "Indépendante", "Jobs d'appoint", "💶", CareerKind.Work, new[]
        {
            new CareerTier("Saisonnière", 0, 0.00), new CareerTier("Multi-jobs", 300, 0.10),
            new CareerTier("Débrouillarde", 900, 0.25),
        }),

        // ───── Voie ÉTUDES (progression LINÉAIRE, un seul parcours) ─────
        new("etudes", "Études", "Parcours scolaire", "🎓", CareerKind.Study, new[]
        {
            new CareerTier("Écolière", 0, 0.00), new CareerTier("Lycéenne", 250, 0.00),
            new CareerTier("Étudiante", 600, 0.00), new CareerTier("En licence", 1100, 0.00),
            new CareerTier("En master", 1800, 0.00), new CareerTier("Doctorante", 2800, 0.00),
        }),
    };

    public static CareerTrack? Track(string? id) => id == null ? null : Tracks.FirstOrDefault(t => t.Id == id);

    /// <summary>Les voies (familles), dans l'ordre d'apparition.</summary>
    public static IEnumerable<string> Families => Tracks.Select(t => t.Family).Distinct();
    public static IEnumerable<CareerTrack> InFamily(string family) => Tracks.Where(t => t.Family == family);
}
