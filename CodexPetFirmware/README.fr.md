# CodexPetFirmware

[English](README.md) | [Français](README.fr.md) | [Upstream 简体中文](README.zh-CN.md)

Firmware **1.1.7** pour M5Stack StopWatch, dérivé de
[Trentct/m5stack-stopwatch-avatar](https://github.com/Trentct/m5stack-stopwatch-avatar),
base `2049632`. L'auteur du firmware d'origine est **trentct**, avec ses contributeurs.
Le moteur d'avatar, les crédits et la licence AGPL d'origine sont conservés.

Le [guide utilisateur](../docs/USER-GUIDE.fr.md) explique l'installation complète.
Le firmware reçoit des commandes graphiques du Hub par USB ; il ne connaît
ni les prompts, ni les transcriptions, ni les identifiants d'outils Codex.

## Compiler et flasher

Utilise PlatformIO Core 6.1.18. Les bibliothèques et la plateforme ESP32 sont
fixées dans [platformio.ini](platformio.ini). Depuis ce dossier :

```powershell
python -m pip install platformio==6.1.18
python -m platformio run
```

Quitte le Hub et les moniteurs série avant d'accéder au port USB. Identifie
le bon StopWatch dans le Gestionnaire de périphériques, puis :

```powershell
$petPort = Read-Host 'Port COM du StopWatch'
python -m platformio run --target upload --upload-port $petPort
```

Le flash remplace le firmware de l'appareil. Si l'envoi automatique ne démarre
pas, la procédure upstream consiste à maintenir reset environ deux secondes,
puis à relâcher quand la LED verte s'allume. Voir la
[référence matérielle](docs/HARDWARE_BASELINE.md).

## Fonctionnalités

- Yeux procéduraux sur écran AMOLED rond 466 × 466.
- Repos, réflexion, travail, attente et réactions ponctuelles.
- Luminosité, veille et réveil de l'écran avec maintien de l'USB.
- Identité matérielle stable, état d'alimentation et diagnostic disponibles.
- Son de fin de réponse : voix « TA-DA! », deux notes, ou silence.
- Interactions tactiles, boutons et IMU héritées du projet d'origine.

Les détails sont dans [le guide du firmware](docs/CODEXPET.fr.md).
Les [fichiers audio source](assets/audio/README.md) et leur génération font partie
des sources à redistribuer. Seule la réaction `success` joue le son configuré.

## Vérifications

```powershell
cmd /c tests\Run-Native.cmd
python -m unittest discover -s tests -p "test_*.py"
```

Le banc natif nécessite les outils C++ de Visual Studio et produit des images
simulées. Les essais matériels sont distincts et demandent un port et une
identité d'appareil explicites. Une compilation ou un acquittement série ne
prouve pas le rendu à l'écran, le son ou l'autonomie.

## Licence

**AGPL-3.0-or-later**, comme le Hub et le plugin. Les ajouts CodexPet sont
Copyright © 2026 RunDeleteParadox et contributeurs. Voir [LICENSE](LICENSE),
[NOTICE](NOTICE), [notices tierces](THIRD_PARTY_NOTICES.md) et [SOURCE.md](SOURCE.md).
Les références techniques upstream restent publiques ; les comptes rendus de
travail privés sont conservés localement dans `.local/notes` à la racine du dépôt.
