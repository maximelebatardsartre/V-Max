using System;
using System.Linq;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using VPet_Simulator.Windows.Assistant;

namespace VPet_Simulator.Windows.HUD;

/// <summary>
/// V-Max HUD : moniteur de performances qui RESTE affiché et se met à jour en direct (processeur, mémoire, disque,
/// température, carte graphique, réseau, temps d'allumage). Inspiré du plugin « Performance Monitor » de VPet.
/// </summary>
public sealed class MonitorPanel : HudSidePanel
{
    private readonly DispatcherTimer timer;
    private readonly Row cpu, ram, disk, temp, net, gpu, up;

    // état pour les deltas (CPU et réseau se calculent entre deux mesures)
    private ulong pIdle, pKernel, pUser;
    private long pBytes;
    private DateTime pTime = DateTime.MinValue;

    public MonitorPanel(MainWindow pet) : base(pet, "PERFORMANCES", "Moniteur", 300)
    {
        var stack = new StackPanel();
        cpu = new Row(this, "Processeur"); stack.Children.Add(cpu.Element);
        temp = new Row(this, "Température", bar: false); stack.Children.Add(temp.Element);
        ram = new Row(this, "Mémoire"); stack.Children.Add(ram.Element);
        disk = new Row(this, "Disque"); stack.Children.Add(disk.Element);
        net = new Row(this, "Réseau", bar: false); stack.Children.Add(net.Element);
        gpu = new Row(this, "Carte graphique", bar: false); stack.Children.Add(gpu.Element);
        up = new Row(this, "Allumé depuis", bar: false); stack.Children.Add(up.Element);
        Body = stack;

        timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
        timer.Tick += (_, _) => Sample();
        IsVisibleChanged += (_, _) => { if (IsVisible) { Prime(); Sample(); timer.Start(); } else timer.Stop(); };
    }

    /// <summary>Première mesure de référence (pour les deltas), sans l'afficher.</summary>
    private void Prime()
    {
        if (GetSystemTimes(out var i, out var k, out var u)) { pIdle = FT(i); pKernel = FT(k); pUser = FT(u); }
        pBytes = TotalBytes();
        pTime = DateTime.UtcNow;
    }

    private void Sample()
    {
        // Processeur (delta depuis la dernière mesure)
        if (GetSystemTimes(out var i, out var k, out var u))
        {
            ulong di = FT(i) - pIdle, dk = FT(k) - pKernel, du = FT(u) - pUser;
            pIdle = FT(i); pKernel = FT(k); pUser = FT(u);
            ulong total = dk + du;
            int pct = total == 0 ? 0 : (int)Math.Clamp(100.0 * (total - di) / total, 0, 100);
            cpu.Set(pct + " %", pct);
        }

        // Température
        var t = HardwareInfo.CpuTempC();
        temp.Set(t.HasValue ? t + " °C" : "n/d", null);

        // Mémoire
        var m = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        if (GlobalMemoryStatusEx(ref m))
        {
            double tot = m.ullTotalPhys / 1073741824.0, used = (m.ullTotalPhys - m.ullAvailPhys) / 1073741824.0;
            ram.Set($"{used:0.0} / {tot:0.0} Go", (int)Math.Round(100.0 * used / Math.Max(0.1, tot)));
        }

        // Disque système
        var (du2, dt2) = HardwareInfo.SystemDiskGb();
        if (dt2 > 0)
            disk.Set($"{du2:0} / {dt2:0} Go", (int)Math.Round(100.0 * du2 / dt2));

        // Réseau (débit instantané, delta)
        long now = TotalBytes();
        double secs = Math.Max(0.5, (DateTime.UtcNow - pTime).TotalSeconds);
        double bps = Math.Max(0, (now - pBytes) / secs);
        pBytes = now; pTime = DateTime.UtcNow;
        net.Set(Speed(bps), null);

        // Carte graphique + uptime (changent peu : on peut rafraîchir à chaque tick, c'est peu coûteux)
        gpu.Set(HardwareInfo.GpuName() ?? "n/d", null);
        up.Set(HardwareInfo.Uptime(), null);
    }

    private static string Speed(double bytesPerSec)
    {
        if (bytesPerSec >= 1048576) return $"{bytesPerSec / 1048576:0.0} Mo/s";
        if (bytesPerSec >= 1024) return $"{bytesPerSec / 1024:0} Ko/s";
        return "~0 Ko/s";
    }

    private long TotalBytes()
    {
        try
        {
            long sum = 0;
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                if (ni.OperationalStatus == OperationalStatus.Up && ni.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                {
                    var s = ni.GetIPv4Statistics();
                    sum += s.BytesReceived + s.BytesSent;
                }
            return sum;
        }
        catch { return pBytes; }
    }

    /// <summary>Une ligne du moniteur : libellé, valeur, et barre de progression optionnelle.</summary>
    private sealed class Row
    {
        private static readonly Brush Red = new SolidColorBrush(Color.FromRgb(0xE5, 0x5A, 0x5A));
        private readonly TextBlock value;
        private readonly Border fill;
        private readonly Border track;
        public readonly FrameworkElement Element;

        public Row(MonitorPanel owner, string label, bool bar = true)
        {
            var head = new Grid { Margin = new Thickness(0, 8, 0, bar ? 4 : 2) };
            head.ColumnDefinitions.Add(new ColumnDefinition());
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            head.Children.Add(new TextBlock { Text = label, FontSize = 13, Foreground = (Brush)owner.FindResource("HudTextMuted") });
            value = new TextBlock { Text = "…", FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = (Brush)owner.FindResource("HudText") };
            Grid.SetColumn(value, 1);
            head.Children.Add(value);

            if (bar)
            {
                track = new Border { Height = 7, CornerRadius = new CornerRadius(4), Background = (Brush)owner.FindResource("HudStroke") };
                fill = new Border { Height = 7, CornerRadius = new CornerRadius(4), HorizontalAlignment = HorizontalAlignment.Left, Width = 0, Background = (Brush)owner.FindResource("HudAccent") };
                var grid = new Grid();
                grid.Children.Add(track);
                grid.Children.Add(fill);
                var col = new StackPanel();
                col.Children.Add(head);
                col.Children.Add(grid);
                Element = col;
            }
            else
            {
                track = fill = null!;
                Element = head;
            }
        }

        public void Set(string text, int? percent)
        {
            value.Text = text;
            if (fill == null || percent == null)
                return;
            double w = Math.Clamp(percent.Value, 0, 100) / 100.0 * Math.Max(1, track.ActualWidth);
            fill.Width = w;
            // couleur : vert < 70, ambre < 90, rouge au-delà
            fill.Background = percent >= 90 ? Red
                : percent >= 70 ? (Brush)Application.Current.FindResource("HudAmber")
                : (Brush)Application.Current.FindResource("HudSuccess");
        }
    }

    #region Win32
    [DllImport("kernel32.dll")] private static extern bool GetSystemTimes(out FILETIME idle, out FILETIME kernel, out FILETIME user);
    [DllImport("kernel32.dll")] private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);
    private static ulong FT(FILETIME f) => (ulong)f.dwHighDateTime << 32 | (uint)f.dwLowDateTime;

    [StructLayout(LayoutKind.Sequential)]
    private struct FILETIME { public int dwLowDateTime; public int dwHighDateTime; }
    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength; public uint dwMemoryLoad;
        public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual;
    }
    #endregion
}
