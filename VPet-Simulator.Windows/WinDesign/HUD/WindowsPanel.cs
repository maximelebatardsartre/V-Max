using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace VPet_Simulator.Windows.HUD;

/// <summary>
/// V-Max HUD : liste des fenêtres ouvertes. Clic = passer à cette fenêtre ; « ✕ » = la fermer proprement.
/// Un sélecteur de fenêtres (Alt+Tab maison) piloté à la voix (« montre-moi mes fenêtres »).
/// </summary>
public sealed class WindowsPanel : HudSidePanel
{
    private readonly StackPanel list;
    private IntPtr self;

    public WindowsPanel(MainWindow pet) : base(pet, "FENÊTRES", "Fenêtres ouvertes", 340, 460)
    {
        AddHeaderButton("", "Rafraîchir", Build);
        list = new StackPanel();
        Body = list;
        Loaded += (_, _) => { self = new WindowInteropHelper(pet).Handle; Build(); };
    }

    protected override void OnOpening() => Build();

    private void Build()
    {
        list.Children.Clear();
        int count = 0;
        EnumWindows((h, _) =>
        {
            if (!IsWindowVisible(h) || h == self)
                return true;
            // on ignore les fenêtres outils et celles sans barre des tâches
            int ex = GetWindowLong(h, GWL_EXSTYLE);
            if ((ex & WS_EX_TOOLWINDOW) != 0)
                return true;
            int len = GetWindowTextLength(h);
            if (len == 0)
                return true;
            var sb = new StringBuilder(len + 1);
            GetWindowText(h, sb, sb.Capacity);
            var title = sb.ToString().Trim();
            if (title.Length == 0 || title == "V-Max" || title.StartsWith("Program Manager"))
                return true;
            list.Children.Add(Row(h, title));
            count++;
            return true;
        }, IntPtr.Zero);

        if (count == 0)
            list.Children.Add(new TextBlock { Text = "Aucune fenêtre ouverte à afficher.", Foreground = Res("HudTextMuted"), Margin = new Thickness(0, 8, 0, 0) });
    }

    private FrameworkElement Row(IntPtr handle, string title)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var tb = new TextBlock { Text = title, Foreground = Res("HudText"), FontSize = 13, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        grid.Children.Add(tb);

        var close = new Button { Style = (Style)FindResource("HudIconButton"), Content = "", ToolTip = "Fermer la fenêtre", FontSize = 12 };
        close.Click += (_, _) => { PostMessage(handle, WM_CLOSE, IntPtr.Zero, IntPtr.Zero); Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new Action(Build)); };
        Grid.SetColumn(close, 1);
        grid.Children.Add(close);

        var b = new Border
        {
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(12, 7, 6, 7),
            Margin = new Thickness(0, 0, 0, 4),
            Background = Res("HudSurfaceRaised"),
            Cursor = Cursors.Hand,
            Child = grid,
        };
        b.MouseLeftButtonUp += (_, e) =>
        {
            if (e.OriginalSource is DependencyObject d && IsInsideButton(d))
                return;
            if (IsIconic(handle))
                ShowWindow(handle, SW_RESTORE);
            SetForegroundWindow(handle);
            HideAnimated();
        };
        return b;
    }

    private static bool IsInsideButton(DependencyObject d)
    {
        while (d != null)
        {
            if (d is Button) return true;
            d = System.Windows.Media.VisualTreeHelper.GetParent(d);
        }
        return false;
    }

    #region Win32
    private const int GWL_EXSTYLE = -20, WS_EX_TOOLWINDOW = 0x80, SW_RESTORE = 9, WM_CLOSE = 0x0010;
    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern int GetWindowTextLength(IntPtr hWnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    #endregion
}
