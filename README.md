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
- Economy: gold from kills plus passive civilian income; gems are awarded per completed wave.
- Two independent progression layers: **EXP multiplier meta-upgrades** and **Resilience Point flat-additive upgrades**.
- A **population/civilization simulation** with generational growth, role assignment, rescue events, planet capacity and milestone unlocks.
- Persistence uses obfuscated JSON with an MD5 checksum, one-generation backup and backward-compatible flags.

## My Role

Solo developer. I designed and implemented the gameplay systems, scaling math, progression, population simulation, save architecture, UI logic and optimization work described here. Unfinished or legacy areas are labeled rather than omitted.

## Technical Highlights

1. **Enemy scaling for an endless game**  
   Three staged exponential bands with continuous handoffs, followed by a polynomial/log end-game tail. `GameBalancer` uses boundaries at waves **200 / 2000 / 5000** with shipped HP rates `1.012 / 1.010 / 1.015`.

2. **Closed-form level curve**  
   Levels **1–600** use a precomputed EXP table with binary search; levels above 600 use an analytic inverse. Lookup is ≤10 comparisons in the table range and O(1) beyond it.

3. **Save integrity pipeline**  
   `JsonUtility` → salted-MD5 header → XOR → `player_progress.dat`, with a one-generation `.bak`, bounds validation, fallback loading and compatibility flags.

4. **Event-driven population simulation**  
   Per-wave growth ticks with diminishing returns and fractional carry; age-based cohort maturation; capacity owned by one authority; UI updates are event-driven rather than per-frame polling.

5. **Prefab-keyed object pooling**  
   `Dictionary<int, Queue<GameObject>>` keyed by prefab `InstanceID`, reusable marker components, parent caching and pre-warm. Average get/recycle cost is O(1).

6. **Randomized anti-focus-fire targeting**  
   In-range enemies are collected into a reusable candidate list and sampled uniformly; heroes retain a soft target lock revalidated in O(1) per frame.

7. **Squad combat architecture**  
   A 10-unit formation shares squad-level data while each unit maintains its own attack timer, cached muzzle point and projectile flow. Initial attack desync reduces synchronized volleys.

8. **Data-driven progression**  
   ScriptableObjects hold design data while multiplicative meta upgrades and flat-additive Resilience upgrades use separate registries and save keys.

## Architecture

```text
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

### Communication & Design Decisions

- **`IWaveObserver`** (`OnWaveStarted/OnWaveFinished/OnEnemyKilled`) is registered on `WaveManager` and implemented by 12 systems.
- **C# events** drive UI/progression updates such as `OnPointsChanged`, `OnResilienceChanged`, population events and capacity changes.
- **`GameBalancer`** centralizes scaling formulas; **`SaveManager`** is the file gate.
- **ScriptableObjects** hold design data; serializable DTOs hold runtime/save state.
- Banded scaling keeps early/mid/late tuning independent while remaining continuous.
- Uniform random targets spread defender DPS instead of forcing nearest-target focus.
- Separate multiplicative and flat-additive registries prevent percentage/flat stacking from mixing.
- Checksum + backup provide integrity/obfuscation without external crypto dependencies; this is explicitly **not cryptographic security**.

### Data Flow

```text
Enemy death → gold + Resilience Points + EXP → Global Level → Upgrade Points → meta multipliers
Wave end    → population tick + maturation + rescue roll → currentWave++ → SaveGame
```

## Core Systems

### Waves & Scaling — Implemented

```text
SpawnCount(W) = min(10 + floor(W × 3), 500)
scaling(W)    = rEarly^min(W,200) × rMid^min(W−200,1800) × rLate^min(W−2000,3000) × end(W)
end(W)        = 1 + (W − 5000) × p + log10(W − 4999) × l
```

```csharp
// GameBalancer.cs — continuous handoffs between growth bands
int w1 = Mathf.Min(w, earlyEnd); mult *= Math.Pow(rEarly, w1);
if (w > earlyEnd) { int w2 = Mathf.Min(w - earlyEnd, midEnd - earlyEnd); mult *= Math.Pow(rMid, w2); }
if (w > midEnd)   { int w3 = Mathf.Min(w - midEnd, lateEnd - midEnd);     mult *= Math.Pow(rLate, w3); }
```

- Boss cadence: mini every 5th wave, mega every 10th; bosses spawn after escorts are cleared (+1.5 s).
- Victory increments the wave and saves; defeat and manual cancel do not advance.
- Per-mode difficulty multipliers exist, but the selector is **Unused** and runtime remains Normal.
- Spawn loop is O(N); target acquisition is O(n) over live enemies.

### Combat & Targeting — Implemented

```text
HeroDamage = (base + 5a + 0.005a²) × 1.025^a × playerDmgMult × Meta("Dame") + RP_flat
CritChance = (c + cPerLevel × (level − 1)) × Meta("Critical Chance")
AttackRate = (baseAS + 0.05 × level) × Meta("Attack Speed") × (1 + buff%/100)
Interval   = max(0.05, 1 / AttackRate)
Resilience = base × (1 + 0.0005 × L) + 0.5 × L
```

```csharp
// WaveManager.GetRandomEnemy — reusable candidate list, no per-call allocation
s_TargetCandidates.Clear();
for (...) if (distSqr <= rangeSqr) s_TargetCandidates.Add(enemy);
return s_TargetCandidates[Random.Range(0, s_TargetCandidates.Count)].transform;
```

- Targeting: O(n) nearest-enemy scans for skills/summons; uniform random selection for defenders; soft hero lock revalidated in O(1).
- Damage order: base curve → crit → skill marks / boss bonus → resilience → element multiplier → target `ArmorBreak`.
- Piercing uses `OverlapCircleNonAlloc` plus a hit `HashSet<int>`; each pierce applies `×0.5` damage.
- Status effects use string IDs with refresh/max-value rules; DOT zones tick every 0.5 s.
- Skills route through `SkillLogicType`; some declared targeting priorities/effect types remain **Partial/Unused**.
- Squad units fire independently with random in-range targets and initial timing desync.

### Progression & Simulation — Implemented

```text
EnemyEXP = baseExp × W² × tierMult{1, 3, 6, 25} × difficultyGold × Meta("Exp")

T(L) = 28.8·L^2.5·1.03^L − 28.8·1.03                     (1 ≤ L ≤ 600)
T(L) = T600 + r600·(1.06^(L−600) − 1)/0.06               (L > 600)
```

```csharp
// GlobalExpSystem.GetLevelFromExp
if (currentExp < _xpTotalTable[600]) {              // binary search 1..600
    int lo = 1, hi = 600;
    while (lo < hi) {
        int mid = lo + (hi - lo + 1) / 2;
        if (currentExp >= _xpTotalTable[mid]) lo = mid; else hi = mid - 1;
    }
    return lo;
}
double val = (currentExp - _xpTotalTable[600]) * (POST_600_GROWTH - 1.0) / _r600 + 1.0;
return (int)Math.Floor(600.0 + Math.Log(val) / Math.Log(POST_600_GROWTH));
```

- Each global level grants +1 Upgrade Point; level lookup uses binary search then a closed-form tail.
- Meta upgrades use `multiplier = 1 + level × pct`; several registered items have no gameplay consumer (**Partial**).
- Resilience Points are earned from kills/bosses and feed five flat-additive upgrades:

| Upgrade | Bonus/level | Consumed by |
|---|---:|---|
| Hero Damage | +0.5 | single-hero/skill damage |
| Space Fighter Damage | +0.5 | squad damage |
| Spaceship HP | +25 | castle max HP |
| Gold Per Kill | +1 | enemy/boss gold |
| Gold Per Tick | +0.1 | civilian income tick |

- Population uses diminishing-return growth, fractional carry, age-based maturation, rescue events and capacity-controlled milestones.
- Civilian income is event/tick driven rather than continuously recalculated in UI.
- Castle HP grows additively: `base + 100·(level−1) + 25·RP`.

### Persistence — Implemented

```text
save:  JsonUtility.ToJson(PlayerData) → MD5(json + key) + ":" + json → XOR → player_progress.dat
load:  decode → verify MD5 → deserialize → validate → apply → fallback to .bak → new save
```

```csharp
// SaveManager.SaveGame
string json = JsonUtility.ToJson(currentData);
string hash = GetMD5Hash(json);
string fullContent = hash + ":" + json;
File.Copy(savePath, backupPath, true);
File.WriteAllText(savePath, EncryptDecrypt(fullContent));
```

- Restore order is deliberate: language → progression/economy → resilience → Space Fighter position → planets/capacity → population → slots.
- Validation covers wallet/wave bounds, gem caps, resilience bounds and slot-array length.
- Compatibility flags support older saves, but there is no version-based migration (**Partial**).
- Core save data and several progression values are split between the encrypted file and PlayerPrefs (**design debt**).
- Security note: XOR + salted MD5 provide **obfuscation/integrity, not cryptographic encryption**.

## Performance Engineering

- **Pooling:** prefab-`InstanceID` dictionary + queue, marker component, parent cache and pre-warm; O(1) average get/recycle.
- **Reusable buffers:** static `Collider2D[]` with `OverlapCircleNonAlloc` and reusable target candidates.
- **Precomputed search:** EXP table + binary search; no full curve evaluation for common level lookups.
- **Caching:** components, muzzle points, active skill instances and hero lists; enemy path checks throttled to 0.15 s.
- **Event-driven UI:** push updates after state changes; population UI has no `Update()`.
- **Additional controls:** cached waits, VFX caps, aspect-ratio recalculation only when needed, vSync/60 FPS configuration.

No performance percentages are claimed because the repository does not contain a benchmark suite.

## Verified Limitations

| Issue | State | Impact / Note |
|---|---|---|
| Difficulty selector never invoked | Partial | Runtime always uses Normal |
| Meta items without consumers / key mismatch | Partial / Unused | Purchases can have no gameplay effect |
| Legacy `UpgradeManager` castle/hero path | Unused / legacy | Duplicates another upgrade path without persistence |
| `CastleUpgradeSystem.ConsumeGold` fallback | Bug | Can allow free upgrades if `GameManager` is missing |
| Split persistence | Design debt | `.bak` does not protect PlayerPrefs |
| Non-atomic save write | Design debt | Recovery depends on `.bak` |
| Dual hero-level sources | Inconsistency | Leveling paths can diverge |
| `Wave to Mature` bonus mismatch | Bug | Current configuration slows maturation instead of accelerating it |
| Piercing projectile pool lifecycle | Bug | Can continue damaging beyond expected decay flow |
| Declared skill priorities/effects not fully consumed | Partial | Runtime targeting remains nearest/random in those paths |
| No gameplay unit tests | Gap | Pure formula functions are currently untested |
| Pet / leader / some boss/runtime manager paths | Unused | Dead or editor-only code |

## AI-Assisted Development

This project was developed using **AI-assisted engineering workflows** alongside hands-on design, implementation, testing, debugging and technical decision-making.

AI was used during development for tasks such as:

- turning gameplay requirements into implementation plans;
- reviewing architecture and identifying technical risks;
- generating and refining C# implementation approaches;
- diagnosing bugs and edge cases;
- analyzing algorithms, formulas and performance trade-offs;
- iterating through implementation → review → testing → refinement.

The project itself does **not** currently contain a machine-learning model. AI assisted the **development process**, while the final systems, validation and technical decisions remained grounded in the actual project requirements and source code.

### Future AI Directions

Planned explorations only, not current features:

- Adaptive difficulty driven by player-performance telemetry
- Player behavior modeling for pacing and economy tuning
- Automated balancing through simulation and parameter search
- Learned targeting policies replacing uniform random selection
- Simulation-based optimization of wave and progression curves

## Showcase Scope

This repository is a selected subset of the project's programming source, limited for confidentiality and security reasons; art, audio and full project content are omitted. Technical claims are restricted to what can be verified in the available source, and incomplete or unused systems are labeled rather than presented as finished.

## Tech Stack

**Unity** `6000.3.10f1` · **C#** · UnityEngine.UI + TextMeshPro · Physics2D (`OverlapCircleNonAlloc`, raycasts) · Animator triggers/bools · Coroutines (`WaitForSeconds` / `WaitForSecondsRealtime`) · ScriptableObjects · `JsonUtility` + MD5/XOR save pipeline · PlayerPrefs · Unity Localization (12 locales).

The public repository does not use Addressables, Jobs/Burst, ECS/DOTS, networking, or machine-learning models.
