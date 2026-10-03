using System.Linq;
using VPet_Simulator.Windows;
using Xunit;

namespace VMax.Tests;

public class VMaxBubbleTests
{
    [Fact]
    public void Repertoire_charge_et_sans_maitre()
    {
        var bubbles = VMaxBubbles.Load();
        Assert.True(bubbles.Count > 50, $"seulement {bubbles.Count} bulles chargées");
        Assert.DoesNotContain(bubbles, b => b.Text.Contains("maître") || b.Text.Contains("maitre"));
        // au moins quelques bulles utilisent le prénom du propriétaire
        Assert.Contains(bubbles, b => b.Text.Contains("{hostname}"));
    }
}
