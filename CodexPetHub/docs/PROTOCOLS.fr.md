# Protocoles CodexPet

[English — full specification](PROTOCOLS.md) | [Français](PROTOCOLS.fr.md)

## Du plugin au Hub

Le plugin utilise le Named Pipe local `CodexPetHub.v2.<SID Windows>`, réservé au
compte courant. Les messages JSON UTF-8 portent une base (`idle`, `thinking`,
`working`, `waitingForUser`), une réaction facultative (`success`, `error`, `stop`),
un identifiant de producteur, un numéro de séquence et une date avec fuseau.

Les identifiants de conversation et d'outils restent dans le plugin. Les prompts,
résultats bruts et transcriptions ne sont pas transmis. Une reconnexion rétablit
la base, sans rejouer les anciennes réactions. Le heartbeat confirme uniquement
la santé du transport, jamais la fin d'une réponse.

## Du Hub au firmware

Seul le Hub ouvre la liaison série : protocole 1, 115200 bauds, 8N1, sans contrôle
de flux. Les commandes ASCII se terminent par LF ; les réponses commencent par
`CP ` puis un objet JSON. `INFO` vérifie la version et l'identité matérielle avant
toute commande de contrôle. Le numéro COM est seulement un point de connexion.

| Commande | Effet |
| --- | --- |
| `INFO`, `STATUS` | Identité, versions, état et diagnostic |
| `STATE <expression>` | Installe la base et annule la réaction en cours |
| `REACT success/error/stop/wake` | Joue une réaction puis revient à la base |
| `BRIGHTNESS 0..100` | Règle la luminosité |
| `SLEEP`, `WAKE` | Éteint ou réveille l'écran, USB maintenu |
| `SOUND none/fanfare/voice` | Choisit le son, sans le jouer ; firmware 1.1.7+ |

Seule `REACT success` joue le son. Le Hub protège cette réaction cinq secondes
et garde en mémoire les nouveaux états pour la suite. La veille et les tests
manuels peuvent prendre la priorité. Les erreurs de transport provoquent une
reconnexion ; un état graphique acquitté n'est pas une validation visuelle.

Les champs exacts, limites, délais et diagnostics optionnels sont détaillés dans
la [spécification anglaise](PROTOCOLS.md). Les identifiants du protocole restent
en anglais quelle que soit la langue de l'interface.
