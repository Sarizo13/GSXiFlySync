# GSX → iFly 737 MAX — synchronisation des passagers

Utilitaire Windows externe pour MSFS 2024. Il lit les passagers dans GSX Pro et les compare
à ceux de l'iFly 737 MAX. Il ne les reporte dans l'avion **que si** les variables à écrire
ont été confirmées.

**État actuel** : la phase 1 (diagnostic en lecture seule) est complète. La synchronisation
(phase 3) est codée et testée unitairement, mais **désactivée**. Aucune variable iFly n'est
encore confirmée en écriture : voir `docs/recherche.md`, points 2.1 à 2.3. En l'état,
l'outil affiche l'écart à corriger à la main dans l'EFB.

## Prérequis

| Élément | Version / remarque |
|---|---|
| Windows | 10 ou 11, **64 bits** |
| Runtime | .NET 8 Desktop Runtime x64 (pour exécuter) ou SDK .NET 8 (pour compiler) |
| Visual Studio | 2022 17.8 ou plus récent, charge « Développement .NET Desktop » (facultatif : `dotnet build` suffit) |
| MSFS | 2024 |
| SDK MSFS 2024 | pour `SimConnect.dll` (voir ci-dessous) |
| GSX Pro | à jour (manuel de référence : version du 31/12/2025) |
| Avion | iFly 737 MAX 8 |

### SimConnect.dll

- **Plateforme : x64 obligatoire.** `SimConnect.dll` est une DLL native 64 bits. Tous les
  projets sont compilés en `PlatformTarget=x64`.
- **Emplacement** : dans le SDK MSFS 2024, `<SDK>\SimConnect SDK\lib\SimConnect.dll`.
  Le dossier par défaut supposé est `C:\MSFS 2024 SDK`. À vérifier sur ta machine.
- La compilation la copie à côté de l'exécutable si elle la trouve :
  1. dans `lib\SimConnect.dll` de ce dépôt (le plus simple : la copier là) ;
  2. sinon dans `%MSFS2024_SDK%\SimConnect SDK\lib\` ;
  3. sinon dans `C:\MSFS 2024 SDK\SimConnect SDK\lib\`.
- Si elle est absente, le projet compile quand même, avec un avertissement. L'outil
  affiche alors « SimConnect.dll introuvable ».
- Le wrapper managé `Microsoft.FlightSimulator.SimConnect.dll` **n'est pas utilisé** :
  l'outil appelle l'API C directement (P/Invoke).

## Compilation

```bash
dotnet build GsxIflySync.sln -c Release
```

```bash
dotnet test GsxIflySync.sln -c Release
```

Exécutable produit : `src\GsxIflySync.App\bin\x64\Release\net8.0-windows\GsxIflySync.exe`.

Dans Visual Studio : ouvrir `GsxIflySync.sln`. La configuration de solution s'appelle
« Any CPU », mais chaque projet est compilé en x64. Définir `GsxIflySync.App` comme projet
de démarrage.

## Architecture

| Couche | Projet / fichier | Rôle |
|---|---|---|
| Connexion SimConnect | `GsxIflySync.SimConnect/SimConnectClient.cs` | ouverture, reconnexion, une définition par variable, exceptions rattachées à leur variable |
| Décodage SimConnect | `GsxIflySync.SimConnect/SimConnectMessages.cs` | lecture des structures `SIMCONNECT_RECV_*` (testée) |
| Lecteur GSX | `GsxIflySync.Core/Gsx.cs` | variables `L:FSDT_GSX_*` du manuel, états 0 à 6 |
| Adaptateur iFly | `GsxIflySync.Core/Ifly.cs` | **le seul fichier à remplacer** si l'accès iFly passe par une autre voie (SDK mémoire partagée, par exemple) |
| Garde d'écriture | `GsxIflySync.Core/WriteGuard.cs` | liste fermée + interdits (carburant, centrage, GSX) ; seul chemin d'écriture |
| Synchronisation | `GsxIflySync.Core/SyncPlanner.cs`, `SyncService.cs` | conditions, anti double chargement, confirmation, relecture |
| Interface | `GsxIflySync.App/MainForm.cs` | WinForms |
| Journal | `GsxIflySync.Core/Logging.cs` | `logs\GsxIflySync_AAAAMMJJ.log` |

## Configuration — `config.json`

Le fichier est créé au premier lancement à côté de l'exécutable, comme copie de
`config.example.json` (commenté). Les valeurs par défaut **n'écrivent rien**.

| Clé | Défaut | Sens |
|---|---|---|
| `readOnly` | `true` | bloque toute écriture ; la case « Lecture seule » le change pour la session |
| `syncEnabled` | `false` | autorise le bouton « Synchroniser » à proposer des écritures |
| `stabilitySeconds` | `10` | durée sans changement du compte GSX avant synchronisation |
| `massToleranceKg` | `5` | tolérance des comparaisons de masse |
| `readbackDelayMs` | `2500` | délai avant relecture après écriture |
| `ifly.aircraftTitleFilter` | `"iFly"` | la synchronisation est refusée si `A:TITLE` ne le contient pas |
| `ifly.passengerCountVariable` | `null` | variable « nombre de passagers » iFly : **inconnue** |
| `ifly.passengerStations` | `[]` | stations des zones passagers : **inconnues**, à trouver avec le scénario 0 |
| `ifly.kgPerPassenger` | `84` | hypothèse, à mesurer (scénario 0) |
| `confirmedWritableVariables` | `[]` | **liste fermée** des variables inscriptibles |
| `watchVariables` | 4 variables GSX | variables affichées en plus par le diagnostic |

**Règle** : n'ajoute une variable à `confirmedWritableVariables` que si
`docs/recherche.md` la classe « fait sourcé », ou si le diagnostic l'a confirmée en vol.
Même listées, ces variables restent refusées : tout nom qui contient `FUEL`, `CG`,
`CENTER OF GRAVITY`, `EMPTY WEIGHT`, `TOTAL WEIGHT` ou `FSDT_`.

## Utilisation

- **Variable libre** : saisir `L:NOM`, `L:NOM, bool` ou `PAYLOAD STATION WEIGHT:3, kilograms`,
  puis Entrée. La valeur s'affiche en direct dans la liste.
- **Tester la connexion** : rouvre la connexion si besoin et résume l'état.
- **Synchroniser** : calcule un plan. S'il est prêt, l'outil demande une confirmation
  **avant chaque écriture**, puis relit et compare les masses.
- **Lecture seule** : cochée, aucune écriture ne peut partir. La garde le revérifie à
  chaque écriture.

Affichage honnête : une donnée absente s'affiche « absent » ou « indisponible », jamais 0.
Une L: qui vaut 0 s'affiche « 0 (L: à 0 ou inexistante — indiscernable) ». SimConnect
ne distingue pas ces deux cas.

## Sécurité et limites

- Aucun fichier de l'iFly, de GSX ni de MSFS n'est ouvert en écriture ni modifié.
- Pas de module WASM, pas d'injection : uniquement SimConnect, en client externe.
- L'outil ne pose aucun état temporaire. S'il plante ou se ferme, il n'y a rien à
  défaire : seules restent les écritures confirmées une à une. Sans écriture confirmée,
  l'avion n'a jamais été modifié.
- Si l'écriture dans l'EFB s'avère impossible (scénario 7), elle n'est pas contournée.
  L'alternative est la saisie dans l'EFB iFly, la voie officielle, guidée par l'écart
  affiché.

## Documents

- `docs/recherche.md` : étape 1, chaque point classé fait / hypothèse / inconnu.
- `docs/procedure-de-test.md` : les 8 scénarios en vol, plus un scénario 0 de découverte.
- `docs/rapport-final.md` : vérifié / à valider en simulateur / impossible ou inconnu.
