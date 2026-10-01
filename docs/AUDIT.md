# V-Max — Rapport d'audit technique et UX

> **Livrable de l'étape 2.** Il s'agit d'un audit en lecture seule du code forké de VPet, réalisé après le retrait de Steam et la francisation (branche `vmax/phase1`). Les références `fichier:ligne` peuvent décaler légèrement au fil des commits : rechercher le code cité avant de modifier.
>
> Sévérités : **🔴 Critique** · **🟠 Majeur** · **🟡 Mineur**. Les chiffres de CPU et de mémoire sont des **estimations issues de la lecture du code**, à confirmer avec `dotnet-counters` et PerfView.

---

## Sommaire

1. [Synthèse](#1-synthèse)
2. [Chiffres clés](#2-chiffres-clés)
3. [Architecture et dette technique](#3-architecture-et-dette-technique)
4. [Build, frameworks et dépendances](#4-build-frameworks-et-dépendances)
5. [Performances (fonctionnement 24 h/24)](#5-performances-fonctionnement-24-h24)
6. [Fuites mémoire et croissance non bornée](#6-fuites-mémoire-et-croissance-non-bornée)
7. [Sécurité](#7-sécurité)
8. [Vie privée](#8-vie-privée)
9. [UX/UI : inventaire et incohérences](#9-uxui--inventaire-et-incohérences)
10. [Modularité et extensibilité](#10-modularité-et-extensibilité)
11. [Bugs logiques relevés](#11-bugs-logiques-relevés)
12. [Plan d'action et refonte de l'interface](#12-plan-daction-et-refonte-de-linterface)

---

## 1. Synthèse

Le code de VPet est **fonctionnel et riche** : un moteur d'animation éprouvé, un système de mods et de plugins, un contenu abondant. Il porte néanmoins les stigmates d'un jeu indépendant qui a grandi vite :

- **Une seule classe fait tout.** `MainWindow` compte 4 224 lignes réparties en 4 fichiers partiels, et la méthode `GameLoad()` fait à elle seule environ 1 000 lignes. Il n'y a ni injection de dépendances, ni bus d'événements, ni tests, ni intégration continue.
- **Le fonctionnement en tâche de fond n'a pas été pensé pour durer.** Le code contient un `GC.Collect()` à chaque changement d'animation, une comparaison heure locale contre UTC qui vide le cache toutes les 30 secondes en France, un minuteur à 10 Hz qui ne s'arrête jamais, et rien n'est mis en pause quand l'écran est verrouillé ou qu'un jeu tourne en plein écran.
- **La sécurité des plugins est illusoire.** Leur signature n'est pas vérifiée : le certificat est extrait sans contrôle de validité. L'approbation se fait par nom de mod, pas par empreinte du fichier, et elle est stockée dans un fichier que les plugins peuvent eux-mêmes modifier.
- **L'interface est datée.** Elle repose sur des onglets et des menus contextuels WinForms, affiche 86 boîtes de dialogue modales, n'a aucune micro-animation, ne détecte pas le mode sombre de Windows et utilise des largeurs fixes qui tronquent les textes français.

**Bonne nouvelle :** les fondations du moteur d'animation sont solides. La fenêtre est rendue de façon accélérée (sans `AllowsTransparency`), les zones de toucher sont rectangulaires, les sprite sheets sont figées en mémoire (`Freeze`) et aucun appel réseau ne subsiste. Les correctifs les plus rentables tiennent en quelques lignes ; certains sont déjà appliqués dans l'étape 4 (voir §12).

### Les 10 priorités

| # | Constat | Sévérité | Effort |
|---|---|---|---|
| 1 | `GC.Collect()` bloquant à chaque changement d'animation (P-A1) | 🔴 | 3 lignes |
| 2 | Cache d'animations : `UtcNow` comparé à `Now`, vidé toutes les 30 s en UTC+1/+2 (P-A2) | 🔴 | 1 ligne |
| 3 | `closePanelTimer` à 10 Hz, jamais arrêté (P-T1) | 🔴 | 2 lignes |
| 4 | Signature des plugins non vérifiée, confiance automatique beaucoup trop large (S-01, S-02) | 🔴 | moyen |
| 5 | Une animation = un thread bloqué + `Thread.Sleep` + `Invoke` synchrone par image, sans pause (P-T2, P-T11) | 🔴 | moyen |
| 6 | `VPet.Solution` ne compile pas en .NET 10 (Fody) (B-01) | 🔴 | moyen |
| 7 | Fuites : `winCharacterPanel`, `winWorkMenu`, `ActivityLogs` non borné, arrêt d'un compagnon incomplet (M1 à M4) | 🟠 | faible |
| 8 | Énumérateur audio COM recréé à chaque vérification, jusqu'à 5 fois par seconde (P-T4) | 🟠 | faible |
| 9 | Données utilisateur, approbations et commandes DIY dans le dossier d'installation (S-07) | 🟠 | moyen |
| 10 | God class `MainWindow` + code-behind : obstacle à toute évolution (A-01 à A-03) | 🟠 | élevé, progressif |

---

## 2. Chiffres clés

| Mesure | Valeur |
|---|---|
| Lignes C# (hors `obj`/`bin`) | ~29 100 |
| `MainWindow` (4 fichiers partiels) | **4 224 lignes, une seule classe** |
| `winGameSetting.xaml.cs` | 1 684 lignes |
| Lignes contenant du chinois | 2 749 (dont 2 187 dans des commentaires) |
| Appels `Dispatcher.Invoke(` | 114 |
| Appels `Task.Run(` | 57 |
| Boîtes modales `MessageBox(X).Show(` | 86 |
| Déréférencements `Core.Save!` | 293 |
| Clés de paramètres « magiques » (`(gint)"…"`…) | 326 |
| Appels réseau programmatiques | **0** (Steam et télémétrie retirés à l'étape 1) |
| Projets de test, CI, `Directory.Build.props` | **aucun** |
| Animations du compagnon | 6 181 PNG 1000×1000 (836 Mo), ~578 animations, ~8 images/s |
| Réveils de thread au repos (1 compagnon) | **~18 par seconde** (objectif : ≤ 8 visible, ~0 masqué) |
| CPU au repos (estimation) | ~1 à 3 % d'un cœur (objectif < 0,3 %) |

---

## 3. Architecture et dette technique

### A-01 🟠 `MainWindow`, la classe qui fait tout
- `MainWindow.cs:1353-2372` : `GameLoad()`, une méthode asynchrone d'environ 1 000 lignes qui enchaîne :
  - chargement des mods et invalidation du cache ;
  - filtrage des textes par langue et chargement de la sauvegarde ;
  - équilibrage des objets et création d'objets codés en dur ;
  - construction des menus, de la zone de notification et de la boîte de discussion ;
  - événements d'anniversaire et de fêtes ;
  - cycle de vie des plugins.
- `MainWindow_Function.cs:30-613` : un `switch` de 95 cas pour un seul objet (le quiz du « gâteau d'anniversaire »).
- **Recommandation :** extraire `ModLoaderService`, `SaveRepository`, `SettingsService`, `MenuRegistry`, `TrayService`, `EventCalendarService` (événements datés en données, pas en code) et `PluginHost`. `MainWindow` ne doit plus qu'héberger la vue.

### A-02 🟠 `winGameSetting.xaml.cs` (1 684 lignes) mélange tout
Ce fichier contient le gestionnaire de mods et les vérifications de certificat, la création du raccourci de démarrage (COM `IShellLink`), le choix de l'API de discussion, la gestion des sauvegardes multiples, les liens « À propos » et le vu-mètre audio. **Recommandation :** une vue et un ViewModel par catégorie (voir §12).

### A-03 🟠 Deux paradigmes d'interface incompatibles
L'application principale est entièrement en code-behind (`AllowChange` pour neutraliser les gestionnaires d'événements pendant l'initialisation). `VPet.Solution` est en MVVM avec injection de dépendances (HKW.MVVM, MvvmDialogs). Aucun code n'est partagé entre les deux. **Recommandation :** CommunityToolkit.Mvvm + `Microsoft.Extensions.Hosting` pour toute la nouvelle interface.

### A-04 🟠 État global mutable, qui casse le multi-instance
- Champs statiques dans `App.xaml.cs:28-36`, `CoreMOD.cs:25-33`, `Item.cs:42,49,132`, `GraphCore.cs:57` et `FoodAnimation.cs:216`.
- **Bug avéré :** les actions des objets (`Item.UseAction`) sont enregistrées une seule fois, par la première fenêtre (`MainWindow.xaml.cs:158-238`). Ouvrir un objet « courrier » dans un second compagnon agit donc sur le premier.
- `Console.SetOut(...)` (`MainWindow.cs:1196`) est global au processus : la dernière fenêtre ouverte capte tous les journaux.

### A-05 🟡 `Random` statique partagé entre threads
`Function.cs:25` est appelé depuis des minuteurs, des tâches et le thread d'interface. Si son état se corrompt, il renvoie 0 en boucle et le compagnon répète toujours la même animation. **Correctif :** `Random.Shared`.

### A-06 🟠 `Setting.cs` dupliqué, et les deux copies divergent déjà
- `VPet-Simulator.Windows/Function/Setting.cs` et `VPet.Solution/Models/SettingEditor/Setting.cs` sont presque identiques. Ils diffèrent déjà sur la langue par défaut (`fr` d'un côté, `null` de l'autre).
- Bug présent dans les deux copies : `Math.Max(..., 20000)` au lieu de `Math.Min` (`Setting.cs:106`).
- Booléens stockés inversés : `allowmove`, `autochangewindow`, `topmost`.

### A-07 🟠 Lecture des métadonnées de mod implémentée trois fois
`CoreMOD.cs`, `winGameSetting.xaml.cs` (`ModInfo.FromDirectory`) et `VPet.Solution/Models/ModLoader.cs` lisent chacun les métadonnées à leur façon. Les comportements divergent ; en particulier, `ModLoader.cs:173` enregistre la langue sous le nom « lang » au lieu du code de culture.

### A-08 🟠 Exceptions avalées
- Exemples : `try { foreach (plugin) mp.EndGame(); } catch { }` (`MainWindow.xaml.cs:264-270`). Un seul plugin défaillant empêche les suivants d'être sauvegardés ou arrêtés.
- `App.xaml.cs:22-25` (en Release) marque toutes les exceptions non gérées comme traitées.
- `TimeHandle` et `FunctionSpendHandle` sont invoqués sans isolation dans des minuteurs `System.Timers` qui avalent les exceptions : un abonné fautif interrompt silencieusement toute la boucle de jeu.
- **Correctif :** une fonction `SafeInvoke` qui parcourt la liste des abonnés (`GetInvocationList`) et journalise chaque échec, plus un vrai journal (`Microsoft.Extensions.Logging` avec écriture dans un fichier).

### A-09 🟠 Code mort et vestiges
- `winReport.xaml.cs` : un `Timer_Elapsed` qui lève `NotImplementedException`, et une fausse barre de progression jamais appelée.
- Blocs conditionnels morts : `#if DEBUGs` (faute de frappe), `#if NewYear` (symbole jamais défini), et un bloc `#if BDAY` avec des dates de 2026 déjà expirées.
- Stubs Steam conservés volontairement pour la compatibilité des plugins.
- Interface multijoueur (`IMPWindows`) sans aucune implémentation.
- Télémétrie : retirée de l'application, mais **encore visible dans `VPet.Solution`** (`DiagnosticSettingView`, lien `exlb.net/Diagnosis`).
- Environ 149 lignes de code commenté ; `using` orphelins (`System.Net`, `NAudio.Midi`…).

### A-10 🟠 `async void`, attentes bloquantes et `Thread.Sleep`
- `.Wait()` ou `.Result` sur des chemins accessibles depuis le thread d'interface, avec risque d'interblocage : `Main.xaml.cs:192,202,238` et `MainLogic.cs:62`.
- `Thread.Sleep` de **3 à 6,7 minutes** à l'intérieur de `Task.Run` (`MainWindow.cs:2072`, entre autres) : chacun monopolise un thread du pool.
- Attente active sur `IsFinishGen` (`SayInfo.cs:185-206`), sans délai maximum ni annulation. Si un plugin n'appelle jamais `FinishGenerate()`, la boucle tourne indéfiniment.

### A-11 🟠 Accès concurrents sans synchronisation
- `ActivityLogs` (une `ObservableCollection`) est modifiée depuis le pool de threads et énumérée ailleurs.
- `SayProcess` est parcourue pendant que des plugins y ajoutent des éléments.
- `Save()` n'a ni verrou ni écriture atomique : le fichier est déplacé, puis réécrit (`MainWindow.cs:248-347`).
- Le gestionnaire `VoicePlayer.MediaFailed` est ajouté à **chaque** lecture de voix.
- **Correctif :** une boucle de jeu unique, un tampon circulaire pour les journaux, une sauvegarde via fichier temporaire + `File.Replace` protégée par un `SemaphoreSlim`.

### A-12 🟠 Le moteur est couplé à WPF
Le projet `Core` active `UseWPF` et `UseWindowsForms`, et la classe `Main` contient à la fois les minuteurs, les règles du jeu et des `MessageBoxX`. La logique n'est donc ni testable ni pilotable par un service sans interface, ce qui est un prérequis pour l'agent IA (voir VISION §2).

### A-13 🟠 Clés de paramètres « magiques »
- 326 accès du type `Set["CGPT"][(gstr)"type"]`, avec des états sous forme de chaînes (`"DIY"`, `"LB"`, `"OFF"`, `"API"`).
- **Correctif :** classes de constantes, énumérations et accesseurs typés.

### A-14 🟠 Chinois omniprésent pour une équipe française
- 2 749 lignes concernées, et les identifiants métier sont des littéraux chinois (`"每日礼包"`, `"生日蛋糕2"`).
- Les clés de traduction sont les phrases chinoises elles-mêmes. Pour les nouvelles chaînes, V-Max utilise désormais la phrase française comme clé.
- **Recommandation à terme :** des clés ASCII stables (`food.birthday_cake_2`).

### A-15 🟡 Fautes de frappe figées dans l'API publique
`Nomal` (117 occurrences), `Idel` (36), `Muti*` (22), `Seach`, `GameVerison`, `IMassageBar`… **Correctif :** des alias correctement nommés, avec les anciens noms marqués `[Obsolete]`.

### A-16 🟡 Nullabilité activée mais contournée
On compte 293 opérateurs `!`, des initialisations `null!`, et `GameCore` expose des champs publics qui restent nuls jusqu'au chargement.

### A-17 🟡 Version codée en dur
`version = 11073` (`MainWindow_Property.cs:23`), alors que tous les assemblages sont en `1.0.0.0`. **Correctif :** une version unique dans `Directory.Build.props`, plus un `ApiVersion` distinct pour la compatibilité des mods.

---

## 4. Build, frameworks et dépendances

### B-01 🔴 `VPet.Solution` ne compile pas
- Le tisseur Fody `HKW.MVVM.SourceGenerator` 0.1.8 n'arrive pas à résoudre `System.Diagnostics.DebuggerBrowsableState` en .NET 10. Le problème est **hérité de VPet** : il a été reproduit sur une copie propre du dépôt d'origine. Il n'existe pas de version plus récente.
- Repasser en .NET 8 est impossible, car `HKW.MVVM` 0.1.2 exige .NET 10.
- Le projet utilise aussi CommunityToolkit.Mvvm dont les générateurs sont désactivés par une cible MSBuild maison.
- **Recommandation :** remplacer HKW et Fody par les générateurs de CommunityToolkit.Mvvm, ou intégrer l'éditeur de paramètres dans l'application (voir §12, refonte des paramètres). Cette seconde option est retenue.

### B-02 🟠 Frameworks hétérogènes

| Projet | Framework cible (TFM) |
|---|---|
| Core | `net8.0-windows7.0` |
| Windows.Interface | `net8.0-windows` |
| Windows (application) | `net10.0-windows7.0` |
| VPet.Solution | `net10.0-windows` |
| Tool | `net48` |

- Le support de .NET 8 LTS se termine en **novembre 2026**.
- `LangVersion preview` est utilisé dans l'API publique des plugins.
- Il n'y a ni `Directory.Build.props`, ni `Directory.Packages.props`, ni `global.json`.
- **Recommandation :** tout passer en `net10.0-windows10.0.19041.0`. Cette cible est nécessaire pour les API WinRT (média, alimentation, notifications) ; voir VISION §3.3.

### B-03 🟠 Dépendances

| Paquet | Version | Remarque |
|---|---|---|
| LinePutScript (+ Localization.WPF) | 1.11.9 / 1.0.7 | Format propriétaire de l'auteur d'origine ; toute la persistance et la traduction en dépendent |
| Panuon.WPF / .UI | 1.1.3 / 1.2.4.10 | Bibliothèque de composants ; `MessageBoxX` est utilisée plus de 86 fois |
| SkiaSharp | 3.116.1 | Construction des sprite sheets ; bibliothèque native à maintenir à jour |
| NAudio | 3.0.1 | Utilisé seulement pour le vu-mètre ; à réutiliser pour la capture micro (voix) |
| WpfAnimatedGif | 2.0.2 | Galerie uniquement |
| **Microsoft.CSharp** | 4.7.0 | **Inutile en .NET 10**, à supprimer |
| HKW.* | 0.x | Un seul mainteneur ; risque de pérennité |

### B-04 🟡 Pas de tests, pas de CI, pas d'analyseurs
**Recommandation :**
- tests xUnit pour le format de sauvegarde, les paramètres, `FunctionSpend` et le rééquilibrage des objets ;
- un workflow GitHub Actions x64/x86 ;
- les analyseurs `Microsoft.CodeAnalysis.NetAnalyzers` (CA2000, CA1416, CS8602 en avertissement).

---

## 5. Performances (fonctionnement 24 h/24)

**Ce qui est déjà bien :**
- pas d'attente active pour garder la fenêtre au premier plan ;
- fenêtre rendue sans `AllowsTransparency` : fenêtre *layered* forcée par un hook, donc rendu GPU ;
- zones de toucher rectangulaires ;
- sprite sheets figées (`Freeze`) ;
- aucun appel HTTP.

### 5.1 Budget de réveils au repos (un compagnon, animation par défaut)

| Source | Intervalle | Réveils/s | S'arrête quand le compagnon est masqué ? |
|---|---|---|---|
| Pompe d'images (`PNGAnimation.Run`) | 125 ms | ~8 (avec `Invoke` synchrone) | **Non** |
| `ToolBar.closePanelTimer` | 100 ms | 10 | **Jamais** (P-T1) |
| `Main.EventTimer` | 15 s | 0,07 | Non |
| Sonde audio (`Handle_Music`) | 15 s, puis 200 ms pendant la musique | 0,07 → 5 | Non |
| `GraphCore.CleanTimer` | 30 s | 0,03 | Non |
| **Total actuel** | | **~18** | |
| **Cible** | | ≤ 8 quand visible, **~0 quand masqué** | Oui |

À cela s'ajoutent des pics ponctuels :
- **déplacement :** +8 réveils/s, chacun avec environ 6 `Invoke` synchrones et 2 à 4 `SetWindowPos` ;
- **musique :** +5 créations COM par seconde ;
- **libellé de statistiques :** 100 Hz pendant 2 à 4 s.

### 5.2 Constats

| ID | Sév. | Constat | Emplacement | Correctif |
|---|---|---|---|---|
| P-A1 | 🔴 | `GC.Collect()` bloquant et compactant à **chaque** changement d'animation, sur le thread d'interface (5 à 50 ms de gel à chaque fois) | `MainDisplay.cs:618,663,676` | Supprimer : WPF signale déjà la mémoire des bitmaps au GC |
| P-A2 | 🔴 | Éviction du cache : `LastUseTimeTicks = UtcNow` comparé à `DateTime.Now - 2 min`. **En France (UTC+2), chaque sprite sheet qui ne joue pas est jetée toutes les 30 s**, puis redécodée (jusqu'à 31 Mo, 50 à 150 ms d'à-coup). À l'ouest de UTC, rien n'est jamais libéré | `GraphCore.cs:46`, `PNGAnimation.cs:57`, `APNGAnimation.cs:72` | Utiliser partout un temps monotone (`Environment.TickCount64`) |
| P-T1 | 🔴 | `closePanelTimer` (100 ms, `AutoReset`) ne s'arrête qu'au survol suivant. Après le premier passage de la souris, il tourne à 10 Hz avec un `Invoke` synchrone, soit **864 000 réveils par jour** | `ToolBar.xaml.cs:39-40,251-263` | Mode un seul tir (`AutoReset=false`), délai de 400 ms, `Stop()` après fermeture |
| P-T2 | 🔴 | Pompe d'images : un thread du pool bloqué par animation (3 pour la nourriture), `Thread.Sleep` et `Dispatcher.Invoke` synchrone à chaque image, avec une gigue de ±15,6 ms | `PNGAnimation.cs:272-319`, `APNGAnimation.cs:516-579`, `FoodAnimation.cs:148-214` | Une seule horloge d'animation par compagnon sur le thread d'interface (`DispatcherTimer` ou boucle `await Task.Delay`) avec un jeton d'annulation |
| P-T11 | 🟠 | **Aucune pause globale** quand la session est verrouillée, l'écran éteint, une application en plein écran ou la fenêtre masquée | — | Un service `ActivityGate` : `SessionSwitch`, `GUID_CONSOLE_DISPLAY_STATE`, `SHQueryUserNotificationState`, `DWMWA_CLOAKED`. La logique de jeu (15 s) continue |
| P-A4 | 🟠 | Une nouvelle `CroppedBitmap` à chaque image de chaque boucle : copie, conversion et envoi au GPU de ~1 Mo par image (**~8 Mo/s au repos**), plus 3 à 5 allocations par image | `PNGAnimation.cs:415-467` | Matérialiser une fois les images de l'animation active en Pbgra32 figées, avec un cache LRU limité en octets (150 à 250 Mo) |
| P-A3 | 🟠 | Sprite sheet horizontale décodée entièrement au premier recadrage, sur le thread d'interface | `PNGAnimation.cs:340-379,469-486` | Décoder en arrière-plan (`OnLoad` + `Freeze`) et préchauffer A→B→C |
| P-A5 | 🟠 | Au démarrage, environ 578 PNG 1000×1000 sont **entièrement décodés** juste pour lire leur taille (6 à 12 s de CPU) | `PNGAnimation.cs:202-219` | Manifeste de cache (taille, nombre d'images, durées), ou lecture de l'en-tête PNG seul |
| P-A6 | 🟠 | Construction du cache : toutes les images d'une animation en pleine résolution en mémoire (jusqu'à 124 Mo), parallélisme non borné : **pics de plusieurs Go** au premier lancement | `PNGAnimation.cs:162-195` | Décoder directement à la taille cible, `SemaphoreSlim(ProcessorCount/2)`, compression PNG rapide |
| P-A8 | 🟠 | Les APNG sont lus en entier à chaque lancement, même avec un cache | `APNGAnimation.cs:119` | Utiliser le manifeste |
| P-T4 | 🟠 | Sonde audio : un `MMDeviceEnumerator` et un `MMDevice` (jamais libéré) **à chaque appel** ; 5 Hz pendant la musique, même sans animation « music » (`FindGraphs` ne renvoie jamais `null`) | `MainWindow.cs:975-1105` | Un seul énumérateur en cache ; tester `.Count > 0` ; à terme, événements de session audio |
| P-T3 | 🟠 | Déplacement : ~6 `Invoke` synchrones et 2 `SetWindowPos` par tick de 125 ms, et 2 de plus pour PetHelper | `Main.xaml.cs:366-393`, `MWController.cs:58-101` | Minuteur sur le thread d'interface, un seul `SetWindowPos`, limites d'écran en cache |
| P-T10 | 🟠 | Longs `Thread.Sleep` (jusqu'à 6,7 min) et attentes actives (10 à 100 ms) dans le pool, qui l'épuisent (à-coups au démarrage) | `MainWindow.cs:2072,2101,2128…`, `SayInfo.cs:185` | `await Task.Delay`, `TaskCompletionSource` |
| P-S1 | 🟠 | ~800 lignes de démarrage exécutées dans **un seul bloc** sur le thread d'interface (fenêtres de paramètres et de boutique créées d'avance, plugins, icône de notification…) | `MainWindow.cs:1552-2357` | Découper avec `Dispatcher.Yield`, créer les fenêtres à la demande |
| P-S3 | 🟠 | La date de cache d'un seul mod suffit à **supprimer tout le cache** (reconstruction de plusieurs minutes) | `MainWindow.cs:1371-1379` | Invalidation par animation (clé incluant la date de modification) |
| P-P1/P2 | 🟠 | Multi-compagnon : aucun partage des animations ni des minuteurs ; N compagnons coûtent N fois plus (54 réveils/s pour 3 compagnons) | `MainWindow.cs:1538` | Cache de sprite sheets partagé et compté par références, horloge et sonde audio uniques |
| P-T6 | 🟡 | Libellé de statistiques : minuteur à 10 ms (100 Hz), et le fondu ne fonctionne pas (division entière) | `Main.xaml.cs:243-262` | `DoubleAnimation` |
| P-T8 | 🟡 | **Bug :** `ForceClose()` appelle `CloseTimer.Close()`, ce qui libère le minuteur : après un double-clic, la bulle ne disparaît plus jamais d'elle-même | `MessageBar.xaml.cs:350` | `Stop()` au lieu de `Close()` |
| P-A7 | 🟡 | `Process.GetCurrentProcess()` non libéré, dans une boucle | `Function.cs:88-91` | `Environment.WorkingSet` |
| P-A11 | 🟡 | La résolution de décodage ne suit ni le zoom ni le DPI | `Setting.cs:413` | Résolution choisie selon `500 × zoom × DPI` |
| P-R6 | 🟠 | Glisser le compagnon : 4 déplacements de fenêtre par mouvement de souris (500 à 1 000 Hz) | `MWController.cs:94-101`, `PetHelper.xaml.cs:111` | Regrouper les mouvements, une mise à jour par image affichée |

---

## 6. Fuites mémoire et croissance non bornée

| ID | Sév. | Constat | Correctif |
|---|---|---|---|
| M1 | 🟠 | `ActivityLogs` n'est jamais tronquée et n'est pas thread-safe. Elle reçoit toute la sortie de `Console`, jusqu'à 8 entrées/s lors d'un déplacement mal configuré | Tampon circulaire de 2 000 entrées protégé par un verrou |
| M2 | 🟠 | `winCharacterPanel` s'abonne à `ActivityLogs.CollectionChanged` sans se désabonner. Un nouveau panneau est créé à chaque ouverture, donc **chaque panneau ouvert reste en mémoire** (`winCharacterPanel.xaml.cs:984`) | Se désabonner dans `Closed` |
| M3 | 🟠 | `winWorkMenu` ajoute un gestionnaire en double à chaque changement de catégorie et ne le retire jamais (`winWorkMenu.xaml.cs:34-43`) | S'abonner une fois, se désabonner à la fermeture |
| M4 | 🟠 | Arrêt d'un compagnon incomplet : `Core.Graph.Dispose()` n'est **jamais appelé**, et des minuteurs et abonnements restent actifs. Fermer un compagnon secondaire laisse des centaines de Mo en mémoire | `IDisposable` complet sur `MainWindow` et `Main` |
| M5 | 🟡 | `PlayVoice` ajoute un gestionnaire `MediaFailed` à chaque appel | S'abonner une fois |
| M7 | 🟡 | `LocalizeCore.StoreTranslation = true` en production : liste des clés manquantes illimitée | L'activer seulement en mode développeur |
| M8 | 🟡 | `App.ErrorReport` (un `HashSet` de textes d'exception) est illimité | Plafond de 100 entrées |
| M10 | 🟡 | Icônes de nourriture décodées en pleine résolution et conservées à vie | `DecodePixelWidth`, `Freeze` |
| M11 | 🟠 | Galerie : décodage complet et rendu logiciel (`RenderTargetBitmap`) à chaque ouverture, sur le thread d'interface | Miniatures en cache sur disque, décodage en arrière-plan |

---

## 7. Sécurité

| ID | Sév. | Constat | Recommandation |
|---|---|---|---|
| S-01 | 🔴 | Les plugins tournent avec une confiance totale dans le contexte de chargement par défaut. L'approbation (`passmod`) se fait **par nom de mod** : remplacer la DLL plus tard conserve l'approbation | Approbation liée à l'empreinte SHA-256 de chaque DLL, nouvelle demande si elle change ; à terme, un `AssemblyLoadContext` par plugin |
| S-02 | 🔴 | `new X509Certificate2(dll)` **extrait** le certificat Authenticode sans vérifier la signature : une signature transplantée passe et la DLL est **chargée sans demander**. `IsTrustedCertificate` fait confiance à **tout client** de DigiCert G4 et Certum EV, et à tout émetteur contenant « Microsoft Corporation » (`CoreMOD.cs:356,449-462`) | Supprimer la confiance automatique. Sinon, `WinVerifyTrust` et une liste blanche d'empreintes V-Max |
| S-03 | 🟠 | En Debug, `IsPassMOD` renvoie toujours `true` (`CoreMOD.cs:430`) | À supprimer |
| S-04 | 🟠 | Les approbations sont stockées dans `Setting.lps`, que **les plugins peuvent modifier** (`ISetting.this[string]`) : un plugin approuvé peut en approuver d'autres | Stockage séparé dans `%LOCALAPPDATA%`, hors de l'API des plugins |
| S-05 | 🟠 | Un mod **désactivé** enregistre quand même ses traductions et ses `dllskip`. Comme les traductions sont indexées par le texte source, un mod peut **réécrire l'avertissement de sécurité** des plugins (usurpation d'interface) | Ignorer `lang` et `dllskip` des mods désactivés ; textes de sécurité non traduisibles par les mods |
| S-06 | 🟠 | Les boutons DIY lancent sans confirmation n'importe quel programme, n'importe quelle URI (y compris `ms-*:` et `search-ms:`) ou des frappes clavier `SendKeys` dans la fenêtre au premier plan (`MainWindow.cs:419-482`) | Afficher et confirmer la cible, autoriser seulement http(s) et les fichiers, retenir l'empreinte des entrées approuvées |
| S-07 | 🟠 | Paramètres, sauvegardes, journaux, cache et données des mods sont écrits **dans le dossier d'installation** : écritures impossibles sous Program Files, et fichiers modifiables par n'importe quel processus local | `%APPDATA%\V-Max` et `%LOCALAPPDATA%\V-Max`, avec migration au premier lancement |
| S-08 | 🟡 | Pas de désérialisation dangereuse ; mais une archive zip piégée (*zip bomb*) peut épuiser la mémoire via la galerie (`Photo.cs:595-664`) | Limiter la taille |
| S-09 | 🟡 | Le « HashCheck » des sauvegardes est un simple hachage sans clé : un badge anti-triche, pas une protection | — |

---

## 8. Vie privée

**Depuis l'étape 1 :** Steam, la télémétrie (envoi de la sauvegarde et des paramètres à `report.exlb.net`), l'envoi automatique des rapports de bug et la « réparation des données » via SteamID ont tous été supprimés. **Il ne reste aucun appel réseau programmatique.**

| ID | Sév. | Constat | Action |
|---|---|---|---|
| P-01 | 🟠 | Au premier lancement, le navigateur **s'ouvre tout seul** sur le wiki exLB (`MainWindow.cs:2046-2055`) | Supprimer ; tutoriel intégré (étape 4) |
| P-02 | 🟠 | Liens restants vers exLB, bilibili et Steam (« À propos », tutoriel, événement d'anniversaire) ; e-mail `service@exlb.net` dans certains messages C# (`MainWindow.cs:1299`, `App.xaml.cs:127`) | Pointer vers V-Max ; conserver le lien d'attribution GitHub de VPet |
| P-03 | 🟠 | Le rapport de bug joint par défaut **toute la sauvegarde et tous les paramètres**. Or les plugins y stockent leurs clés API, qui finiraient dans un ticket GitHub public | Case à décocher par défaut, masquage (`*key*`, `*token*`, nom d'hôte) et aperçu avant copie |
| P-04 | 🟡 | Le nom d'utilisateur Windows est utilisé comme nom du maître et inscrit dans les images générées | Demander un prénom au premier lancement |
| P-05 | 🟡 | Télémétrie encore visible dans `VPet.Solution` | Disparaîtra avec l'intégration des paramètres |

---

## 9. UX/UI : inventaire et incohérences

### 9.1 Surfaces existantes

| Surface | Rôle | Problèmes principaux |
|---|---|---|
| **Barre d'outils** (clic droit) | 5 menus : Nourrir, État, Interaction, Personnalisé, Système | Grille de 5 colonnes d'environ 100 px en police 24 : **troncature en français**. Masquage automatique au bout de 4 s. Entrée « Gestion des mods » inaccessible (menu parent masqué) |
| **Boîte de discussion** | Saisie de texte pour l'IA | Placée *dans* la barre d'outils, donc elle disparaît avec elle. Aucun raccourci clavier global |
| **Bulle** (MessageBar) | Paroles du compagnon | Effet machine à écrire (2 caractères toutes les 150 ms), pas de Markdown, fondu par minuteur |
| **Zone de notification** | Menu système | `ContextMenuStrip` WinForms non thématisé, **aucune action au clic gauche**, état des cases synchronisé en cherchant les éléments par leur texte |
| **Paramètres** | 7 onglets, ~104 contrôles | Double navigation (liste à gauche + onglets), index d'onglets « magiques » dans le code, réglages de développeur mélangés aux réglages courants |
| **Boutique, Inventaire, Travail, Panneau, Galerie** | Fenêtres de jeu | Chacune a sa propre barre de titre ; boutique en `Topmost` ; panneau recréé à chaque ouverture |
| **Console de développement** | Outils de développement | Correctement réservée au mode développeur |
| **PetHelper** | Bouton flottant de 40×40 | Utile, mais doublon du menu de notification |
| **VPet.Solution** | Éditeur de paramètres externe | Duplique les paramètres et **ne compile pas** |

### 9.2 Paramètres : proposition de tri Basic / Advanced

| Catégorie (nouvelle) | Basic | Advanced |
|---|---|---|
| **Général** | Langue (sans l'entrée « null »), démarrage avec Windows, toujours au premier plan, clic traversant | Masquer d'Alt+Tab, position de départ, console de développement |
| **Apparence** | Thème (**Système / Clair / Sombre**), taille du compagnon (curseur **corrigé** : il affiche aujourd'hui 2× la valeur réelle) | Police, résolution du cache, opacité, bulle intérieure ou extérieure, apparence du compagnon |
| **Compagnon** | Prénom du compagnon, ton prénom, ton anniversaire, déplacements libres | Intervalle de calcul, fréquence des interactions, zone de déplacement multi-écran, durée d'appui long, seuils de détection de la musique |
| **IA (agent)** | Activer l'agent, fournisseur et clé API (Gemini) | Modèle, température, outils autorisés, journal d'audit, raccourci et mot d'activation |
| **Sauvegardes** | — | Intervalle de sauvegarde automatique, copies de secours, gestionnaire de sauvegardes, multi-instance |
| **Extensions** | Liste des mods (activer/désactiver) | Plugins de code (approbation par empreinte), raccourcis DIY |
| **À propos** | Version, crédits, **attribution obligatoire des animations VUP-Simulator**, licence | — |

**À supprimer :**
- les options de télémétrie (fait à l'étape 1) ;
- le « calibrage des prix déséquilibrés » et le badge « HashCheck » : la notion de triche n'a plus de sens pour un agent ;
- le choix « LB / OFF / DIY » pour la discussion, remplacé par la section IA ;
- l'option « Plus de zoom », fusionnée avec le curseur de taille.

### 9.3 Parcours illogiques
- **Paramètres accessibles par 5 chemins ou plus**, le tutoriel par 2 chemins plus une ouverture automatique, l'option « au premier plan » par 3 interfaces différentes.
- **Travailler ou ouvrir le menu de travail** dépend d'un simple clic ou d'un double-clic, ce que l'utilisateur ne peut pas deviner.
- **Changer l'apparence du compagnon** propose d'ouvrir une nouvelle instance.
- **Les réglages qui exigent un redémarrage** ne le signalent pas de façon cohérente : parfois seulement dans une info-bulle.
- **Au démarrage, jusqu'à 7 interruptions :** une boîte modale par mod cassé, une boîte d'erreur d'animation, l'ouverture du navigateur, une notification de bienvenue, des invitations datées (rapport annuel, anniversaire)…
- **86 boîtes modales** et quatre mécanismes de retour différents (`MessageBox` Win32, `MessageBoxX`, `NoticeBox`, `LabelDisplay`), sans règle commune.

### 9.4 Troncatures en français (largeurs fixes)
- **Barre d'outils :** environ 100 px par élément en police 24 ; sous-menus de 200 px.
- **Boîte de discussion :** bouton « Envoyer ». **Corrigé** à l'étape 1 : colonne en largeur automatique et texte indicatif raccourci.
- **Paramètres :**
  - lignes de 35 px de haut qui contiennent des interrupteurs à long libellé ;
  - deux interrupteurs dans une grille `*,*` ;
  - `CBAutoSave` de 200 px de large ;
  - marges absolues dans l'onglet « À propos ».
- **Autres fenêtres :** `winCharacterPanel` (zones de 300/160 px), `winBetterBuy` (recherche de 200 px), `winGallery`.
- *Mesure transitoire :* `Size.lps` (fr) élargit les fenêtres de 5 à 10 %.

### 9.5 Thème, DPI et multi-écran
- **Aucune détection du mode clair ou sombre de Windows.** Il n'existe que 2 thèmes, tous deux clairs, et de nombreuses couleurs sont codées en dur (`#FF4C4C`, `LightGray`, couleurs de travail dans `vup.lps`).
- `StaticResource PrimaryText` ne suit pas les changements de thème.
- Pas de manifeste DPI. Les distances se calculent sur l'écran **entier** et non sur la zone de travail, si bien que le compagnon peut passer sous la barre des tâches.
- Multi-écran : l'écran courant est retenu par son **index** (`GameScreenIndex`), qui n'est pas stable. Aucune gestion du débranchement d'un écran : le compagnon peut finir hors écran et seule une action manuelle (« Réinitialiser la position ») le récupère.
- **Aucune micro-animation :** pas de `Storyboard` dans l'application, toutes les ouvertures et fermetures sont brutales.
- **Accessibilité :** aucune `KeyBinding`, aucun `AutomationProperties`, aucun `TabIndex`. Seul `Ctrl+Entrée` existe, dans la boîte de discussion.

---

## 10. Modularité et extensibilité

### 10.1 Ce qui existe
- **Système de plugins :** les DLL placées dans `mod/*/plugin/` sont chargées, puis chaque type qui hérite *directement* de `MainPlugin` est instancié. Cycle de vie : `LoadPlugin` → `LoadDIY` → `GameLoaded` → `Save` → `EndGame`, plus `Setting`.
- **Événements :**
  - `Main.TimeHandle` (toutes les 15 s), `FunctionSpendHandle`, `SayProcess` ;
  - `Event_TouchHead` et `Event_TouchBody`, `Event_WorkStart` et `Event_WorkEnd`, `Event_MoveStart` et `Event_MoveEnd` ;
  - `GraphDisplayHandler`, `Event_TakeItem`, `Event_NewDay`.
- **Délégués remplaçables :** `DisplayNomal`, `SayRndFunction`, `RandomInteractionAction`, `DefaultClickAction`, `WorkCheck`.
- **Discussion :** l'abstraction `TalkBox` / `ITalkAPI`, avec du streaming via `SayInfoWithStream` et l'animation `think`.
- **Menus :** `ToolBar.AddMenuButton(type, texte, action)`.

### 10.2 Ce qui manque
| ID | Manque | Pourquoi c'est bloquant pour V-Max |
|---|---|---|
| M-01 | Conteneur d'injection de dépendances, bus d'événements, services ciblés | Tout transite par `IMainWindow`, environ 60 membres qui exposent des types WPF concrets |
| M-02 | Registre de commandes et d'outils | Un agent IA a besoin d'outils typés et découvrables ; `RunAction` n'est qu'un `switch` de 9 cas |
| M-03 | Cycle de vie asynchrone, manifeste de plugin (permissions, version d'API), isolation | `LoadPlugin` bloque l'interface ; un seul niveau d'héritage est permis |
| M-04 | `Responded` annulable, gestion d'erreur dans le streaming, stockage des secrets | Les exceptions des plugins de discussion sont perdues ; une clé API finirait en clair dans `Setting.lps` |
| M-05 | Service de métriques système | À créer (facile, voir VISION §3) |
| M-06 | Raccourcis globaux, capture audio, abstraction de la reconnaissance et de la synthèse vocales | Aucun raccourci global aujourd'hui ; `PlayVoice` ne lit qu'un seul fichier |
| M-07 | Découplage du thread d'interface | 114 `Dispatcher.Invoke`, et des boîtes modales lancées depuis la logique de jeu |

**Faisabilité des modules prévus :**
- **Agent IA :** faisable dès maintenant sous forme de plugin, mais fragile ; le socle M-01, M-02 et M-04 est prévu pour la v1.0.
- **Métriques OS :** effort faible.
- **Voix :** effort moyen ; NAudio est déjà présent.

Le détail figure dans [VISION.md](VISION.md).

---

## 11. Bugs logiques relevés

| Emplacement | Bug |
|---|---|
| `MainLogic.cs:407,411` | `Rnd.Next(0, 1)` renvoie toujours 0 : ces variations de santé ne se produisent jamais |
| `MainLogic.cs:263` | `>= sm25` au lieu de `<= sm25` (la branche soif est l'inverse de la branche faim) ; la branche suivante est inatteignable |
| `MainWindow_Function.cs:70-73` | Le texte dit « a trouvé {1} pièces », mais le code ajoute de l'**expérience** |
| `MainWindow_Function.cs:507` | `stat_work_time / 60.0` est affiché en heures, alors que les autres durées utilisent `/3600.0` |
| `winGameSetting.xaml.cs` (`GenStartUP`) | `BaseDirectory + @"vpeticon.ico"` : il manque le `\`, donc l'icône du raccourci est introuvable |
| `winCharacterPanel.xaml.cs:465-467` | `startlengthrank = 0` puis `if (< 0.5)`, toujours vrai (sans effet depuis le retrait des classements Steam) |
| `GraphCore.FindGraphs` | Ne renvoie jamais `null`, donc les tests `== null` (`MainWindow.cs:1031,2576`) sont toujours faux |
| `Main.xaml.cs:87-96` | `Remove` dans une boucle `for` indexée : un élément sur deux est ignoré |
| `MessageBar.xaml.cs:350` | `CloseTimer.Close()` libère le minuteur (voir P-T8) |
| `Setting.cs:106` | `Math.Max` au lieu de `Math.Min` |
| Migration | L'ancien raccourci `VPET_Simulator.lnk` n'est pas supprimé depuis le renommage en `V-Max.lnk` |
| Paramètres | Le curseur de zoom affiche 2× la valeur réelle (`ZoomLevel*2`) |

---

## 12. Plan d'action et refonte de l'interface

### 12.1 Correctifs immédiats (début de l'étape 4)
Ces correctifs tiennent en quelques lignes, pour un gain mesurable :
1. P-A1 : supprimer `GC.Collect()` ;
2. P-A2 : temps monotone dans le cache d'animations ;
3. P-T1 : `closePanelTimer` en un seul tir ;
4. P-T8 : `Stop()` au lieu de `Close()` ;
5. M2, M3, M5 : désabonnements ;
6. M1 : `ActivityLogs` bornée ;
7. P-T4 : énumérateur audio en cache et test `.Count > 0` ;
8. A-05 : `Random.Shared` ;
9. M7 : `StoreTranslation` seulement en mode développeur ;
10. P-01 : plus d'ouverture automatique du navigateur.

### 12.2 Feuille de route de la v1.0

| Lot | Contenu | Constats traités |
|---|---|---|
| **Performances** | `ActivityGate` (pause écran verrouillé, plein écran, masqué), horloge d'animation unique, cache LRU d'images Pbgra32, manifeste de cache, déplacement avec un seul `SetWindowPos`, démarrage découpé | P-T2, P-T3, P-T11, P-A3 à P-A6, P-S1, P-S3 |
| **Sécurité et données** | Données dans `%APPDATA%`, approbation des plugins par empreinte, suppression de la confiance automatique, confirmation des DIY, rapport de bug masqué | S-01 à S-07, P-03 |
| **Socle** | `Directory.Build.props`, tout en .NET 10, hôte avec injection de dépendances, bus d'événements, `SafeInvoke`, journalisation, CI et premiers tests | A-08, B-02 à B-04, M-01 |
| **Interface 2026** | Voir 12.3 | §9 |
| **Agent** | `PetStateController`, `IChatProvider` Gemini, 5 outils à faible risque, stockage des secrets | VISION §4, §5 |

### 12.3 Refonte de l'interface : principes
- **Une seule couche de surcouches au lieu de fenêtres modales.** Les menus, la discussion, les confirmations et les notifications deviennent des **surcouches ancrées au compagnon** :
  - fond acrylique ou Mica (`DWMWA_SYSTEMBACKDROP_TYPE`, sinon verre dépoli WPF) ;
  - coins de 12 px, ombre douce ;
  - positionnement intelligent : côté libre de l'écran, sans jamais déborder sur la barre des tâches.
- **Menu radial ou palette au clic droit** à la place de la grille de 5 colonnes : icône + libellé, dont la largeur s'adapte au texte.
- **Discussion permanente** : une surcouche indépendante de la barre d'outils, ouverte par `Ctrl+Alt+Espace` ou par un clic sur le compagnon, avec historique et Markdown.
- **Paramètres :**
  - une fenêtre unique avec navigation latérale (§9.2) et un sélecteur **Basic / Advanced** en haut ;
  - recherche plein texte ;
  - bandeau « Redémarrage requis » cohérent ;
  - aperçu immédiat.
- **Micro-animations** : 150 à 250 ms, courbe `CubicEase` sortante :
  - les surcouches apparaissent en fondu, translation de 8 px et échelle de 0,98 → 1 ;
  - au survol des boutons, couleur et élévation ;
  - les bulles apparaissent par un léger rebond d'échelle ;
  - le paramètre Windows « Animations » est respecté.
- **Thème système** : clair, sombre ou suivre Windows (`AppsUseLightTheme`, `UISettings.ColorValuesChanged`), couleur d'accentuation de Windows, et toutes les couleurs exprimées en ressources dynamiques.
- **Notifications** : notifications Windows natives à la place des `MessageBoxX` non bloquantes ; les modales sont réservées aux décisions destructrices.
- **Multi-écran** : identifiant de moniteur stable, zone de travail, retour automatique sur un écran visible (`DisplaySettingsChanged`), manifeste DPI *PerMonitorV2*.
