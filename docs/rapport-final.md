# Rapport final

Machine de développement : Windows 11, SDK .NET 8.0.403. Ni MSFS ni le SDK MSFS 2024
n'étaient disponibles pour les tests. GSX Pro est installé, l'iFly ne l'est pas.

| Vérifié (compilé, testé unitairement) | Reste à valider en simulateur | Impossible ou inconnu |
|---|---|---|
| La solution compile en Release x64 avec `TreatWarningsAsErrors` : 0 erreur, 0 avertissement. | Connexion réelle à MSFS 2024 : ouverture, message OPEN, flux de données chaque seconde (scénario 5). | Variable « nombre de passagers » propre à l'iFly : **inconnue** (recherche, point 2.2). |
| 67 tests unitaires xUnit verts : garde d'écriture, planificateur, service, lecteur GSX, adaptateur iFly, stabilité, config, journal, décodage SimConnect. | Les déclarations P/Invoke, les offsets `SIMCONNECT_RECV_*` (données à l'octet 40) et les valeurs d'énumérations. Repris de `SimConnect.h` de mémoire : le SDK était absent, je n'ai pas pu les recouper. | Contenu du SDK iFly (`SDK_Defines.h`) : expose-t-il passagers ou charge ? **Inconnu** : l'iFly n'est pas installé ici. |
| L'exe démarre sans simulateur ni `SimConnect.dll`. Il affiche « SimConnect.dll introuvable », journalise, crée `config.json` depuis l'exemple. Lancement réel, 8 s. | Lecture des `L:FSDT_GSX_*` via SimConnect, et sens de `_BOARDING_TOTAL` en fin d'embarquement (scénarios 2-3, questions D1-D2). | Mise à jour de l'EFB iFly après une écriture de station : **inconnue**, signal défavorable sur l'EFB Asobo (recherche, point 2.3). |
| Aucune écriture ne peut partir vers une variable absente de `confirmedWritableVariables`, ni en lecture seule, ni vers carburant, centrage ou GSX. Testé, y compris le passage en lecture seule pendant un plan. | Comportement de MSFS sur une L: inexistante (supposé : 0 sans erreur) et exception `NAME_UNRECOGNIZED` sur une A: inconnue (scénario 6, champ libre). | L'iFly charge-t-il seul ses passagers à partir de GSX ? **Inconnu** ; protégé par la détection de chargement externe (point 4). |
| Une donnée absente n'est jamais affichée 0. Un 0 sur une L: est marqué « indiscernable ». Testé. | Index et noms des stations de charge passagers iFly, masse par passager (scénario 0, questions D3-D4). | Écriture fiable dans l'EFB iFly : **non démontrée**. Tant que le scénario 7 n'a pas réussi, la synchronisation reste désactivée et l'outil affiche l'écart à corriger à la main. |
| Anti double chargement : bon nombre déjà présent, masse déjà présente, masse changée hors outil pendant l'embarquement. Testé. | Une écriture de station tient-elle sans être écrasée par l'iFly ? (scénario 7, question D6). | État GSX 0 = « GSX non démarré » : **hypothèse** (source tierce), non documentée par FSDT. |
| Embarquement interrompu puis repris : attente de l'état 6 et de 10 s de stabilité. Testé avec un faux simulateur. | Ce que fait réellement GSX à l'interruption et à la reprise : remise à 0 ou poursuite du cumul (scénario 4). | Le sens de « SO1 » dans la demande : lu comme « SP1 » (hypothèse). Sans effet sur le code. |
| Journal horodaté à la milliseconde avec décalage UTC, un fichier par jour. Testé. | Reconnexion après retour au menu principal ou fermeture de MSFS, détection de 10 s de silence (scénario 5). | |
| Confirmation avant chaque écriture, relecture, comparaison de masse. Testé : réussite, refus, valeur non retenue. | Fluidité de l'interface en vol et volume du journal sur un vol complet. | |

## Ce que je n'ai pas fait

- Je n'ai pas exécuté l'outil contre MSFS : aucun comportement simulateur n'est déclaré
  fonctionnel.
- Je n'ai pas lu la page de référence SimVars MSFS 2024 pour `PAYLOAD STATION WEIGHT` : le
  caractère inscriptible vient d'un message de forum.
- Je n'ai pas consulté directement le sujet 33592 du forum FSDT : il est cité via un
  assistant de recherche, donc classé hypothèse.
