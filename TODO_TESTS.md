# V-Max — tests manuels à faire (session QA finale)

Ces points n'ont pas pu être vérifiés automatiquement : ils demandent une vraie souris, une vraie clé d'API,
un vrai bureau Windows ou plusieurs jours d'utilisation. Cocher chaque ligne pendant la session de QA globale.

Légende : **[B]** bloquant pour une version publique · **[N]** normal · **[C]** confort.

## 1. Démarrage et finition visuelle
- [ ] **[B]** Premier lancement (cache des animations vide) : l'écran d'accueil s'affiche, le pourcentage « Préparation des animations » avance, puis fondu vers le compagnon.
- [ ] **[N]** Lancements suivants : écran d'accueil bref, aucune fenêtre ni bandeau de l'ancien moteur.
- [ ] **[N]** Un plugin qui plante au démarrage : notification courte, détails dans `%APPDATA%\V-Max\logs\demarrage.log`.
- [ ] **[N]** Boîtes de dialogue V-Max (Oui/Non, erreur avec « Copier ») : clavier (Entrée, Échap), glisser la fenêtre, thème clair.
- [ ] **[C]** Pastille de message du compagnon (gains de jauges) lisible en thème clair et sombre.

## 2. Interface HUD
- [ ] **[B]** Clic droit sur le compagnon → anneau ; chaque satellite ouvre le bon panneau.
- [ ] **[B]** Ctrl+Alt+Espace ouvre la discussion depuis n'importe quelle application (et la ramène au premier plan).
- [ ] **[N]** Panneaux (État, Garde-manger, Occupations, Plus) : suivent le compagnon quand on le déplace ; un seul ouvert à la fois.
- [ ] **[N]** Changement de thème clair/sombre de Windows pendant que V-Max tourne.
- [ ] **[N]** Entrées ajoutées par un plugin tiers présentes dans « Plus ».

## 3. IA
- [ ] **[B]** Vraies clés : Gemini, Groq, Mistral, Cerebras, OpenRouter (bouton Connecter, vérification, discussion).
- [ ] **[N]** Bascule automatique : épuiser le quota d'un fournisseur (ou clé invalide) → le suivant répond.
- [ ] **[N]** IA locale : Ollama et LM Studio détectés sans configuration.
- [ ] **[N]** « Va dans la cuisine » demandé à l'agent → le compagnon y va.

## 4. Habitat : éditeur (à la souris)
- [ ] **[B]** Outil Sol : tracer, aimantation entre sols et sur les bords, déplacer un sol et ses extrémités, Suppr.
- [ ] **[B]** Outil Escalade : extrémités qui s'aimantent et deviennent vertes ; côté de la paroi.
- [ ] **[N]** Outils Chute, Pièce (nom, type), Emplacement (fantôme de l'activité avec ses meubles).
- [ ] **[N]** Zoom molette, défilement clic droit / clic molette, « Ajuster », annuler / rétablir.
- [ ] **[N]** « Détecter les sols » sur ton propre fond d'écran.
- [ ] **[N]** « Suggérer les pièces » avec une vraie clé Gemini (carte de consentement, qualité des cadres).
- [ ] **[C]** Ctrl + molette sur le compagnon : taille dans l'habitat et sur le bureau (notification en % de l'écran).

## 5. Habitat : vie autonome
- [ ] **[B]** Win+D : le compagnon passe sur le vrai fond d'écran (carte du fond d'écran), puis revient dans l'habitat quand une application reprend la main.
- [ ] **[N]** Inactivité (délai réglable) : sortie sur le bureau, retour au premier mouvement.
- [ ] **[N]** Fermer la fenêtre habitat (Alt+F4) : V-Max reste ouvert, compagnon rendu au bureau.
- [ ] **[B]** **Vivre dans le fond d'écran (expérimental)** : le compagnon est visible ENTRE le fond d'écran et les fenêtres, sur chacun des 3 écrans ; décrochage propre en désactivant l'option.
- [ ] **[N]** Plusieurs écrans / échelles d'affichage différentes : fenêtre habitat et bureau.

## 6. Routines de vie (sur plusieurs jours)
- [ ] **[B]** Heure tirée différente chaque jour, identique après un redémarrage le même jour.
- [ ] **[N]** Plage qui passe minuit (sommeil 23 h 00 → 01 h 30), réveil à la durée tirée.
- [ ] **[N]** Fréquence « 1 fois sur 2 », « seulement s'il en a besoin », pause entre deux routines, il traîne après un repas.
- [ ] **[N]** Autonomie financière : presque à sec → il part travailler (bureau de l'habitat s'il existe).
- [ ] **[N]** Démarrer V-Max au milieu d'une plage : la routine est rattrapée ; après la plage : sautée.

## 7. Bac à sable et économie
- [ ] **[N]** Argent illimité : « ∞ $ » partout ; en le désactivant, la vraie somme revient.
- [ ] **[N]** Jauges qui ne baissent jamais ; repas et cadeaux gratuits (effets conservés).

## 8. Partage
- [ ] **[N]** Exporter un habitat `.vmaxhome`, l'importer sur une autre session (ou après avoir supprimé la carte locale).
- [ ] **[C]** Importer un paquet abîmé : message clair, rien d'installé.

## 9. Voix
- [ ] **[B]** Vrai micro : maintenir « ² », parler, relâcher → bulle d'écoute, texte transcrit dans la discussion, réponse du compagnon.
- [ ] **[B]** Réponse lue à voix haute en français (Julie, Paul, Hortense) ; changer de voix et de débit dans Paramètres › Voix.
- [ ] **[N]** Transcription avec une vraie clé Groq (Whisper), puis avec Gemini seul, puis hors ligne (option « hors ligne uniquement »).
- [ ] **[N]** « Hey Max » activé : déclenche l'écoute ; pas de déclenchement intempestif pendant une heure de musique ou de vidéo.
- [ ] **[N]** Autre touche de *push-to-talk* choisie dans les paramètres ; la touche n'est pas « avalée » dans les autres applications quand la voix est coupée.
- [ ] **[C]** Micro débranché ou refusé par Windows : message clair, pas de plantage.

## 10. Fenêtres V-Max (remplacent celles de VPet)
- [ ] **[B]** Plus aucune fenêtre de l'ancien moteur : menus du compagnon, zone de notification, paramètres, plugins tiers (`ShowSetting(page)`).
- [ ] **[N]** Planning : démarrer une occupation, construire un emploi du temps avec pauses, signer un contrat (niveau 15+).
- [ ] **[N]** Galerie : débloquer une photo payante, favoris, export d'une et de plusieurs photos, visionneuse (← →, GIF animés).
- [ ] **[N]** Panneau du compagnon : générer le bilan de l'année et l'enregistrer en PNG ; carte d'anniversaire avec image et date ; journal qui se met à jour en direct.
- [ ] **[N]** Sac : utiliser un objet, favoris, tri ; garde-manger ouvert depuis les anciens boutons « Manger », « Boire »…
- [ ] **[N]** Sauvegardes : revenir à une sauvegarde automatique, puis à une copie de secours.
- [ ] **[N]** Mods : activer, désactiver, autoriser le code d'un mod, redémarrer ; raccourcis personnalisés (programme, site, capture de touches), enregistrement et apparition dans « Plus ».
- [ ] **[N]** Signaler un problème : le rapport est copié, le ticket GitHub s'ouvre ; sauvegarde jointe avec clés et mots de passe masqués.
- [ ] **[C]** Saisie de texte demandée par un plugin (`ShowInputBox`) : Entrée valide, Échap annule.

## 11. Mods, Studio et démarrage
- [ ] **[B]** Démarrage normal (cache déjà construit) : compagnon visible en 2 s environ ; relevé dans `%APPDATA%\V-Max\logs\demarrage-temps.log`.
- [ ] **[N]** Premier démarrage après « Vider le cache des animations » : compagnon visible en moins de 5 s, puis aucune animation figée ou sautée pendant le préchauffage (caresses, occupations, repas, Habitat).
- [ ] **[N]** Studio : 7 clics sur la version (Paramètres › À propos) → message « Studio débloqué », Ctrl+Maj+F12 l'ouvre depuis n'importe quelle application ; « Masquer le Studio » retire le raccourci.
- [ ] **[N]** Studio : lire chaque type d'animation (enchaînement, humeurs, ×0,5 / ×2, image par image), donner un verdict à la souris et au clavier (1 à 4, ↑/↓), le retrouver après redémarrage.
- [ ] **[N]** Traduction réelle avec ta clé Gemini (`python tools/mods/translate.py --verdicts garder,integrer`) : relire un échantillon dans le Studio (Max, tutoiement, pas de « maître »).
- [ ] **[C]** Un mod de la liste d'exclusion copié dans `mod\` n'est pas chargé.
- [ ] **[N]** Studio : glisser un dossier de mod (ou un .zip, ou un dossier contenant plusieurs mods) dans la fenêtre → cadre « Dépose ici », mod copié et affiché ; bouton + (choix de dossiers) ; un mod refusé affiche la raison et n'est pas copié.
