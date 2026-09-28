# CodexPetPlugin

[English](README.md) | [Français](README.fr.md)

Plugin Windows PowerShell 5.1 qui transforme les hooks Codex en états et les
transmet à CodexPetHub par un **Named Pipe local**. Il n'ouvre aucun port série
et ne contrôle pas directement le M5Stack.

## Installer

Suis le [guide utilisateur](../docs/USER-GUIDE.fr.md), qui couvre aussi la
configuration du lecteur de fin de réponse et l'approbation des hooks. Depuis
la racine commune du dépôt :

```powershell
codex plugin marketplace add .\CodexPetPlugin
codex plugin add codex-pet@codexpet
```

Le catalogue local est inclus dans `.agents/plugins/marketplace.json`.
Active une seule copie du plugin. L'ancien prototype série et une copie provenant
d'un autre catalogue doivent être désactivés avant une migration.
Ouvre une nouvelle conversation Codex après installation ou réinstallation.

## Fonctionnement

Les hooks installés couvrent SessionStart, SessionEnd, UserPromptSubmit,
PreToolUse, PostToolUse, PermissionRequest, Stop, Interrupt, PreCompact et PostCompact.
Ils appellent `Invoke-CodexPetHook.ps1`, qui enregistre les métadonnées autorisées
dans une file locale. Un processus PowerShell les agrège et transmet l'état au Hub.

États : repos, réflexion, travail et attente d'une réponse. Réactions : fin de
réponse, erreur et arrêt. Le Hub gère animations, priorités, veille et réveil.
La dernière conversation sollicitée sélectionne le Pet ; les outils parallèles
sont corrélés sans laisser les doublons ou événements anciens relancer une activité.

Après Stop, un lecteur local en lecture seule demande `thread/turns/list`, sans
charger les messages. Une célébration exige le même tour, `completed`, un
`completedAt` entier positif et aucune erreur. Un délai, Stop seul, un outil
réussi ou un tour interrompu ne suffisent jamais. Une activité nouvelle annule
la vérification. Si l'API est indisponible, la célébration peut manquer.

## Données et commandes

Les données sont dans `%USERPROFILE%\.codex-pet-plugin\v1.1`. Les identifiants
de sessions, tours et outils restent locaux au plugin. Les prompts, arguments,
résultats bruts et transcriptions ne sont pas transmis au Hub ni au Pet.

Depuis ce dossier :

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File plugins/codex-pet/scripts/CodexPet.ps1 -Action status
```

Les actions `enable` et `disable` activent ou arrêtent le relais. `send` est un
test manuel qui peut changer l'affichage réel ; utilise de préférence le menu de
test du Hub. `Configure-CompletionApi.py` configure le lecteur local, avec aperçu
par défaut et sauvegardes lors de `--apply`.

`Switch-Plugin.ps1` est un ancien outil de migration spécifique au développement.
Il n'est pas nécessaire pour une installation utilisateur.

## Développement et licence

Le [README anglais](README.md) détaille les événements et les tests sans matériel.
Les essais utilisent des données et canaux privés sous `artifacts`.
Les comptes rendus privés restent dans `.local/notes` à la racine du dépôt.

Copyright © 2026 RunDeleteParadox et contributeurs. **AGPL-3.0-or-later**, sans garantie.
Voir [LICENSE](LICENSE), [NOTICE](NOTICE), [notices](THIRD_PARTY_NOTICES.md) et [SOURCE.md](SOURCE.md).
