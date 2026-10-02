using LinePutScript.Localization.WPF;
using Panuon.WPF.UI;
using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using VPet_Simulator.Windows.Interface;
using static VPet_Simulator.Windows.Interface.Food;
using VPet_Simulator.Core;

namespace VPet_Simulator.Windows.HUD;

/// <summary>
/// V-Max : nourrir le compagnon avec les règles de la boutique de VPet (crédit sous 1 000 $, contrôle des objets
/// déséquilibrés, lassitude). Partagé par le garde-manger et les routines de vie.
/// </summary>
public static class PetCare
{
    private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

    /// <summary>
    /// Donne un aliment ou un cadeau. Retourne null si c'est fait, sinon la raison.
    /// En mode non interactif (routines), un objet déséquilibré est refusé au lieu de demander.
    /// </summary>
    public static string? Feed(MainWindow mw, Food item, bool interactive)
    {
        var save = mw.Core.Save!;
        if (mw.Set.EnableFunction)
        {
            bool paid = mw.Sandbox?.FreeItems != true && mw.Sandbox?.UnlimitedMoney != true;
            if (paid && (item.Price >= 1000 || item.Exp >= 1000) && item.Price >= save.Money)
                return $"Il manque {(item.Price - save.Money).ToString("N2", Fr)} $ pour {item.TranslateName}.";
            if (mw.HashCheck && item.IsOverLoad())
            {
                if (!interactive)
                    return $"{item.TranslateName} est déséquilibré : ignoré par les routines.";
                if (VDialog.Show("当前食物/物品属性超模,是否继续使用?\n使用超模食物可能会导致游戏发生不可预料的错误\n使用超模食物不影响大部分成就解锁\n本物品推荐价格为{0:f0}"
                    .Translate(item.RealPrice), "超模食物/物品使用提醒".Translate(), MessageBoxButton.YesNo) != MessageBoxResult.Yes)
                    return "";
                mw.HashCheck = false;
            }
            if (mw.Sandbox?.FreeItems != true)
                save.Money -= item.Price;
            mw.Sandbox?.AfterSpending();
            mw.TakeItem(item);
            mw.TakeItemHandle(item, 1, interactive ? "betterbuy" : "vmax_routine");
        }
        mw.DisplayFoodAnimation(item.GetGraph(), item.ImageSource);
        return null;
    }

    /// <summary>
    /// Choix automatique pour une routine : favoris d'abord, puis ce qui lasse le moins, puis le moins cher ;
    /// un peu de hasard parmi les meilleurs pour varier les menus
    /// </summary>
    public static Food? Choose(MainWindow mw, FoodType type)
    {
        var now = DateTime.Now;
        var save = mw.Core.Save!;
        var candidates = mw.Foods.Where(f => f.Type == type && f.Visibility && f.CanUse
                && !(mw.HashCheck && f.IsOverLoad())
                && (!mw.Set.EnableFunction || mw.Sandbox?.FreeItems == true || mw.Sandbox?.UnlimitedMoney == true
                    || f.Price < 1000 && f.Exp < 1000 || f.Price < save.Money))
            .Select(f =>
            {
                var eat = mw.GameSavesData["buytime"].GetDateTime(f.Name, now);
                double tired = eat > now ? (eat - now).TotalHours : 0;
                return (food: f, score: (f.Star ? 0 : 1000) + tired * 100 + f.Price);
            })
            .OrderBy(x => x.score)
            .Take(4)
            .ToList();
        return candidates.Count == 0 ? null : candidates[Random.Shared.Next(candidates.Count)].food;
    }
}
