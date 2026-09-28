# Guide utilisateur CodexPet

[English](USER-GUIDE.md) | [Français](USER-GUIDE.fr.md)

CodexPet affiche l'activité de Codex sur un M5Stack StopWatch. Le Hub Windows relie
le plugin Codex à l'appareil. Ce guide concerne un Pet connecté en USB.

## 1. Prérequis

- Un PC Windows x64 avec l'application Codex ou sa CLI, compatible avec les plugins et les hooks.
- Un **M5Stack StopWatch** et un câble USB permettant le transfert de données.
- Ce dépôt, téléchargé en ZIP puis extrait, ou cloné avec Git.
- Pour compiler le Hub : le **SDK .NET 10**. Une distribution autonome déjà compilée n'en a pas besoin.
- Pour configurer le plugin : Windows PowerShell 5.1, inclus dans Windows, Python 3.11+
  et l'exécutable natif `codex.exe` accessible à la CLI.
- Pour installer le firmware : Python et **PlatformIO Core 6.1.18**.

Ouvre PowerShell à la racine du dépôt extrait, dans le dossier contenant les trois
composants. Toutes les commandes suivantes partent de ce dossier. L'utilisation
normale ne demande ni session administrateur ni moniteur série.

## 2. Préparer le Pet

Tu peux sauter l'installation du firmware si ton StopWatch possède déjà une
version CodexPet compatible. Le firmware original de trentct doit être remplacé
par le fork CodexPet. La version actuelle est 1.1.7 ; le Hub demande au moins
1.1.0, et les sons nécessitent 1.1.7.

Quitte le Hub depuis son menu et ferme les moniteurs série avant de flasher.
Branche le StopWatch et repère son port COM dans le Gestionnaire de périphériques.

```powershell
python -m pip install platformio==6.1.18
python -m platformio run --project-dir CodexPetFirmware
$petPort = Read-Host 'Port COM du StopWatch, par exemple COM3'
python -m platformio run --project-dir CodexPetFirmware --target upload --upload-port $petPort
```

Cette opération remplace le firmware de l'appareil. Vérifie que le port choisi
correspond au bon StopWatch. Le [guide firmware](../CodexPetFirmware/README.fr.md)
précise la compilation et la procédure de récupération.

## 3. Démarrer le Hub

Si tu disposes d'une distribution Windows complète, extrais-la dans un dossier
stable puis lance `CodexPetHub.exe`. Garde ensemble les DLL, le dossier `fr`, les
licences et l'archive des sources. Sinon, compile depuis la racine du dépôt :

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File CodexPetHub/scripts/Build.ps1 -Publish
.\CodexPetHub\artifacts\publish\CodexPetHub.exe
```

L'icône peut se trouver dans les icônes masquées de Windows. Double-clique dessus
pour ouvrir les **Paramètres**. Choisis anglais ou français puis enregistre.
Pour un seul appareil, garde le port **Automatique** ; sélectionne un port COM
si plusieurs appareils sont détectés. Le Hub mémorise l'identité matérielle du
Pet même si Windows change ensuite son numéro de port.

## 4. Installer le plugin Codex

Démarre Codex et connecte-toi une première fois. Vérifie que `codex --version`
fonctionne dans PowerShell. Depuis la racine du dépôt :

```powershell
codex plugin marketplace add .\CodexPetPlugin
codex plugin add codex-pet@codexpet
powershell.exe -NoProfile -ExecutionPolicy Bypass -File CodexPetPlugin/plugins/codex-pet/scripts/CodexPet.ps1 -Action enable
$codexExe = (Get-Command codex.exe -CommandType Application).Source
python CodexPetPlugin/Configure-CompletionApi.py --codex $codexExe
python CodexPetPlugin/Configure-CompletionApi.py --codex $codexExe --apply
```

La première commande Python affiche les changements prévus. La seconde enregistre
le chemin du lecteur de fin de réponse et crée des sauvegardes, en conservant les
autres réglages Codex et le notificateur existant. Elle attend le fichier
`%USERPROFILE%\.codex\config.toml`. Si tu utilises un autre dossier Codex, passe
son fichier avec `--config`. Sur une première installation sans ce fichier, tu
peux créer un `config.toml` vide à cet emplacement avant de lancer le script.
Utilise un vrai `codex.exe`, et non un lanceur npm `.cmd` ou `.ps1` ; repère
l'exécutable natif si ta commande habituelle passe par un lanceur.

Redémarre Codex si le plugin n'apparaît pas. Vérifie et approuve ses hooks dans
les paramètres de hooks de Codex (`/hooks` dans la CLI), puis ouvre une **nouvelle
conversation**. Installer le plugin ne suffit pas à approuver ses hooks.
Voir la [documentation officielle OpenAI](https://developers.openai.com/plugins/build/plugins).

Active une seule copie de Codex Pet. Si tu avais installé `codex-pet@personal`
ou l'ancien prototype qui accédait directement au port série, désactive cette
copie avant d'activer la nouvelle. Le script `Switch-Plugin.ps1` concerne une
installation de développement particulière et n'est pas nécessaire ici.
Conserve le dépôt source pour les mises à jour du catalogue local.

## 5. Utilisation quotidienne

Le Hub dispose d'une seule fenêtre avec **Paramètres**, **Diagnostic** et **Licence
et crédits** dans la navigation à gauche. Les trois entrées du menu de la systray
ouvrent cette fenêtre sur la bonne page. **Paramètres** est en gras car c'est
l'action du double-clic.

- Changer de page conserve les modifications en cours. **Enregistrer** les applique
  sans fermer la fenêtre ; **Annuler** recharge les préférences enregistrées.
  Fermer la fenêtre abandonne les modifications non enregistrées.
- Le choix de langue est prévisualisé dans toute la fenêtre. L'enregistrement
  applique aussi la langue au menu de la systray.
- Les Paramètres permettent de choisir luminosité, nom du Pet, son de fin de
  réponse, veille liée à Windows et démarrage à l'ouverture de session.
- Fermer la fenêtre laisse le Hub actif dans la systray. **Quitter** l'arrête.
- **Tester un état → Réponse terminée** joue le son choisi. **Conserver le test actif**
  maintient les tests manuels ; désactive-le pour revenir à l'activité Codex.

| Affichage | Signification |
| --- | --- |
| Au repos | Aucune activité sélectionnée |
| Réfléchit | Le tour Codex sélectionné est actif, sans outil actif |
| Travaille | Un ou plusieurs outils sont actifs |
| Attend ton intervention | Une demande d'information ou d'autorisation a été observée |
| Réponse terminée | Le tour correspondant est explicitement terminé ; cela ne certifie pas sa justesse |
| Erreur / Interrompu | Erreur d'un outil / arrêt ou interruption neutre |

Après Stop, le plugin vérifie la fin du tour correspondant avec une API locale
en lecture seule. Le silence ou le temps écoulé ne suffisent jamais. La dernière
conversation sollicitée pilote le Pet ; celles en arrière-plan ne la remplacent pas.

## 6. Connexion et dépannage

Le chemin actuel est **hooks Codex → plugin → canal local Named Pipe → Hub → USB → Pet**.
Seul le Hub ouvre le port série. Le plugin ne possède aucun accès direct de secours
au Pet. L'ancien prototype utilisait le port série directement : c'est une autre installation.

- **Pet déconnecté :** vérifie le câble USB et le port, ferme les moniteurs série,
  choisis **Reconnecter**, puis consulte le Diagnostic. Débrancher/rebrancher peut rétablir l'USB.
- **Pet connecté mais sans activité Codex :** vérifie le statut distinct du **Plugin**,
  son activation et la confiance des hooks, puis ouvre une nouvelle conversation.
- **Pas de son :** enregistre un son autre qu'Aucun, vérifie le firmware 1.1.7+
  et teste Réponse terminée. Un signal de fin absent empêche aussi la célébration.
- **Plus de célébration après une mise à jour Codex :** vérifie que `completionCodexPath`
  désigne toujours un exécutable natif existant, puis relance le script de configuration.
- **Écran noir :** le Pet peut être en veille. Choisis **Réveiller** avant de conclure à une panne.
- **Traitement en erreur ou bloqué :** exporte le diagnostic, puis quitte et relance le Hub.

Statut du plugin depuis la racine du dépôt :

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File CodexPetPlugin/plugins/codex-pet/scripts/CodexPet.ps1 -Action status
```

Les réglages et journaux du Hub sont dans `%USERPROFILE%\.codex-pet-hub` ; l'état
du plugin est dans `%USERPROFILE%\.codex-pet-plugin\v1.1`. Les prompts, résultats
bruts d'outils et transcriptions ne sont pas envoyés au Pet. **Exporter le diagnostic**
inclut des chemins locaux et des identifiants d'appareil : vérifie le ZIP avant de le partager.

## 7. Mettre à jour ou désinstaller

Quitte le Hub avant de remplacer tout son dossier de distribution. Les préférences
restent dans ton profil. Si tu déplaces l'exécutable, enregistre le réglage de
démarrage automatique depuis le nouvel emplacement. Mets le firmware à jour
séparément, avec le Hub arrêté.

Après une mise à jour des sources du plugin, réinstalle `codex-pet@codexpet` et
ouvre une nouvelle conversation. Approuve les hooks modifiés si Codex le demande,
et vérifie le chemin du lecteur de fin de réponse. N'active pas une deuxième
copie provenant d'un autre catalogue pour mettre à jour une installation existante.

Pour retirer le plugin, exécute d'abord `CodexPet.ps1 -Action disable` en utilisant
le chemin complet relatif du script ci-dessus, puis `codex plugin remove codex-pet@codexpet`.
Pour un autre catalogue, utilise son identifiant installé. Désactive le démarrage
du Hub dans les Paramètres, enregistre puis quitte avant de supprimer son dossier.
Tu peux conserver les préférences et journaux locaux ; leur suppression constitue
une réinitialisation distincte de tes réglages.

## Licence et crédits

Les trois composants utilisent **AGPL-3.0-or-later**. Le firmware dérive du
[M5Stack StopWatch Avatar de trentct](https://github.com/Trentct/m5stack-stopwatch-avatar).
RunDeleteParadox maintient les adaptations CodexPet, le Hub et le plugin.
Voir les [crédits](../CREDITS.md) et les [conditions de distribution](../CodexPetHub/docs/LICENSING.md),
notamment les sources correspondantes et notices à fournir avec des binaires.
