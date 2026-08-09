# CurlingRoundScript — Architecture Overview

This folder holds the scripts that drive a solo curling round: aiming and throwing a
stone, its sliding/curl physics, turn sequencing, and the on-screen HUD.

The defining idea of this branch (`feature/stone-lauch-abstracted`) is that **a throw is
decoupled from who decides it**. The old monolithic `CurlingStoneController` has been split
into pieces connected by a single event, so the human player and an AI can drive the *exact
same* launcher without it knowing which is which. Both game modes spawn their stones from
**provider-carrying prefabs**, so switching in a real AI is a prefab swap, not a code change.

---

## The shot abstraction (the heart of the branch)

A throw is split along one clean seam:

| Concern | Type | Role |
| --- | --- | --- |
| **Who decides a shot** | [`IShotProvider`](Shooting/IShotProvider.cs) implementations | Compose a shot, then announce it |
| **The shot itself** | [`ShotData`](Shooting/ShotData.cs) | Immutable value describing one throw |
| **What the stone does with it** | [`StoneLauncher`](Shooting/StoneLauncher.cs) | Applies physics — nothing else |

### The seam: `IShotProvider`

```csharp
event Action<ShotData> ShotReady;   // raised exactly ONCE when the shot is committed
ShotData CurrentShot { get; }       // the live, in-progress shot — for HUD preview
float MaxCurl { get; }              // max curl magnitude, so the HUD can scale its gauge
void Rearm();                       // re-arm to accept a fresh shot on reset
```

A provider spends several frames (human) or a "thinking" delay (AI) building a shot, then
raises `ShotReady` once. `StoneLauncher` subscribes and executes it. Crucially, the launcher
stores its provider as a plain `MonoBehaviour shotProviderSource` and casts it to
`IShotProvider` at runtime — so **the launcher never names a concrete provider type**, and any
provider can be wired in from the inspector on the prefab.

### Injecting scene context: `IShotContextReceiver`

A prefab can't bake in scene references (what to aim at, the shared aim arrow). So the manager
hands those to a freshly spawned provider through a second, optional seam
([`IShotContextReceiver`](Shooting/IShotContextReceiver.cs)):

```csharp
void Configure(ShotContext context);   // context = { Transform HouseCenter; GameObject AimArrow; }
```

The AI takes `HouseCenter` as its aim target; the player takes the `AimArrow`. The manager calls
`Configure` **through the interface**, so it injects context without naming a concrete provider
type either. Providers that don't need context simply don't implement it.

### `ShotData`

An immutable `readonly struct` — the only thing that ever crosses the launch seam:

- `Direction` — normalized world-space aim (the constructor normalizes defensively, falling
  back to `Vector3.forward` for a near-zero vector, so callers can pass a raw
  `target − position`).
- `Power` — launch impulse magnitude.
- `Curl` — signed: **negative = curl left, positive = curl right** relative to travel.

---

## Control flow

```mermaid
flowchart TD
    subgraph Providers["IShotProvider (who decides)"]
        P[PlayerShotProvider<br/>keyboard aim/power/curl<br/>Space commits]
        A[FakeAIShotProvider<br/>thinks, then aims at target]
    end

    P -->|ShotReady ShotData| L
    A -->|ShotReady ShotData| L

    subgraph Launcher["StoneLauncher (physics)"]
        L[OnShotReady<br/>queues pendingShot]
        F[FixedUpdate<br/>apply impulse + spin<br/>simulate curl each step<br/>stop when speed < stopThreshold]
        L --> F
    end

    F -->|sets| S[HasBeenShot / ShotFinished]

    M[SoloCurlingGameManager<br/>BuildStone: instantiate provider-carrying prefab,<br/>Configure ShotContext, sequence turns]
    M -.->|spawns & activates| Providers
    M -.->|Configure scene refs| Providers
    M -.->|polls| S

    U[CurlingUIManager]
    U -.->|reads CurrentShot / MaxCurl| Providers
    U -.->|reads HasBeenShot / ShotFinished| S
    M -.->|SetActiveShot / SetBanner| U
```

**In one sentence:** a provider raises `ShotReady(ShotData)` → `StoneLauncher.OnShotReady`
queues it → the next `FixedUpdate` applies the impulse + pre-shot spin, then bends the
velocity heading a little each physics step (the curl) until speed drops below
`stopThreshold`, flipping `ShotFinished` to true.

---

## Directory map

```
CurlingRoundScript/
├── Shooting/                     ← the shot abstraction
│   ├── ShotData.cs               immutable description of one throw
│   ├── IShotProvider.cs          launch seam: ShotReady / CurrentShot / MaxCurl / Rearm
│   ├── IShotContextReceiver.cs   context seam: Configure(ShotContext) for scene refs
│   ├── PlayerShotProvider.cs     human input half (keyboard → ShotData)
│   └── StoneLauncher.cs          physics half (ShotData → impulse, curl, stop)
├── AIShotProviderTemp/           ← throwaway demo, meant to be replaced
│   └── FakeAIShotProvider.cs     a dumb AI proving the seams work
├── SoloCurlingGameManager.cs     orchestrator: modes, prefab spawning, turns, scoring
├── CurlingUIManager.cs           single HUD authority (TextMeshPro)
├── CameraSwitcher.cs             independent: cycle cameras with Tab
└── PlayerTagAssigner.cs          independent: assign a tag in edit/play mode
```

---

## Per-script responsibilities

### The shooting seam (`Shooting/` + `AIShotProviderTemp/`)

**[`ShotData`](Shooting/ShotData.cs)** — immutable `readonly struct` (Direction, Power,
Curl). Pure data, source-agnostic; produced by any provider, consumed by the launcher and
read by the HUD. Depends on nothing but `UnityEngine`.

**[`IShotProvider`](Shooting/IShotProvider.cs)** — the interface every shot source implements:
the `ShotReady` event, the `CurrentShot` preview getter, `MaxCurl` (so the HUD can scale its
curl gauge without knowing the concrete type), and `Rearm()`. This is the only thing
`StoneLauncher` and `CurlingUIManager` know about a shot source.

**[`IShotContextReceiver`](Shooting/IShotContextReceiver.cs)** — an optional second interface
plus the `ShotContext` struct. Lets the manager pass per-turn scene references (house center,
aim arrow) into a provider without naming a concrete type. Implemented by both providers today.

**[`PlayerShotProvider`](Shooting/PlayerShotProvider.cs)** — the human input half
(`MonoBehaviour, IShotProvider, IShotContextReceiver`). Each `Update` reads the keyboard (← →
aim, ↑ ↓ power, Q/E curl) and updates the aim-preview arrow; **Space** commits the shot via
`CommitShot()`, which raises `ShotReady`. An `armed` flag flips off the instant Space is pressed
so a shot can't fire twice until `Rearm()`. `Configure` receives the aim arrow the manager spawns
for this stone. Knows nothing about physics.

**[`FakeAIShotProvider`](AIShotProviderTemp/FakeAIShotProvider.cs)** — a **temporary,
throwaway** demo (`MonoBehaviour, IShotProvider, IShotContextReceiver`), explicitly *not* the
real `AIStoneController` (which lives outside this folder and is untouched). `OnEnable` starts
the `ThinkThenShoot()` coroutine — the manager only activates the stone on the AI's turn, so
enabling *is* the cue to start deliberating. After `thinkDelaySeconds` it aims flat at its
`target` (injected via `Configure` as the house center), applies a configurable, optionally
jittered power/curl, and raises `ShotReady`. Its whole point is to prove a non-human source
drops into the same launcher and prefab pipeline with zero launcher/manager changes.

**[`StoneLauncher`](Shooting/StoneLauncher.cs)** — the physics half
(`[RequireComponent(typeof(Rigidbody))]`). In `OnEnable` it casts `shotProviderSource` to
`IShotProvider` (logging an error if the cast fails) and subscribes to `ShotReady`;
`OnDisable` unsubscribes. `OnShotReady` stashes the shot and defers it to `FixedUpdate`, where
the impulse and one-shot spin are applied and then curl is simulated until the stone stops.
Exposes `HasBeenShot` / `ShotFinished` (polled by the manager and UI) and `ResetStone()`,
which restores the start pose and calls `provider?.Rearm()`.

### Orchestration & UI

**[`SoloCurlingGameManager`](SoloCurlingGameManager.cs)** — the orchestrator, with two modes
(`GameMode { TestDrop, Match }`). **Both modes spawn every stone from a prefab** via `BuildStone`;
the pre-placed scene `stone` is disabled and never launched.

- **`BuildStone(isAI, out launcher, out provider)`** is the shared spawn path. It instantiates
  the right prefab (`playerStonePrefab` / `aiStonePrefab`), reads the `StoneLauncher` and its
  wired `IShotProvider` off the prefab, spawns a per-stone aim-arrow instance for a human, and
  calls `Configure(new ShotContext(houseCenter, aimArrow))` — **without naming a concrete
  provider type**. A misconfigured prefab logs a clear error and the turn is skipped rather than
  crashing. (Tuning now lives on the prefab; there is no runtime `AddComponent` or component
  stripping.)
- **TestDrop** (original showcase): `SpawnTestPlayerStone` builds one player stone (tracked in
  `playerLauncher`) and `SpawnEnemyStones` drops `enemyStoneCount` plain obstacle stones from
  `enemyStonePrefab`; `ComputeScore()` gives +1 if the player is closest, else −1 per closer
  enemy. **R** resets.
- **Match** (temp turn-based round): `RunMatch()` loops → `RunTurn(isAI)` per throw (`i % 2 == 0`
  is the AI, so the **AI throws first**), `stonesPerSide * 2` throws total → `ComputeMatchResult()`
  (standard curling end scoring, excluding stones that fell off the sheet) → banner → **R** to
  replay. Recovery: **N** (`forceNextTurnKey`) force-skips a stuck turn; `FixedUpdate` freezes
  stones that fall below `killY` and snaps slow creepers to a stop. `DestroyStoneAndArrow` cleans
  up a stone together with its aim-arrow instance.

**[`CurlingUIManager`](CurlingUIManager.cs)** — the single HUD authority, rendering both modes
through one `TMP_Text infoText`. It's driven by an "active shot" (a `StoneLauncher` + optional
provider) plus an optional banner line, tracked internally as an **`IShotProvider`** so any
provider works. `Update` picks the state from `HasBeenShot`/`ShotFinished`: live aiming HUD
(power/curl bar/aim, read from `CurrentShot` and `MaxCurl`), "stone is sliding", a banner, or the
test-mode result panel. `SetActiveShot(stone, provider)` reassigns it each turn — a **null
provider suppresses the aiming HUD** (used on AI turns); the serialized `provider` field is only
the inspector default for TestDrop and is not broken by the interface routing.

### Independent utilities

**[`CameraSwitcher`](CameraSwitcher.cs)** — cycles a `Camera[]` with **Tab** (configurable
`switchKey`), enabling one at a time. Also exposes `SwitchTo(int)` for other scripts. No
dependency on the shot system.

**[`PlayerTagAssigner`](PlayerTagAssigner.cs)** — `[ExecuteAlways]` helper that assigns a
configurable `playerTag` to its GameObject in both edit and play mode (via `OnValidate`,
`OnEnable`, `Awake`), warning once if the tag isn't defined. Unrelated to the shot flow.

---

## How a turn plays out (Match mode)

1. `RunMatch()` clears the sheet and loops `stonesPerSide * 2` turns.
2. `RunTurn(isAI)` calls `BuildStone`, which instantiates the provider-carrying prefab, reads its
   `StoneLauncher` + `IShotProvider`, and injects the `ShotContext` — everything wired *before*
   the object is activated.
3. It waits until `launcher.HasBeenShot` (the provider raised `ShotReady`) — or a forced skip.
4. Then it waits until `launcher.ShotFinished && AllMatchStonesSettled()` (with a 20 s safety
   timeout and the force-skip escape).
5. The stone is filed into `aiStones` or `playerStones` by side.
6. After all throws, `ComputeMatchResult()` scores the end, pushes the result to the UI
   banner, and waits for **R** to replay.

---

## Patterns & conventions

- **Two interface seams:** `IShotProvider` (a C# `event Action<ShotData>`, not a `UnityEvent`)
  for who-decides-vs-physics, and `IShotContextReceiver` for injecting scene references — both
  keep the manager and launcher from naming concrete provider types.
- **Prefab-authored providers:** each stone prefab carries exactly one `StoneLauncher` + one
  `IShotProvider` with `shotProviderSource` wired, so a new provider (e.g. a real AI) is a prefab,
  not a manager edit. Tuning lives on the prefab.
- **Interface polymorphism via `shotProviderSource as IShotProvider`** — the launcher is wired to
  a `MonoBehaviour` and never names a concrete provider.
- **Polling of public getters** (`HasBeenShot`, `ShotFinished`, `CurrentShot`, `MaxCurl`,
  `GetLastScore`) for state the manager and UI observe.
- **Coroutines** (`RunMatch`, `RunTurn`, `ThinkThenShoot`) with `WaitUntil` / `WaitForSeconds`
  for turn and think sequencing.
- **New Input System** throughout (`Keyboard.current`).
- **No singletons** — the manager finds the UI once via `FindFirstObjectByType<CurlingUIManager>()`
  as a fallback and otherwise passes references explicitly.

---

## Extension points (foundations for later work)

Three seams exist so the planned features can be built without touching the launch pipeline:

- **`Stone` entity** ([Shooting/Stone.cs](Shooting/Stone.cs)) — identity (`Side`) + lifecycle
  (`Phase`: Idle/Sliding/Stopped/Lost) + cached `Body`. The thing systems hang off of instead of
  raw GameObjects.
- **Stackable powers** ([Shooting/StoneAbility.cs](Shooting/StoneAbility.cs)) — subclass
  `StoneAbility` and override any of `OnLaunch` / `OnSlideTick` / `OnStopped` / `OnStoneCollision`.
  `StoneLauncher` fires each hook on **every** `StoneAbility` on the stone, so powers **stack** by
  simply adding more components. `OnSlideTick` is the hook for in-flight powers (e.g. "brake on
  key press"). See the sample [Abilities/ExtraPowerAbility.cs](Shooting/Abilities/ExtraPowerAbility.cs).
- **Match events** ([Shooting/MatchEvents.cs](Shooting/MatchEvents.cs)) — a static hub raising
  `TurnStarted` / `StoneReleased` / `StoneStopped` / `StoneLost` / `EndScored`. Anything (a power,
  the UI, audio) can subscribe in `OnEnable` and unsubscribe in `OnDisable` without wiring a
  manager reference. `SoloCurlingGameManager` raises them at the turn lifecycle points.

> **Note:** the real AI, `AIStoneController`, lives *outside* this folder and is untouched.
> Everything under `AIShotProviderTemp/` is a temporary demo and is meant to be deleted or
> replaced once a real AI provider implements `IShotProvider` (and, if it needs the house center,
> `IShotContextReceiver`).
