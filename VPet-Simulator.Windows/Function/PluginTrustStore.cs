using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using VPet_Simulator.Windows.Interface;

namespace VPet_Simulator.Windows;

/// <summary>
/// V-Max : approbations des plugins de code (DLL).
/// <list type="bullet">
/// <item>Une DLL ne se charge que si son empreinte SHA-256 a été approuvée explicitement par l'utilisateur.</item>
/// <item>Si le fichier change (mise à jour, remplacement), l'approbation ne vaut plus : nouvelle demande.</item>
/// <item>Les approbations sont stockées dans %LOCALAPPDATA%\V-Max\plugin-approvals.json, hors de Setting.lps,
///       donc hors de portée de l'API <c>ISetting</c> utilisée par les plugins.</item>
/// </list>
/// Remplace la confiance automatique de VPet (certificat extrait sans vérification de signature,
/// confiance accordée à n'importe quel client de certaines autorités de certification) — AUDIT S-01 à S-04.
/// </summary>
public static class PluginTrustStore
{
    private static readonly object Lock = new();
    private static Dictionary<string, Dictionary<string, string>>? approvals;

    private static string StorePath
    {
        get
        {
            string dir = ExtensionValue.IsPortable ? ExtensionValue.DataDirectory
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "V-Max");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "plugin-approvals.json");
        }
    }

    private static Dictionary<string, Dictionary<string, string>> Load()
    {
        if (approvals != null)
            return approvals;
        try
        {
            if (File.Exists(StorePath))
                approvals = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(File.ReadAllText(StorePath));
        }
        catch (Exception e)
        {
            Trace.TraceWarning("plugin-approvals.json illisible : " + e.Message);
        }
        approvals ??= new(StringComparer.OrdinalIgnoreCase);
        approvals = new(approvals, StringComparer.OrdinalIgnoreCase);
        return approvals;
    }

    private static void Save()
    {
        try
        {
            string tmp = StorePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(approvals, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(tmp, StorePath, true);
        }
        catch (Exception e)
        {
            Trace.TraceWarning("Impossible d'enregistrer les approbations de plugins : " + e.Message);
        }
    }

    /// <summary>
    /// Empreinte SHA-256 (hexadécimal) d'un fichier
    /// </summary>
    public static string ComputeHash(string file)
    {
        using var stream = File.OpenRead(file);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    /// <summary>
    /// La DLL de ce mod est-elle approuvée, avec ce contenu exact ?
    /// </summary>
    public static bool IsApproved(string modName, FileInfo dll)
    {
        lock (Lock)
        {
            if (!Load().TryGetValue(modName, out var dlls) || !dlls.TryGetValue(dll.Name, out var hash))
                return false;
            try
            {
                return string.Equals(hash, ComputeHash(dll.FullName), StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>
    /// DLL du dossier plugin d'un mod (toutes architectures confondues)
    /// </summary>
    public static IEnumerable<FileInfo> PluginDlls(DirectoryInfo modDirectory)
    {
        var plugin = new DirectoryInfo(Path.Combine(modDirectory.FullName, "plugin"));
        return plugin.Exists ? plugin.EnumerateFiles("*.dll") : Enumerable.Empty<FileInfo>();
    }

    /// <summary>
    /// Toutes les DLL du mod sont-elles approuvées dans leur version actuelle ?
    /// </summary>
    public static bool IsModApproved(string modName, DirectoryInfo modDirectory)
    {
        var dlls = PluginDlls(modDirectory).ToList();
        return dlls.Count > 0 && dlls.All(d => IsApproved(modName, d));
    }

    /// <summary>
    /// Approuve toutes les DLL actuelles du mod (empreintes enregistrées)
    /// </summary>
    public static void ApproveMod(string modName, DirectoryInfo modDirectory)
    {
        lock (Lock)
        {
            var store = Load();
            var dlls = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var dll in PluginDlls(modDirectory))
                dlls[dll.Name] = ComputeHash(dll.FullName);
            store[modName] = dlls;
            Save();
        }
    }

    /// <summary>
    /// Retire l'approbation d'un mod
    /// </summary>
    public static void RevokeMod(string modName)
    {
        lock (Lock)
        {
            if (Load().Remove(modName))
                Save();
        }
    }
}
