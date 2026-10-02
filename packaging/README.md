# Fabriquer et diffuser Maxine

Maxine est packagée avec **Velopack** : il fabrique un installeur « un clic », crée le raccourci, et gère la
mise à jour automatique depuis les **Releases GitHub**. Pas d'Inno Setup : Velopack remplit le même rôle et
ajoute en plus les mises à jour par deltas (voir « Pourquoi Velopack seul » plus bas).

## Une fois, sur ta machine

```powershell
dotnet tool install -g vpk      # l'outil de packaging Velopack
```

Pour publier sur GitHub, il faut un jeton : GitHub › Settings › Developer settings › Tokens, droit `repo`,
puis `setx GITHUB_TOKEN "ghp_..."` (rouvre le terminal ensuite).

## Fabriquer un installeur

```powershell
powershell -ExecutionPolicy Bypass -File packaging\pack.ps1 -Version 1.0.0
```

Ça produit `packaging/releases/Maxine-win-Setup.exe` : un installeur autonome (le runtime .NET 10 est inclus,
aucun prérequis), qui installe pour l'utilisateur courant, crée le raccourci bureau et lance Maxine.
La case « Me lancer au démarrage de Windows » est proposée, cochée, dans la carte d'accueil au premier lancement.

> La première version pèse ~1 Go : les animations de base de Maxine (`mod/0000_core`, 6 300 images) en font
> l'essentiel. C'est le prix d'entrée une seule fois — les mises à jour suivantes ne téléchargent que les
> **différences** (deltas), donc quelques Mo.

## Pousser une mise à jour à distance

Quand tu veux corriger un bug ou ajouter une vanne, incrémente la version et publie :

```powershell
powershell -ExecutionPolicy Bypass -File packaging\pack.ps1 -Version 1.0.1 -Upload
```

Au prochain lancement sur le PC d'un ami, Maxine voit la nouvelle release, propose « Mise à jour disponible »,
télécharge le delta et redémarre sur la nouvelle version. **Rien ne s'installe sans un clic de la personne.**

## Pourquoi Velopack seul (et pas Inno Setup en plus)

Inno Setup et Velopack font le même travail d'installeur. Les coupler, c'est deux installeurs qui se marchent
dessus (chemins, raccourcis, désinstallation en double) et ça casse le mécanisme de mise à jour par deltas,
qui est justement la raison d'utiliser Velopack. Velopack fournit déjà l'installeur « un clic », le raccourci,
la désinstallation propre et l'auto-update GitHub. Si un jour tu veux l'assistant d'installation classique
avec des pages (licence, dossier…), on pourra l'ajouter **autour** de Velopack, mais ce n'est pas nécessaire ici.

## Comment c'est branché dans le code

- `App.xaml.cs` : `VelopackApp.Build().Run()` en toute première instruction (Velopack intercepte les étapes
  d'installation et de mise à jour).
- `Function/UpdateService.cs` : au lancement, sur une installation faite par l'installeur uniquement, vérifie
  les Releases GitHub et propose la mise à jour. En développement, c'est un no-op.
