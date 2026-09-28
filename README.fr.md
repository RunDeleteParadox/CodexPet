# CodexPet

[English](README.md) | [Français](README.fr.md)

Un compagnon animé sur **M5Stack StopWatch** qui affiche l'activité de Codex.
Projet maintenu par **RunDeleteParadox**, pour Windows et un Pet connecté en USB.

Le firmware dérive de **KK / M5Stack StopWatch Avatar**, créé par **trentct et ses
contributeurs** : [projet d'origine](https://github.com/Trentct/m5stack-stopwatch-avatar).
Sa licence, ses crédits et son historique Git sont conservés.

## Commencer

Le [guide utilisateur complet](docs/USER-GUIDE.fr.md) explique les prérequis,
l'installation du firmware, du Hub et du plugin, puis l'utilisation, le dépannage,
les mises à jour et la désinstallation. [English user guide](docs/USER-GUIDE.md).

| Composant | Rôle |
| --- | --- |
| [CodexPetHub](CodexPetHub/README.fr.md) | Interface Windows en français et anglais, réglages et liaison USB |
| [CodexPetPlugin](CodexPetPlugin/README.fr.md) | Hooks Codex et états transmis au Hub par un canal local |
| [CodexPetFirmware](CodexPetFirmware/README.fr.md) | Firmware ESP32-S3, animations, veille et sons |

```text
Codex → Plugin → Named Pipe local → Hub → USB / série → Pet
```

Un seul dépôt contient les trois composants ; aucun sous-module n'est nécessaire.
Seul le Hub ouvre le port série. Les prompts et transcriptions ne vont pas au Pet.

## Compiler le Hub

Avec le SDK .NET 10, depuis la racine du dépôt :

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File CodexPetHub/scripts/Build.ps1 -Publish
.\CodexPetHub\artifacts\publish\CodexPetHub.exe
```

Double-clique sur l'icône de la systray pour ouvrir les Paramètres. La navigation
à gauche donne accès au Diagnostic et à la Licence et aux crédits. Les préférences
restent dans le profil utilisateur, en dehors du dépôt.

## Documentation et contribution

- [Index bilingue](docs/README.md)
- [Contribution et vérifications](CONTRIBUTING.md)
- [Crédits](CREDITS.md)
- [Licence et distribution](CodexPetHub/docs/LICENSING.md)

La documentation utilisateur est maintenue en français et en anglais. Les notes
de travail personnelles sont conservées localement dans `.local/notes`, ignoré par Git.

## Licence

Les trois composants utilisent **AGPL-3.0-or-later**. Voir [LICENSE](LICENSE),
[NOTICE](NOTICE) et les notices de chaque composant. Les dépendances conservent
leurs propres licences. Toute distribution binaire doit proposer les sources
correspondantes et les notices applicables.

Projet communautaire, sans affiliation ni approbation d'OpenAI ou de M5Stack.
