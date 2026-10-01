using Microsoft.Win32;
using System;

namespace VPet_Simulator.Windows;

/// <summary>
/// V-Max : intégration au thème de Windows (mode clair / sombre des applications).
/// </summary>
public static class SystemTheme
{
    /// <summary>
    /// Valeur du paramètre « theme » : suivre Windows
    /// </summary>
    public const string FollowSystem = "system";
    /// <summary>
    /// Valeur du paramètre « font » : police système
    /// </summary>
    public const string SystemFont = "system";

    private static Action? onChanged;
    private static bool watching;
    private static bool? lastDark;

    /// <summary>
    /// Windows est-il en mode sombre pour les applications ?
    /// </summary>
    public static bool IsAppsDarkMode()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int v && v == 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Appelle <paramref name="changed"/> quand le mode clair/sombre de Windows change (un seul abonnement)
    /// </summary>
    public static void EnsureWatching(Action changed)
    {
        onChanged = changed;
        lastDark ??= IsAppsDarkMode();
        if (watching)
            return;
        watching = true;
        SystemEvents.UserPreferenceChanged += (_, e) =>
        {
            if (e.Category != UserPreferenceCategory.General && e.Category != UserPreferenceCategory.Color)
                return;
            bool dark = IsAppsDarkMode();
            if (dark == lastDark)
                return;
            lastDark = dark;
            onChanged?.Invoke();
        };
    }
}
