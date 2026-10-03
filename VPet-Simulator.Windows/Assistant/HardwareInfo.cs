using System;
using System.IO;
using System.Management;

namespace VPet_Simulator.Windows.Assistant;

/// <summary>
/// Infos matérielles « au-delà » du CPU/RAM : température (WMI, sans pilote ni admin — dispo surtout sur portables),
/// nom de la carte graphique, espace disque, temps d'allumage. Tout est défensif (renvoie null/0 si indisponible).
/// </summary>
internal static class HardwareInfo
{
    private static string? gpuCache;

    /// <summary>Température (°C) via la zone thermique ACPI (WMI). Null si le PC ne l'expose pas (fréquent sur desktop).</summary>
    public static int? CpuTempC()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(@"root\WMI", "SELECT CurrentTemperature FROM MSAcpi_ThermalZoneTemperature");
            foreach (ManagementObject o in searcher.Get())
            {
                double tenthKelvin = Convert.ToDouble(o["CurrentTemperature"]);
                int c = (int)Math.Round(tenthKelvin / 10.0 - 273.15);
                if (c > 0 && c < 125)
                    return c;
            }
        }
        catch { }
        return null;
    }

    /// <summary>Nom de la carte graphique (mis en cache). Null si introuvable.</summary>
    public static string? GpuName()
    {
        if (gpuCache != null)
            return gpuCache.Length == 0 ? null : gpuCache;
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Name FROM Win32_VideoController");
            foreach (ManagementObject o in searcher.Get())
            {
                var name = o["Name"]?.ToString();
                if (!string.IsNullOrWhiteSpace(name))
                {
                    gpuCache = name.Trim();
                    return gpuCache;
                }
            }
        }
        catch { }
        gpuCache = "";
        return null;
    }

    /// <summary>Espace du disque système (Go utilisés, Go total).</summary>
    public static (double used, double total) SystemDiskGb()
    {
        try
        {
            var root = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows)) ?? "C:\\";
            var d = new DriveInfo(root);
            double total = d.TotalSize / 1073741824.0;
            double free = d.AvailableFreeSpace / 1073741824.0;
            return (total - free, total);
        }
        catch { return (0, 0); }
    }

    /// <summary>Durée depuis le démarrage de Windows, lisible (« 3 j 5 h », « 2 h 14 min », « 40 min »).</summary>
    public static string Uptime()
    {
        var ts = TimeSpan.FromMilliseconds(Environment.TickCount64);
        if (ts.TotalDays >= 1) return $"{(int)ts.TotalDays} j {ts.Hours} h";
        if (ts.TotalHours >= 1) return $"{(int)ts.TotalHours} h {ts.Minutes} min";
        return $"{ts.Minutes} min";
    }
}
