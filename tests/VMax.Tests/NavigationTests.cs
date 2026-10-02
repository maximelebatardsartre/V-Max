using VPet_Simulator.Windows.Habitat;
using Xunit;

namespace VMax.Tests;

public class HabitatNavigatorTests
{
    /// <summary>
    /// Maison : rez-de-chaussée (f1, y=1000), étage (f2, y=550, x 300→1500), échelle à x=1200,
    /// grenier (f3, y=200, x 600→1000) sans accès, chute de l'étage vers le jardin à x=1450 (hors maison → f1)
    /// </summary>
    private static HabitatMap House()
    {
        var m = new HabitatMap { Image = new HabitatImage { Sha256 = "x", Width = 1920, Height = 1080 }, PetHeight = 250 };
        m.Floors.Add(new HabitatFloor { Id = "f1", Y = 1000, X1 = 0, X2 = 1920 });
        m.Floors.Add(new HabitatFloor { Id = "f2", Y = 550, X1 = 300, X2 = 1500 });
        m.Floors.Add(new HabitatFloor { Id = "f3", Y = 200, X1 = 600, X2 = 1000 });
        m.Climbs.Add(new HabitatClimb { Id = "c1", X = 1200, Y1 = 552, Y2 = 998 });
        m.Rooms.Add(new HabitatRoom { Id = "r1", Name = "Cuisine", Tag = "kitchen", X = 300, Y = 560, Width = 600, Height = 440 });
        m.Rooms.Add(new HabitatRoom { Id = "r2", Name = "Chambre", Tag = "bedroom", X = 300, Y = 220, Width = 1200, Height = 330 });
        m.Spots.Add(new HabitatSpot { Id = "s1", Room = "r2", Floor = "f2", X = 450, Activity = "sleep" });
        return m;
    }

    [Fact]
    public void Monter_a_l_etage_par_l_echelle()
    {
        var nav = new HabitatNavigator(House(), NavCapabilities.All);
        var path = nav.FindPath("f1", 200, "f2", 400);
        Assert.NotNull(path);
        Assert.Collection(path!,
            s => { Assert.Equal(NavStepKind.Walk, s.Kind); Assert.Equal("f1", s.FromFloor); Assert.Equal(1200, s.ToX); },
            s => { Assert.Equal(NavStepKind.Climb, s.Kind); Assert.Equal("f1", s.FromFloor); Assert.Equal("f2", s.ToFloor); Assert.Equal("c1", s.Climb!.Id); },
            s => { Assert.Equal(NavStepKind.Walk, s.Kind); Assert.Equal("f2", s.FromFloor); Assert.Equal(400, s.ToX); });
    }

    [Fact]
    public void Redescendre_par_l_echelle_ou_par_une_chute_plus_courte()
    {
        var m = House();
        var nav = new HabitatNavigator(m, NavCapabilities.All);
        var down = nav.FindPath("f2", 1300, "f1", 1250)!;
        Assert.Contains(down, s => s.Kind == NavStepKind.Climb);

        m.Drops.Add(new HabitatDrop { Id = "d1", From = "f2", To = "f1", X = 1500 });
        var viaDrop = new HabitatNavigator(m, NavCapabilities.All).FindPath("f2", 1480, "f1", 1700)!;
        Assert.Contains(viaDrop, s => s.Kind == NavStepKind.Drop);
        Assert.DoesNotContain(viaDrop, s => s.Kind == NavStepKind.Climb);
    }

    [Fact]
    public void Sans_animation_d_escalade_l_etage_est_inaccessible()
    {
        var nav = new HabitatNavigator(House(), new NavCapabilities(false, false));
        Assert.Null(nav.FindPath("f1", 200, "f2", 400));
        Assert.NotNull(nav.FindPath("f1", 200, "f1", 1800));
    }

    [Fact]
    public void Grenier_sans_acces_et_chute_a_sens_unique()
    {
        var m = House();
        Assert.Null(new HabitatNavigator(m, NavCapabilities.All).FindPath("f1", 100, "f3", 800));
        m.Drops.Add(new HabitatDrop { Id = "d1", From = "f3", To = "f2", X = 990 });
        var nav = new HabitatNavigator(m, NavCapabilities.All);
        Assert.NotNull(nav.FindPath("f3", 700, "f2", 400));
        Assert.Null(nav.FindPath("f2", 400, "f3", 700));
    }

    [Fact]
    public void Echelle_qui_ne_touche_pas_deux_sols_est_ignoree()
    {
        var m = House();
        m.Climbs[0].Y1 = 700; // n'atteint plus l'étage
        var nav = new HabitatNavigator(m, NavCapabilities.All);
        Assert.Empty(nav.ConnectedClimbs());
        Assert.Null(nav.FindPath("f1", 200, "f2", 400));
    }

    [Fact]
    public void Sols_contigus_se_rejoignent()
    {
        var m = new HabitatMap { Image = new HabitatImage { Width = 1000, Height = 500 }, PetHeight = 100 };
        m.Floors.Add(new HabitatFloor { Id = "a", Y = 400, X1 = 0, X2 = 500 });
        m.Floors.Add(new HabitatFloor { Id = "b", Y = 402, X1 = 503, X2 = 1000 });
        var path = new HabitatNavigator(m, NavCapabilities.All).FindPath("a", 100, "b", 900);
        Assert.NotNull(path);
        Assert.All(path!, s => Assert.Equal(NavStepKind.Walk, s.Kind));
    }

    [Fact]
    public void Cible_dans_une_piece_emplacement_puis_sol()
    {
        var m = House();
        var bed = m.TargetIn(m.RoomNamed("chambre")!, "sleep");
        Assert.Equal(450, bed!.Value.x);
        Assert.Equal("s1", bed.Value.spot!.Id);
        var kitchen = m.TargetIn(m.RoomNamed("CUISINE")!, "eat");
        Assert.Equal("f1", kitchen!.Value.floor.Id);
        Assert.Equal(600, kitchen.Value.x);
        Assert.Null(kitchen.Value.spot);
        Assert.Equal("r1", m.RoomNamed("kitchen")?.Id);
        Assert.Equal("r1", m.RoomAt(400, 900)?.Id);
    }
}

public class RoutinePlannerTests
{
    private static readonly DateTime Monday = new(2026, 10, 5);

    [Fact]
    public void Heure_tiree_dans_la_plage_et_stable_pour_un_jour()
    {
        var r = new LifeRoutine { Id = "repas", From = "12:00", To = "14:00" };
        var a = RoutinePlanner.Occurrence(r, Monday)!;
        var b = RoutinePlanner.Occurrence(r, Monday)!;
        Assert.Equal(a.Start, b.Start);
        Assert.InRange(a.Start, Monday.AddHours(12), Monday.AddHours(14).AddTicks(-1));
        Assert.Equal(Monday.AddHours(14), a.WindowEnd);
    }

    [Fact]
    public void Heure_differente_selon_les_jours()
    {
        var r = new LifeRoutine { Id = "repas", From = "12:00", To = "14:00" };
        var starts = Enumerable.Range(0, 14).Select(i => RoutinePlanner.Occurrence(r, Monday.AddDays(i))!.Start.TimeOfDay).Distinct().Count();
        Assert.True(starts >= 10, $"seulement {starts} heures différentes sur 14 jours");
    }

    [Fact]
    public void Plage_qui_passe_minuit()
    {
        var r = new LifeRoutine { Id = "nuit", Action = "sleep", From = "23:00", To = "01:30", MinMinutes = 420, MaxMinutes = 510 };
        for (int i = 0; i < 30; i++)
        {
            var day = Monday.AddDays(i);
            var o = RoutinePlanner.Occurrence(r, day)!;
            Assert.InRange(o.Start, day.AddHours(23), day.AddDays(1).AddHours(1.5).AddTicks(-1));
            Assert.Equal(day.AddDays(1).AddHours(1.5), o.WindowEnd);
            Assert.InRange(o.Duration.TotalMinutes, 420, 510);
        }
    }

    [Fact]
    public void Due_apres_minuit_retrouve_la_plage_de_la_veille()
    {
        var r = new LifeRoutine { Id = "nuit", From = "23:00", To = "01:30" };
        var o = RoutinePlanner.Occurrence(r, Monday)!;
        var now = o.Start.AddMinutes(1);
        var due = RoutinePlanner.Due(new[] { r }, now, _ => false);
        Assert.Single(due);
        Assert.Equal(Monday, due[0].Day);
        Assert.Empty(RoutinePlanner.Due(new[] { r }, now, k => k == o.Key));
        Assert.Empty(RoutinePlanner.Due(new[] { r }, Monday.AddDays(1).AddHours(2), _ => false)); // plage terminée : sautée
    }

    [Fact]
    public void Jours_actifs_et_routine_desactivee()
    {
        // lundi à vendredi seulement
        int weekdays = Enumerable.Range(1, 5).Sum(d => 1 << d);
        var r = new LifeRoutine { Id = "travail", From = "09:00", To = "10:00", Days = weekdays };
        Assert.NotNull(RoutinePlanner.Occurrence(r, Monday));
        Assert.Null(RoutinePlanner.Occurrence(r, Monday.AddDays(5))); // samedi
        r.Enabled = false;
        Assert.Null(RoutinePlanner.Occurrence(r, Monday));
    }

    [Fact]
    public void Prochaine_occurrence_saute_celles_deja_jouees()
    {
        var r = new LifeRoutine { Id = "repas", From = "12:00", To = "14:00" };
        var today = RoutinePlanner.Occurrence(r, Monday)!;
        Assert.Equal(today.Key, RoutinePlanner.Next(r, Monday.AddHours(8), _ => false)!.Key);
        Assert.Equal(Monday.AddDays(1), RoutinePlanner.Next(r, Monday.AddHours(8), k => k == today.Key)!.Day);
    }

    [Fact]
    public void Frequence_certains_jours_seulement_et_stable()
    {
        var r = new LifeRoutine { Id = "sport", From = "18:00", To = "19:00", Chance = 50 };
        var days = Enumerable.Range(0, 200).Select(i => Monday.AddDays(i)).ToList();
        int count = days.Count(d => RoutinePlanner.Occurrence(r, d) != null);
        Assert.InRange(count, 70, 130);
        Assert.All(days.Take(20), d => Assert.Equal(RoutinePlanner.Occurrence(r, d) != null, RoutinePlanner.Occurrence(r, d) != null));
        r.Chance = 100;
        Assert.All(days.Take(20), d => Assert.NotNull(RoutinePlanner.Occurrence(r, d)));
        r.Chance = 0;
        Assert.All(days.Take(20), d => Assert.Null(RoutinePlanner.Occurrence(r, d)));
    }
}
