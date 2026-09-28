# Comportement du firmware CodexPet

[English](CODEXPET.md) | [Français](CODEXPET.fr.md)

Ce guide concerne le firmware **1.1.7**. Le fork conserve le moteur procédural
d'origine : timelines, interpolation, clignements, projection des yeux, rendu
des zones modifiées, tactile et IMU.

Le Hub choisit les états Codex, les priorités, la durée des réactions, la veille,
les réglages et la connexion USB. Le firmware dessine les expressions demandées,
gère les interactions locales et renvoie son état. Il ne connaît pas les messages Codex.

## États et commandes

Les douze expressions d'origine sont complétées par `working`, `waiting`,
`success`, `error`, `stop` et `wake`. Une interaction locale peut afficher une
réaction différente de la base demandée ; `STATUS` expose les deux.

Le protocole série 1 utilise des lignes ASCII à 115200 bauds et des réponses
JSON précédées de `CP `. Le Hub doit vérifier `INFO` et l'identité avant contrôle.

| Commande | Comportement |
| --- | --- |
| `INFO` / `STATUS` | Identité stable, versions, capacités / état et diagnostics |
| `STATE <expression>` | Installe une base et annule la réaction active |
| `REACT success/error/stop/wake` | Réaction unique puis retour à la base |
| `BRIGHTNESS 0..100` | Luminosité de l'écran |
| `SLEEP` | Transition sleepy, fondu, extinction après 1,5 s ; USB actif |
| `WAKE` | Allumage en fondu sur 0,4 s et restauration de la base |
| `SOUND none/fanfare/voice` | Sélection du son de fin, sans lecture immédiate |

Le Hub demande la réaction wake séparément après WAKE. STATE peut modifier la
base mémorisée pendant la veille sans rallumer l'écran. La déduplication des
événements appartient au Hub. Voir les [protocoles](../../CodexPetHub/docs/PROTOCOLS.fr.md).

## Sons

La version 1.1.7 annonce `successAudio=true`. Les WAV PCM16 mono sont embarqués en
flash et lus de façon asynchrone par M5Unified. Aucun réseau ou modèle vocal
n'est nécessaire sur le Pet. Le démarrage est silencieux jusqu'à configuration.

Seule `REACT success` joue un son. STATE, SLEEP, une autre réaction ou un changement
de son arrêtent la lecture. Les compteurs de lecture renseignent le logiciel,
sans prouver que le son est audible. Les [sources audio](../assets/audio/README.md)
et leur provenance font partie du dépôt.

## Alimentation et validation

Les diagnostics optionnels comprennent l'identifiant de démarrage, la durée de
fonctionnement, la cause de reset, l'état de l'écran et les tensions USB/batterie.
Une valeur absente reste indisponible ; la charge n'est pas renseignée dans cette
intégration. Le diagnostic ne modifie pas la politique de charge et n'ajoute pas
de veille profonde. Un nouveau démarrage permet au Hub de resynchroniser le Pet.

La compilation PlatformIO, les tests du moteur, les acquittements USB et la
validation humaine de l'écran et du haut-parleur sont des vérifications distinctes.
Les journaux matériels, identifiants d'appareil et images de test restent dans
des dossiers `artifacts` ignorés par Git. Les comptes rendus privés vont dans
`.local/notes`. La portée reste un Pet en USB.
