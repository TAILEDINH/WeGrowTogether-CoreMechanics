# We Grow Together

**2D wave-based space-defense / tower-defense hybrid built in Unity 6 and C#.**
Solo-developed, systems-heavy project: **108 scripts / ~18,350 lines** under `Assets/Script`, focused on mathematical game modeling, event-driven simulation, combat architecture, persistence integrity and allocation-conscious engineering.

<img width="1095" height="629" alt="image" src="https://github.com/user-attachments/assets/ba57099e-9b18-4707-b02c-101fa9aedc60" />
<img width="1099" height="626" alt="image" src="https://github.com/user-attachments/assets/65a97075-cf1d-4189-bdce-d4ea6f17d2e8" />
<img width="1093" height="624" alt="image" src="https://github.com/user-attachments/assets/2faf0abf-d950-41d5-8e0a-32d34606458d" />
<img width="1097" height="619" alt="image" src="https://github.com/user-attachments/assets/092ef3ef-fc76-4152-9016-33ad9cbad5cd" />
<img width="1097" height="624" alt="image" src="https://github.com/user-attachments/assets/8865cbb0-29a1-41b0-a1cb-a9bbba9b92d9" />

> **Labels used below:** **Implemented** = wired into runtime · **Partial** = present but incomplete/inconsistent · **Unused** = written but not wired · **Planned** = design/comment only.

## Project Overview

- Endless waves of enemies attack a spaceship castle; the player deploys single heroes and 10-unit **Space Fighter squads** on a 16-slot grid.
- Economy: gold from kills plus passive civilian income; gems awarded per completed wave.
- Two independent progression layers: **EXP multiplier meta-upgrades** and **Resilience Point flat-additive upgrades**.
- A **population/civilization simulation**: generational growth, role assignment, rescue events, planet capacity and milestone unlocks.
- Persistence: obfuscated JSON save with MD5 checksum, one-generation backup and backward-compatible migration flags.

## My Role

Solo developer. I designed and implemented the gameplay systems, scaling math, progression, population simulation, save architecture, UI logic and optimization work described here. Unfinished or legacy areas are labeled rather than omitted. No multiplayer, networking, ECS/DOTS, Jobs/Burst or Addressables usage exists in this codebase.

## Technical Highlights

1. **Enemy scaling that stays tunable in an endless game**
   Problem: exponential growth either trivializes early waves or explodes later.
   Approach: three-band staged exponential with continuous handoffs plus a polynomial/log end-game tail.
   Detail: `GameBalancer` — bands end at waves 200 / 2000 / 5000; shipped HP rates `1.012 / 1.010 / 1.015`.

2. **Closed-form level curve with O(1) tail lookup**
   Problem: exact level lookup at arbitrary EXP totals.
   Approach: precomputed table for levels 1–600 + binary search, analytic inverse above 600.
   Detail: lookup in ≤10 steps for levels 1–600 and O(1) closed form beyond (`GlobalExpSystem`); curve shown in Core Systems.

3. **Save integrity pipeline**
   Problem: persistence must survive corruption, tampering and old saves.
   Approach: `JsonUtility` → salted-MD5 header → XOR → `player_progress.dat` with a one-generation `.bak`.
   Detail: bounds validation, backup fallback, and `has*` migration flags (`SaveManager`, `PlayerData`).

4. **Event-driven population simulation**
   Problem: model a living civilization without per-frame polling.
   Approach: per-wave growth ticks with diminishing returns and fractional carry; child cohorts mature by age; capacity owned by a single authority.
   Detail: growth math and tick flow in Core Systems; events drive a UI with no per-frame updates (`PopulationSystem`, `PlanetManager`).

5. **Prefab-keyed object pooling**
   Problem: constant spawn/despawn churn of enemies, projectiles and VFX.
   Approach: `Dictionary<int, Queue<GameObject>>` keyed by prefab `InstanceID`, marker component for allocation-free recycle, parent cache and pre-warm.
   Detail: O(1) average get/recycle; reset logic fixes pooled-visual corruption (`SimpleObjectPooler`).

6. **Randomized target selection with anti-focus fire**
   Problem: many defenders repeatedly focus the same nearest enemy.
   Approach: scan in-range candidates into a reusable static list and pick uniformly; heroes keep a soft target lock re-validated in O(1) per frame.
   Detail: O(n) candidate fill + O(1) pick (`WaveManager.GetRandomEnemy`).

7. **Squad combat architecture**
   Problem: many independent shooters without per-frame overhead.
   Approach: one squad object with shared stats, per-unit attack timers with random desync, cached muzzle points, piercing volleys with an instance-ID hit set.
   Detail: 10-unit grid formation, 0–0.15 s initial desync, 50% pierce damage decay (`HeroSquad` / `HeroUnit`).

8. **Data-driven configuration with separated progression registries**
   Problem: designers must tune without code, and two currencies must not mix.
   Approach: ScriptableObjects for design data; a multiplicative meta registry vs a flat-additive Resilience registry with separate save keys.
   Detail: `HeroData`, `EnemyData`, `SkillData`, `BalanceConfig`, `PopulationConfig` (`MetaUpgradeManager` vs `ResilienceUpgradeManager`).

## Architecture

```
Game / Wave   GameManager, WaveManager (IWaveObserver hub), SimpleObjectPooler
Combat        HeroController, HeroSquad/HeroUnit, ProjectileController, Monster/BossController
Balance       GameBalancer (static formulas) ← BalanceConfig (ScriptableObject)
Progression   GlobalExpSystem/GlobalExpManager → MetaUpgradeManager
              ResilienceManager + ResilienceUpgradeManager
              CastleController / CastleUpgradeSystem
Population    PopulationSystem (IWaveObserver) → PopulationUI; PlanetManager (capacity)
Persistence   SaveManager ⇄ PlayerData (JsonUtility + MD5/XOR + .bak)
UI            GameUIManager (push model), camera switching/zoom, upgrade-hub transitions
```

Communication patterns:

- **`IWaveObserver`** (`OnWaveStarted/OnWaveFinished/OnEnemyKilled`) registered on `WaveManager` and implemented by 12 systems.
- **C# events** for UI/progression: `OnPointsChanged`, `OnResilienceChanged`, population events, `OnCapacityChanged`.
- **Static formula layer** (`GameBalancer`) keeps scaling math in one place; **`SaveManager`** is the single file gate.
- **ScriptableObjects** hold design data; **serializable DTOs** hold runtime/save state.

### Design Decisions

- **Banded scaling over a single exponential** — early/mid/late tuning stay independent while the curve remains continuous.
- **Uniform random targets over nearest-only** — spreads defender DPS across the wave instead of focus-firing one enemy.
- **Separate multiplicative and flat-additive registries** — prevents percentage stacking exploits and keeps save keys independent.
- **Checksum + backup over external crypto libraries** — integrity without dependencies; the limitation (obfuscation, not security) is stated explicitly.
- **Static formula layer (`GameBalancer`)** — one authoritative scaling source for balance changes instead of per-system math.

### Data Flow

```
Enemy death → gold + Resilience Points + EXP → Global Level → Upgrade Points → meta multipliers
Wave end    → population tick + maturation + rescue roll → currentWave++ → SaveGame
```

## Core Systems

### Waves & Scaling — Implemented

```
SpawnCount(W) = min(10 + floor(W × 3), 500)          // batches of 3 every 1.2 s
scaling(W)    = rEarly^min(W,200) × rMid^min(W−200,1800) × rLate^min(W−2000,3000) × end(W)
end(W)        = 1 + (W − 5000) × p + log10(W − 4999) × l
```

```csharp
// GameBalancer.cs — continuous handoffs between growth bands
int w1 = Mathf.Min(w, earlyEnd); mult *= Math.Pow(rEarly, w1);
if (w > earlyEnd) { int w2 = Mathf.Min(w - earlyEnd, midEnd - earlyEnd); mult *= Math.Pow(rMid, w2); }
if (w > midEnd)   { int w3 = Mathf.Min(w - midEnd, lateEnd - midEnd);     mult *= Math.Pow(rLate, w3); }
```

- Boss cadence: mini every 5th wave, mega every 10th; bosses spawn after escorts are cleared (+1.5 s) with phase multipliers from `2.0/3.0` up to `4.0/8.0`.
- Victory increments the wave and saves; defeat (castle HP 0) and manual cancel do not advance.
- Per-mode difficulty multipliers exist (Casual→Endless) but the selector is **Unused**: runtime is always Normal.
- Complexity: spawn loop O(N); target acquisition O(n) over live enemies.

### Combat & Targeting — Implemented

```
HeroDamage = (base + 5a + 0.005a²) × 1.025^a × playerDmgMult × Meta("Dame") + RP_flat,   a = level − 1
CritChance = (c + cPerLevel × (level − 1)) × Meta("Critical Chance")      // crit ×2
AttackRate = (baseAS + 0.05 × level) × Meta("Attack Speed") × (1 + buff%/100)
Interval   = max(0.05, 1 / AttackRate)
Resilience = base × (1 + 0.0005 × L) + 0.5 × L
```

- **Targeting:** O(n) nearest-enemy scan for skills/summons; uniform random among in-range candidates for defenders; soft per-hero lock re-validated in O(1).

```csharp
// WaveManager.GetRandomEnemy — reusable static candidate list, no per-call allocation
s_TargetCandidates.Clear();
for (...) if (distSqr <= rangeSqr) s_TargetCandidates.Add(enemy);
return s_TargetCandidates[Random.Range(0, s_TargetCandidates.Count)].transform;
```

- **Damage order:** base curve → crit → skill marks / boss bonus → resilience → element multiplier → target `ArmorBreak`.
- **Projectiles:** homing `Lerp` with parabolic arc, or straight piercing using `OverlapCircleNonAlloc(1.0)` plus a `0.36` squared center test; `HashSet<int>` of hit instance IDs; `×0.5` damage per pierce hit.
- **Statuses:** string-ID list (no stacking; duration refresh; max value per prefix): `Freeze/Stun`, `Slow_n`, `ArmorBreak_n`; DOT zones tick every 0.5 s.
- **Skills:** routed by `SkillLogicType` (drones, instant gold, laser, mortar, summons, projectiles); skill power `= heroDamageCurve × powerValue/100 × (1 + valueGrowthRate × (skillLevel−1)) × Meta("AOE Skill Dame")`. Declared targeting priorities (`Strongest`, `LowestHP`) and some effect types are **Partial/Unused**.
- **Squads:** each unit fires independently at a random in-range enemy, one projectile per cached muzzle, initial desync avoids synchronized volleys.

### Progression & Simulation — Implemented

```
EnemyEXP = baseExp × W² × tierMult{1, 3, 6, 25} × difficultyGold × Meta("Exp")
T(L)     = 28.8·L^2.5·1.03^L − 28.8·1.03            (1 ≤ L ≤ 600)
T(L)     = T600 + r600·(1.06^(L−600) − 1)/0.06      (L > 600),  r600 = T600 − T599
```

```csharp
// GlobalExpSystem.GetLevelFromExp
if (currentExp < _xpTotalTable[600]) {              // binary search 1..600
    int lo = 1, hi = 600;
    while (lo < hi) { int mid = lo + (hi - lo + 1) / 2;
        if (currentExp >= _xpTotalTable[mid]) lo = mid; else hi = mid - 1; }
    return lo;
}
double val = (currentExp - _xpTotalTable[600]) * (POST_600_GROWTH - 1.0) / _r600 + 1.0;
return (int)Math.Floor(600.0 + Math.Log(val) / Math.Log(POST_600_GROWTH));
```

- **Global EXP → Level → Points:** each level grants +1 Upgrade Point; lookup is binary search (≤10 steps) then closed form; PlayerPrefs persistence with 5 s auto-save.
- **Meta upgrades:** data-driven page items; `multiplier = 1 + level × pct`. Several registered items have no gameplay consumer (**Partial**).
- **Upgrade costs:** polynomial 3-stage curve with continuity factors — `baseCost·1.06^L` (L≤30) → `baseCost·L²·1.5·k2` (L≤100) → `baseCost·L^1.5·5·k3`; hero levels use `(base + 10L) × 1.04^L`.
- **Resilience Points:** earned `0.1` per kill, `1` per mini boss, `10` per mega boss. The base track is the hybrid percentage + flat damage formula shown in Combat & Targeting; a shop sells five flat-additive upgrades with RP, cost `5 + 2L` each:

  | Upgrade | Bonus/level | Consumed by |
  |---|---|---|
  | Hero Damage | +0.5 | single-hero/skill damage |
  | Space Fighter Damage | +0.5 | squad damage |
  | Spaceship HP | +25 | castle max HP |
  | Gold Per Kill | +1 | enemy/boss gold |
  | Gold Per Tick | +0.1 | civilian income tick |

- **Population simulation:** per-wave growth with diminishing returns and fractional carry; child cohorts mature by age; civilian income `Civilians × 0.5 × Meta + 0.1·RP` per 5 s; rescue events (20% chance, 3–12 survivors, 30% children); milestones at 500/1000 population fire once and persist. Capacity is owned by `PlanetManager` and synced via events.

```csharp
// PopulationSystem.TickGrowth
double diminish = 1.0 - config.diminishingFactor * (total / (double)Mathf.Max(1, capacity));
double potential = _civilians * config.baseGrowthRate * diminish;   // × Meta("Birth Rate")
_growthCarry += potential;
int birth = Mathf.Min((int)Math.Floor(_growthCarry), capacity - total);
```

- **Castle:** `maxHP = base + 100·(level−1) + 25·RP`; armor/mana grow linearly per level.
- **Population UI** is fully event-driven (no `Update()`), with a 0.5 s throttled growth bar, roll-up counter, milestone pulse and income popups.

### Persistence — Implemented

```
save:  JsonUtility.ToJson(PlayerData) → MD5(json + key) + ":" + json → XOR → player_progress.dat
load:  decode → verify MD5 → deserialize → validate → apply in order → fallback to .bak → new save
```

```csharp
// SaveManager.SaveGame
string json = JsonUtility.ToJson(currentData);
string hash = GetMD5Hash(json);
string fullContent = hash + ":" + json;
File.Copy(savePath, backupPath, true);          // one-generation backup
File.WriteAllText(savePath, EncryptDecrypt(fullContent));
```

- **Ordered restore:** language → hero levels → economy/wave → resilience → pending Space Fighter position → planets **before** population (capacity authority) → hero/fighter slots.
- **Validation:** gold vs wave bounds, gem caps, resilience bounds, 16-slot array length; failed main file falls back to `.bak`; double failure creates a new save (logs only).
- **Backward compatibility:** `hasSpaceFighterPosition` / `hasPopulationData` / `hasPlanetData` flags select defaults instead of migrating; no version-based migration (**Partial**).
- **Split storage (design debt):** castle level, Global EXP, meta levels and RP shop levels live in PlayerPrefs; core wallet/population/planets live in the encrypted save.
- **Lifetime stats:** first-launch timestamp, focused playtime, victories, monsters defeated; 60 s auto-save plus focus/pause/quit hooks.
- **Security note:** XOR + salted MD5 provide integrity/obfuscation, not cryptographic encryption.

## Performance Engineering

- **Pooling** — prefab-`InstanceID` dictionary + queue, marker component for recycle, parent cache, pre-warm (6 per enemy, 1 per mini boss); O(1) average.
- **Reusable buffers** — static `Collider2D[]` with `OverlapCircleNonAlloc` in projectiles, skills, summons and hazard zones; static candidate list reused for targeting.
- **Precomputed + logarithmic** — EXP table + binary search; no curve evaluation per lookup.
- **Caching** — components, muzzle points, active skill instance, hero list; enemy path checks throttled to 0.15 s.
- **Event-driven UI** — push updates after state changes only; population UI has no per-frame update; text/`SetActive` change guards.
- **Misc** — cached `WaitForSeconds`, VFX caps (50 total / 20 popups / 30 skill VFX), 16:9 camera rect recomputed only on aspect change, 60 FPS + vSync configuration, build log stripping.
- **Potential optimizations (not implemented, no benchmarks exist):** pool caps/trim, throttled per-frame piercing scans, NonAlloc for mortar/laser queries, registry-based skill targets instead of `FindObjectsByType`, pooled skill buttons.

## Verified Limitations

| Issue | State | Impact / Note |
|---|---|---|
| Difficulty selector never invoked | Partial | Always Normal; Casual→Endless branches unreachable |
| Meta items without consumers (`CoolDown`, `Pet Dame`, `Soul`; `SpaceShip Defense` key mismatch) | Partial / Unused | Purchases have no gameplay effect |
| Legacy `UpgradeManager` castle/hero path duplicates `CastleUpgradeSystem` | Unused / legacy | Mutates runtime objects without persistence |
| `CastleUpgradeSystem.ConsumeGold` returns `true` as fallback | Bug | Can grant free upgrades if `GameManager` is missing |
| Split persistence (PlayerPrefs vs encrypted save) | Design debt | `.bak` does not protect PlayerPrefs; `PlayerData.castleLevel`/`version` are dead |
| Non-atomic save write (`File.WriteAllText` over live file) | Design debt | Recovery depends entirely on `.bak` |
| Up to three `ApplyDataToGame` executions on startup | Inefficiency | Redundant full re-application |
| `HeroController.level` vs `HeroData.heroLevel` dual source | Inconsistency | Leveling paths can diverge |
| `Wave to Mature` registered with negative bonus while code divides | Bug | Maturation slows instead of accelerating |
| Piercing projectile not returned to pool after decay steps | Bug | Keeps damaging until off-screen |
| Skill priorities / crit bonuses / some effect types declared but unread | Partial | Targeting is nearest/random only |
| No gameplay unit tests | Gap | Formula functions are pure and untested |
| Pet / leader / `BossData` / runtime `AssetManager` / `LanguageManager` unwired | Unused | Dead or editor-only code |

## Relevance to AI

This is **not** an AI/ML project. The work here is relevant as a foundation: mathematical modeling of systems, probabilistic behavior (randomized target and rescue selection), state-based simulation, allocation-aware optimization, and data-driven configuration. It demonstrates the engineering discipline such systems require — not machine learning experience.

### Future AI Directions

Planned explorations only, not current features:

- Adaptive difficulty driven by player performance telemetry
- Player behavior modeling for pacing and economy tuning
- Automated balancing via simulation and parameter search
- Learned targeting policies replacing uniform random selection
- Simulation-based optimization of wave and progression curves

## Showcase Scope

This repository is a selected subset of the project's programming source, limited for confidentiality and security reasons; art, audio and full project content are omitted. Every claim in this document is restricted to what can be verified in the available source code, and incomplete systems are labeled rather than presented as finished.

## Tech Stack

**Unity** `6000.3.10f1` · **C#** · UnityEngine.UI + TextMeshPro · Physics2D (`OverlapCircleNonAlloc`, raycasts) · Animator triggers/bools · Coroutines (`WaitForSeconds` / `WaitForSecondsRealtime`) · ScriptableObjects · `JsonUtility` + MD5/XOR save pipeline · PlayerPrefs · Unity Localization (12 locales).

Not used anywhere in the source: Addressables APIs, Jobs/Burst, ECS/DOTS, networking, machine learning.
