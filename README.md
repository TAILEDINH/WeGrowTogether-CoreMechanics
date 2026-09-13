# We Grow Together

**A wave-based space-defense game built in Unity 6 / C# — gameplay systems portfolio.**

`Unity 6000.3.10f1` · `C#` · `2D` · `108 C# scripts / ~18,350 lines (Assets/Script)` · `Unity Localization` · `TMPro`

> This document describes only systems that exist in the source code. Every algorithm, formula and code path below was extracted from the actual project files. Items that are partial, unused or unverifiable are labeled explicitly — see the **Verification Legend**.

---

## Verification Legend

| Label | Meaning |
|---|---|
| **Implemented** | Present and wired into runtime behavior. |
| **Partial** | Code exists, but a piece is missing/incomplete/inconsistent (described). |
| **Unused / Dead** | Written but not referenced by runtime logic. |
| **Planned** | Referenced in comments/design only, no implementation found. |
| **Not verified from source code** | Could not be confirmed in the codebase. |

**Audit scope:** all 108 `.cs` files under `Assets/Script` were reviewed, with deep reads of the wave, combat, progression, save, population, pooling and UI systems. Third-party code (`unity-mcp-beta`, `Spine`, `Samples`) was excluded.

**Project state note (factual):** the gameplay scene used during development is currently an editor recovery snapshot (`Assets/_Recovery/0 (4).unity`). `Assets/Scenes/scences.unity` and `SampleScene.unity` are listed in Build Settings but disabled. This is a development-state observation, not a feature claim.

---

## 1. Project Overview

**We Grow Together** is a 2D tower-defense / survivor hybrid:

- Endless **wave combat**: enemies march at a spaceship castle while the player deploys defenders.
- **Hero deployment**: single-hero units and 10-unit **Space Fighter squads** placed on a 16-slot grid.
- **Economy**: gold from kills and civilian income; gem rewards per wave.
- **Meta progression**: a **Global EXP → Global Level → Upgrade Points** system with data-driven stat upgrades.
- **Second progression layer**: **Resilience Points (RP)**, earned per kill/boss, spent on a hybrid damage track and a 5-stat flat-additive upgrade shop.
- **Population & civilization layer**: generational population growth, role assignment, rescue events, planet capacity and milestone "tech" unlocks.
- **Persistence**: encrypted (obfuscated) JSON save with checksum, backup and backward-compatibility flags.

The interesting engineering is in the **math and systems plumbing**: hybrid scaling curves, a closed-form/binary-search level curve, event-driven simulation, pooled spawning, target selection, save integrity and data-driven configuration.

---

## 2. My Role

**Solo developer / gameplay & systems programmer.**

Unless otherwise noted, the code, algorithms, data structures and UI logic described here were designed and implemented by me in this codebase. That includes:

- Wave/spawn logic, enemy stat scaling models and difficulty plumbing
- Hero/squad combat, targeting, projectiles, skills and status effects
- Global EXP curve, meta upgrades and the Resilience Point progression
- Population/civilization simulation and its save/migration handling
- Save/load architecture, encryption/checksum/backup and lifetime statistics
- HUD, upgrade UIs, camera switching, morph animations and localization integration
- Optimization work: object pooling, cached buffers, allocation reduction, event-driven UI

I do not claim systems that are not in the source (no multiplayer, AI pathfinding, ECS/DOTS, Addressables usage, networking, etc.).

---

## 3. Technical Highlights

Ranked by engineering value (all verified in source):

1. **Hybrid staged-exponential enemy scaling with an end-game polynomial/log extension**
   `GameBalancer.GetStagedExponential` + `GetEndGameFactor` — piecewise growth rates across three wave bands, then a polynomial+logarithmic tail that avoids unbounded exponential blowup for an endless game.

2. **Closed-form + binary-search EXP/level model with a post-600 geometric extension**
   `GlobalExpSystem` — a precomputed `T(L) = A·L^Q·C^L − A·C` table for levels 1–600, O(log 600) binary search for lookups, and an analytic `log` inverse above 600.

3. **Encrypted, checksummed, versioned save pipeline**
   `SaveManager` — `JsonUtility` serialization, salted-MD5 integrity header, XOR obfuscation, one-generation `.bak` fallback, bounds validation, and `has*`-flag backward compatibility.

4. **Event-driven population simulation with generational cohorts**
   `PopulationSystem` — per-wave ticks, diminishing-returns growth, fractional carry, child cohorts that mature by age, capacity authority delegated to `PlanetManager`, and rescue events on wave completion.

5. **Prefab-keyed object pool with pre-warm and cached hierarchy classification**
   `SimpleObjectPooler` — `Dictionary<int, Queue<GameObject>>` keyed by prefab `InstanceID`, `PooledObjectMarker` for allocation-free recycle, parent cache, and spawn-time visual/Animator reset to defeat pooled-visual bugs.

6. **Target selection with randomized anti-focus-fire and a recyclable candidate buffer**
   `WaveManager.GetRandomEnemy` scans live enemies with squared distances into a static reusable `List`, then picks uniformly at random; `HeroController` adds a soft target lock that re-validates in O(1) per frame.

7. **Parametric UI morph transitions (button ⇄ panel)**
   `UpgradePointsUI` / `ProjectGrowthUI` / `CastleUpgradeSystem` — `AnimationCurve` easing over `Time.unscaledDeltaTime`, interpolating `anchoredPosition`, `localScale` and `CanvasGroup.alpha` with explicit open/closed state machines.

8. **Space Fighter squad system with grid formations and piercing volleys**
   `HeroSquad` / `HeroUnit` — O(size) formation generation, per-unit attack timers with randomized desync, muzzle-point volleys, piercing projectiles with an instance-ID hit set and 50% damage decay per hit.

9. **Dual-currency progression separation (multiplicative vs flat additive)**
   EXP meta items register multiplicative modifiers (`1 + level × pct`) via `MetaUpgradeManager`; RP upgrades apply flat additive bonuses (`level × flat`) through `ResilienceUpgradeManager`. The two never share a registry or save key.

10. **Data-driven design with ScriptableObjects + serializable save DTOs**
    `HeroData`, `EnemyData`, `SkillData`, `BalanceConfig`, `PopulationConfig` define design data; `PlayerData` DTOs (`HeroSaveEntry`, `ChildCohort`, `PlanetSaveData`, `SavedVector3`) carry only runtime state to disk.

---

## 4. Unity System Architecture

Relationship map built from actual object references and static singletons:

```
GameManager (economy: gold/gems, IWaveObserver)
│
├── WaveManager (singleton)
│   ├── SpawnRoutine() ──► SimpleObjectPooler (enemies/bosses)
│   ├── liveEnemies list ──► GetNearestEnemy / GetRandomEnemy (target registry)
│   ├── IWaveObserver broadcast (start / finish)
│   └── currentWave ──► GameBalancer scaling
│
├── Combat
│   ├── HeroController (single hero: targeting, crit, skills)
│   ├── HeroCombatManager / HeroManager (16 slots, save/restore)
│   ├── HeroSquad + HeroUnit (Space Fighter squads)
│   └── ProjectileController ──► MonsterController / BossController
│
├── Balance
│   └── GameBalancer (static formulas) ──► BalanceConfig (ScriptableObject)
│
├── Progression
│   ├── GlobalExpSystem (curve) ◄── GlobalExpManager (PlayerPrefs state)
│   │        └── Page1UI / Page2UI ──► MetaUpgradeManager (multipliers)
│   ├── ResilienceManager (RP wallet + hybrid damage track)
│   │        └── ResilienceUpgradeManager (5 flat upgrades) ──► ResilienceUpgradeUI
│   └── CastleUpgradeSystem / CastleController (castle level, HP/armor/mana)
│
├── Population
│   ├── PopulationSystem (IWaveObserver) ──► PopulationUI / PopulationAssignUI
│   └── PlanetManager (capacity authority) ──► PopulationSystem (OnCapacityChanged)
│
├── Persistence
│   └── SaveManager ◄──► PlayerData (JsonUtility + MD5/XOR + .bak)
│
└── UI
    ├── GameUIManager (HUD values, push model)
    ├── CameraSwitcher + CameraZoomController (16:9, wave zoom)
    └── Upgrade hub morphs (UpgradePointsUI / ProjectGrowthUI / CastleUpgradeSystem)
```

Communication patterns actually used:

- **`IWaveObserver` interface** (`OnWaveStarted/OnWaveFinished/OnEnemyKilled`) implemented by 12 classes and registered in a list on `WaveManager` (dedup via `List.Contains`).
- **C# events**: `GlobalExpManager.OnExpChanged/OnPointsChanged`, `ResilienceManager.OnResilienceChanged` (static), `ResilienceUpgradeManager.OnResilienceUpgradesChanged` (static), `PopulationSystem` events (12), `HeroManager.OnHeroChanged`, `SpaceFighterManager.OnFighterChanged`, `VipSkillManager.OnVipBuffStateChanged` (static), `PlanetManager.OnCapacityChanged`.
- **Retry-based subscription** (`Invoke(nameof(TrySubscribe), 0.5f)`) for systems whose singletons may initialize late.
- **Static utility layer** (`GameBalancer`) for all scaling math; no per-system formula duplication.

---

## 5. Gameplay Systems

### 5.1 Waves (Implemented)

- `WaveManager.StartNextWave()` resets wave gold/RP counters, hides lobby UI, notifies observers, and runs `SpawnRoutine()`.
- Boss cadence: `wave % 5 == 0` → mini boss; `wave % 10 == 0` → mega boss. Boss spawns **after** all escorts die + 1.5 s.
- Completion requires `!isSpawning && liveEnemies.Count == 0 && isWaveActive`; victory popup (2 s) → observers → `currentWave++` → `SaveGame()`.
- Defeat = castle HP ≤ 0 → `ForceStopWave()` (wave does **not** advance, `LastWaveWasDefeat = true`).
- Manual cancel through the pause popup shares the force-stop path.

### 5.2 Heroes & Squads (Implemented)

**Single heroes** (`HeroController`):
- Per-hero attack timer: `interval = max(0.05, 1 / attacksPerSecond)`; idle re-scan throttled to 0.15 s.
- Soft target lock; attack speed buffs from skills + VIP aura; mana-gated active skills; mana-free auto skills.

**Space Fighter squads** (`HeroSquad`/`HeroUnit`):
- `HeroData.IsSquad` selects squad deployment; default 10 units in a grid formation.
- Each unit fires independently at a random in-range enemy; initial `Random.Range(0, 0.15)` desync prevents synchronized volleys.
- One projectile per cached muzzle point; piercing mode when `piercing > 0`.

### 5.3 Skills & Status Effects (Implemented, some effect types Partial)

- `SkillExecutor` routes by `SkillLogicType`: drone summons, instant gold, laser lines, mortar drops, summons/barricades, standard projectiles.
- Skill damage = `heroDamageCurve × powerValue/100 × (1 + valueGrowthRate × (skillLevel−1)) × Meta("AOE Skill Dame")`.
- Statuses are string-ID based: `Freeze`/`Stun` stop movement, `Slow_n` reduces speed, `ArmorBreak_n` increases damage taken; same ID refreshes duration instead of stacking.
- DOT zones are implemented via `HazardArea` ticking every 0.5 s.
- **Partial:** `SearchPriority` (`Nearest/Strongest/Random/LowestHP`), `addCritRate`, `addCritDamage`, `chargeTime`, and several `SkillEffectType` branches are declared but never read.

### 5.4 Castle (Implemented, with duplicate legacy path — Partial)

- `CastleController` holds HP/mana, applies armor then divides by the `"Castle Defense"` meta multiplier (which no UI item currently registers — see Limitations).
- `CastleUpgradeSystem` owns gold-cost castle levels (PlayerPrefs `Castle_Total_Level`) and recalculates max HP/armor/mana.
- A legacy `UpgradeManager.TryUpgradeCastle` path exists too; it mutates the controller without persisting (Unused/legacy duplicate).

---

## 6. Enemy & Wave Systems

### 6.1 Spawn Count — Implemented

```csharp
// WaveManager.cs:341
int totalToSpawn = Mathf.Min(10 + Mathf.FloorToInt(currentWave * enemyCountScalingPerWave), 500);
```

```
SpawnCount(W) = min(10 + floor(W × 3), 500)
```

Batching (`WaveManager.cs:351-365`): `spawnBatchSize = 3`, one `WaitForSeconds(spawnInterval = 1.2f)` per batch.

| Wave | Enemies |
|---|---|
| 1 | 13 |
| 5 (mini boss) | 25 |
| 10 (mega boss) | 40 |
| 100 | 310 |
| 164+ | 500 (cap) |

Spawn positions are randomized within `±1.5 X`, `±0.5 Y` of the spawn point, `Z = 0`.

### 6.2 Enemy Stat Scaling — Implemented (`GameBalancer`)

Staged exponential across three bands (`earlyEnd=200`, `midEnd=2000`, `lateEnd=5000`):

```
mult(W) = rEarly^min(W,200) × rMid^min(W−200,1800) × rLate^min(W−2000,3000)
```

End-game factor beyond wave 5000:

```
end(W) = 1 + (W−5000)·polyFactor + log10(W−5000+1)·logFactor
```

Shipped rates (from `BalanceConfig.asset`): HP `1.012 / 1.010 / 1.015`, DMG `1.005 / 1.010 / 1.015`, Gold `1.008 / 1.005 / 1.002`.

```
MonsterHP  = baseHP  × mult(W) × end(W) × difficultyHP
MonsterDMG = baseDMG × mult(W) × end(W) × difficultyDMG
MonsterGold = baseGold × mult(W) × end(W) × earlyBonus(W) × difficultyGold
              × Meta("Gold") + RP.GoldPerKill
```

`earlyBonus(W)`: ×5 for W ≤ 200 in code defaults (shipped asset: 1.5), lerped to 1.0 by wave 2000.

Boss phase multipliers: mini/mega = `2.0/3.0` (W≤100) → `2.5/4.0` (≤500) → `3.0/5.0` (≤1000) → `3.5/6.0` (≤3000) → `4.0/8.0`. Boss gold ×1.5 (mini) / ×3 (mega); boss speed ×0.7.

**Boss EXP oddity (Partial):** `GameBalancer.CalculateBossExp` exists (`×(1.5 or 3.0)`) but is never called; kills route through `CalculateMonsterExp`, so the boss EXP bonus is not applied.

### 6.3 Difficulty System (Partial — implemented but not wired)

`DifficultyManager` exposes `Casual/Normal/Hard/Nightmare/Endless` and `BalanceConfig.GetConfigForDifficulty` defines HP/DMG/Gold/PlayerDMG multipliers per mode. However, `SetDifficulty`, `LoadFromPrefs`, `SaveToPrefs` have **no call sites**, so runtime is always `Normal`. Additionally `GetConfigForDifficulty` allocates a new `ScriptableObject` per call (**Potential optimization**: cache it).

### 6.4 Spawn Safety & Pooling

Spawns reset `localScale`, position (Z forced 0), all `SpriteRenderer.enabled/color`, and call `Animator.Rebind(); Animator.Update(0f)` — a deliberate fix for pooled-enemy visual corruption. Pre-warm: 6 per normal enemy, 1 per mini boss (mega boss pool not pre-warmed — Partial).

---

## 7. Combat & Targeting Algorithms

### 7.1 Nearest-Enemy Search — O(n)

```csharp
// WaveManager.cs — GetNearestEnemy (squared-distance scan over the central live list)
float minDistSqr = range * range;
for (int i = liveEnemies.Count - 1; i >= 0; i--) {
    ...
    float distSqr = dx * dx + dy * dy;
    if (distSqr < minDistSqr) { minDistSqr = distSqr; near = liveEnemies[i].transform; }
}
```

Used by skills, summons and drones. No priority weights or lowest-HP targeting exists (despite declared enums).

### 7.2 Randomized Target Among In-Range Enemies — O(n) fill + O(1) pick

```csharp
// WaveManager.cs — GetRandomEnemy, static reusable buffer avoids per-call allocation
s_TargetCandidates.Clear();
for (...) if (distSqr <= rangeSqr) s_TargetCandidates.Add(enemy);
int randomIndex = UnityEngine.Random.Range(0, s_TargetCandidates.Count);
return s_TargetCandidates[randomIndex].transform;
```

**Why it matters:** in a tower-defense game, many defenders firing at the same "nearest" enemy wastes DPS; uniform random selection spreads fire while keeping acquisition allocation-free.

### 7.3 Soft Target Lock — O(1)/frame

`HeroController.AcquireTarget()` keeps `_currentTarget` while it is active and inside range; otherwise it re-acquires a random in-range enemy. This prevents per-frame full scans without hard target commitment.

### 7.4 Hero Damage Pipeline — exact order (Implemented)

```
1. base curve: (base + 5a + 0.005a²) × 1.025^a × difficultyPlayerDMG × Meta("Dame")
2. crit roll:  (chance = (c + cPerLevel·(level−1)) × Meta("Critical Chance")) → ×2.0
3. skill marks: per matching effect, dmg ×= (1 + powerValue/100)
4. boss bonus:  dmg ×= (1 + bossDamageBonus/100) vs boss targets
5. Resilience:  dmg = dmg × (1 + 0.0005·L_RP) + 0.5·L_RP
6. element:     dmg ×= Meta(elementKey) on projectile hit
7. target:      dmg ×= (1 + ArmorBreak_n/100), then hp −= dmg
```

`a = max(0, level−1)`. Squads use steps 1, 6, 7 only (no crit/marks/resilience).

### 7.5 Piercing Projectiles — Implemented

- One projectile flies straight; per frame `Physics2D.OverlapCircleNonAlloc(position, 1.0, buffer)` then a tighter `sqrMagnitude < 0.36` center test.
- `HashSet<int>` of enemy instance IDs prevents double hits.
- Damage decays `×= 0.5` per hit while pierce steps remain.
- **Observed behavior (not a claim):** after the decay counter reaches 0 the projectile is not returned to the pool and keeps damaging new enemies at the final value until off-screen (`|x| > 50`).

### 7.6 Projectiles — Implemented

- Homing mode interpolates `Lerp(startPos, targetPos, t)` with `t = timeElapsed / (distance / speed)` and a parabolic arc (`y += arcHeight·4t(1−t)`); the target position is re-read every frame while alive.
- Collision is math-based (`OverlapCircleNonAlloc`), not Rigidbody events; hit dispatch calls `MonsterController.TakeDamage` or `BossController.TakeDamage`.
- Skill projectiles support AOE on hit via callback or a `HazardArea` DOT zone.

### 7.7 Status Effects — Implemented

- Flat `List<StatusInstance>` per enemy (comment states this replaced a Dictionary to avoid GC), linear scan O(s).
- `Slow_n`, `ArmorBreak_n` values are parsed from the suffix after `_` once and cached.
- Same ID refreshes duration; different IDs with the same prefix resolve to the **max** value.

---

## 8. Progression & Experience Systems

### 8.1 Global EXP — Implemented

```
EXP(enemy) = baseExp × W² × TierMult × difficultyGold × Meta("Exp")
TierMult = { Normal 1, Elite 3, Special 6, Boss 25 }
```

`W²` is `Math.Pow(max(1, wave), 2.0)` — deliberately **quadratic**, not exponential, so late-game EXP remains tractable while enemy HP/DMG still grow exponentially. (Implemented; `CalculateBossExp` unused.)

### 8.2 Level Curve — Implemented (closed form + binary search)

For `1 ≤ L ≤ 600`:

```
T(L) = A·L^Q·C^L − A·C        (A = 28.8, Q = 2.5, C = 1.03)
```

Above level 600 the curve continues as a geometric series with ratio `g = 1.06`:

```
T(L) = T(600) + r600 · (g^(L−600) − 1)/(g − 1)
r600 = T(600) − T(599)
```

Level lookup:
- `exp < T(600)`: binary search in a precomputed table → **O(log 600) ≈ O(1)**.
- else: closed-form inverse `L = floor(600 + ln(1 + (exp−T600)(g−1)/r600) / ln g)` → **O(1)**.

Each Global Level grants **+1 Upgrade Point**. (`WAVE_AT_LEVEL_600 = 100000` is declared but unused — Partial.)

### 8.3 Meta Upgrade Points — Implemented

- Pages are data lists of `UpgradeItem { itemName, maxLevel 20, baseBonusPerLevel, cost 1 }`; purchase spends points, saves to PlayerPrefs and re-registers.
- `MetaUpgradeManager` stores **multiplicative** modifiers: `mult = 1 + level × bonusPerLevel`, default `1.0`.
- Verified consumed keys: `Gold`, `Exp`, `Dame`, `Critical Chance`, `Attack Speed`, `Birth Rate`, `Wave to Mature`, `Gold Per Tick`, `Attack Range`, `AOE Skill Dame`, the five element damage keys, and `Castle Defense` (consumer exists; no item registers it).
- Not consumed (Partial/Planned): `CoolDown`, `SpaceShip Defense` (name mismatch vs `Castle Defense`), `Pet Dame`, `Soul`.

### 8.4 Resilience Points — Implemented

Earned: `0.1` per normal kill, `1.0` per mini boss, `10.0` per mega boss. Persisted in the encrypted save (`resiliencePoints`, `resilienceLevel`).

Hybrid base damage track (`ResilienceManager.ApplyResilienceBonus`):

```
FinalDmg = baseDmg × (1 + 0.0005·L) + 0.5·L
```

A separate shop (`ResilienceUpgradeManager`) sells five **flat additive** upgrades with RP, cost `5 + 2L` each:

| Upgrade | Bonus / level | Gameplay consumer |
|---|---|---|
| Hero Damage | +0.5 | `GameBalancer.CalculateHeroDamage` (single heroes/skills) |
| Space Fighter Damage | +0.5 | same function with `isSpaceFighter = true` (squads) |
| Spaceship HP | +25 | `CalculateCastleMaxHP` (+ forces castle stat refresh) |
| Gold Per Kill | +1 | `CalculateMonsterGold` / `CalculateBossGold` |
| Gold Per Tick | +0.1 | `PopulationSystem.ApplyCivilianIncome` |

This satisfies the design requirement "final = existing + flat bonus" without touching EXP registries.

---

## 9. Upgrade Systems

### 9.1 Cost Formulas — Implemented (`GameBalancer`)

**Polynomial 3-stage upgrade cost with continuity correction** (used by the castle):

```
lvl ≤ 30:      cost = baseCost · 1.06^lvl
31 ≤ lvl ≤ 100: cost = baseCost · lvl² · 1.5 · k2
lvl > 100:      cost = baseCost · lvl^1.5 · 5.0 · k3
k2 = 1.06^30 / (30² · 1.5)     (ratio continuity at lvl 30)
k3 = (100² · 1.5 · k2) / (100^1.5 · 5.0)
```

This blends an exponential early game into quadratic then `L^1.5` growth while keeping the curve continuous at the stage boundaries.

**Hero upgrade cost** (multiplicative × additive): `cost(L) = (base + 10·L) × 1.04^L`.

**Resilience costs** (linear): `cost(L) = 5 + 2L` for both the base track and all five RP upgrades.

**Unused cost helpers** (`Partial`): `CalculatePetUpgradeGoldCost`, `CalculatePetUpgradeSoulCost`, `CalculateLeaderUpgradeCost`, `CalculatePetHarvestRate` — defined but never called (no pet/leader systems are wired).

### 9.2 Castle Upgrade — Implemented

```
maxHP   = CalculateCastleMaxHP(baseHP, level)   = baseHP + 100·(level−1) + 25·RP_spaceshipLevel
armor   = baseArmor  + (level−1)·armorGainPerLevel
maxMana = baseMana   + (level−1)·manaGainPerLevel
regen   = baseRegen  + (level−1)·regenGainPerLevel
```

Level persists in PlayerPrefs (`Castle_Total_Level`). `CastleTier` objects add bonus HP. **Partial:** the legacy `UpgradeManager` duplicates castle/hero upgrades without persistence; `hpGainPerLevel` and `castleHPBaseRate/PolyFactor` are unused.

---

## 10. Save & Persistence

### 10.1 Pipeline (Implemented)

```
Gameplay state
  → SaveManager.SyncDataFromGame()  (gathers gold/gems/wave/heroes/population/planets/RP/fighter position)
  → JsonUtility.ToJson(PlayerData)
  → MD5(json + secret) + ":" + json
  → XOR obfuscation with a fixed key
  → player_progress.dat   (previous file copied to player_progress.bak)
```

Load:

```
player_progress.dat → XOR decode → split hash/json → verify MD5
  → JsonUtility.FromJson<PlayerData> → ValidateLoadedData()
  → SaveManager.ApplyDataToGame() (ordered restore)
  → fallback: .bak → CreateNewSave()
```

**Integrity/validation (Implemented):** MD5 mismatch rejects the file; bounds validation (`gold ≥ 0`, `wave ≥ 1`, gold sanity vs wave, gems caps, resilience bounds, 16-slot arrays) rejects corrupted saves. Backup fallback and silent new-save creation on double failure (logs only).

**Backward compatibility (Implemented):** per-feature presence flags — `hasSpaceFighterPosition`, `hasPopulationData`, `hasPlanetData` — let old saves fall back to defaults instead of crashing. There is **no version-based migration** (`PlayerData.version` is declared but never read — Partial).

**Ordered restore highlights:** language → hero levels → economy/wave → resilience → space-fighter pending position → planets **before** population (capacity authority) → hero/fighter slots.

### 10.2 What Is Persisted (verified)

| Storage | Contents |
|---|---|
| Encrypted `.dat` | gold, white/orange gems, current wave, resilience points/level, 16 hero + 16 fighter slots, hero levels, 5 RP upgrade states are PlayerPrefs but the *RP wallet* is in the save, population counts/cohorts/milestones, planet capacity, space-fighter world position, lifetime stats |
| PlayerPrefs | castle level, Global EXP + Upgrade Points, Page1/Page2 meta levels, 5 RP upgrade levels, settings (volume/VSync/FPS/resolution), difficulty (never used) |

### 10.3 Position Persistence — Implemented (pending pattern)

`SpaceFighterManager` saves the active squad's world position into `SavedVector3`. On load, `SetPendingSavedPosition` stores it; `ApplyPendingToLiveSquad` applies it immediately if a squad exists, otherwise `SpaceFighterCombatManager` consumes it when the next squad spawns.

### 10.4 Lifetime Statistics — Implemented

`PlayerStatsManager` tracks first-launch timestamp (write-once, Unix seconds), total play time (only while `Application.isFocused`), total victories (per victory popup) and total monsters defeated (per kill). Auto-save every 60 s; save also on focus loss/pause/quit.

### 10.5 Honest Limitations (code-supported)

- **Not atomic:** `File.WriteAllText` over the live file (no temp+rename); recovery depends on `.bak`.
- **Split progression stores:** PlayerPrefs progression is not covered by the `.dat` backup.
- **Redundant saves at wave end:** observers save (pre-increment wave) and `WaveManager` saves again after incrementing.
- **Obfuscation, not security:** fixed XOR key + salted MD5; stated plainly rather than oversold.

---

## 11. UI Systems

### 11.1 HUD — Implemented (push model)

`GameUIManager` is driven by `GameManager` after state changes only (gold, gems, wave, messages, start button). Gold formatting compresses values (B/T suffixes); gem text uses TMP rich text for `MAX` and orange bonus. No `Update()` polling.

### 11.2 Morph Transition (button ⇄ hub) — Implemented

Shared pattern across `UpgradePointsUI`, `ProjectGrowthUI` and `CastleUpgradeSystem`:

```csharp
t += Time.unscaledDeltaTime / Mathf.Max(0.01f, morphDuration);        // 0.4 s, pause-independent
float e = morphCurve != null ? morphCurve.Evaluate(Mathf.Clamp01(t))
                             : Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t));
hubPanel.anchoredPosition = Vector2.Lerp(startPos, _hubAnchoredPos, e);
hubPanel.localScale = new Vector3(Mathf.Lerp(sX, 1f, e), Mathf.Lerp(sY, 1f, e), 1f);
_hubContentCG.alpha = Mathf.Clamp01((e - 0.3f) / 0.7f);
_openCG.alpha       = Mathf.Clamp01(1f - e / 0.55f);
```

- Sizes are captured once (`CaptureStates`) and start scale is derived as `openSize / hubSize` so the animation grows from the button's exact rect.
- Explicit state machines (`Closed/Opening/Open/Closing` or `_isOpen/_isMorphing`) guard re-entry, stop prior coroutines, and snap final transforms.
- `UpgradeUIStateManager` enforces mutual exclusion between the Castle and Space Fighter hubs.

### 11.3 Population UI Juice — Implemented

- Event-driven text updates with cached-value guards (no per-frame writes).
- Growth bar coroutine at 0.5 s intervals (only recompute when shown).
- Rescue roll-up counter: linear `Mathf.Lerp(from, to, t)` over 0.6 s.
- Milestone pulse: `scale = 1 + 0.25·sin(t·π)`, 0.5 s.
- Income popup rises 40 units with `alpha = 1 − t` over 1.2 s.

### 11.4 Camera — Implemented

- `CameraSwitcher` controls three cameras + UI roots, enforces a **16:9 letterbox/pillarbox** via `Camera.rect`, auto-creates a black culling-mask-0 camera to prevent edge flicker, and only recomputes when the screen aspect changes.
- `CameraZoomController` runs a single guarded coroutine that interpolates inverse zoom `1/orthographicSize` with smoothstep over 1.5 s between normal and battle framings, also moving/scaling UI containers.

### 11.5 Localization — Implemented

Unity Localization with 12 locales, string tables and `LocalizedString`/`StringDatabase` usage; locale changes refresh subscribed UIs and persist through `SaveManager`.

---

## 12. Mathematical Models & Algorithms (formula reference)

| System | Formula | File |
|---|---|---|
| Spawn count | `min(10 + floor(W×3), 500)` | WaveManager.cs |
| Staged enemy multiplier | `rE^min(W,200) · rM^min(W−200,1800) · rL^min(W−2000,3000)` | GameBalancer.cs |
| End-game factor | `1 + (W−5000)·p + log10(W−4999)·l` | GameBalancer.cs |
| Boss phase mult | piecewise `2.0/3.0 … 4.0/8.0` | GameBalancer.cs |
| Enemy EXP | `baseExp · W² · tierMult · difficultyGold · MetaExp` | GameBalancer.cs |
| Level curve | `T(L)=A·L^Q·C^L−A·C`, `A=28.8, Q=2.5, C=1.03` | GlobalExpSystem.cs |
| Post-600 curve | `T600 + r600·(1.06^(L−600)−1)/0.06` | GlobalExpSystem.cs |
| Hero damage | `(base + 5a + 0.005a²)·1.025^a·dmgMult·MetaDame + RPflat` | GameBalancer.cs |
| Attack interval | `max(0.05, 1 / ((baseAS + 0.05·lvl)·MetaAS·(1+buffs/100)))` | HeroController.cs |
| Crit | `chance = (c + cLvl·(lvl−1))·MetaCrit`; `dmg ×= 2` | HeroController.cs |
| Pierce | `dmg ×= 0.5` per hit (while pierce steps remain) | ProjectileController.cs |
| Resilience hybrid | `base·(1+0.0005L) + 0.5L` | ResilienceManager.cs |
| RP shop bonus | `level × {0.5, 0.5, 25, 1, 0.1}` | ResilienceUpgradeManager.cs |
| Polynomial upgrade cost | 3-stage with `k2, k3` continuity factors | GameBalancer.cs |
| Hero upgrade cost | `(base + 10L)·1.04^L` | GameBalancer.cs |
| Castle HP | `base + 100·(level−1) + 25·RP` | GameBalancer.cs |
| Population growth | `births = floor(carry + Civ·rate·clamp(1−0.9·Total/Cap,0,1)·MetaBirth)` | PopulationSystem.cs |
| Civilian income | `Civ·0.5·MetaTick + 0.1·RP` per 5 s | PopulationSystem.cs |
| Morph easing | `e = curve(t)`, `pos = Lerp(a,b,e)`, `scale = Lerp(s,1,e)` | UpgradePointsUI.cs |

Complexity notes:
- Target scans: **O(n)** per acquisition (`n` = live enemies), O(1) per frame with target lock.
- Level lookup: **O(log 600)** ≤ 10 iterations, then O(1).
- Population maturation: **O(C)** typical (reverse scan), worst-case **O(C²)** when many non-adjacent cohorts expire due to `List.RemoveAt` shifting.
- Pool get/recycle: **O(1)** average (dictionary + queue), with O(k) skip of externally destroyed entries.
- Growth tick/income: **O(1)** arithmetic + **O(M)** milestone check (M = 2 by default).

---

## 13. Performance & Optimization

### Implemented optimizations (evidence)

| Technique | Implementation |
|---|---|
| Object pooling | `Dictionary<int, Queue<GameObject>>` keyed by prefab `InstanceID`; `Queue.Dequeue` loop instead of recursion; parent-cache dictionary to classify pool roots once |
| Pre-warm | 6 normal enemies per prefab, 1 mini boss, spread across frames with `yield return null` |
| Allocation-free recycle | `PooledObjectMarker.poolKey` — no name parsing or `GetComponent` classification per return |
| Allocation-free target candidates | static `List<GameObject> s_TargetCandidates` reused by `GetRandomEnemy` |
| Squared-distance math | no `Vector3.Distance` in target loops |
| Non-alloc physics | static `Collider2D[]` buffers + `OverlapCircleNonAlloc` in projectiles, skills, summons, hazard zones |
| Hit bookkeeping | `HashSet<int>` of instance IDs instead of lists |
| Coroutine wait caching | static `WaitForSeconds` in `VFXManager`; per-loop wait object in `PopulationUI` |
| Cached components | `anim`, `spriteRenderer`, `waveManager`, muzzles, active skill instance; cached hero list in Auto-Battle |
| Raycast throttle | path-block check cached for 0.15 s on enemies/bosses |
| Event-driven UI | no per-frame population/HUD recompute; push updates after state changes |
| Change detection for `SetActive` | game speed, auto-battle, VIP UI only toggle on state change |
| Screen-aspect change guard | camera rect recomputed only when aspect delta > 0.001 |
| VFX caps | max 50 total VFX, 20 popups, secondary cap 30 in skill cast |
| Framerate control | `Application.targetFrameRate = 60`, `vSyncCount = 1`, `runInBackground = true`; build-time log stripping (`LogType.Warning` minimum) |
| Unscaled-time UI | morphs and notifications run while paused (`timeScale = 0`) |

### Potential optimizations (identified, not implemented)

- `SimpleObjectPooler`: cap/trim pools; replace `ContainsKey` + indexer double lookups; formal `OnSpawn/OnDespawn` contract (reset logic currently duplicated at spawn sites).
- Piercing projectiles run a per-frame physics scan each; could be throttled or use a shared query.
- `Mortar` (`OverlapCircleAll`) and `Laser` (`CircleCastAll`) allocate — NonAlloc used elsewhere in the same project.
- `SkillTargetHelper.HandleAllEnemies` uses `FindObjectsByType` instead of `WaveManager.liveEnemies`.
- `SkillBarUI` destroys/re-instantiates buttons per hero change.
- HUD text assigned even when unchanged (no diff guard).
- `BalanceConfig.GetConfigForDifficulty` allocates a `ScriptableObject` per `GetConfig()` call (per spawn/stat calc) — cache is commented but absent.
- `Resources.LoadAll<HeroData>` full scans; the project has an `AssetManager` registry that is editor-functional but runtime-unused.

---

## 14. Data Architecture

**Design data — ScriptableObjects:**
`HeroData` (combat/economy/skills/evolution), `EnemyData` + `BossData` (stats, tiers, prefabs), `SkillData` (effects, logic type, VFX), `BalanceConfig` (scaling + difficulty), `PopulationConfig` (simulation tuning), `ShopItemData`.

**Runtime/save data — serializable DTOs:**
`PlayerData` + `HeroSaveEntry`, `SavedVector3`, `ChildCohort`, `PlanetSaveData/PlanetInfo`, `SkillInstance`.

**Verified separation pattern:** most systems only expose `SaveToData(PlayerData)` / `LoadFromData(PlayerData)` and never touch files; `SaveManager` is the single file gate. Backward compatibility is handled with presence flags rather than a migration framework.

**Known anti-pattern (honest):** hero leveling mutates `HeroData.heroLevel` on the design asset; on load the code resets all hero levels to 1 and re-applies saved entries because ScriptableObject edits persist into builds.

---

## 15. Important Code Examples

### 15.1 Staged exponential scaling

```csharp
// GameBalancer.cs — cleanly composable growth bands
int w1 = Mathf.Min(w, earlyEnd);
mult *= Math.Pow(rEarly, w1);
if (w > earlyEnd) { int w2 = Mathf.Min(w - earlyEnd, midEnd - earlyEnd); mult *= Math.Pow(rMid, w2); }
if (w > midEnd)   { int w3 = Mathf.Min(w - midEnd, lateEnd - midEnd);     mult *= Math.Pow(rLate, w3); }
```

*Why it matters:* a single exponential curve either trivializes early waves or explodes later; banded rates give designers three tuning regions without breaking continuity.

### 15.2 Level lookup (binary search + closed form)

```csharp
// GlobalExpSystem.GetLevelFromExp
if (currentExp < _xpTotalTable[600]) {
    int lo = 1, hi = 600;
    while (lo < hi) {
        int mid = lo + (hi - lo + 1) / 2;
        if (currentExp >= _xpTotalTable[mid]) lo = mid; else hi = mid - 1;
    }
    return lo;
} else {
    double val = (currentExp - _xpTotalTable[600]) * (POST_600_GROWTH - 1.0) / _r600 + 1.0;
    return (int)Math.Floor(600.0 + Math.Log(val) / Math.Log(POST_600_GROWTH));
}
```

*Why it matters:* precomputation keeps the common path branch-light; the analytic tail keeps an endless game O(1) even at extreme levels.

### 15.3 Randomized target with reusable buffer

```csharp
// WaveManager.GetRandomEnemy — no per-call allocations
s_TargetCandidates.Clear();
for (...) if (distSqr <= rangeSqr) s_TargetCandidates.Add(enemy);
return s_TargetCandidates[UnityEngine.Random.Range(0, s_TargetCandidates.Count)].transform;
```

### 15.4 Save integrity

```csharp
// SaveManager.SaveGame
string json = JsonUtility.ToJson(currentData);
string hash = GetMD5Hash(json);
string fullContent = hash + ":" + json;
File.Copy(savePath, backupPath, true);         // one-generation backup
File.WriteAllText(savePath, EncryptDecrypt(fullContent));
```

*Why it matters:* detects tampering/truncation and provides recovery without external libraries.

### 15.5 Population growth tick

```csharp
// PopulationSystem.TickGrowth
double diminish = 1.0 - config.diminishingFactor * ((double)total / Mathf.Max(1, capacity));
double potential = (double)_civilians * (double)config.baseGrowthRate * diminish;
_growthCarry += potential;
int birth = Mathf.FloorToInt((float)Math.Floor(_growthCarry));
birth = Mathf.Min(birth, config.allowOverflow ? int.MaxValue : Mathf.Max(0, capacity - total));
```

*Why it matters:* fractional carry + capacity clamp + diminishing returns gives a stable, non-explosive population curve that self-limits at capacity.

---

## 16. Technical Challenges & Solutions

| Problem | Solution (implemented) |
|---|---|
| Pooled enemies spawning with stale visuals (invisible/oversized corpses) | Multi-layer reset on spawn: transform reset, all `SpriteRenderer` enabled/white, `Animator.Rebind() + Update(0)`; duplicated reset in `MonsterController.Setup` |
| Many defenders focus-firing one "nearest" enemy | Random selection among all in-range candidates (uniform), plus soft per-hero target lock to avoid per-frame rescans |
| Unbounded enemy scaling in an endless game | Three-band staged exponential + polynomial/log end-game factor with continuous boundaries |
| Level progression lookup at huge EXP totals | Precomputed table (1–600) + binary search + analytic geometric extension above 600 |
| Save tampering/corruption | Salted-MD5 integrity header + XOR obfuscation + `.bak` fallback + bounds validation |
| Old saves missing new systems | Per-feature presence flags (`has*`) and config defaults instead of migration scripts |
| UI desync after load / between systems | Event-driven UI with retry subscriptions; ordered restore (planets before population capacity) |
| Population growth hitting a wall then needing to resume | Capacity authority (`PlanetManager`) + `OnCapacityChanged` resync; growth carry reset at cap and resumes after expansion |
| Duplicated progression registries/currency confusion | Explicit separation: EXP meta = multipliers in `MetaUpgradeManager`; RP shop = flat additive in `ResilienceUpgradeManager` with its own save keys |
| Pause freezing menu animations | UI morphs/notifications use `Time.unscaledDeltaTime` / `WaitForSecondsRealtime` |

---

## 17. Current Limitations / Technical Debt

Honest, code-supported list:

1. **Difficulty selector not wired** — `DifficultyManager.SetDifficulty/LoadFromPrefs/SaveToPrefs` are never called; runtime is always `Normal`. Unreachable branches remain.
2. **Meta items with no gameplay effect** — `CoolDown`, `Pet Dame`, `Soul`, and `SpaceShip Defense` (name mismatch with the `Castle Defense` consumer). `ProjectGrowthUI` buttons only show notifications and its fields are unwired in the scene; `UpgradePointsUI.TryUpgradeByIndex` is a placeholder.
3. **Two castle upgrade paths** — `CastleUpgradeSystem` (persistent) vs legacy `UpgradeManager.TryUpgradeCastle` (non-persistent). Also `CastleUpgradeSystem.ConsumeGold` returns `true` as a fallback, which can grant free upgrades if `GameManager` is missing or gold is insufficient.
4. **Two castle damage paths** — one applies `"Castle Defense"` division, the other doesn't.
5. **Split persistence model** — castle level, EXP, meta levels and RP shop levels live in PlayerPrefs while the core wallet lives in the encrypted `.dat`; the `.bak` does not protect PlayerPrefs. `PlayerData.castleLevel` and `version` are dead fields.
6. **Non-atomic save writes** — `File.WriteAllText` over the live file.
7. **Up to three `ApplyDataToGame` executions on startup** (`Awake`, `OnSceneLoaded`, `Start` paths) — redundant full re-application.
8. **Hero level dual source** — `HeroController.level` (combat) vs `HeroData.heroLevel` (squads/UI) are not synchronized by the same systems.
9. **`Wave to Mature` sign/semantics bug** — the scene registers a negative bonus while the code divides by the multiplier, making maturation slower instead of faster.
10. **Skill system partial coverage** — `SearchPriority`, `addCritRate/addCritDamage`, `chargeTime` are unused; several `SkillEffectType` values have no dedicated handler.
11. **Piercing projectile pool leak** — after decay steps are exhausted, it keeps flying/damaging until off-screen instead of returning.
12. **Double AOE multiplier** — skill projectiles multiply `"AOE Skill Dame"` at cast and again at hit.
13. **Per-frame costs remain** — piercing physics scan per projectile; `HeroController` allocates physics-independent `GetComponent` lookups per attack; `Debug.DrawLine` per frame in `HeroController.Update`.
14. **Unused/dead systems** — pet and leader code paths, `BossData`, `AssetManager` runtime registry, `LanguageManager` unwired, empty animator controller asset, `VFXCleanupManager` dead fields/`RegisterSkillObject`.
15. **Population UI preview discrepancy** — estimated births omit the `Birth Rate` meta and carry accumulation.
16. **Population drives on defeat/manual stop too** — growth/maturation tick on `OnWaveFinished` even when the wave was lost (only rescue success checks the defeat flag).
17. **`GlobalExpSystem.GetXpTotal` dead local** (`excess`) and unused `GetEnemyCount/GetWaveIncomeEstimate`, which use a different count formula than the live wave system.
18. **Testing/automation** — no unit tests were found in `Assets/Script` for the gameplay systems (verification was manual/in-editor).

---

## 18. Future Improvements

1. Wire the difficulty selector to the existing multiplier pipeline and expose it in settings.
2. Unify castle/hero upgrades into one persistent path; delete the legacy duplicate.
3. Complete or remove unused meta items; fix the `SpaceShip Defense` vs `Castle Defense` key mismatch.
4. Consolidate persistence: move PlayerPrefs progression into `PlayerData`, add atomic write (temp + `File.Replace`) and a simple version-migration table.
5. Introduce a pooled-UI contract (`IPoolable`) and centralize reset logic.
6. Remove per-frame physics scans by throttling piercing checks or batching enemy hit queries.
7. Implement the declared targeting priorities (`Strongest`, `LowestHP`) using the existing candidate list.
8. Add EditMode tests for all formula functions (`GameBalancer`, `GlobalExpSystem`, `PopulationSystem` math) — they are pure and easy to test.
9. Replace `Resources.LoadAll` scans with the existing `AssetManager` registry or Addressables.
10. Convert `GameUIManager` to a `MonoBehaviour` (or delete its dead `Start`) and add text-diff guards.

---

## 19. Skills Demonstrated

Verified through implementation:

- **Unity / C# gameplay engineering** — 108 scripts, MonoBehaviour lifecycle, coroutines, serialization, ScriptableObjects, EventSystems/UI, Physics2D, Animator triggers, Unity Localization.
- **Mathematical game modeling** — staged exponential curves, hybrid linear/quadratic/exponential damage formulas, polynomial upgrade costs with continuity correction, diminishing-returns population growth.
- **Algorithms & data structures** — binary search + analytic inversion, weighted/random selection, soft target locking, HashSets for hit tracking, dictionary/queue object pooling, O(1) capacity authority.
- **Architecture** — singleton service locators, observer pattern (`IWaveObserver`), C# events with retry subscription, static formula layer, data/runtime/save separation, backward-compatible persistence flags.
- **Persistence engineering** — JSON serialization, checksum-based integrity, obfuscation, backup recovery, validation, ordered restore.
- **UI programming** — event-driven HUD, `AnimationCurve`-based morph transitions with unscaled time and state machines, throttled updates, juice (roll-ups, pulses, popups).
- **Performance work** — object pooling with pre-warm, non-alloc physics queries, cached components/waits/lists, change-detection updates, VFX caps, frame-rate control.
- **Engineering judgment** — systems are documented, partials are labeled, and trade-offs (obfuscation vs encryption, PlayerPrefs vs save file) are acknowledged rather than hidden.

---

## 20. Conclusion

This project is the work of a developer who **designs systems mathematically, implements them in production-shaped Unity code, debugs the messy parts (pooled visuals, save corruption, target focus), and optimizes where it counts** — pooling, allocation-free physics, event-driven UI.

The strongest evidence a recruiter can inspect are the **formula-driven systems**: the staged enemy scaling curve, the closed-form EXP level model, the dual multiplicative/additive progression layers, the generational population simulation, and the integrity-checked save pipeline. The codebase also includes honest, documented debt (unwired difficulty, unused meta items, split persistence) — which reflects a real project in active development rather than a polished vertical slice.

**Verification reminder:** every claim above maps to source files in `Assets/Script`. Anything not implemented is labeled **Partial**, **Unused**, **Planned**, or **Not verified from source code**.
