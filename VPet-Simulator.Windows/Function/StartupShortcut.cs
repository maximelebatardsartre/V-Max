using LinePutScript.Localization.WPF;
using System;
using System.IO;
using System.Windows;
using VPet_Simulator.Windows.Interface;
using static VPet_Simulator.Windows.Win32;
using VPet_Simulator.Core;

namespace VPet_Simulator.Windows;

/// <summary>
/// V-Max : raccourci « Lancer avec Windows » (dossier Démarrage de l'utilisateur, sans registre)
/// </summary>
public static class StartupShortcut
{
    public static string ShortcutPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), "V-Max.lnk");

    /// <summary>
    /// Crée ou supprime le raccourci selon <c>Set.StartUPBoot</c>
    /// </summary>
    public static void Apply(MainWindow mw)
    {
        mw.Set["v"][(LinePutScript.gbol)"newverstartup"] = true;
        var path = ShortcutPath;
        // ancien raccourci de VPet
        var legacy = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), "VPET_Simulator.lnk");
        if (File.Exists(legacy))
            File.Delete(legacy);
        if (File.Exists(path))
            File.Delete(path);
        if (!mw.Set.StartUPBoot)
            return;
        var link = (IShellLink)new ShellLink();
        link.SetPath(System.Reflection.Assembly.GetExecutingAssembly().Location.Replace(".dll", ".exe"));
        link.SetDescription("V-Max");
        link.SetIconLocation(Path.Combine(ExtensionValue.BaseDirectory, "maxine.ico"), 0);
        try
        {
            ((IPersistFile)link).Save(path, false);
        }
        catch
        {
            VDialog.Show("创建快捷方式失败,权限不足\n请以管理员身份运行后重试".Translate(), "权限不足".Translate());
        }
    }
}
