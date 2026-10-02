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
