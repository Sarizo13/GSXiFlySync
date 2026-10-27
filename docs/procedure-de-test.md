# Procédure de test en simulateur

Ces tests valident ce que je n'ai pas pu vérifier sans MSFS. Fais-les dans l'ordre : chaque
scénario s'appuie sur ce que les précédents ont confirmé.

**Avant chaque session**

1. Fermer l'outil. Supprimer ou renommer le dossier `logs\` pour partir d'un journal propre.
2. Vérifier que `config.json` contient `"readOnly": true` et `"syncEnabled": false`.
   Seul le scénario 7 les modifie.
3. Lancer MSFS 2024. Charger l'iFly 737 MAX au parking, GSX actif.
4. Lancer `GsxIflySync.exe`.

**À m'envoyer après la session** : le fichier `logs\GsxIflySync_AAAAMMJJ.log`, `config.json`,
et pour chaque scénario une capture de l'outil et une de l'EFB iFly.

Dans le journal, les lignes `[DEBUG] <variable> = <valeur>` tracent chaque changement de
valeur. Les lignes `instantané :` résument l'état toutes les 30 s.

---

## Scénario 0 — Découverte (prépare tous les autres)

But : répondre aux questions D1 à D7 de `docs/recherche.md`.

1. Dans la liste des variables, relever `A:TITLE`, `A:PAYLOAD STATION COUNT`, puis chaque
   `A:PAYLOAD STATION NAME:n` et `A:PAYLOAD STATION WEIGHT:n`.
2. Dans l'EFB iFly, passer de 0 à 100 passagers, sans soute ni carburant. Noter quelles
   `PAYLOAD STATION WEIGHT:n` changent, et de combien.
3. Remettre 0 passager. Vérifier que les mêmes stations reviennent à leur valeur de départ.
4. Si tu as le SDK iFly, chercher dans `SDK_Defines.h` les champs « pax », « passenger »,
   « payload » ou « weight ». Saisir chaque candidat dans « Variable libre », par exemple
   `L:NOM_TROUVE`, puis Entrée.

**Résultat attendu** : une liste « index de station → zone passagers », et une masse par
passager (masse ajoutée / 100).

**Dans le journal** : `PAYLOAD STATION WEIGHT:n = …` aux moments du changement dans l'EFB.

---

## Scénario 1 — Avion chargé sans embarquement GSX

1. Charger des passagers dans l'EFB iFly. Ne pas appeler l'embarquement GSX.
2. Observer l'outil pendant 1 minute.

**Attendu**
- GSX : « disponible », embarquement « peut être demandé (1) ».
- Passagers embarqués (GSX) : 0. Passagers iFly : la valeur de l'EFB, si les stations sont
  configurées ; sinon « absent ».
- « Synchroniser » répond **Blocked : embarquement GSX non terminé**.

**Dans le journal** : `GSX embarquement : inconnu -> peut être demandé (1)`, aucune ligne
`ÉCRITURE`.

---

## Scénario 2 — Embarquement en cours

1. Avion vide dans l'EFB. Appeler l'embarquement GSX.
2. Pendant l'embarquement, cliquer une fois sur « Synchroniser ».

**Attendu**
- L'état passe à « demandé (4) » puis « en cours (5) ».
- `NUMPASSENGERS_BOARDING_TOTAL` augmente. « Stable : non » tant que des passagers montent.
- « Synchroniser » répond **Blocked : embarquement GSX non terminé : en cours (5)**.
- **Question D5** : les stations passagers iFly bougent-elles seules ? Si oui, le journal
  contient `masse passagers modifiée hors outil`.

**Dans le journal** : `référence masse passagers au début d'embarquement : … kg`, puis les
valeurs successives de `L:FSDT_GSX_NUMPASSENGERS_BOARDING_TOTAL`.

---

## Scénario 3 — Embarquement terminé

1. Laisser GSX terminer. Attendre au moins 10 s (`stabilitySeconds`).
2. Comparer « Passagers embarqués (GSX) » avec le message de fin de GSX.
3. Cliquer « Synchroniser », toujours en lecture seule.

**Attendu**
- État « terminé (6) ». Embarqués = nombre annoncé par GSX (**question D1**). « Stable : oui ».
- « Prévus » : « non publié » si GSX ne reçoit pas de nombre de l'avion (**question D2**).
- « Synchroniser » répond **Blocked : mode lecture seule actif**.

**Dans le journal** : `GSX embarquement : en cours (5) -> terminé (6)`, puis
`plan de synchronisation : Blocked — mode lecture seule actif`.

---

## Scénario 4 — Embarquement interrompu puis repris

1. Lancer l'embarquement. À mi-parcours, l'interrompre depuis le menu GSX, si GSX le
   permet. Sinon, quitter le parking et revenir.
2. Relever l'état et le compteur affichés.
3. Relancer l'embarquement jusqu'à la fin.

**Attendu**
- Pendant l'interruption, l'état n'est pas 6 : la synchronisation reste **Blocked**.
- Après reprise, le cumul `_BOARDING_TOTAL` repart soit du compte précédent, soit de 0.
  **À noter** : le manuel ne le précise pas.
- Elle ne redevient possible qu'à l'état 6 et après 10 s de stabilité.

**Dans le journal** : toutes les transitions d'état, et la valeur de `_BOARDING_TOTAL` à
l'interruption et à la reprise.

---

## Scénario 5 — Reconnexion au simulateur

1. Outil connecté. Revenir au menu principal de MSFS, puis recharger un vol.
2. Variante : fermer l'outil, puis le relancer, MSFS ouvert.
3. Variante : lancer l'outil avant MSFS.

**Attendu**
- Au menu principal, les variables passent « indisponible » et les compteurs « absent ».
  Jamais 0.
- Si MSFS se ferme : l'état devient `Disconnected`, puis l'outil retente toutes les 5 s
  (`reconnectIntervalSeconds`).
- Au rechargement, les valeurs reviennent seules, sans relancer l'outil.
- Fermer l'outil ne change rien dans l'avion ni dans GSX.

**Dans le journal** : `SimConnect : Disconnected — MSFS a fermé la connexion` ou
`… aucune donnée depuis 10 s`, puis `SimConnect : Connected — connecté à MSFS`.

---

## Scénario 6 — GSX indisponible

1. Fermer Couatl : clic droit sur l'icône GSX de la zone de notification, Quitter. Ou
   lancer un vol avec GSX désactivé.
2. Observer l'outil.

**Attendu**
- GSX : **« indisponible — variables d'état GSX à 0 : GSX absent ou pas encore démarré »**.
- Prévus et embarqués : « absent ». Écart : « non calculable ».
- Les lignes `L:FSDT_*` affichent « 0 (L: à 0 ou inexistante — indiscernable) ».
- « Synchroniser » répond **Blocked : GSX indisponible**.

**Dans le journal** : une ligne `instantané :` avec `GSX=indisponible`.

---

## Scénario 7 — iFly non modifiable (premier essai d'écriture)

À faire **seulement** si le scénario 0 a identifié les stations passagers.

1. Fermer l'outil. Dans `config.json`, renseigner :
   - `ifly.passengerStations` avec les index trouvés et leur part (par exemple 0.5 / 0.5) ;
   - `ifly.kgPerPassenger` avec la masse mesurée ;
   - `confirmedWritableVariables` : exactement les mêmes stations, par exemple
     `"A:PAYLOAD STATION WEIGHT:3"`.
   - `"syncEnabled": true`. Laisser `"readOnly": true`.
2. Relancer l'outil. EFB à 0 passager. Faire un embarquement GSX complet.
3. Décocher « Lecture seule ». Cliquer « Synchroniser ». Accepter chaque confirmation.
4. Après le message de résultat, ouvrir l'EFB iFly et regarder la page charge.
5. Recocher « Lecture seule ».

**Attendu**, un des trois cas :
- **Réussite** : relecture conforme ET l'EFB affiche le bon nombre → l'écriture marche.
- **Relecture KO** (« l'iFly n'a pas retenu la valeur ») → l'iFly écrase les stations.
  Écriture **impossible** par cette voie.
- **Relecture OK mais EFB inchangé** → la masse simulateur bouge, pas l'EFB. Synchronisation
  **non fiable** : on revient à l'affichage de l'écart et à une correction à la main.

**Dans le journal** : `ÉCRITURE A:PAYLOAD STATION WEIGHT:n = …`, puis
`relecture … : attendu …, relu …`, puis `synchronisation Succeeded|Impossible : …`.

---

## Scénario 8 — Écart de masse ou de passagers

1. Après le scénario 3, mettre dans l'EFB un nombre différent de celui de GSX (par exemple
   GSX 150, EFB 140).
2. Observer l'écart. Cliquer « Synchroniser » en lecture seule : réponse **Blocked**.
3. Mettre `"syncEnabled": true` dans `config.json`, en laissant `confirmedWritableVariables`
   vide. Relancer, décocher « Lecture seule », cliquer « Synchroniser ». Sans variable
   confirmée, aucune écriture n'est possible.
4. Variante : mettre dans l'EFB exactement le nombre GSX, puis cliquer « Synchroniser ».

**Attendu**
- Écart affiché `+10 passagers`, si les stations sont configurées ; sinon « non calculable ».
- Étape 3 : **Impossible**. Sans stations configurées, le message est « aucune variable
  iFly confirmée en écriture. À corriger à la main dans l'EFB : 150 passagers (… kg) ».
  Avec stations configurées mais non confirmées : « écriture refusée par la garde — … »,
  suivi de « (écart actuel : +10 passagers) ».
- Variante au bon nombre : **NothingToDo : masse passagers déjà chargée**. Aucune écriture.
- Aucune ligne `ÉCRITURE` dans le journal, quel que soit le cas.

**Dans le journal** : la ligne `plan de synchronisation : …` avec la cible et l'écart.
