# Mods du Workshop → V-Max

Chaîne de traitement des mods VPet téléchargés (dossier `MOD/`, jamais versionné).

```
MOD/1920960 ─► ingest.py ─► catalog.json ─► Studio (aperçu + verdicts) ─► translate.py ─► translations.fr.json
                               │                                                         (puis intégration)
                               └─ mods exclus : jamais lus au-delà de leur fiche, refusés par V-Max (ModBlocklist)
```

Tous les fichiers produits vont dans `%APPDATA%\V-Max\studio\`.

## 1. Catalogue

```
python tools/mods/ingest.py MOD/1920960
```

- Lit chaque mod : fiche, occupations, animations (classées comme le fait le jeu), nourriture, répliques, plugins.
- Le classe dans une catégorie : Travail, Études, Loisir, Attitude, Nourriture, Système, Dialogues ou Langue.
- Les fichiers `*.codex-*.bak` laissés par un renommage précédent sont les originaux : ce sont eux qui sont lus.
- **Exclu** : mods à caractère sexuel, repérés par leur titre ou par leur contenu. Ils ne sont ni traduits ni montrés dans le Studio, et V-Max refuse de les charger.
- **Répliques bloquées** : une réplique ambiguë dans un mod par ailleurs anodin est écartée seule, sans exclure le mod.

## 2. Studio (tri)

Dans V-Max : Paramètres › À propos, 7 clics rapprochés sur le numéro de version, puis **Ctrl+Maj+F12**.

- **Lecteur** : lit les images d'origine, sans déblocage ni argent.
- **Verdict** : Garder, Intégrer au jeu, À revoir ou Supprimer, avec une note facultative. Il est enregistré dans `verdicts.json`.
- **Ajout manuel** : glisse un dossier de mod, un dossier de plusieurs mods ou une archive `.zip` dans la fenêtre (ou bouton +). Le mod est copié dans `studio\mods\`, lu avec les mêmes règles et les mêmes contrôles, puis ajouté au catalogue ; `ingest.py` conserve ces ajouts quand il régénère le catalogue.
- **Clavier** : 1 à 4 pour le verdict, ↑/↓ pour changer de mod, Espace pour lecture ou pause, ←/→ pour avancer image par image.

## 3. Traduction

```
python tools/mods/translate.py --dry-run                  # estimation, sans appel
python tools/mods/translate.py --verdicts garder,integrer # seulement les mods retenus
```

- **Clé** : celle de V-Max (Paramètres › IA, stockée dans le Gestionnaire d'identifiants Windows), ou la variable `GEMINI_API_KEY`.
- **Règles de traduction** :
  - le personnage s'appelle Max ;
  - l'utilisateur est tutoyé (jamais « maître ») ;
  - les variables `{0}`, `{name}` et `/n` sont vérifiées sur chaque réponse.
- **Reprise sans frais** : `tm.fr.json` mémorise ce qui est déjà traduit, une relance ne repaie rien.

## Mesurer le démarrage avec ces mods (développement)

```
set VMAX_EXTRA_MODS=C:\chemin\vers\un\dossier\de\mods
```

Les mods de ce dossier sont chargés et actifs sans toucher aux paramètres. Le relevé du démarrage est écrit dans `%APPDATA%\V-Max\logs\demarrage-temps.log`.

## Noms des mods en français (sans clé API)

Les **noms visibles** des mods (occupations, aliments, noms de mods) sont traduits à la main dans
`tools/mods/fr_names.py`. Pour les appliquer aux mods chargés (écrit un `lang/fr/vmax-trad.lps` dans chaque mod) :

```
python tools/mods/apply_names.py
```

Ça couvre les listes (planning, routines, garde-manger). Les **dialogues** et **descriptions** passent, eux, par
`translate.py` (Gemini), qui nécessite une clé.

