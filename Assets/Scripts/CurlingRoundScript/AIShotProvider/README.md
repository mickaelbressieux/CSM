# AIShotProvider

Ce dossier contient l'IA de tir du mode curling. Son rôle s'arrête à la décision et à la
préparation d'un `ShotData` : le déplacement réel de la pierre reste entièrement exécuté par
le `StoneLauncher` existant.

L'IA n'utilise aucun ancien fichier de trajectoire enregistré. La distance d'arrêt et la courbe
sont estimées à partir du `Rigidbody`, des paramètres actuels de `StoneLauncher` et d'un modèle
de ralentissement configurable.

## Architecture

```text
AIOpponentProfile (asset partagé)
    identité, difficulté, séquence de stratégies et réglages
                         |
                         v
AIOpponentController (GameObject persistant du PNJ)
    adversaire actif, mémoire du match et numéro du lancer
                         |
                         v
StrategicAIShotProvider (sur chaque prefab de pierre IA)
    choisit la cible et demande un tir au planificateur
                         |
                         v
AIShotTrajectoryPlanner (sur la même pierre)
    cherche direction, puissance, curl et offset sans collision
                         |
                         v
ShotReady(ShotData) -> StoneLauncher -> Rigidbody
```

Cette séparation permet à un même prefab de pierre d'être utilisé par plusieurs adversaires.
La personnalité et la mémoire appartiennent au PNJ, tandis que la pierre temporaire ne connaît
que l'ordre correspondant à son lancer.

## Fichiers

### `AIOpponentProfile.cs`

`ScriptableObject` décrivant un adversaire réutilisable :

- nom de l'adversaire ;
- difficulté ;
- nombre de pierres disponibles pendant un match ;
- stratégie par défaut ;
- stratégie de chaque lancer ;
- répétition éventuelle de la séquence ;
- temps de réflexion ;
- distance de détection d'un joueur autour du centre ;
- bonus de puissance pour attaquer un joueur ;
- puissance d'urgence.

Créer un profil avec `Create > Curling > AI Opponent Profile`.

### `AIOpponentController.cs`

Composant persistant placé sur le GameObject du PNJ rencontré. Il conserve entre les pierres :

- le nombre de tirs planifiés, lancés, arrêtés et perdus ;
- le dernier `ShotData` préparé ;
- la dernière cible ;
- l'utilisation éventuelle d'une solution de secours ;
- le dernier résultat du match.

`BeginEncounter()` rend ce PNJ actif. Un seul `AIOpponentController` peut être actif à la fois.
`EndEncounter()` le désactive. Lors d'un replay avec **R**, la séquence recommence automatiquement
au premier lancer.

### `StrategicAIShotProvider.cs`

Implémente `IShotProvider` et `IShotContextReceiver`, comme demandé par l'architecture du
`CurlingRoundScript`. Il :

1. demande le prochain ordre à l'adversaire actif ;
2. attend le délai de réflexion ;
3. choisit une cible ;
4. demande un `ShotData` à `AIShotTrajectoryPlanner` ;
5. applique le bonus d'attaque puis l'imprécision de difficulté ;
6. publie exactement une fois `ShotReady(shot)`.

Sans adversaire actif, ses champs locaux servent de configuration de secours.

### `AIShotTrajectoryPlanner.cs`

Calcule une trajectoire sans déplacer la vraie pierre. Il utilise directement la convention de
`StoneLauncher` : **curl négatif vers la gauche, curl positif vers la droite**.

Il ne lit aucun fichier dans `Logs/`.

## Stratégies

| Stratégie | Comportement |
| --- | --- |
| `CenterControl` | Vise la cible `Center`. |
| `AttackNearestPlayer` | Vise l'objet `Player` le plus proche ; revient au centre si absent. |
| `Adaptive` | Attaque un `Player` assez proche du centre ; sinon vise `Center`. |

Dans `Strategy By Throw`, l'élément 0 correspond au premier lancer, l'élément 1 au deuxième,
etc. Si la séquence est terminée, `Default Strategy` est utilisée, sauf si
`Repeat Strategy Sequence` est coché.

## Difficultés

La variation est ajoutée après la trajectoire idéale :

| Difficulté | Puissance | Direction | Curl | Offset |
| --- | ---: | ---: | ---: | ---: |
| `Easy` | ±12,5 % | ±8° | ±0,15 | ±0,15 m |
| `Intermediate` | ±8,5 % | ±5° | ±0,10 | ±0,10 m |
| `Hard` | ±2,5 % | ±3° | ±0,05 | ±0,05 m |
| `Insane` | aucune variation | aucune | aucune | aucune |

`Insane` transmet exactement le résultat du planificateur. Les autres difficultés peuvent
légèrement dégrader une trajectoire d'évitement parfaite.

## Calcul de la trajectoire

### Vitesse et distance d'arrêt

Comme `StoneLauncher` utilise `ForceMode.Impulse`, la vitesse initiale estimée est :

```text
vitesse initiale = puissance / masse du Rigidbody
```

Le ralentissement est approché par :

```text
dv/dt = -EstimatedSlidingDeceleration - slideDrag * vitesse
```

Le planificateur calcule la distance parcourue jusqu'au `stopThreshold`. Le paramètre
`Estimated Sliding Deceleration` représente principalement le frottement de contact avec la
glace, que `slideDrag` seul ne décrit pas.

Pour calibrer ce paramètre avec un tir droit :

- si la pierre réelle dépasse la cible, diminuer la valeur ;
- si elle s'arrête avant la cible, augmenter la valeur.

### Courbe

La courbure par mètre reprend directement `StoneLauncher.curlDegreesPerMeter` :

```text
courbure = curl * curlDegreesPerMeter
```

La trajectoire prédite est un arc à courbure constante. Pour chaque angle et curl, une recherche
ternaire entre `Minimum Force` et `Maximum Force` sélectionne la puissance dont le point d'arrêt
est le plus proche de la cible.

### Ordre de recherche

1. tir direct : angle 0, curl 0, offset 0 ;
2. augmentation progressive de l'angle et du curl ;
3. nouvelle recherche avec un départ latéralement aligné sur la cible ;
4. nouvelle recherche à mi-chemin entre cet alignement et le départ initial ;
5. si aucun tir précis n'existe, trajectoire libre terminant le plus près de la cible.

Quand un curl est utilisé, l'angle et le curl final sont obligatoirement opposés :

```text
angle positif -> curl négatif
angle négatif -> curl positif
```

Un angle sans curl reste autorisé. Un curl non nul sans angle n'est pas testé.

## Détection des obstacles

La courbe est découpée en `Path Segments`. Chaque segment est contrôlé avec
`Physics.OverlapCapsuleNonAlloc`, avec le rayon de la pierre et `Obstacle Clearance Margin`.

Le planificateur ignore :

- les colliders de la pierre elle-même ;
- la cible finale ;
- les triggers ;
- les colliders sous la hauteur de la pierre ;
- une couche appelée `Ground`, sans tenir compte de la casse.

Il est néanmoins recommandé de créer une couche `Ground` et de la décocher explicitement dans
`Obstacle Layer Mask`. Les pierres et autres obstacles doivent rester cochés.

## Configuration Unity

### Prefab de pierre IA

Le prefab doit contenir :

```text
Rigidbody
Collider(s)
Stone
StoneLauncher
StrategicAIShotProvider
AIShotTrajectoryPlanner
```

Dans `StoneLauncher`, configurer :

```text
Shot Provider Source -> StrategicAIShotProvider
```

Ne pas laisser `FakeAIShotProvider` comme source : le nouveau provider calculerait le tir, mais
`StoneLauncher` ne recevrait pas son événement.

### PNJ

1. créer un asset `AIOpponentProfile` ;
2. ajouter `AIOpponentController` sur le GameObject du PNJ ;
3. assigner le profil au contrôleur ;
4. appeler `BeginEncounter()` avant de lancer le match ;
5. appeler `EndEncounter()` lorsque le joueur quitte l'adversaire.

Pour une scène de test ne contenant qu'un adversaire, `Begin Encounter On Enable` peut être
coché. Sur une carte avec plusieurs PNJ, il doit normalement rester décoché.

## Réglages de performance

Le planificateur ne charge plus les milliers d'anciens logs. Il utilise aussi une recherche de
puissance bornée et des requêtes physiques sans allocation.

En cas de calcul encore trop long, réduire dans cet ordre :

- `Path Segments` vers 10 ;
- `Force Search Iterations` vers 10 ou 12 ;
- `Maximum Angle` vers 20 ou 25 ;
- augmenter `Angle Step` vers 2 ;
- augmenter `Curl Step` vers 0,2.

## Limites du modèle

La simulation prédit un glissement libre jusqu'à l'arrêt et utilise les colliders pour rejeter
les chemins bloqués. Elle ne simule pas :

- les rebonds ou déplacements après collision ;
- les variations locales de friction ;
- les pentes ;
- les forces ajoutées par une `StoneAbility` ;
- une modification de physique effectuée après la création du `ShotData`.

Le réglage de `Estimated Sliding Deceleration` doit donc correspondre à la scène et aux matériaux
physiques réellement utilisés.

## Diagnostic rapide

- **Le log indique un tir, mais la pierre ne part pas :** vérifier `StoneLauncher > Shot Provider
  Source`.
- **La pierre s'arrête trop tôt :** augmenter `Estimated Sliding Deceleration` afin que l'IA
  choisisse davantage de puissance.
- **La pierre dépasse la cible :** diminuer cette valeur.
- **Le sol bloque tous les chemins :** retirer `Ground` de `Obstacle Layer Mask` et vérifier la
  couche des enfants portant les colliders.
- **La mauvaise IA joue :** vérifier quel `AIOpponentController` a reçu `BeginEncounter()`.
- **Les tirs précis varient encore :** sélectionner la difficulté `Insane`.
