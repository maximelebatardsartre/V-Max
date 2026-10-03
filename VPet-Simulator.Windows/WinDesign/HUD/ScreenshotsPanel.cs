using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace VPet_Simulator.Windows.HUD;

/// <summary>V-Max HUD : galerie des captures d'écran récentes (prises par « capture d'écran »). Clic = ouvrir.</summary>
public sealed class ScreenshotsPanel : HudSidePanel
{
    private static string Dir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "V-Max");

    public ScreenshotsPanel(MainWindow pet) : base(pet, "CAPTURES", "Captures récentes", 300, 440)
    {
        AddHeaderButton("", "Ouvrir le dossier", () => { try { if (Directory.Exists(Dir)) Process.Start(new ProcessStartInfo(Dir) { UseShellExecute = true }); } catch { } });
        Build();
    }

    protected override void OnOpening() => Build();

    private void Build()
    {
        var list = new StackPanel();
        try
        {
            if (Directory.Exists(Dir))
                foreach (var f in new DirectoryInfo(Dir).GetFiles("*.png").OrderByDescending(f => f.LastWriteTime).Take(20))
                    list.Children.Add(Thumb(f.FullName, f.LastWriteTime));
        }
        catch { }
        if (list.Children.Count == 0)
            list.Children.Add(new TextBlock { Text = "Aucune capture pour l'instant. Dis « capture d'écran ».", Foreground = Res("HudTextMuted"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) });
        Body = list;
    }

    private FrameworkElement Thumb(string path, DateTime when)
    {
        var img = new Image { Height = 120, Stretch = Stretch.UniformToFill };
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.DecodePixelHeight = 160;
            bmp.UriSource = new Uri(path);
            bmp.EndInit();
            img.Source = bmp;
        }
        catch { }
        var stack = new StackPanel();
        stack.Children.Add(img);
        stack.Children.Add(new TextBlock { Text = when.ToString("dd/MM à HH:mm"), Foreground = Res("HudTextMuted"), FontSize = 11, Margin = new Thickness(2, 3, 0, 0) });
        var card = new Border
        {
            CornerRadius = new CornerRadius(12),
            Background = (Brush)FindResource("HudSurfaceRaised"),
            Margin = new Thickness(0, 0, 0, 10),
            Padding = new Thickness(6),
            Cursor = Cursors.Hand,
            Child = stack,
        };
        card.MouseLeftButtonUp += (_, _) => { try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); } catch { } };
        return card;
    }
}
