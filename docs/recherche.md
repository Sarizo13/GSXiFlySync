# Étape 1 — Recherche préalable

Recherche menée le 27/10/2026. Chaque affirmation porte une étiquette :

- **FAIT SOURCÉ** : écrit dans une source citée, lue directement.
- **HYPOTHÈSE** : plausible, appuyée sur une source indirecte ou non lue en entier. À confirmer en vol.
- **INCONNU** : aucune source trouvée. Le diagnostic doit le découvrir.

Source primaire principale : le **manuel GSX Pro installé sur cette machine**,
`C:\Program Files (x86)\Addon Manager\Couatl\GSX\GSX_manual_MSFS.pdf` (fichier daté du
31/12/2025, 152 pages). Chapitre « DEVELOPERS - Interfacing with GSX », pages 86 à 91, et
page 117. Les numéros de page ci-dessous renvoient à ce fichier.

> Écart relevé entre sources : un assistant de recherche (Perplexity) a donné pour les états
> GSX une table différente (« 1 = inactif… 6 = annulé »). Elle est **fausse** : le manuel
> donne une autre table (point 1.2). Le code suit le manuel.

---

## 1. Données passagers de GSX Pro

### 1.1 Méthode d'accès — FAIT SOURCÉ

GSX publie ses données sous forme de **variables L:** (p. 86) : « Most of the information
can be retrieved using L: variables ». Elles sont créées par le petit module WASM de GSX
(p. 93). Le manuel distingue les variables en lecture seule (fond rouge) et celles que les
modules tiers peuvent écrire (fond vert). Il demande de ne pas écrire les premières.

**Cet outil n'écrit dans aucune variable GSX.** La garde d'écriture refuse tout nom qui
contient `FSDT_`, même s'il est listé dans la configuration.

### 1.2 États de service — FAIT SOURCÉ (1 à 6), HYPOTHÈSE (0)

`L:FSDT_GSX_BOARDING_STATE` et `L:FSDT_GSX_DEBOARDING_STATE` (p. 86) :

| Valeur | Sens (manuel p. 86) |
|---|---|
| 1 | service can be called |
| 2 | service is not available |
| 3 | service has been bypassed |
| 4 | service has been requested |
| 5 | service is being performed |
| 6 | service has been completed |

L'exemple XML de la p. 90 confirme que 5 signifie « en cours ».

- **HYPOTHÈSE** : la valeur **0** signifie « GSX pas encore démarré ». Source tierce, pas
  FSDT : [simconnect.mrlm.net/docs/gsx](https://simconnect.mrlm.net/docs/gsx).
- Conséquence dans le code : si les deux états valent 0, GSX est affiché **indisponible**.
  Les compteurs passagers ne sont alors pas affichés, même s'ils lisent 0.

**Début et fin d'embarquement** : pas de variable dédiée. Ils se déduisent des transitions
d'état : 4 ou 5 = début, 6 = fin. L'outil journalise chaque transition.

### 1.3 Variables passagers — FAIT SOURCÉ (noms), HYPOTHÈSE (sens exact)

Manuel p. 87, section « Interfacing with the Passengers simulation » :

| Variable | Ce qu'en dit le manuel | Usage dans l'outil |
|---|---|---|
| `L:FSDT_GSX_NUMPASSENGERS` | peut être fixée par le code avion ; GSX l'utilise alors à la place de son propre calcul | « prévus » si > 0 |
| `L:FSDT_GSX_NUMPASSENGERS_BOARDING` | à lire par l'avion pour suivre l'embarquement | affiché (bus courant) |
| `L:FSDT_GSX_NUMPASSENGERS_DEBOARDING` | idem pour le débarquement | affiché |
| `L:FSDT_GSX_NUMPASSENGERS_BOARDING_TOTAL` | avec plusieurs bus, compte cumulé sur tous les bus | **« embarqués »**, valeur cible |
| `L:FSDT_GSX_NUMPASSENGERS_DEBOARDING_TOTAL` | idem pour le débarquement | affiché |
| `L:FSDT_GSX_MAX_NUMPASSENGERS` | maximum de passagers rapporté par Simbrief | affiché |

- **HYPOTHÈSE** : `_BOARDING_TOTAL` vaut le nombre réellement embarqué en fin
  d'embarquement, aussi avec un seul bus. La mise en page du manuel est ambiguë sur ce point.
- **INCONNU** : GSX ne documente aucune variable qui publie **son propre** nombre prévu
  quand l'avion ne l'a pas fixé. `NUMPASSENGERS` est une entrée vers GSX. Elle reste
  probablement à 0 avec l'iFly. L'interface affiche alors « non publié », pas 0.
- **FAIT SOURCÉ** : variables de soute (`L:FSDT_GSX_BOARDING_CARGO_PERCENT`, etc., p. 87).
  Elles sont affichées, mais ne servent pas à la synchronisation.

---

## 2. Passagers et charge utile de l'iFly 737 MAX

### 2.1 SDK iFly — FAIT SOURCÉ (existence), INCONNU (contenu)

- **FAIT SOURCÉ** : iFly publie un SDK qui couvre le MAX8 pour MSFS 2020 et 2024. Il
  repose sur une **mémoire partagée**. Une mise à jour y ajoute le mutex `hMutex_737MAXSDK`
  dans `SDK.h`. Il contient aussi `key_command.h` et `SDK_Defines.h`.
  Source : [page produit Flight1](https://www.flight1.com/products.asp?pid=iflymax8&vid=flt1sf).
- **FAIT SOURCÉ** : des outils tiers exploitent ce SDK, avec des commandes de type
  `KEY_COMMAND_AUTOMATICFLIGHT_BANK_ANGLE_SET`.
  Source : [flightsim.to, « add lvar for ifly 737 max8 »](https://flightsim.to/addon/89774/add-lvar-for-ifly-737-max8).
- **INCONNU** : le SDK expose-t-il le nombre de passagers ou la charge utile, en lecture ou
  en écriture ? Aucune source publique ne liste ses champs. Pour trancher, il faut lire
  `SDK_Defines.h` dans le paquet iFly installé.
- **Constat local** : l'iFly n'est pas installé dans `D:\Microsoft\Community` sur cette
  machine. Je n'ai donc pas pu lire le SDK.

### 2.2 Variables du simulateur

- **FAIT SOURCÉ** : MSFS définit des SimVars standard de charge : `PAYLOAD STATION WEIGHT:n`,
  `PAYLOAD STATION COUNT`, `PAYLOAD STATION NAME:n`, `TOTAL WEIGHT`, `EMPTY WEIGHT`. Un
  message de forum indique que `PAYLOAD STATION WEIGHT` est décrite comme inscriptible.
  Source : [forum MSFS, « Open fuel and payload data to third party developers »](https://forums.flightsimulator.com/t/open-fuel-and-payload-data-to-third-party-developers/408972).
  Je n'ai pas pu lire directement la page de référence SimVars de MSFS 2024 : ce point
  reste à vérifier.
- **INCONNU** : l'iFly range-t-il ses passagers dans ces stations ? Quels index, avec quels
  noms ? Le diagnostic les balaie de 1 à 12 et affiche noms et masses.
- **INCONNU** : existe-t-il une variable L: « nombre de passagers » propre à l'iFly ? Aucune
  source trouvée. `passengerCountVariable` vaut donc `null` par défaut.

### 2.3 L'EFB se met-il à jour ? — INCONNU, avec un signal défavorable

- **INCONNU** pour l'iFly.
- **FAIT SOURCÉ, cas voisin** : sur l'EFB standard d'Asobo, modifier la SimVar d'une station
  par code **ne met pas à jour** l'affichage. L'inverse fonctionne.
  Source : [devsupport, « Weights displayed in EFB do not update… »](https://devsupport.flightsimulator.com/t/weights-displayed-in-efb-do-not-update-when-payload-is-changed-by-accessing-simvar-via-code/17939).
- **HYPOTHÈSE** : l'EFB iFly calcule ses masses lui-même et pousse ensuite les stations. Si
  c'est le cas, une écriture externe est ignorée ou écrasée. La relecture après écriture
  existe pour détecter ce cas. Il faut aussi vérifier l'EFB à l'œil.
- **HYPOTHÈSE** (rapports d'utilisateurs relayés par recherche, non lus en entier) : la
  charge iFly se règle à la main dans l'EFB, et GSX ne la reporte pas.
  [Forum FSDT, sujet 33592](http://www.fsdreamteam.com/forum/index.php?topic=33592.0).

### 2.4 Version de l'avion

- **FAIT SOURCÉ** : l'iFly 737 MAX SP1 est sorti en juin 2026 et met fin à la version MSFS
  2020. L'article signale que certaines automatisations GSX ne fonctionnent pas encore.
  Source : [msfsaddons.com](https://msfsaddons.com/2026/06/12/ifly-737-max-sp1-is-out-now-and-it-closes-the-book-on-msfs-2020/).
- **HYPOTHÈSE** : « pas SO1 » dans la demande signifie « pas SP1 ». L'outil ne dépend
  d'aucune version : tout ce qui est propre à l'avion se règle dans `config.json`.

---

## 3. Variables L: via SimConnect sous MSFS 2024 — FAIT SOURCÉ

Source : [doc SDK MSFS 2024, SimConnect_AddToDataDefinition](https://docs.flightsimulator.com/msfs2024/flighting/programming-apis/simconnect/api-reference/events-and-data/simconnect_addtodatadefinition/index.md).

- `SimConnect_AddToDataDefinition` accepte directement `"L:NOM"`. **Aucun module WASM
  intermédiaire n'est nécessaire.** Lecture et écriture passent par
  `RequestDataOnSimObject` et `SetDataOnSimObject`.
- Les L: sont globales et partagées. Si deux clients écrivent la même, le dernier gagne.
- Elles ne prennent que FLOAT64.
- Les préfixes `Z:` et `L:1:` désignent des variables locales à un objet. Pendant le
  chargement de l'objet, elles se lisent 0.
- **HYPOTHÈSE** : la doc ne dit pas ce que donne la lecture d'une L: inexistante.
  Hypothèse retenue : elle renvoie 0 sans erreur. **Conséquence** : un 0 lu sur une L: est
  affiché « 0 (L: à 0 ou inexistante — indiscernable) », jamais comme une vraie valeur.
  Une SimVar A: inconnue, elle, provoque l'exception `NAME_UNRECOGNIZED` : l'outil
  l'affiche en erreur.

Choix technique : l'outil appelle l'API C de `SimConnect.dll` (P/Invoke). Il n'utilise pas
le wrapper managé `Microsoft.FlightSimulator.SimConnect.dll`. La solution compile donc sans
le SDK. À l'exécution, `SimConnect.dll` (natif, x64, fourni par le SDK MSFS 2024) doit être
à côté de l'exécutable.

---

## 4. Risque de double chargement

- **FAIT SOURCÉ** (manuel p. 117) : « As of today, the only airplanes that comes with GSX
  internal automation are the PMDG 737 and 777 ». Cette automatisation comprend la saisie
  de charge utile dans le FMC. **Selon ce manuel, GSX ne charge donc pas lui-même la charge
  utile de l'iFly.**
- **FAIT SOURCÉ** (p. 117) : `L:FSDT_GSX_AUTOMATION_PAYLOAD` vaut 1 par défaut pour tous
  les avions. Ce n'est **pas** la preuve qu'un chargement a lieu : c'est un réglage.
- **INCONNU** : l'iFly lit-il lui-même les variables GSX pour charger ses passagers (option
  « intégration GSX » de la tablette) ? Si oui, le risque de double chargement est réel.
- **Protections codées**, puisque ce point reste inconnu :
  1. rien n'est écrit si l'iFly affiche déjà le bon nombre ;
  2. rien n'est écrit si la masse des stations passagers correspond déjà à la cible ;
  3. si cette masse change pendant l'embarquement sans action de l'outil, la
     synchronisation est **refusée** : un autre système charge l'avion.

---

## Ce que le diagnostic doit découvrir en vol

| # | Question | Comment |
|---|---|---|
| D1 | `BOARDING_TOTAL` vaut-il bien le nombre embarqué en fin d'embarquement ? | Comparer avec le message GSX de fin d'embarquement |
| D2 | `NUMPASSENGERS` reste-t-il à 0 avec l'iFly ? | Lire la ligne « prévus » |
| D3 | Quelles stations portent les passagers iFly ? | Changer le nombre de passagers dans l'EFB et regarder quelles `PAYLOAD STATION WEIGHT:n` bougent |
| D4 | Masse par passager de l'iFly | Écart de masse divisé par l'écart de passagers |
| D5 | L'iFly charge-t-il seul pendant l'embarquement GSX ? | Masse des stations pendant l'état 5, sans toucher à l'EFB |
| D6 | Une écriture externe dans une station tient-elle, et l'EFB l'affiche-t-il ? | Uniquement après D3, avec écriture confirmée (procédure, scénario 7) |
| D7 | Variable L: passagers propre à l'iFly | `SDK_Defines.h` du SDK iFly, puis champ « Variable libre » |
