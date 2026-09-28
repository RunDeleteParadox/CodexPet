# Plugin Codex Pet

[English](README.md) | [Français](README.fr.md)

Ce plugin Windows PowerShell 5.1 envoie les états Codex à **CodexPetHub** par un
Named Pipe local. Il n'utilise aucun port COM et n'envoie aucune commande
directement au firmware.

États : repos, réflexion, travail, attente. Réactions : fin de réponse, erreur,
arrêt. Après Stop, la fin d'un tour doit être confirmée explicitement par le
lecteur local ; ni le temps écoulé ni un outil réussi ne déclenchent une célébration.
Le lecteur ne charge pas les messages et ne démarre ni ne reprend de conversation.

Depuis le dossier de ce plugin :

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/CodexPet.ps1 -Action status
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/CodexPet.ps1 -Action disable
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/CodexPet.ps1 -Action enable
```

Les données locales sont dans `%USERPROFILE%\.codex-pet-plugin\v1.1`.
Les prompts, arguments et résultats bruts ne sont pas stockés ni envoyés au Hub.
Les identifiants de session, tour et outil restent dans ce dossier local.

Configure `completionCodexPath` avec `Configure-CompletionApi.py`, fourni à la
racine du composant CodexPetPlugin. L'API est expérimentale : vérifie le chemin
de `codex.exe` après une mise à jour. `completionStatus` renseigne sa disponibilité.

Après réinstallation, approuve les hooks si Codex le demande et ouvre une nouvelle
conversation. Le README du dépôt et son guide utilisateur expliquent l'installation
complète. Ne garde qu'une copie de Codex Pet activée.

Copyright © 2026 RunDeleteParadox et contributeurs. **AGPL-3.0-or-later**, sans garantie.
Voir [LICENSE](LICENSE), [NOTICE](NOTICE) et [notices](THIRD_PARTY_NOTICES.md).
