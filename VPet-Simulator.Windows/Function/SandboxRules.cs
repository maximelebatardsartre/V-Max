using LinePutScript;
using System;
using VPet_Simulator.Core;

namespace VPet_Simulator.Windows;

/// <summary>
/// V-Max : options « bac à sable » pour contourner la simulation sans la désactiver entièrement.
/// Argent illimité, jauges qui ne baissent jamais, repas et cadeaux gratuits. Le compagnon garde son
/// économie normale tant que ces options sont coupées. Réglages dans Setting.lps, section vmax_sandbox.
/// </summary>
public sealed class SandboxRules
{
    /// <summary>Montant affiché en mode argent illimité</summary>
    public const double UnlimitedAmount = 9_999_999;

    private readonly MainWindow mw;
    private Snapshot? before;

    private sealed record Snapshot(double Strength, double Food, double Drink, double Feeling, double Health, double Likability, double Exp);

    public SandboxRules(MainWindow mw)
    {
        this.mw = mw;
        // juste avant le calcul des besoins : on mémorise ; juste après : on annule toute baisse
        mw.Main.TimeHandle += _ => Before();
        mw.Main.FunctionSpendHandle += After;
        if (UnlimitedMoney)
            mw.Core.Save!.Money = UnlimitedAmount;
    }

    private ILine Cfg => mw.Set["vmax_sandbox"];

    public event Action? Changed;

    /// <summary>L'argent ne s'épuise jamais (la vraie somme est mise de côté et rendue à la désactivation)</summary>
    public bool UnlimitedMoney
    {
        get => Cfg.GetBool("unlimited_money");
        set
        {
            if (value == UnlimitedMoney)
                return;
            var save = mw.Core.Save!;
            if (value)
            {
                Cfg.SetFloat("real_money", save.Money);
                save.Money = UnlimitedAmount;
            }
            else
                save.Money = Cfg.GetFloat("real_money", Math.Min(save.Money, 1000));
            Cfg.SetBool("unlimited_money", value);
            Changed?.Invoke();
        }
    }

    /// <summary>Les jauges peuvent monter (repas, sommeil) mais jamais baisser</summary>
    public bool FrozenGauges
    {
        get => Cfg.GetBool("frozen_gauges");
        set { Cfg.SetBool("frozen_gauges", value); Changed?.Invoke(); }
    }

    /// <summary>Repas, boissons et cadeaux ne coûtent rien (ils gardent leurs effets)</summary>
    public bool FreeItems
    {
        get => Cfg.GetBool("free_items");
        set { Cfg.SetBool("free_items", value); Changed?.Invoke(); }
    }

    private void Before()
    {
        var s = mw.Core.Save;
        if (s == null)
            return;
        before = FrozenGauges ? new Snapshot(s.Strength, s.StrengthFood, s.StrengthDrink, s.Feeling, s.Health, s.Likability, s.Exp) : null;
        if (UnlimitedMoney && s.Money < UnlimitedAmount)
            s.Money = UnlimitedAmount;
    }

    private void After()
    {
        var s = mw.Core.Save;
        if (s == null || before is not { } b || !FrozenGauges)
            return;
        if (s.Strength < b.Strength) s.Strength = b.Strength;
        if (s.StrengthFood < b.Food) s.StrengthFood = b.Food;
        if (s.StrengthDrink < b.Drink) s.StrengthDrink = b.Drink;
        if (s.Feeling < b.Feeling) s.Feeling = b.Feeling;
        if (s.Health < b.Health) s.Health = b.Health;
        if (s.Likability < b.Likability) s.Likability = b.Likability;
        if (s.Exp < b.Exp) s.Exp = b.Exp;
    }

    /// <summary>Après un achat : l'argent illimité est remis à niveau tout de suite</summary>
    public void AfterSpending()
    {
        if (UnlimitedMoney && mw.Core.Save!.Money < UnlimitedAmount)
            mw.Core.Save!.Money = UnlimitedAmount;
    }

    /// <summary>Texte d'affichage de l'argent</summary>
    public string MoneyText(System.Globalization.CultureInfo culture, string format = "N0") =>
        UnlimitedMoney ? "∞ $" : mw.Core.Save!.Money.ToString(format, culture) + " $";
}
