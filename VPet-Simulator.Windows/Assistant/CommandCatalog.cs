namespace VPet_Simulator.Windows.Assistant;

/// <summary>
/// Source UNIQUE des commandes de l'assistant déterministe, regroupées par thème avec des exemples de formulation.
/// Alimente à la fois les suggestions du chat et le panneau « Commandes » (la documentation que Maxine propose
/// quand elle ne comprend pas). Ajoute un exemple ici → il apparaît partout.
/// </summary>
public static class CommandCatalog
{
    public sealed record Group(string Icon, string Title, string[] Examples);

    public static readonly Group[] Groups =
    {
        new("🚀", "Applications & sites", new[]
        {
            "ouvre Spotify", "lance Discord", "démarre Steam", "va sur youtube", "ouvre le lanceur", "mes applications",
        }),
        new("🔎", "Recherche web", new[]
        {
            "cherche une recette de pâtes", "google la météo de demain",
        }),
        new("⏱", "Heure, minuteurs & rappels", new[]
        {
            "quelle heure il est", "quel jour on est", "minuteur de 10 minutes",
            "rappelle-moi dans 20 minutes de boire", "mes minuteurs",
        }),
        new("🔊", "Son & volume", new[]
        {
            "mets le son à 30%", "baisse le son", "monte le son", "coupe le son", "le son à fond",
        }),
        new("📊", "PC & performances", new[]
        {
            "comment va mon PC", "la température", "espace disque", "qu'est-ce qui rame", "mes fenêtres ouvertes",
        }),
        new("🖥", "Système & capture", new[]
        {
            "verrouille l'écran", "capture d'écran", "mes captures",
        }),
        new("📋", "Mémo & notes", new[]
        {
            "ce que j'ai copié", "prends une note",
        }),
        new("💼", "Carrière de Maxine", new[]
        {
            "ma carrière", "mes métiers", "ma voie",
        }),
    };
}
