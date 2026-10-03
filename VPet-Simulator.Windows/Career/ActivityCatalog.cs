using System;
using System.Collections.Generic;
using System.Linq;
using VPet_Simulator.Core;
using VPet_Simulator.Windows.Interface;
using static VPet_Simulator.Core.GraphHelper;

namespace VPet_Simulator.Windows.Career;

/// <summary>Où classer une activité : un vrai métier, un loisir libre, ou une activité de vie/ambiance (0 gain).</summary>
public enum Bucket { Metier, Loisir, Ambiance }

/// <summary>Classement curaté d'une activité : sa catégorie, son titre propre, et (pour un métier) le métier/palier/gain.</summary>
public sealed record ActivityDef(Bucket Bucket, string Title, string? Track = null, int Tier = 0, double Money = 0);

/// <summary>
/// V-Max : AUDIT des occupations. Les mods rangent tout et n'importe quoi dans Travail/Études avec des gains absurdes
/// (émotes, ménage, siestes mieux payés que de vrais jobs…). Ce catalogue reclasse chaque activité — par NOM, car le
/// graphe d'animation n'est pas unique — en Métier (gains cohérents, titre propre, palier), Loisir (libre, sans argent)
/// ou Vie/Ambiance (0 gain, retirée des occupations, jouée par le comportement autonome). Appliqué au démarrage.
/// </summary>
public static class ActivityCatalog
{
    public static readonly Dictionary<string, ActivityDef> ByName = new()
    {
        // ───────── MÉTIERS · Numérique ─────────  (MoneyBase choisi pour un $/min resserré : Get()≈(base·1.075+1)^1.25)
        ["文案"]   = new(Bucket.Metier, "Rédiger",            "redaction", 0, 6),   // ≈12 $/min
        ["写材料"] = new(Bucket.Metier, "Rédiger un dossier", "redaction", 0, 6),   // ≈12
        ["做PPT"]  = new(Bucket.Metier, "Faire un diaporama", "redaction", 1, 8),   // ≈16
        ["直播"]   = new(Bucket.Metier, "Faire un live",      "stream", 0, 7),       // ≈14
        ["做图"]   = new(Bucket.Metier, "Faire un visuel",    "stream", 1, 8),       // ≈16
        ["写代码"] = new(Bucket.Metier, "Programmer",             "dev", 0, 6),       // ≈12
        ["Coding Lv.10"] = new(Bucket.Metier, "Programmer (confirmé)", "dev", 1, 8),  // ≈16
        ["Coding Lv.20"] = new(Bucket.Metier, "Programmer (senior)",   "dev", 2, 11), // ≈23
        ["清屏"]   = new(Bucket.Metier, "Nettoyer les écrans", "maintenance", 0, 6),  // ≈12
        ["修屏幕"] = new(Bucket.Metier, "Réparer un écran",    "maintenance", 2, 10), // ≈22

        // ───────── MÉTIERS · Restauration ─────────
        ["烧烤"]   = new(Bucket.Metier, "Tenir le barbecue", "cuisine", 0, 6),        // ≈12

        // ───────── MÉTIERS · Indépendante (échelle plus marquée : c'est sa progression propre) ─────────
        ["挂机工作Lv1"] = new(Bucket.Metier, "Petites missions",    "freelance", 0, 5),  // ≈10
        ["挂机工作Lv2"] = new(Bucket.Metier, "Missions régulières", "freelance", 1, 8),  // ≈16
        ["挂机工作Lv3"] = new(Bucket.Metier, "Gros contrats",       "freelance", 2, 12), // ≈26
        ["挂机工作Lv4"] = new(Bucket.Metier, "Contrats premium",    "freelance", 3, 17), // ≈40
        ["Syrian Summer Job"] = new(Bucket.Metier, "Job d'été",       "appoint", 0, 6),  // ≈12
        ["Genshin Shift"]     = new(Bucket.Metier, "Genshin au boulot","appoint", 1, 8), // ≈16

        // ───────── MÉTIERS · Études (linéaire, Exp croissante) ─────────
        ["上学"]       = new(Bucket.Metier, "Aller à l'école",     "etudes", 0, 35),
        ["上自习"]     = new(Bucket.Metier, "Étude en autonomie",  "etudes", 1, 45),
        ["学习"]       = new(Bucket.Metier, "Étudier",             "etudes", 2, 60),
        ["写作业"]     = new(Bucket.Metier, "Faire ses devoirs",   "etudes", 3, 70),
        ["研究"]       = new(Bucket.Metier, "Mener une recherche", "etudes", 4, 90),
        ["Grind!!"]    = new(Bucket.Metier, "Réviser à fond",      "etudes", 4, 90),
        ["挂机学习Lv1"] = new(Bucket.Metier, "Réviser en autonomie","etudes", 5, 110),

        // ───────── LOISIRS (libres, sans argent) ─────────
        ["玩游戏"]  = new(Bucket.Loisir, "Jouer aux jeux vidéo"),
        ["删错误"]  = new(Bucket.Loisir, "Chasse aux bugs"),
        ["玩水"]    = new(Bucket.Loisir, "Jeux d'eau"),
        ["跳绳"]    = new(Bucket.Loisir, "Corde à sauter"),
        ["玩CS 2"]  = new(Bucket.Loisir, "Jouer à CS 2"),
        ["玩死亡搁浅"] = new(Bucket.Loisir, "Jouer à Death Stranding"),
        ["玩原神"]  = new(Bucket.Loisir, "Jouer à Genshin"),
        ["玩绝地求生"] = new(Bucket.Loisir, "Jouer à PUBG"),
        ["玩永劫无间"] = new(Bucket.Loisir, "Jouer à Naraka"),
        ["玩老头环"] = new(Bucket.Loisir, "Jouer à Elden Ring"),
        ["玩赛博朋克"] = new(Bucket.Loisir, "Jouer à Cyberpunk"),
        ["玩GTA"]   = new(Bucket.Loisir, "Jouer à GTA"),
        ["玩地平线"] = new(Bucket.Loisir, "Jouer à Horizon"),
        ["玩英雄联盟"] = new(Bucket.Loisir, "Jouer à League of Legends"),
        ["Genshin Dailies"]   = new(Bucket.Loisir, "Genshin au quotidien"),
        ["Genshin Shift Farm"]= new(Bucket.Loisir, "Genshin : farm"),
        ["玩手机"]  = new(Bucket.Loisir, "Sur son téléphone"),
        ["看直播Only Climb"] = new(Bucket.Loisir, "Regarder un stream"),
        ["健身"]    = new(Bucket.Loisir, "Faire du sport"),
        ["练瑜伽"]  = new(Bucket.Loisir, "Faire du yoga"),
        ["跳舞"]    = new(Bucket.Loisir, "Danser"),
        ["刷视频"]  = new(Bucket.Loisir, "Mater des vidéos"),
        ["听音乐"]  = new(Bucket.Loisir, "Écouter de la musique"),
        ["学习乐器"] = new(Bucket.Loisir, "Apprendre un instrument"),
        ["追剧"]    = new(Bucket.Loisir, "Regarder une série"),
        ["来弹吉他"] = new(Bucket.Loisir, "Jouer de la guitare"),
        ["学书法"]  = new(Bucket.Loisir, "Calligraphie"),
        ["学画画"]  = new(Bucket.Loisir, "Dessiner"),
        ["Cosplay Catgirl"] = new(Bucket.Loisir, "Cosplay fille-chat"),
        ["\\Cheer/"]    = new(Bucket.Loisir, "Chant et danse"),
        ["\\Rock Out/"] = new(Bucket.Loisir, "Rock"),
        ["吃麦当劳"] = new(Bucket.Loisir, "Manger McDo"),

        // ───────── VIE & AMBIANCE (0 gain, jouées par le comportement autonome) ─────────
        ["Mopping"]   = new(Bucket.Ambiance, "Passer la serpillière"),
        ["休息"]      = new(Bucket.Ambiance, "Se reposer"),
        ["打坐"]      = new(Bucket.Ambiance, "Méditer"),
        ["Warm Winter Nap"] = new(Bucket.Ambiance, "Sieste au chaud"),
        ["Fireside Nap"]    = new(Bucket.Ambiance, "Sieste près du feu"),
        ["做梦~5lv"]   = new(Bucket.Ambiance, "Faire la sieste"),
        ["做梦~30lv"]  = new(Bucket.Ambiance, "Faire la sieste"),
        ["做梦~60lv"]  = new(Bucket.Ambiance, "Faire la sieste"),
        ["做梦~100lv"] = new(Bucket.Ambiance, "Faire la sieste"),
        ["做梦~150lv"] = new(Bucket.Ambiance, "Faire la sieste"),
        ["做梦~200lv"] = new(Bucket.Ambiance, "Faire la sieste"),
        ["做梦~500lv"] = new(Bucket.Ambiance, "Faire la sieste"),
        ["发呆"]      = new(Bucket.Ambiance, "Rêvasser"),
        ["Daydreaming ○"] = new(Bucket.Ambiance, "Rêvasser"),
        ["Daydreaming ●"] = new(Bucket.Ambiance, "Rêvasser"),
        ["摸鱼"]      = new(Bucket.Ambiance, "Glander"),
        ["Slacking Off"] = new(Bucket.Ambiance, "Glander"),
        ["网抑云"]    = new(Bucket.Ambiance, "Déprimer en musique"),
        ["Cheering"]  = new(Bucket.Ambiance, "Acclamations"),
        ["Corpse Role at Hengdian"] = new(Bucket.Ambiance, "Jouer le cadavre"),
        ["Play Dead"] = new(Bucket.Ambiance, "Faire le mort"),
        ["Fan Breeze"]  = new(Bucket.Ambiance, "Devant le ventilateur"),
        ["Fan Breeze!"] = new(Bucket.Ambiance, "Devant le ventilateur"),
        ["Feed the Cat"]  = new(Bucket.Ambiance, "Nourrir le chat"),
        ["Finger Heart"]  = new(Bucket.Ambiance, "Petit cœur"),
        ["Wiggle"]    = new(Bucket.Ambiance, "Se trémousser"),
        ["Hug"]       = new(Bucket.Ambiance, "Câlin"),
        ["阴暗地爬行"] = new(Bucket.Ambiance, "Ramper sournoisement"),
        ["偷看主人"]  = new(Bucket.Ambiance, "T'épier"),
        ["装病"]      = new(Bucket.Ambiance, "Faire semblant d'être malade"),
        ["Stay With You"] = new(Bucket.Ambiance, "Te tenir compagnie"),
        ["用厌恶的眼神盯着主人"] = new(Bucket.Ambiance, "Te fixer d'un air dégoûté"),
        ["114"] = new(Bucket.Ambiance, "Flâner"),
        ["514"] = new(Bucket.Ambiance, "Flâner"),
    };

    /// <summary>Classe une activité (repli : loisir si type Play, sinon on la laisse telle quelle via null).</summary>
    public static ActivityDef? Of(Work w) => w?.Name != null && ByName.TryGetValue(w.Name, out var d) ? d : null;

    public static Bucket BucketOf(Work w) => Of(w)?.Bucket ?? (w.Type == Work.WorkType.Play ? Bucket.Loisir : Bucket.Metier);
    public static bool IsAmbiance(Work w) => Of(w)?.Bucket == Bucket.Ambiance;
    public static CareerTrack? TrackOf(Work w) => Of(w) is { Bucket: Bucket.Metier, Track: { } id } ? CareerTree.Track(id) : null;
    public static int TierOf(Work w) => Of(w)?.Tier ?? 0;

    private static bool globalDone;

    /// <summary>
    /// Nettoie les occupations CHARGÉES : re-type (émotes/loisirs sortent du Travail), fixe des gains cohérents,
    /// pose des titres propres, retire le plafond de niveau (les métiers se débloquent par la carrière). Idempotent
    /// et RE-JOUABLE (appelé aussi à l'ouverture du panneau) : si des mods/graphes changent en cours de route, on
    /// reclasse les nouvelles activités. Les activités hors catalogue sont rééquilibrées (filet de sécurité).
    /// </summary>
    public static void Apply(MainWindow mw)
    {
        var works = mw.Core?.Graph?.GraphConfig?.Works;
        if (works == null || works.Count == 0) return; // mods pas encore chargés : on réessaiera au prochain appel
        if (!globalDone)
        {
            globalDone = true;
            try { mw.Set["gameconfig"].SetBool("noAutoCal", true); } catch { } // nos gains ne sont plus réécrits
            // (l'alerte native « travail déséquilibré » est neutralisée à l'init de MainWindow via Main.WorkCheck)
        }

        foreach (var w in works)
        {
            try
            {
                var d = Of(w);
                if (d == null)
                {
                    // hors catalogue (mod non prévu) : au moins le rendre raisonnable (l'auto-calcul natif est coupé)
                    try { if (w.IsOverLoad()) w.FixOverLoad(); } catch { }
                    continue;
                }
                w.nametrans = d.Title;        // titre propre à l'affichage
                w.LevelLimit = 0;             // plus de plafond de niveau : la carrière gère le déblocage
                switch (d.Bucket)
                {
                    case Bucket.Metier:
                        var track = CareerTree.Track(d.Track);
                        w.Type = track?.Kind == CareerKind.Study ? Work.WorkType.Study : Work.WorkType.Work;
                        w.MoneyBase = d.Money;
                        w.FinishBonus = 0.15;  // bonus de fin modéré et UNIFORME (sinon les mods gonflent le $/min affiché)
                        break;
                    case Bucket.Loisir:
                        w.Type = Work.WorkType.Play;                 // loisir = détente, pas d'argent
                        w.MoneyBase = Math.Clamp(w.MoneyBase, 0, 18);
                        break;
                    case Bucket.Ambiance:
                        w.Type = Work.WorkType.Play;                 // reste jouable, mais…
                        w.MoneyBase = 0;                             // …0 gain, et masquée des occupations (UI)
                        break;
                }
            }
            catch { }
        }
    }
}
