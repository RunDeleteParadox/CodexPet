# CodexPetHub

[English](README.md) | [Français](README.fr.md)

Application Windows en zone de notification, **version 1.2.0**. Le Hub transforme
les états Codex reçus du plugin en animations et possède la connexion USB du Pet.

Consulte le [guide utilisateur complet](../docs/USER-GUIDE.fr.md) pour installer
l'ensemble, le configurer, le dépanner, le mettre à jour et le désinstaller.

## Compiler et démarrer

Prérequis de compilation : Windows et SDK .NET 10. Depuis ce dossier :

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/Build.ps1 -Publish
.\artifacts\publish\CodexPetHub.exe
```

La distribution Windows x64 est autonome : garde tous ses fichiers, dont `fr`,
les notices et `CodexPetHub-source.zip`. Le SDK n'est pas nécessaire pour l'exécuter.

## Fenêtre et réglages

Double-clique sur l'icône pour ouvrir les **Paramètres**. Les pages **Paramètres**,
**Diagnostic**, et **Licence et crédits** sont réunies dans une fenêtre, avec
navigation à gauche. Les trois entrées du menu contextuel sélectionnent la bonne
page ; Paramètres est en gras. Fermer la fenêtre laisse le Hub dans la systray.

Changer de page conserve les modifications. **Enregistrer** les applique sans
fermer la fenêtre ; **Annuler** recharge les préférences sauvegardées. Fermer
la fenêtre abandonne les changements non enregistrés. La langue est prévisualisée
dans toute la fenêtre et appliquée au menu après enregistrement.

Choix disponibles : anglais/français, nom du Pet, port automatique ou COM,
luminosité, délai de reconnexion, son de fin de réponse, démarrage automatique,
veille au verrouillage ou à l'extinction de l'écran Windows.

Les préférences sont dans `%USERPROFILE%\.codex-pet-hub\config.json`.
Le numéro COM peut changer ; l'identité matérielle reste la référence du Pet.

## Tests et diagnostic

Le menu **Tester un état** permet un essai manuel. **Réponse terminée** joue le son
choisi ; cela demande le firmware 1.1.7. Désactive **Conserver le test actif** pour
revenir aux vrais événements Codex.

Le Diagnostic distingue la connexion du plugin, la liaison USB et la santé des
boucles de traitement. Son export inclut des chemins locaux et identifiants
matériels ; vérifie le ZIP avant de le partager. **Quitter** arrête le Hub et libère
le port série, notamment avant de flasher le firmware.

Références : [protocoles](docs/PROTOCOLS.fr.md), [traductions](docs/LOCALIZATION.md),
[contribution](CONTRIBUTING.md), [documentation](docs/README.md).

## Licence

Copyright © 2026 RunDeleteParadox et contributeurs. **AGPL-3.0-or-later**, sans garantie.
Voir [LICENSE](LICENSE), [NOTICE](NOTICE), [notices](THIRD_PARTY_NOTICES.md) et
[sources correspondantes](SOURCE.md). Les crédits du Hub reconnaissent aussi le
firmware original de trentct.
