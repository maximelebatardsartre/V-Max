using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace VPet_Simulator.Windows.HUD;

/// <summary>V-Max HUD : bloc-notes rapide, enregistré automatiquement (reste entre les sessions).</summary>
public sealed class NotesPanel : HudSidePanel
{
    private static string File => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "V-Max", "notes.txt");

    private readonly TextBox box;
    private readonly DispatcherTimer debounce;

    public NotesPanel(MainWindow pet) : base(pet, "BLOC-NOTES", "Mes notes", 300, 360)
    {
        box = new TextBox
        {
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            MinHeight = 250,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = (Brush)FindResource("HudText"),
            CaretBrush = (Brush)FindResource("HudText"),
            FontSize = 14,
        };
        try { if (System.IO.File.Exists(File)) box.Text = System.IO.File.ReadAllText(File); } catch { }
        debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
        debounce.Tick += (_, _) => { debounce.Stop(); Save(); };
        box.TextChanged += (_, _) => { debounce.Stop(); debounce.Start(); };
        Body = box;
        Loaded += (_, _) => box.Focus();
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(File)!);
            System.IO.File.WriteAllText(File, box.Text);
        }
        catch { }
    }
}
