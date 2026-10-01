# V-Max — Vision et opportunités

> **Livrable de l'étape 3.** Ce document décrit l'architecture qui transforme le compagnon VPet en **agent IA de bureau** francophone, branché sur le système (OS) et sur l'API Gemini. Il s'appuie sur l'audit du code ([AUDIT.md](AUDIT.md)) ; les références `fichier:ligne` renvoient à la branche `vmax/phase1`.

---

## 1. La promesse

V-Max réunit deux produits :

- **un compagnon « idle »** qui vit sa vie sur le bureau. C'est l'héritage de VPet : animations, humeur, besoins, mods ;
- **un agent de nouvelle génération** qui écoute, comprend le contexte du PC, **agit** (ouvrir, chercher, piloter la musique, exécuter des routines) et le **montre** visuellement.

La différence avec un chatbot dans une fenêtre tient en une phrase : **le personnage est l'interface**. Chaque état de l'agent (écoute, réflexion, action, succès, erreur) a une traduction visuelle, et le personnage réagit à la vie réelle de la machine.

### Principes directeurs

| Principe | Conséquence technique |
|---|---|
| **Local d'abord** | Mot d'activation, détection de voix, métriques et mémoire tournent sur le PC. Rien ne part vers le cloud avant une activation explicite. |
| **L'agent demande avant d'agir** | Chaque outil a un niveau de risque, et les actions sensibles exigent une confirmation visible (§4.4). |
| **Sobriété** | L'application tourne 24 h/24 : budget au repos de moins de 1 % de CPU et de moins de 250 Mo de mémoire (§7). |
| **Rétrocompatibilité des mods** | L'API `IMainWindow` / `MainPlugin` reste disponible sous forme de façade ; les nouveaux services s'ajoutent à côté. |
| **Le français comme langue native** | Les nouvelles chaînes ont leur clé en français ; la voix, le mot d'activation et le *prompt* système sont en français. |

---

## 2. Architecture cible

Aujourd'hui, tout transite par `MainWindow` (une classe de 4 224 lignes) et par des minuteurs `System.Timers` qui appellent l'interface de manière synchrone (audit A-01, A-12, M-07). L'agent a besoin d'une colonne vertébrale : un **hôte .NET** avec injection de dépendances et un **bus d'événements**.

```
┌──────────────────────────────── V-Max (processus WPF, .NET 10) ───────────────────────────────┐
│                                                                                                │
│  UI Shell (WPF)            ┌───────────────────── Services (Microsoft.Extensions.Hosting) ────┐ │
│  ├ Compagnon (Core.Main)   │                                                                    │ │
│  ├ Bulle / Chat overlay  ◄─┤  IEventBus  ◄──────────── événements typés ─────────────────┐    │ │
│  ├ Palette de commandes    │     ▲   ▲                                                     │    │ │
│  └ Paramètres Basic/Adv.   │     │   │                                                     │    │ │
│                            │  ┌──┴───┴──────────┐   ┌─────────────────┐   ┌──────────────┐ │    │ │
│  Façade legacy             │  │ AgentOrchestrator│──►│ IChatProvider   │──►│ Gemini API   │ │    │ │
│  IMainWindow / MainPlugin  │  │ (boucle outils)  │   │ (Gemini, …)     │   │ (HTTPS)      │ │    │ │
│  (mods existants)          │  └──┬──────────┬────┘   └─────────────────┘   └──────────────┘ │    │ │
│                            │     │          │                                               │    │ │
│                            │  ┌──▼───────┐ ┌▼──────────────────┐ ┌───────────────────────┐ │    │ │
│                            │  │ToolRegistry│ │ PetStateController│ │ MemoryStore (SQLite) │ │    │ │
│                            │  │+ Permissions│ │ (animations IA)  │ └───────────────────────┘ │    │ │
│                            │  └──┬───────┘ └────────▲─────────┘                             │    │ │
│                            │     │                  │                                       │    │ │
│                            │  ┌──▼──────────────┐ ┌─┴───────────────┐ ┌─────────────────────┐│    │ │
│                            │  │ OS Bridge        │ │ MetricsService  │ │ VoiceService        ││    │ │
│                            │  │ apps, fichiers,  │ │ CPU, batterie,  │ │ wake word, VAD,     │┘    │ │
│                            │  │ média, scripts   │ │ appli active…   │ │ STT, TTS            │     │ │
│                            │  └─────────────────┘ └─────────────────┘ └─────────────────────┘     │ │
│                            │  ISecretStore (Windows Credential Manager / DPAPI) · ILogger        │ │
│                            └─────────────────────────────────────────────────────────────────────┘ │
└────────────────────────────────────────────────────────────────────────────────────────────────────┘
```

### 2.1 Les briques

| Brique | Rôle | Remplace ou complète |
|---|---|---|
| **Hôte générique** (`Microsoft.Extensions.Hosting`) | Durée de vie des services, injection de dépendances, configuration, journalisation | Code d'initialisation de `GameLoad()` (`MainWindow.cs:1353-2372`) |
| **`IEventBus`** | Événements typés : `PetTouched`, `PetSaid`, `TickElapsed`, `MetricsSampled`, `AgentStateChanged`, `WakeWordDetected`, `ToolInvoked`… Chaque abonné est isolé, de sorte qu'une erreur de plugin ne casse pas la boucle de jeu (A-08). | `Main.TimeHandle`, `SayProcess`, les `List<Action>` publiques |
| **`AgentOrchestrator`** | Boucle conversationnelle avec appel d'outils, annulation et streaming | — (nouveau) |
| **`IChatProvider`** | Abstraction du LLM : `IAsyncEnumerable<ChatChunk> StreamAsync(...)`. Gemini en premier, d'autres fournisseurs possibles. | `TalkBox.Responded` (non annulable, exceptions perdues, `TalkBox.xaml.cs:65`) |
| **`ToolRegistry` et `PermissionService`** | Catalogue d'outils typés avec schéma JSON, niveau de risque et journal d'audit | `MainWindow.RunAction` (switch de 9 cas, M-02) et les boutons DIY (S-06) |
| **`PetStateController`** | Traduit l'état de l'agent en animations et protège ces animations des interruptions (§5) | Appels directs à `Main.Display(...)` |
| **`MetricsService`** | Échantillonne le système et publie un instantané (§3) | Minuteur musique à 200 ms (`MainWindow.cs:975-1025`) |
| **`VoiceService`** | Mot d'activation, détection d'activité vocale, reconnaissance et synthèse vocales (§6) | `Main.PlayVoice` (lecture d'un seul fichier) |
| **`MemoryStore`** | Préférences, faits et historique résumé, en local (SQLite) | — |
| **`ISecretStore`** | Clé API Gemini stockée dans le Gestionnaire d'identification Windows, jamais dans `Setting.lps` | — (aujourd'hui, une clé atterrirait en clair à côté de l'exécutable, puis dans les rapports de bug : P-03) |

### 2.2 Compatibilité avec l'existant

- `IMainWindow` devient une **façade** au-dessus des services. Les mods et plugins existants continuent de fonctionner, et les membres liés à Steam restent des stubs (déjà fait en étape 1).
- Les nouveaux plugins reçoivent des services ciblés (`IPetController`, `ISpeechService`, `IToolRegistry`, `ISettingsStore<T>`) plutôt que la fenêtre entière.
- `Core` (le moteur d'animation) est conservé. Seule la logique métier (`FunctionSpend`, minuteurs) migre progressivement vers une boucle de jeu indépendante de WPF, ce qui la rend testable.

---

## 3. Ponts OS ↔ statistiques du compagnon

VPet simule faim, soif, humeur et endurance avec des règles internes (`MainLogic.FunctionSpend`, `MainLogic.cs:234-427`). V-Max peut **relier ces jauges à la réalité de la machine**, de sorte que le personnage devient un tableau de bord vivant.

### 3.1 Sources de métriques

| Signal | API Windows (sans droits administrateur) | Fréquence |
|---|---|---|
| Charge CPU globale | `GetSystemTimes` (deux lectures, calcul delta) | 5 s |
| Mémoire | `GlobalMemoryStatusEx` | 15 s |
| Batterie / secteur / économie d'énergie | `GetSystemPowerStatus` + `PowerManager` (WinRT) | événement + 60 s |
| Heure, jour, jours fériés | horloge + calendrier local | 60 s |
| Application au premier plan | `SetWinEventHook(EVENT_SYSTEM_FOREGROUND)` → processus + titre | **événementiel** |
| Plein écran / jeu / présentation / « Ne pas déranger » | `SHQueryUserNotificationState` | événement + 30 s |
| Inactivité de l'utilisateur | `GetLastInputInfo` | 10 s |
| Musique en cours | `GlobalSystemMediaTransportControlsSessionManager` (WinRT) : titre, artiste, état | **événementiel** |
| Réseau | `NetworkChange.NetworkAvailabilityChanged` | événement |
| Températures (option avancée) | WMI `MSAcpi_ThermalZoneTemperature` (souvent indisponible) | 60 s |

Le `MetricsService` tourne sur un `PeriodicTimer` hors du thread d'interface. Il publie un `SystemSnapshot` immuable sur le bus. **Quand le PC est en plein écran ou que le compagnon est masqué, l'échantillonnage ralentit** (×4).

### 3.2 Correspondance avec les jauges

Deux modes, au choix dans les paramètres :

- **Mode « Compagnon »** (par défaut) : la simulation d'origine est conservée et la réalité ne fait que *moduler* les jauges et l'humeur.
- **Mode « Miroir système »** : les jauges *affichent* la machine (le panneau d'état devient un moniteur système incarné).

| Jauge VPet | Mode Compagnon (modulation) | Mode Miroir système |
|---|---|---|
| **Endurance** (体力) | Baisse plus vite quand le CPU dépasse 80 % pendant plus de 2 minutes ; animation `yawning` | = 100 − charge CPU lissée |
| **Satiété** (饱腹) | Batterie sous 20 % : « J'ai faim… branche-moi ! », animation `switch_hunger` | = niveau de batterie (ou 100 % sur secteur) |
| **Soif** (口渴) | Mémoire disponible sous 15 % : demande de fermer des applications | = mémoire disponible |
| **Humeur** | + musique en cours (danse `music`), + soirée calme ; − nombreuses erreurs, − réseau coupé | moyenne pondérée des trois précédentes |
| **Sommeil** | Entre 0 h et 6 h et après 10 minutes d'inactivité : `DisplaySleep()`. Au réveil (retour de l'utilisateur) : salutation contextuelle | idem |

**Contexte de l'application active** (catégories configurables, liste par défaut) :

| Catégorie | Exemples | Réaction |
|---|---|---|
| Jeu / plein écran | `SHQueryUserNotificationState = QUNS_RUNNING_D3D_FULL_SCREEN` | Mode discret : pas de déplacement, pas de bulle, échantillonnage ralenti |
| Visioconférence | Teams, Zoom, Meet | Silence total, micro jamais écouté |
| Développement | Visual Studio, VS Code, terminal | Propose de l'aide (« Une erreur de compilation ? ») si l'utilisateur l'autorise |
| Bureautique | Word, Excel, navigateur | Rappels de pause (bien-être) |

### 3.3 Implémentation

1. `IMetricSource` (une classe par signal) → `MetricsService` → `SystemSnapshot` sur `IEventBus`.
2. `StatsBridge` s'abonne au bus et applique les règles (fichier `vmax-bridge.lps`, modifiable par un mod) via l'API existante `Core.Save` (`IGameSave`).
3. Le minuteur musique à 200 ms, qui recrée un `MMDeviceEnumerator` à chaque tick (audit de performances), est remplacé par les **événements de session média** WinRT. On réduit le CPU au repos et on gagne le titre du morceau.
4. Prérequis : cibler `net10.0-windows10.0.19041.0` pour accéder aux API WinRT (`Windows.Media.Control`, `Windows.System.Power`).

---

## 4. Exécution de tâches (function calling)

### 4.1 Boucle de l'agent

```
Utilisateur (voix / texte / palette)
        │
        ▼
AgentOrchestrator ── contexte : prompt système FR + SystemSnapshot + mémoire pertinente + historique
        │
        ▼ streamGenerateContent (Gemini, tools = déclarations de fonctions)
Réponse en streaming
   ├─ texte          → bulle (streaming immédiat) + TTS par phrase
   └─ functionCall   → ToolRegistry.Validate(args, schéma)
                         → PermissionService (niveau de risque, liste d'autorisations, confirmation)
                         → PetStateController.Set(Acting, tool)
                         → Execute (annulable, délai max)
                         → functionResponse renvoyée au modèle (boucle, 8 tours maximum)
        │
        ▼
Réponse finale → Speaking → Success/Idle
```

Le modèle Gemini est **configurable** : un modèle rapide pour la conversation et les outils, un modèle plus puissant pour les tâches longues. La clé API est saisie dans **Paramètres › Avancé › IA** et stockée dans le Gestionnaire d'identification Windows. L'appel passe par l'API REST `generateContent` / `streamGenerateContent`, avec `tools.functionDeclarations`, ou par le SDK .NET officiel de Google s'il convient.

### 4.2 Contrat d'un outil

```csharp
public interface IAgentTool
{
    string Name { get; }                 // "media.play_pause"
    string Description { get; }          // en français, lue par le modèle
    JsonElement ParametersSchema { get; } // JSON Schema (OpenAPI subset accepté par Gemini)
    ToolRisk Risk { get; }               // ReadOnly, Low, Sensitive, Dangerous
    Task<ToolResult> ExecuteAsync(JsonElement args, ToolContext ctx, CancellationToken ct);
}
```

Exemple de déclaration envoyée au modèle :

```json
{
  "name": "apps.open",
  "description": "Ouvre une application installée ou un dossier connu de l'utilisateur.",
  "parameters": {
    "type": "object",
    "properties": {
      "target": { "type": "string", "description": "Nom de l'application (ex. « Spotify ») ou dossier connu (Documents, Téléchargements…)" }
    },
    "required": ["target"]
  }
}
```

### 4.3 Catalogue initial

| Domaine | Outils | Risque |
|---|---|---|
| **Compagnon** | `pet.say`, `pet.animate`, `pet.feed`, `pet.start_work`, `pet.status` | Lecture seule / faible |
| **Système (lecture)** | `system.snapshot`, `system.active_window`, `system.time`, `system.battery` | Lecture seule |
| **Média** | `media.play_pause`, `media.next`, `media.previous`, `media.now_playing`, `audio.set_volume` (via `GlobalSystemMediaTransportControlsSessionManager` et `IAudioEndpointVolume`) | Faible |
| **Applications** | `apps.open` (index du menu Démarrer via `shell:AppsFolder`), `apps.list_running`, `apps.focus` | Faible |
| **Fichiers** | `files.search` (Windows Search / `SystemIndex` en OLE DB), `files.open`, `files.reveal` | Faible (lecture) / Sensible (ouverture d'exécutables) |
| **Web** | `web.open_url` (http(s) uniquement), `web.search` | Faible |
| **Productivité** | `timer.start` (minuteur Pomodoro avec l'animation de travail), `reminder.create` (notification Windows), `clipboard.read` / `clipboard.write` | Faible / Sensible (lecture du presse-papiers) |
| **Routines** | `routine.run` : scripts **déclarés à l'avance** dans `%APPDATA%\V-Max\routines\*.ps1` avec un manifeste (nom, description, paramètres) | Sensible |
| **Système (actions)** | `system.lock`, `system.sleep`, `apps.close` | Dangereux (confirmation à chaque fois) |

### 4.4 Garde-fous

- **Pas de shell libre.** Le modèle ne peut pas fournir de ligne de commande arbitraire. Seules les *routines* déclarées par l'utilisateur sont exécutables. Elles tournent dans PowerShell en `ConstrainedLanguage`, avec un délai maximum et une sortie tronquée.
- **Confirmation adaptée au risque.**
  - *Lecture seule* et *Faible* : exécution directe, journalisée.
  - *Sensible* : carte de confirmation dans la bulle (« Max veut ouvrir `rapport.xlsx` — Autoriser / Toujours / Refuser »), avec la portée « toujours » mémorisée par outil et par cible.
  - *Dangereux* : confirmation à chaque fois, jamais mémorisée.
- **Journal d'audit** consultable dans les paramètres : outil, arguments, décision, résultat.
- **Injection de prompt.** Le contenu lu par les outils (titres de fenêtres, fichiers, pages) est balisé comme *donnée* dans le contexte et ne peut pas débloquer une permission.
- **Plugins.** Ils peuvent enregistrer des outils, mais l'approbation des plugins doit d'abord être corrigée : la vérification de signature est cassée (audit S-01, S-02, S-04).

### 4.5 Ouverture : MCP

Ajouter un **client MCP** (Model Context Protocol) permettrait de brancher l'écosystème existant de serveurs d'outils (agenda, notes, domotique…) sans écrire de plugin C#. Chaque outil MCP serait importé dans le `ToolRegistry` avec le niveau de risque *Sensible* par défaut.

---

## 5. Animation dynamique : la machine à états au service de l'IA

### 5.1 Ce qu'offre déjà VPet

- Les animations sont rangées par **type** (`GraphType`), **nom**, **phase** (`A_Start` → `B_Loop` → `C_End` ou `Single`) et **humeur** (Happy, Nomal, PoorCondition, Ill) (`GraphInfo.cs`).
- `Main.Display(nom, phase, suite)` joue n'importe quelle animation par son nom. Un dossier au nom inconnu dans `pet/vup/` devient automatiquement une animation `Common` appelable par ce nom (`GraphInfo.cs:47-148`).
- Des points d'extension existent : `GraphDisplayHandler`, les délégués remplaçables (`DisplayNomal`, `SayRndFunction`), `WorkingState.Empty`.
- **Il existe déjà une animation `think`** (A/B/C), utilisée par `TalkBox.DisplayThink()` (`TalkBox.xaml.cs:70-81`).

### 5.2 Correspondance états de l'agent ↔ animations

| État `AgentState` | Animation existante (v1.0) | Animation dédiée (à produire) | Bulle |
|---|---|---|---|
| `Idle` | Comportement normal de VPet | — | — |
| `Listening` (après « Hey Max ») | `meowlook` (B en boucle forcée) | `listen` : main à l'oreille | Indicateur 🎙 + transcription en direct |
| `Thinking` | `think` (A → B en boucle) | — | « … » animé |
| `Searching` (recherche web/fichiers, réponse longue) | `study` / `studytwo` (lecture d'un livre) | `search` | Nom de l'outil en cours |
| `Acting` (outil en cours) | `workone` (dactylographie), `fixmenu` / `removeobject` (réparation) | `act` | « J'ouvre Spotify… » |
| `Speaking` | `Say` : `shining` (positif), `serious` (factuel), `shy` (excuse), choisi selon le ton | — | Texte en streaming |
| `AwaitingConfirmation` | `serious` (B en boucle) | `ask` | Carte Autoriser / Refuser |
| `Success` | `levelup` (Single) ou `switch_up` | — | ✓ |
| `Error` | `switch_down`, puis `serious` | `confused` | Message clair et bouton « Réessayer » |
| `Offline` (pas de réseau ou de clé) | `boring` | — | Lien vers les paramètres |

Les animations dédiées sont livrées comme **mod** (`mod/1000_vmax_agent/pet/vup/Listen/Nomal/{A,B,C}/…`), ce qui est le format natif. Le moteur est inchangé et le repli sur l'animation existante est automatique. La licence des animations d'origine impose que les nouvelles soient **créées pour V-Max** (et non dérivées des fichiers VUP-Simulator) pour pouvoir être distribuées librement.

### 5.3 `PetStateController` : conserver l'état malgré les interruptions

L'audit (UX, partie B) a relevé cinq sources qui interrompent une animation en cours :

1. un clic gauche (`Main.xaml.cs:435-439`) ;
2. l'arrêt forcé quand la faim ou la soif sont trop basses (`MainWindow.cs:606-689`) ;
3. le tirage aléatoire de `EventTimer` quand le type courant est `Default` ou `Work` (`MainLogic.cs:471-534`) ;
4. la détection de musique ;
5. les changements d'humeur (`PlaySwitchAnimat`).

Le contrôleur procède ainsi :

- Il passe `Main.State = WorkingState.Empty` pendant que l'agent est actif. Ce mode est prévu pour les développeurs et neutralise les comportements aléatoires.
- Il remplace temporairement `Main.DisplayNomal` pour revenir à l'animation de l'état IA au lieu de l'animation par défaut.
- Il s'abonne à `GraphDisplayHandler` pour **réaffirmer** l'état si un autre composant change l'animation.
- Il suspend `lowStrength` et la détection de musique pendant les états `Listening`, `Thinking`, `Acting` et `Speaking`.
- Il gère une **priorité** : l'utilisateur (clic, glisser) passe toujours avant l'agent. Un clic pendant `Thinking` montre une réaction courte, puis l'animation `think` reprend.
- Il restaure tout (`State = Nomal`, délégués d'origine) en revenant à `Idle`.

### 5.4 Bulle et streaming

Le pipeline actuel attend 4 signes de ponctuation ou 80 caractères avant d'afficher le moindre mot (`MainLogic.cs:56`, `TalkBox.xaml.cs:111`), puis attend la fin de l'animation `A_Start`. Pour un agent, la première réponse visible doit arriver **en moins de 300 ms** après le premier token :

- afficher la bulle immédiatement et lancer `Say` A/B en parallèle ;
- remplacer l'attente active par un `TaskCompletionSource` et ajouter l'annulation et les états d'erreur à `SayInfoWithStream` (audit A-10) ;
- rendu Markdown léger (gras, listes, code, liens) dans la bulle ;
- découper la réponse en phrases pour la synthèse vocale.

---

## 6. Vocal d'abord : « Hey Max »

### 6.1 Faisabilité : oui, en natif et en local

| Étage | Choix recommandé | Alternatives | Coût estimé |
|---|---|---|---|
| Capture | NAudio `WasapiCapture` (dépendance déjà présente), 16 kHz mono | — | négligeable |
| Détection d'activité vocale (VAD) | **Silero VAD** (ONNX, MIT) via `Microsoft.ML.OnnxRuntime` | WebRTC VAD | moins de 1 % d'un cœur |
| **Mot d'activation** | **openWakeWord** (Apache-2.0, ONNX) avec un modèle « Hey Max » entraîné sur des voix synthétiques françaises | Porcupine de Picovoice (mot personnalisé simple, mais licence commerciale) ; `KeywordRecognizer` du SDK Azure Speech (hors ligne, mot créé dans Speech Studio) | 1 à 3 % d'un cœur |
| Reconnaissance vocale (STT) | **Whisper local** (`Whisper.net`, modèle *small* ou *base* en français) pour la confidentialité | Entrée audio directe dans Gemini ; `Windows.Media.SpeechRecognition` (qualité variable en français) | ponctuel, après activation seulement |
| Synthèse vocale (TTS) | Voix Windows OneCore **fr-FR** (`Windows.Media.SpeechSynthesis`), gratuites et hors ligne | TTS cloud de meilleure qualité, en option | ponctuel |
| Mode conversation continue | **Gemini Live API** (audio bidirectionnel en streaming), à évaluer en phase 2 | — | réseau |

### 6.2 Pipeline

```
Micro ─► WASAPI 16 kHz ─► Silero VAD ─(voix?)─► openWakeWord ─(« Hey Max » > seuil)─►
   PetStateController.Listening + son discret
   ─► enregistrement jusqu'à 800 ms de silence (max 15 s) ─► STT ─► AgentOrchestrator
```

- **Rien ne quitte le PC avant le mot d'activation.** Le tampon audio circulaire de 2 s reste en mémoire et n'est jamais écrit sur disque.
- **Indicateur permanent** : l'animation d'écoute et l'icône de la zone de notification montrent quand le micro est actif.
- **Coupure automatique** pendant les visioconférences (application active) et en mode plein écran.
- **Repli** : un raccourci global de type *push-to-talk* (`RegisterHotKey`, via `Win32.cs`), utile aussi quand le micro est partagé. Aujourd'hui, l'application n'a **aucun** raccourci global (audit UX A4).
- **Risque principal** : la qualité du modèle « Hey Max » en français (faux positifs). Il faudra un jeu de test enregistré, un seuil réglable dans les paramètres avancés et, si nécessaire, une vérification en deux temps (mot d'activation local, puis confirmation par la reconnaissance vocale).

---

## 7. Socle technique préalable (repris de l'audit)

L'agent ne doit pas être branché sur les fondations actuelles sans ces corrections :

| Priorité | Chantier | Constats de l'audit |
|---|---|---|
| P0 | Sécurité des plugins : vraie vérification de signature, approbation liée au hachage, suppression de l'auto-confiance « LBGame » et « toute AC publique » | S-01 à S-05 |
| P0 | Données utilisateur dans `%APPDATA%\V-Max` (paramètres, sauvegardes, approbations) | S-07 |
| P0 | Secrets dans le Gestionnaire d'identification Windows ; rapports de bug sans données par défaut, avec masquage | P-03 |
| P1 | Hôte avec injection de dépendances, bus d'événements, invocation isolée des abonnés, journalisation | M-01, A-08 |
| P1 | Threads : une seule boucle de jeu, sauvegarde atomique et verrouillée, journaux bornés | A-10, A-11 |
| P1 | Performances au repos : suppression de l'attente active (musique toutes les 200 ms, `Thread.Sleep` dans des tâches), pause quand le compagnon est masqué ou en plein écran | voir AUDIT.md §Performances |
| P2 | Unification des frameworks (.NET 10), `Directory.Build.props`, CI, tests | B-01 à B-04 |

---

## 8. Autres opportunités manquées par VPet

| Opportunité | Idée | Briques |
|---|---|---|
| **Palette de commandes** | `Ctrl+Alt+Espace` ouvre un champ flottant (comme Spotlight) : texte libre vers l'agent, ou commandes directes | Raccourci global, overlay WPF |
| **Assistant proactif et bien-être** | Pauses actives après 50 minutes d'activité continue, rappel d'hydratation (la jauge « soif » prend tout son sens), mode concentration (Pomodoro avec l'animation de travail) | MetricsService (inactivité), notifications Windows |
| **Notifications Windows** | Notifications natives (*toast*) plutôt que des boîtes modales : l'audit relève 86 `MessageBox` | `Microsoft.Toolkit.Uwp.Notifications` / AppNotifications |
| **Vision d'écran (sur consentement)** | « Max, explique-moi cette erreur » : capture de la fenêtre active envoyée à Gemini (multimodal), avec aperçu et confirmation | Outil `screen.capture_active_window` (niveau Sensible) |
| **Mémoire personnelle** | Préférences (« j'écoute du jazz le soir »), faits, résumés ; consultable et effaçable | MemoryStore SQLite + embeddings optionnels |
| **Multi-écran intelligent** | Le compagnon suit l'écran actif, revient automatiquement quand un moniteur est débranché (aujourd'hui, il peut rester hors écran), et utilise la zone de travail (`WorkArea`) au lieu de l'écran entier | `SystemEvents.DisplaySettingsChanged`, identifiant de moniteur stable |
| **Thèmes système** | Clair/sombre/accentuation suivant Windows (aucune détection aujourd'hui), effet Mica/Acrylic | `UISettings`, `DwmSetWindowAttribute` |
| **Écosystème** | Outils MCP, routines partagées, packs d'animations | Client MCP, format de manifeste |
| **Accessibilité** | Navigation au clavier, lecteur d'écran, réduction des animations (aucun `AutomationProperties` aujourd'hui) | WPF Automation |

---

## 9. Feuille de route

| Phase | Contenu | Critère de sortie |
|---|---|---|
| **v1.0 (étape 4)** | Interface 2026 (overlays en verre dépoli, paramètres Basic/Advanced, micro-animations, thème système), corrections de performances au repos, socle P0/P1 (hôte, bus, secrets, données dans `%APPDATA%`), `PetStateController`, `IChatProvider` Gemini en texte avec streaming, 5 outils à faible risque (média, applications, web, minuteur, état du compagnon) | CPU au repos inférieur à 1 %, première réponse visible en moins de 1,5 s, aucune action sensible sans confirmation |
| **v1.1** | `MetricsService` et `StatsBridge` (modes Compagnon et Miroir), notifications Windows, palette de commandes, journal d'audit | Les jauges réagissent à la batterie et au CPU |
| **v1.2** | Voix : raccourci *push-to-talk*, Whisper, TTS fr-FR ; puis « Hey Max » (openWakeWord) en bêta | Moins d'un faux positif par heure sur le jeu de test |
| **v1.3** | Routines PowerShell déclarées, recherche de fichiers, mémoire, vision d'écran, client MCP | Sécurité revue, tests d'injection de prompt |
| **v2** | Animations dédiées (`listen`, `search`, `act`, `confused`), Gemini Live (conversation continue), marketplace de packs | — |

## 10. Risques

| Risque | Impact | Atténuation |
|---|---|---|
| Licence des animations VUP-Simulator (usage commercial soumis à autorisation) | Bloquant pour une commercialisation | Obtenir l'accord écrit, ou produire un personnage V-Max original (recommandé à moyen terme) |
| Coût et latence de l'API Gemini | Expérience et budget | Modèle rapide par défaut, mise en cache du contexte, quotas visibles par l'utilisateur |
| Actions de l'agent sur le système | Sécurité, confiance | Niveaux de risque, confirmations, pas de shell libre, journal d'audit |
| Faux positifs du mot d'activation | Gêne, vie privée | Seuil réglable, coupure en visioconférence, indicateur visible, *push-to-talk* par défaut |
| Compatibilité des mods VPet | Écosystème | Façade `IMainWindow` conservée, tests de non-régression sur les mods officiels |
