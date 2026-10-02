namespace VPet_Simulator.Windows;

/// <summary>
/// V-Max : conversions d'unités affichées à l'écran (reprend les seuils et formules de l'ancien panneau du compagnon).
/// </summary>
public static class Units
{
    /// <summary>Distance parcourue (pixels à 96 ppp) en texte lisible, unité comprise : « 12,3 m »</summary>
    public static string PxToDistance(long px) => PxToDistance(px, out string unit) + " " + unit;

    /// <summary>Distance parcourue (pixels à 96 ppp) : la valeur formatée, et l'unité à part (px, cm, m ou km)</summary>
    public static string PxToDistance(long px, out string unit)
    {
        if (px < 37795)
        {
            unit = "px";
            return px.ToString();
        }
        else if (px < 3779527)
        {
            unit = "cm";
            return (px * 2.54 / 96).ToString("f1");
        }
        else if (px < 377952755)
        {
            unit = "m";
            return (px * 2.54 / 9600).ToString("f1");
        }
        else
        {
            unit = "km";
            return (px * 2.54 / 9600000).ToString("f1");
        }
    }
}
