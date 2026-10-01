using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Threading;
using VPet_Simulator.Core;

namespace VPet_Simulator.Windows;

/// <summary>
/// V-Max : met les animations en pause quand personne ne peut voir le compagnon
/// (session verrouillée, écran éteint, application plein écran / jeu / présentation).
/// Les fenêtres masquées s'enregistrent via <see cref="SetWindowHidden"/>.
/// La logique de jeu (faim, humeur…) continue de tourner normalement.
/// </summary>
public static class ActivityGateService
{
    private const string ReasonLocked = "session-verrouillee";
    private const string ReasonDisplayOff = "ecran-eteint";
    private const string ReasonFullScreen = "plein-ecran";

    private static bool started;
    private static HwndSource? messageWindow;
    private static IntPtr powerNotification;
    private static DispatcherTimer? fullScreenTimer;

    /// <summary>
    /// Démarre la surveillance (idempotent). À appeler depuis le thread UI.
    /// </summary>
    public static void EnsureStarted(Dispatcher dispatcher)
    {
        if (started)
            return;
        started = true;
        try
        {
            SystemEvents.SessionSwitch += SystemEvents_SessionSwitch;

            // Fenêtre de messages invisible pour recevoir l'état de l'écran (allumé / éteint / atténué)
            var p = new HwndSourceParameters("VMaxActivityGate") { Width = 0, Height = 0, WindowStyle = 0, ParentWindow = new IntPtr(-3) /* HWND_MESSAGE */ };
            messageWindow = new HwndSource(p);
            messageWindow.AddHook(WndProc);
            var guid = GUID_CONSOLE_DISPLAY_STATE;
            powerNotification = RegisterPowerSettingNotification(messageWindow.Handle, ref guid, DEVICE_NOTIFY_WINDOW_HANDLE);

            // Plein écran : interrogation légère toutes les 5 s (pas d'événement système disponible)
            fullScreenTimer = new DispatcherTimer(TimeSpan.FromSeconds(5), DispatcherPriority.Background, (_, _) => CheckFullScreen(), dispatcher);
            fullScreenTimer.Start();
            dispatcher.ShutdownStarted += (_, _) => Stop();
        }
        catch (Exception e)
        {
            Trace.TraceWarning("ActivityGateService: " + e.Message);
        }
    }

    /// <summary>
    /// Signale qu'une fenêtre de compagnon est masquée ou visible
    /// </summary>
    public static void SetWindowHidden(string windowKey, bool hidden) => AnimationGate.Set("masque:" + windowKey, hidden);

    private static void Stop()
    {
        SystemEvents.SessionSwitch -= SystemEvents_SessionSwitch;
        fullScreenTimer?.Stop();
        if (powerNotification != IntPtr.Zero)
            UnregisterPowerSettingNotification(powerNotification);
        messageWindow?.Dispose();
    }

    private static void SystemEvents_SessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        switch (e.Reason)
        {
            case SessionSwitchReason.SessionLock:
            case SessionSwitchReason.ConsoleDisconnect:
            case SessionSwitchReason.RemoteDisconnect:
                AnimationGate.Set(ReasonLocked, true);
                break;
            case SessionSwitchReason.SessionUnlock:
            case SessionSwitchReason.ConsoleConnect:
            case SessionSwitchReason.RemoteConnect:
                AnimationGate.Set(ReasonLocked, false);
                break;
        }
    }

    private static void CheckFullScreen()
    {
        bool fullScreen = false;
        if (SHQueryUserNotificationState(out int state) == 0 && state is QUNS_BUSY or QUNS_RUNNING_D3D_FULL_SCREEN or QUNS_PRESENTATION_MODE)
            fullScreen = ForegroundCoversPetMonitor();
        AnimationGate.Set(ReasonFullScreen, fullScreen);
    }

    /// <summary>
    /// La fenêtre au premier plan (d'un autre processus) couvre-t-elle entièrement un écran où se trouve un compagnon ?
    /// (Avec plusieurs écrans, un jeu ou une vidéo plein écran ailleurs ne doit pas figer le compagnon.)
    /// </summary>
    private static bool ForegroundCoversPetMonitor()
    {
        var fg = GetForegroundWindow();
        if (fg == IntPtr.Zero || fg == GetShellWindow())
            return false;
        GetWindowThreadProcessId(fg, out uint pid);
        if (pid == (uint)Environment.ProcessId)
            return false;
        if (!GetWindowRect(fg, out RECT r))
            return false;
        var fgMonitor = MonitorFromWindow(fg, MONITOR_DEFAULTTONEAREST);
        var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(fgMonitor, ref mi))
            return false;
        bool covers = r.Left <= mi.rcMonitor.Left && r.Top <= mi.rcMonitor.Top && r.Right >= mi.rcMonitor.Right && r.Bottom >= mi.rcMonitor.Bottom;
        if (!covers)
            return false;
        foreach (var w in App.MainWindows)
        {
            var h = new WindowInteropHelper(w).Handle;
            if (h != IntPtr.Zero && MonitorFromWindow(h, MONITOR_DEFAULTTONEAREST) == fgMonitor)
                return true;
        }
        return false;
    }

    private static IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_POWERBROADCAST && wParam.ToInt32() == PBT_POWERSETTINGCHANGE && lParam != IntPtr.Zero)
        {
            var setting = Marshal.PtrToStructure<POWERBROADCAST_SETTING>(lParam);
            if (setting.PowerSetting == GUID_CONSOLE_DISPLAY_STATE)
            {
                // 0 = éteint, 1 = allumé, 2 = atténué
                AnimationGate.Set(ReasonDisplayOff, setting.Data == 0);
            }
        }
        return IntPtr.Zero;
    }

    #region Win32
    private const int WM_POWERBROADCAST = 0x0218;
    private const int PBT_POWERSETTINGCHANGE = 0x8013;
    private const int DEVICE_NOTIFY_WINDOW_HANDLE = 0;
    private const int QUNS_BUSY = 2;
    private const int QUNS_RUNNING_D3D_FULL_SCREEN = 3;
    private const int QUNS_PRESENTATION_MODE = 4;
    private static Guid GUID_CONSOLE_DISPLAY_STATE = new("6FE69556-704A-47A0-8F24-C28D936FDA47");

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct POWERBROADCAST_SETTING
    {
        public Guid PowerSetting;
        public uint DataLength;
        public byte Data;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr RegisterPowerSettingNotification(IntPtr hRecipient, ref Guid PowerSettingGuid, int Flags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterPowerSettingNotification(IntPtr handle);

    [DllImport("shell32.dll")]
    private static extern int SHQueryUserNotificationState(out int pquns);

    private const uint MONITOR_DEFAULTTONEAREST = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO { public int cbSize; public RECT rcMonitor; public RECT rcWork; public uint dwFlags; }

    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern IntPtr GetShellWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
    [DllImport("user32.dll")] private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);
    #endregion
}
