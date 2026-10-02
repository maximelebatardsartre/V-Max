using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace VPet_Simulator.Windows.Voice;

/// <summary>
/// V-Max voix : touche à maintenir pour parler (n'importe où dans Windows). Crochet clavier bas niveau :
/// on reçoit l'appui et le relâchement d'une seule touche, qui n'est pas transmise aux autres applications.
/// </summary>
public sealed class PushToTalkKey : IDisposable
{
    /// <summary>Touches proposées : code virtuel et libellé</summary>
    public static readonly (int vk, string label)[] Choices =
    [
        (0xDE, "² (au-dessus de Tab)"),   // VK_OEM_7 sur clavier AZERTY
        (0x77, "F8"),
        (0x13, "Pause"),
        (0x91, "Arrêt défil"),
        (0xA3, "Ctrl droite"),
    ];

    private readonly LowLevelKeyboardProc proc;
    private IntPtr hook;
    private bool down;

    public int VirtualKey { get; set; }
    public event Action? Pressed;
    public event Action? Released;

    public PushToTalkKey(int vk)
    {
        VirtualKey = vk;
        proc = Callback; // garder une référence : le délégué est appelé par Windows
    }

    public bool Enabled => hook != IntPtr.Zero;

    public void Enable()
    {
        if (hook != IntPtr.Zero)
            return;
        using var p = Process.GetCurrentProcess();
        using var m = p.MainModule!;
        hook = SetWindowsHookEx(13 /* WH_KEYBOARD_LL */, proc, GetModuleHandle(m.ModuleName), 0);
    }

    public void Disable()
    {
        if (hook != IntPtr.Zero)
            UnhookWindowsHookEx(hook);
        hook = IntPtr.Zero;
        if (down)
        {
            down = false;
            Released?.Invoke();
        }
    }

    private IntPtr Callback(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0)
        {
            var info = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            if (info.vkCode == VirtualKey && (info.flags & 0x10 /* LLKHF_INJECTED */) == 0)
            {
                int msg = wParam.ToInt32();
                bool isDown = msg is 0x0100 or 0x0104; // WM_KEYDOWN, WM_SYSKEYDOWN
                bool isUp = msg is 0x0101 or 0x0105;
                if (isDown && !down)
                {
                    down = true;
                    Pressed?.Invoke();
                }
                else if (isUp && down)
                {
                    down = false;
                    Released?.Invoke();
                }
                return new IntPtr(1); // la touche reste à V-Max pendant que l'option est active
            }
        }
        return CallNextHookEx(hook, code, wParam, lParam);
    }

    public void Dispose() => Disable();

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);
    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT { public int vkCode, scanCode, flags, time; public IntPtr dwExtraInfo; }
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc fn, IntPtr hMod, uint threadId);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hhk);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string name);
}
