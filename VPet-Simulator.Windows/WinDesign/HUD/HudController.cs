using LinePutScript.Localization.WPF;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using VPet_Simulator.Core;
using VPet_Simulator.Windows.Interface;
using static VPet_Simulator.Core.GraphHelper;

namespace VPet_Simulator.Windows.HUD;

/// <summary>
/// V-Max HUD : point d'entrée de l'interface autour du compagnon.
/// Crée les surcouches à la demande et relie l'anneau orbital aux fonctions existantes
/// (la barre de VPet reste le modèle des menus, y compris les entrées des plugins).
/// </summary>
public sealed class HudController
{
    private readonly MainWindow mw;
    private OrbitDock? orbit;
    private ChatPanel? chat;
    /// <summary>QA : fenêtre de l'anneau</summary>
    public Window? OrbitWindow => orbit;
    /// <summary>QA : panneau de discussion</summary>
    public Window? ChatWindow => chat;

    public HudController(MainWindow mw)
    {
        this.mw = mw;
        if (mw.Main.ToolBar != null)
            mw.Main.ToolBar.ShowOverride = () =>
            {
                ToggleOrbit();
                return true;
            };
        RegisterChatHotkey();
    }

    #region Raccourci global Ctrl+Alt+Espace : ouvre la discussion depuis n'importe où
    private const int HotkeyId = 0x564D; // « VM »
    private const int WM_HOTKEY = 0x0312;
    private const uint MOD_ALT = 0x1, MOD_CONTROL = 0x2, MOD_NOREPEAT = 0x4000, VK_SPACE = 0x20;
    [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    /// <summary>Faux si le raccourci est déjà pris par une autre application</summary>
    public bool HotkeyRegistered { get; private set; }

    private void RegisterChatHotkey()
    {
        var handle = new WindowInteropHelper(mw).Handle;
        if (handle == IntPtr.Zero)
            return;
        HotkeyRegistered = RegisterHotKey(handle, HotkeyId, MOD_CONTROL | MOD_ALT | MOD_NOREPEAT, VK_SPACE);
        HwndSource.FromHwnd(handle)?.AddHook((IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled) =>
        {
            if (msg == WM_HOTKEY && wParam.ToInt32() == HotkeyId)
            {
                OpenChat();
                handled = true;
            }
            return IntPtr.Zero;
        });
        mw.Closed += (_, _) => UnregisterHotKey(handle, HotkeyId);
    }
    #endregion

    public void ToggleOrbit()
    {
        orbit ??= new OrbitDock(mw);
        orbit.Toggle(Actions());
    }

    private IEnumerable<OrbitAction> Actions()
    {
        yield return new OrbitAction("Discuter", "", OpenChat, Accent: true);
        yield return new OrbitAction("Nourrir", "", OpenPantry);
        yield return new OrbitAction("Occupations", "", OpenActivities);
        yield return new OrbitAction(mw.Main.State == Main.WorkingState.Sleep ? "Réveiller" : "Dormir", "", ToggleSleep);
        yield return new OrbitAction("État", "", OpenStatus);
        yield return new OrbitAction("Plus", "", OpenMore);
        yield return new OrbitAction("Paramètres", "", () => mw.ShowSetting());
    }

    #region Actions (les panneaux V-Max remplacent progressivement les anciennes fenêtres)
    public void OpenChat()
    {
        if (mw.AgentPlugin == null)
        {
            LegacyMenu(mw.Main.ToolBar?.MenuInteract);
            return;
        }
        chat ??= new ChatPanel(mw, mw.AgentPlugin.Orchestrator);
        chat.ToggleOpen();
    }
    public void OpenPantry() => mw.ShowBetterBuy(Food.FoodType.Meal);
    public void OpenActivities() => mw.ShowWorkMenu(Work.WorkType.Work);
    public void OpenStatus() => mw.MWController.ShowPanel();
    public void OpenMore() => LegacyMenu(mw.Main.ToolBar?.MenuSetting);

    /// <summary>
    /// Dormir / se réveiller (même logique que l'entrée « Dormir » de VPet)
    /// </summary>
    public void ToggleSleep()
    {
        var sleep = mw.Main.ToolBar?.MenuInteract.Items.OfType<MenuItem>().FirstOrDefault(m => (m.Header as string) == "睡觉".Translate());
        sleep?.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
    }

    private void LegacyMenu(MenuItem? menu)
    {
        if (menu == null)
            return;
        mw.Main.ToolBar!.ShowOverride = null;
        mw.Main.ToolBar.Show();
        mw.Main.ToolBar.ShowOverride = () => { ToggleOrbit(); return true; };
        menu.IsSubmenuOpen = true;
    }
    #endregion
}
