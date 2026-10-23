# Simulation core

Shared gameplay rules in `Shared/PristonTale.Simulation` used by both the server (authority) and the client
(prediction and presentation): characters, classes, items, inventory, combat formulas, the modifier engine,
progression, regeneration and movement rates. `World/`, `Physics/` and `Skills/` are covered by
[world.md](world.md) and [skills.md](skills.md).

## Read this when

- Changing how a derived stat (life, mana, stamina, attack rating, defence, absorption, damage, speed) is computed.
- Adding a stat a buff, item or passive can change, or changing how modifiers stack or expire.
- Changing hit, block, critical, element or absorption math for players or monsters.
- Adding an item family, an equipment rule, a grid size or an item code mapping.
- Changing inventory placement, stacking, equip rules or the command/revision contract.
- Changing experience, level-up rewards, stat/skill points or skill-master training rules.
- Changing life/mana/stamina regeneration, walk/run speed or animation rates.
- Adding a class or changing a class's base attributes or formula profile.

## Code map

| Path | Responsibility |
| --- | --- |
| `PristonTale.Simulation.csproj` | net10.0, references only `PristonTale.Geometry`; embeds `Skills/Data/Classes/*.json` |
| `SimulationCore.cs` | Description constant only |
| `Actors/ActorDefinitions.cs` | `PlayerCompositionFactory` and bone socket names; not referenced by other projects |
| `Characters/CharacterEnums.cs` | `PlayerClass`, `CharacterBrood`, `JobBitCode`, `CharacterMotionName`, `EquipmentSlot` |
| `Characters/CharacterStats.cs` | `CharacterAttributes` (five attributes), `CharacterFunctionProfile` (formula selectors) |
| `Characters/CharacterClass*.cs`, `Classes/` | Class base type, `CharacterClassDef`, `CharacterClassRegistry`, one file per class |
| [`Characters/PlayerCharacter.cs`](../Shared/PristonTale.Simulation/Characters/PlayerCharacter.cs) | Character aggregate: progress, pools, gear, effects, recompute |
| `Characters/DerivedStats.cs` | Base derived-stat formulas from attributes, level and equipped item stats |
| `Characters/Regeneration.cs` | Per-second life/mana/stamina recovery and the `RegenerationState` accumulator |
| `Characters/Locomotion.cs`, `AnimationRate.cs` | Move-speed stat to world units per second; walk/run/attack animation rates |
| `Characters/DestinationMovement.cs` | Straight-line step toward a destination (client player and NPC walking) |
| `Characters/OwnerState.cs` | `OwnerState.Capture`: the owner-only character snapshot sent to the client |
| `Characters/CharacterHead*.cs`, `CharacterVisuals.cs` | Head/face/hair model path resolution per class and tier; `CharacterVisualSet` |
| `Characters/CharacterSlots.cs` | Characters per account (6) |
| `Items/Item.cs` | `ItemKind`, `ItemStatBlock`, `ItemDefinition`, `ItemInstance` |
| `Items/Inventory.cs` | Bitmask grid with anchors, stacking, move/split/merge/exchange |
| `Items/InventoryCommand.cs` | `InventoryCommand` and `InventoryCommands.Apply` |
| `Items/LegacyItemCode.cs` | Catalog key (`WA101`) to 32-bit wire item code and back |
| `Items/ItemEquipmentMetadata.cs` | Generated table: catalog key to kind, equipment slot, two-handed |
| `Items/ItemGridSizes.cs` | Generated table: catalog key to inventory cell size (default 1x1) |
| `Items/ItemClassRestrictions.cs` | Class/gender restriction masks for armor, robes and orbs by catalog key |
| `Items/ItemCatalog.cs`, `PotionDefinition.cs` | Definition lookup by code; potion restore ranges |
| `Items/Usable/` | Usable item rules, premium timers and their effects, pets, scrolls, force orbs ([special-items.md](special-items.md)) |
| [`Combat/CombatResolver.cs`](../Shared/PristonTale.Simulation/Combat/CombatResolver.cs) | Hit, block, damage, critical, element and absorption resolution |
| `Combat/CombatProfiles.cs`, `CombatProfileFactory.cs` | `AttackProfile`/`DefenderProfile` and how they are built from characters and monsters |
| `Combat/WeaponDamage.cs` | Weapon damage range of a player |
| `Combat/CombatRng.cs` | Seeded RNG with the original's inclusive range semantics |
| `Combat/Elements.cs` | `Element` and `ResistanceBlock` |
| `Combat/PlayerAttackReach.cs`, `MeleeReach.cs` | Player attack range (melee 50, ranged from the weapon) |
| `Combat/MonsterAttackReach.cs`, `MonsterOffensiveSkill.cs` | Monster basic-attack reach and offensive-skill areas |
| `Combat/CombatAntiCheat.cs` | Attack rate/power/monotonic checks; currently not called by any project |
| [`Modifiers/ModifierTypes.cs`](../Shared/PristonTale.Simulation/Modifiers/ModifierTypes.cs) | `StatKind`, `StatModifier`, `EffectSourceKind`, `ControlFlags`, `StackRule`, `PeriodicEffect` |
| `Modifiers/EffectContainer.cs` | `EffectSpec`, `ActiveEffect`, `EffectContainer`, `ModifierTotals` |
| `Modifiers/ModifierRules.cs` | How totals change derived stats, damage, resistances, dealt/taken scaling |
| `Progression/ExperienceTable.cs` | 120-level cumulative experience thresholds |
| `Progression/SkillLearning.cs` | Skill-master training: window, prerequisites, prices, point kinds |
| `Progression/ProgressionCommand.cs` | `ProgressionCommand` (allocate attribute, learn skill) |

## Units and conventions

- Integer stats throughout; formulas truncate toward zero like the original integer code. Several formulas are
  computed in `float` and cast (`DerivedStats.Compute`); keep the cast points when editing.
- World distances are in original world units (the same units as `PristonTale.Geometry` and field data).
- Fixed point: `Locomotion` and `AnimationRate` use 8-bit fixed point (256 = 1.0) and the original 70 Hz tick.
  `Locomotion.BaseTranslation(moveSpeed) = 250 + 10 * (clamp(moveSpeed, 1, 9) - 1)` per tick; walk in the field is
  `* 180 >> 8`, run is `* 460 >> 8`, walk in a village uses the base; units/s = perTick / 256 * 70.
- `AnimationRate.AttackSpeedMainFrame(stat) = 256 + 32 * clamp(stat - 6, 0, 6)`; `AttackRate` adds the
  `AttackMotionSpeed` percent, then frame step `(80 * speed) >> 8`, normalised by 80. `MoveRate` caps the per-tick
  translation at 350 and uses `(80 * speed) / 300 / 80`.
- Times in the modifier engine and regeneration are milliseconds (`float dtMs`). Permanent effects use
  `float.PositiveInfinity` as the duration.
- Percent stats are whole percent; regeneration stats (`LifeRegen`, `ManaRegen`, `StaminaRegen`) and
  `LifeCostPerAttack` are tenths of a point.
- `Element.Lighting` is the lightning element; the same spelling is used by the world-data records.
- Item codes: 32-bit `category | ordinal << 8`, low byte zero (see Items).

## Determinism and RNG

- `CombatRng` wraps `System.Random(seed)` and implements `ISimRng` (`World/Ai/ISimRng.cs`). `Range(min, max)` is
  inclusive on both ends (span `max + 1 - min`) and returns `min` when `max <= min`. `RollPercent` is 0..99,
  `RollPerMyriad` 0..9999, `NextExclusive` excludes the upper bound.
- The server creates one `CombatRng` per field instance seeded with the field id (`WorldServer`), and a fixed seed for
  the boss schedule. Every roll of that field (hits, blocks, crits, drops, AI) shares one stream, so outcomes depend
  on call order. Tests seed their own `CombatRng`.
- Roll order inside `CombatResolver.Resolve` is part of the contract: hit, block, damage, critical. A blocked or missed
  hit consumes no damage roll. Adding or reordering a roll shifts every later outcome in the field.
- Nothing in this project reads the clock, the disk or the network; callers pass `dtMs` and RNGs.

## Classes

`CharacterClassRegistry` builds the ten classes; each `Classes/<Name>Class.cs` calls `CharacterClass.BuildDefinition`.
The class also loads its skill window from `ClassSkillCatalog.For(class)` (see [skills.md](skills.md)).

| Class | Id | `JobBitCode` | Brood | Str/Spi/Tal/Agi/Hlt | Sight | Life/Mana/Sta/Melee/Ranged/Magic fn |
| --- | --- | --- | --- | --- | --- | --- |
| Fighter | 1 | 0x2 | Tempskron | 28/6/21/17/27 | 70 | 1/3/1/1/2/0 |
| Mechanician | 2 | 0x1 | Tempskron | 24/8/25/18/24 | 65 | 2/2/2/2/2/0 |
| Archer | 3 | 0x8 | Tempskron | 17/11/21/27/23 | 80 | 3/3/2/3/1/0 |
| Pikeman | 4 | 0x4 | Tempskron | 26/9/20/19/25 | 75 | 1/3/1/1/2/0 |
| Atalanta | 5 | 0x20000 | Morion | 23/15/19/19/23 | 200 | 2/2/2/2/1/0 |
| Knight | 6 | 0x10000 | Morion | 26/13/17/19/24 | 190 | 2/2/2/1/2/0 |
| Magician | 7 | 0x80000 | Morion | 16/29/19/14/21 | 190 | 5/1/3/3/2/1 |
| Priestess | 8 | 0x40000 | Morion | 15/28/21/15/20 | 200 | 4/1/3/3/2/2 |
| Assassin | 9 | 0x10 | Tempskron | 25/10/22/20/22 | 80 | 3/3/2/3/1/0 |
| Shaman | 10 | 0x100000 | Morion | 15/27/20/15/22 | 190 | 5/1/3/3/2/1 |

- Two job masks exist: `JobBitCode` (non-sequential bits) is used for item class restrictions and item specialization;
  `CharacterClassDef.MotionJobMask = 1 << (JobCode - 1)` is used for animation tables. Do not mix them.
- `GrowthPerLevel` is empty for every class: attributes grow only through allocated status points (5 per level).
- `Tier` (0..3) is the class rank. It opens skill-window rows, selects head models (`CharacterHead`) and gates
  experience above level 100. Nothing in this project changes it; it is restored from persistence.
- Head models: `CharacterHead.Resolve` builds `char/tmABCD/<prefix><variant 01..13><suffix>.inx` from the per-class,
  per-tier pattern in `CharacterHeadModels`. Assassin and Shaman tiers 1-3 have only the three creation faces;
  `Apply` falls back to the face index instead of inventing a file name.

## Character aggregate

`PlayerCharacter` owns everything mutable about one character. It is not thread-safe: the authoritative field tick (or
a transaction candidate) is the only writer.

| Member group | Contract |
| --- | --- |
| `Level`, `Experience`, `Money`, point counters, `Tier` | Loaded by `RestoreProgress` (non-negative, tier 0..3) |
| `Life`, `Mana`, `Stamina` | Plain settable pools; clamped to maxima by every recompute and by `Equip` |
| `Attributes` | Base plus allocated points; equipment never changes them |
| `Derived` | Cached `DerivedStats`; reading it recomputes when `Effects.Revision` changed |
| `Equipment`, `Inventory` | 10 equipment slots and the 12x6 carried grid |
| `Effects` | `EffectContainer` with every timed and permanent modifier |
| `Premium`, `ItemUses` | Running premium timers (persisted in `premium`) and the recent item uses; a change recomputes |
| `Skills` | `LearnedSkills` (ranks and use counts); a change triggers a recompute |
| `InventoryRevision`, `ProgressionRevision` | Optimistic-concurrency counters for client commands |

### Derived-stat pipeline

`PlayerCharacter.Recompute` is the single path. It runs from the constructor, `LevelUp`, `Equip`,
`TryAllocateAttribute`, `RestoreDurability`, `CommitInventoryOperation`, `Skills.Changed` and lazily from `Derived`.

1. `PassiveSkillEffects.Sync` puts each learned passive with a modifier block into `Effects` as a permanent
   `PlayerSkill` effect keyed by skill code (only when its weapon kind matches).
2. `Effects.SetHeld` records the item kinds in the weapon and off-hand slots (bit per `ItemKind`, broken items
   excluded); effects with a `HeldMask` count only while a matching kind is held.
3. `EquippedStats` sums `ItemInstance.TotalStats` of all equipped items (a broken item contributes zero).
4. `ArmorDefence` percent: if non-zero and the worn armor's `JobSpecMask` includes the class bit, the armor's own
   defence grows by that percent before it feeds defence and absorption.
5. `DerivedStats.Compute(attributes, level, functionProfile, equipped)` (formulas below).
6. `PassiveSkills.Apply` (table-driven passives, see [skills.md](skills.md)), then `ModifierRules.Apply` with
   `Effects.Totals`; `MaxWeight` gains the premium weight bonus (Might of Awell, Gravity Scrolls). `PremiumEffects.Sync`
   runs with `PassiveSkillEffects.Sync` and keeps Mana Reduce, Terry and Mute as permanent system modifiers.
7. Pools are clamped to the new maxima.

Only `AttackRating`, `Defence` and `AttackSpeed` of the equipped `ItemStatBlock` feed `DerivedStats.Compute`; weapon
damage is computed separately by `WeaponDamage`, and the attribute fields of `ItemStatBlock` are not applied.

### Base formulas (`DerivedStats.Compute`)

Level `L`, attributes `Str Spi Tal Agi Hlt`; float arithmetic, truncated at the end unless noted.

| Stat | Formula |
| --- | --- |
| AttackRating | `items.AR + Agi*3.1 + L*1.9 + Tal*1.5` |
| Defence | `Agi/2 + Tal/4 + L*1.4 + items.Defence` |
| Absorption | `trunc(Defence/100 + L/10 + (Str+Tal)/40) + 1` (uses the truncated defence) |
| MaxWeight | `Str*2 + Hlt*1.5 + L*3 + 60` |
| MoveSpeed | `trunc((Tal + Hlt + L + 60) / 150) + 1` |
| MaxLife fn 1 / 2 | `L*2.1 + Hlt*2.4 + Str*0.8 - 10` / `L*2.1 + Hlt*2.2 + Str*0.6 - 5` |
| MaxLife fn 3 / 4 / 5 | `L*1.8 + Hlt*2.1 + Str*0.3` / `L*1.5 + Hlt*2.1` / `L*1.5 + Hlt*1.9` |
| MaxMana fn 1 / 2 / 3 | `L*1.5+Spi*3.8` / `L*0.9+Spi*2.7` / `L*0.6+Spi*2.2` |
| MaxStamina | `Hlt*1.4 + (Str+Tal)/2 + L*2.3 + 80 + Spi` |
| Bare-hand damage (melee fn 1) | integer: min `2 + (Str+130)/130 + (Tal+Agi)/40`, max `3 + (Str+130)/130 + (Tal+Agi)/35` |
| Bare-hand damage (other) | integer: min `2 + (Str+200)/200 + (Tal+Agi)/50`, max `3 + (Str+200)/200 + (Tal+Agi)/45` |
| AttackSpeed | `items.AttackSpeed` |

Life, mana and stamina are cast through `short`. `MaxWeight` is computed, but the carried weight is not tracked or
enforced for the character; only `WarehouseStorage` enforces a weight cap (see [world.md](world.md)).

### Weapon damage (`WeaponDamage.Compute`)

With no weapon or a broken weapon it returns the derived bare-hand range (modifiers already applied). Otherwise, with
weapon stats `wMin`/`wMax` (`BaseStats + RolledBonus`):

| Weapon | Primary | Divisor | Secondary |
| --- | --- | --- | --- |
| Staff or Phantom, magic fn > 0 | Spi | 150 (fn 1) or 170 | `Tal / 30` |
| Bow or ThrowingWeapon | Agi | 130 (ranged fn 1) or 190 | `(Tal+Str) / 40` (fn 1) or `/ 50` |
| Anything else | Str | 130 / 150 / 190 (melee fn 1 / 2 / other) | `(Tal+Agi) / 40 / 45 / 50` |

- `min = 2 + wMin*(primary+divisor)/divisor + secondary + job + (wMin+wMax)/16`
- `max = 4 + wMax*(primary+divisor)/divisor + secondary + job`
- `job = Level / 6` when the weapon is the class's favoured kind (Fighter axe, Archer bow, Pikeman pole, Knight sword,
  Atalanta javelin, Assassin dagger, Magician/Priestess staff, Shaman phantom; Mechanician: weapon specialized for
  Mechanician).
- Adds the passive weapon mastery bonuses (`PassiveSkills.WeaponBonus`, `PassiveSkillEffects.WeaponBonus`), then
  `ModifierRules.ApplyDamage` (DamageMin/DamageMax totals; max is never below min).

## Modifier engine

Every temporary or permanent stat change is an `EffectSpec` in an entity's `EffectContainer` (players and monsters
each own one). Sources: player skills (buffs, debuffs, passives), monster skills, summons/companions, and the
`Item`, `Consumable`, `Aura` and `System` source kinds reserved in `EffectSourceKind`. Equipped item stats do not go
through the engine; they are summed by `EquippedStats`.

- `EffectSpec(code, source, harmful, modifiers, periodic, control, stacking, maxStacks, heldMask)` is immutable.
  `Code` identifies the effect for stacking and on the wire: skills use their skill code.
- `StatModifier(Stat, Op, Value)`: `Flat` adds; `Percent` scales. `ModifierTotals.Apply(stat, base)` returns
  `(base + flat) * (100 + percent) / 100`, truncated, clamped to `int`.
- `ModifierTotals` sums values times `Stacks` over all effects whose `HeldMask` is 0 or matches `Held`. It is rebuilt
  only when `EffectContainer.Revision` changes; any `Apply`, `Remove`, expiry or `SetHeld` change bumps the revision,
  which in turn makes `PlayerCharacter.Derived` recompute.
- `StackRule`: `Refresh` (restart timer, take new values), `Stack` (add a stack up to `MaxStacks`), `KeepStrongest`
  (replace only if `Magnitude`, the sum of absolute values, is at least as high; `Apply` returns `null` otherwise),
  `PerSource` (one copy per source entity).
- `PeriodicEffect(resource, amount, intervalMs, percentOfMax, nonLethal)`: `Step` emits `EffectChangeKind.Ticked`
  changes; the Application applies them (`FieldSkills.Buffs.cs`).
- `ControlFlags` (Stun, Root, Silence, Slow, Hidden, Charmed) are the OR of all active specs; read with `Has`.
- Lifecycle (driven by the Application tick, `FieldSkills.StepEffects`): `Step(dtMs)` advances timers and removes
  expired effects; on death `ClearTimed` drops timed effects; entering a village removes timed `PlayerSkill` effects
  (`RemoveTimedFrom`); permanent effects stay until removed by code or source (`Remove`, `RemoveFrom`).
- All mutating calls append `EffectChange` records to a caller-owned scratch list; the Application turns them into
  wire events.

`ModifierRules` maps totals onto stats:

| Function | Effect |
| --- | --- |
| `Apply(DerivedStats, totals)` | Every `DerivedStats` field except MaxWeight; MaxLife min 1, AttackSpeed unclamped, rest min 0 |
| `ApplyDamage` | Weapon damage range with DamageMin/DamageMax |
| `Resistances` | Adds `ResistAll` plus the per-element flat values |
| `ScaleDamage` | `damage * (100 + DamageDealt) / 100 * (100 + DamageTaken) / 100`, flat and percent summed, min 1 |
| `AgainstUndead` | Removes `AbsorptionVsUndead` percent (capped at 100) of damage from undead attackers, min 1 |

Stats with no derived-stat slot (block, critical, evasion, leech, ranges, next-hit, companion damage, and so on) are
read directly from `Effects.Totals` by the combat profiles, `PlayerAttackReach`, `Regeneration` and the skill code.

## Combat

Profiles decouple the formulas from entities. `CombatProfileFactory` builds them:

- `ForPlayerBasicAttack(character, normalAttack)`: weapon damage range, derived attack rating, element Bionic,
  critical chance from `CriticalChance` totals plus passives, `CritBonusPercent` from `CriticalDamage`. With
  `normalAttack`, `NormalAttackWeaponDamage`/`NormalAttackWeaponMax` add a share of the weapon's own damage.
  `SkillFormulas.NormalAttack` wraps it and adds `HitDamage` as the flat add.
- `ForPlayerDefender`: derived defence and absorption, block from totals plus passives, resistances from passives plus
  totals. Item resistances (`ItemDefinition.Resistances`) are not applied.
- `ForMonsterBasicAttack` / `DefenderProfile.FromMonster`: from `MonsterCombatStats` (see [world.md](world.md));
  `FromMonster(MonsterInstance)` adds the monster's `CriticalChanceTaken`.

### Player attacking (`CombatResolver.Resolve`)

1. Hit chance: `acR = trunc((AR - defence) * 1.4)`; below -190 gives 50, above 2100 gives 95, else the step table
   value of the highest threshold `<= acR`. Then `chance = trunc(acc - (defLevel - atkLevel) / 100 * 28)`, clamped
   30..95. Hit when `RollPercent() < chance` (skipped with `alwaysHit`).
2. Block: `trunc(block + (defLevel - atkLevel) / 100 * 25)`, capped at 50 with a shield; blocked hits deal 0.
3. Damage: `Range(min, max)`, then `+ dmg * SkillMultiplierPercent / 100 + SkillFlatAdd`, then `+ ExtraPercent`.
4. Critical: `chance = CritChance + (atkLevel - defLevel) * 25 / 100 (+ CritChanceTaken if CritChance > 0)`, capped
   at 70; damage `* (170 + CritBonusPercent) / 100`.
5. Element: `dmg - dmg * resist / 100`, resist clamped to -100..100; with `ResistedPart >= 0` only that part is
   reduced.
6. Absorption as a percent (`ApplyAbsorbMonster`): `dmg - dmg * absorption / 100`, min 1.

The Application then applies `ModifierRules.ScaleDamage` and skill hooks (`FieldCombat.ResolvePlayerSwingHit`).

### Monster attacking a player

- `ResolveMonsterAttack`: block roll first, then hit with `MonsterHitChance` (difference `(AR - defence) * 2`, the
  first table entry whose threshold is `>=` it, level term `* 50`, clamp 30..95, miss only when roll `>` chance),
  damage roll, element, then absorption subtracted as flat points (min 1).
- `ResolveMonsterSkill`: no hit or block roll; `Range(min, max) - absorption`, min 1, Bionic.
- Afterwards the Application applies `ScaleDamage`, `AgainstUndead`, evasion and defensive skills.
- Reach: `MonsterAttackReach.CanBegin` (planar distance below `MeleeRange`, vertical gap <= 64) and `CanLandBasic`
  (squared planar distance below `48 * 256`, or `460^2` for sound families with a ranged basic attack).
  `MonsterOffensiveSkill` supports five sound families and computes area center, query radius and containment.

Absorption is a percent against monsters but flat points against players; this asymmetry is intentional.

`PlayerAttackReach.For`: 50 for melee or no weapon; for bows, javelins, staves held by Magician/Priestess and phantoms
held by Shaman, the weapon's `ShootingRange` plus the `AttackRange` totals.

## Items

- `ItemDefinition` is immutable catalog data built by `WorldDataFactory.ToItemDefinition` in
  `Persistence/PristonTale.Persistence.Abstractions` from imported item templates (see
  [world-import.md](world-import.md) and [persistence.md](persistence.md)). That factory applies `ItemGridSizes.Of`,
  `ItemClassRestrictions.AllowedJobs` and `ItemEquipmentMetadata.Apply`, and sets `BaseStats` to the midpoint of each
  template range.
- `Code` is the 32-bit wire item code; `CatalogKey` is the 5-character key (`WA101`). `LegacyItemCode.TryParse` maps a
  key to `category | ordinal << 8` (category from the 3-letter prefix, ordinal from the two digits);
  `TryFormat` is the inverse and rejects a non-zero low byte or ordinals above 99. Unknown prefixes fail.
- `ItemKind` is persisted as an integer: append new values at the end only.
- Equippable means `EquipSlot` is set, which only `ItemEquipmentMetadata` does. It covers axes, claws, hammers,
  staves, poles, bows, swords, javelins (`WA1 WC1 WH1 WM1 WP1 WS1 WS2 WT1`), armor and robes (`DA1 DA2`) and shields
  (`DS1`). Other families (gloves, boots, accessories, orbs, sheltoms, daggers, phantoms) have no entry and cannot be
  equipped. The table also overrides the imported kind.
- `AllowedJobs` (0 = any class): `DA1` armor for non-casters, `DA2` robes and `OM1` for casters, with fixed key
  numbers restricted to male-only or female-only classes. `SpecializationJobs` seeds `ItemInstance.JobSpecMask`.
- `ItemInstance`: `InstanceId` (Guid, preserved across moves and copies with `preserveId`), `Count`, `UpgradeLevel`,
  durability, `JobSpecMask`, `RolledBonus`. `TotalStats = BaseStats + RolledBonus`, or zero when broken
  (`DurabilityMax > 0 && DurabilityCurrent <= 0`).
- Rolls: `RolledStatsMin`/`RolledStatsMax` hold the template ranges and `RolledBonus` is restored from saves; no code
  in this project rolls a bonus. `JobRandomSpecMask` and `SpecialBonuses` are carried but not applied.
- Prices: `Price`, `Weight` and durability feed `NpcShopRules` and `MoneyLimits.CarryLimit` in `World/Npcs`
  (see [world.md](world.md)).
- `PotionDefinition.Restore` adds `NextExclusive(min, max)` (upper bound excluded unless min == max) to each pool,
  clamped to the maximum; does nothing to a dead character.

### Inventory grid

- `Inventory()` is the 12x6 carried grid (72 cells); `Inventory(width, height)` builds any grid up to 32x32 (the
  warehouse uses 9x9). Cell index = `y * Width + x`; an item is stored only at its top-left anchor and its footprint
  (`GridWidth x GridHeight`) is tracked in one `uint` bit row per grid row.
- `TryAdd` first tops up compatible stacks, then places new stacks first-fit by anchor index; it plans everything
  before mutating and fails without change. Compatible stacks need a stackable, equal definition and identical
  upgrade, durability, spec mask and rolled bonus.
- `TryPlace`, `TryMove`, `TrySplit` (new instance id), `TryMerge`, `TryExchange` (put a replacement at the same anchor
  or first fit), `TakeAt`, `ConsumeAt`. Every change bumps `Version` and raises `Changed`.

### Equipment rules (`PlayerCharacter.CanEquip`)

Count 1, `UpgradeLevel == 0`, matching slot (rings fit either ring slot), level and five attribute requirements met,
`AllowedJobs` contains the class bit, no two-handed weapon together with an off-hand item, and the same instance not
in another slot. `EquipmentSlot` has no helmet and keeps `Sheltom` separate (the persisted equipment order).
`TryEquipFromInventory` swaps with the equipped item; `TryUnequip` needs inventory space.

## Commands, revisions and transactions

- `InventoryCommand` and `ProgressionCommand` carry an `OperationId` (non-empty) and `ExpectedRevision`; they apply
  only when it equals the current `InventoryRevision`/`ProgressionRevision`. The last 64 applied operations of each
  kind (plus NPC service operations) are kept for idempotent replay and persisted with the character.
- `InventoryRevision` counts changes, not commands: one equip can bump it more than once. Clients must adopt the
  revision returned in the outcome.
- `InventoryAction.ConsumePotion` only removes one potion; the restore is applied later by the Application when the
  eat motion reaches its event (`WorldPlayer`). `Pickup` is handled by `GroundPickupTransactions`, not here.
- The Application validates on a copy: `CloneForInventoryOperation` (inventory, equipment, progress, skills, pools,
  histories), applies the command, persists the snapshot, then `CommitInventoryOperation` adopts the candidate's
  inventory and equipment, or replays the progression command on the original (see [application.md](application.md)).

## Progression

- `ExperienceTable` holds 120 cumulative thresholds; `ForLevel(1) = 0`, `LevelFor` returns the highest level whose
  threshold is reached.
- `AwardExperience(amount)`: ignores non-positive amounts and stops at `ForLevel(119)`. Below tier 3, an award that
  would pass `ForLevel(100)` is rejected entirely (not clamped). Levels are applied one `LevelUp` at a time.
- `LevelUp`: +5 status points; +1 skill point at even levels from 10; +1 final-tier (elite) point at even levels from
  60; pools refilled; `OnLevelUp` and `LeveledUp` raised.
- `TryAllocateAttribute` spends status points one attribute at a time (overflow-checked).
- `SkillLearning` (skill master): the class window has 16 skills in order; tier `t` opens the first `4 * (t + 1)`.
  A skill trains when it has ten ranks of costs, rank < 10, its point kind is available (indices 12-15 spend elite
  points, the rest skill points), `level >= RequiredLevel + 2 * rank`, and the previous window skill has a rank.
  Price = `BasePrices[index] + RankPrices[index] * currentRank`. `ApplyProgression` requires a non-zero
  `NpcEntityId`; proximity to the master is checked by the Application.

## Regeneration

`Regeneration` returns per-second rates; `RegenerationState.Step(character, posture, dtMs)` accumulates fractions
and applies whole points, clamped to the pool. A full pool discards its fraction; nothing regenerates at 0 life. The
server steps it per player in `FieldSkills.StepEffects` and resets it on death. All rates include the factor
`17.5 / 17` (the original applies value / 17 every four 70 Hz ticks).

| Pool | Per second |
| --- | --- |
| Life | `((L + Str/2 + Hlt) / 180 + LifeRegen/10) / 1.5` (`Str/2` integer) |
| Mana | `(L + Spi*1.2 + Hlt/2) / 115 + ManaRegen/10` (`Hlt/2` integer) |
| Stamina, standing | `base + L/7 + StaminaRegen/10`; base 3.8 / 3.3 / 2.9 for stamina fn 1 / 2 / other; `L/7` integer |
| Stamina, walking | 0.6 x standing |
| Stamina, running | `-(1000 / max(1, MaxWeight + Str/2 + 500) + 0.4)` (integer division: -1.4 or -0.4) |
| Stamina, acting | 0 (attacking, casting, flinching, eating) |

## Consumers

- `Skills/` builds on `PlayerCharacter`, `EffectContainer`, `StatKind`, `CombatResolver` and `CombatProfileFactory`
  (`SkillFormulas`, `PassiveSkills`, `PassiveSkillEffects`, `LearnedSkills`); see [skills.md](skills.md).
- `World/` uses `CombatRng`/`ISimRng`, `MonsterAttackReach`, `MonsterOffensiveSkill`, items and `Inventory`
  (loot, shops, warehouse); see [world.md](world.md).
- `Shared/PristonTale.Application` drives everything on the authoritative tick (`FieldCombat`, `FieldSkills.*`,
  `WorldPlayer`, transactions); see [application.md](application.md).
- The client uses `Locomotion`, `AnimationRate`, `DestinationMovement`, `CharacterHead` and `OwnerState`
  (`OwnerState.Capture` reuses unchanged lists by revision); see [client.md](client.md).

## How to

- Add a modifiable stat: append to `StatKind` before `Count` (arrays are sized by `Count`). Skill JSON names stats
  by enum member (`JsonStringEnumConverter` in `ClassSkillCatalog`), so never rename members. Read it via
  `Effects.Totals` where it applies, or add it to `ModifierRules.Apply` if it maps onto `DerivedStats`; add a case in
  `tests/PristonTale.Tests` (`PassiveSkillTests`, `SkillSystemTests` or `EquipmentTests`).
- Change a derived-stat formula: edit `DerivedStats.Compute` (or `WeaponDamage`/`Regeneration`), keep the truncation
  points, then run the regression runner (`EquipmentTests`, `ProgressionAndOwnershipTests`, `PotionTests` cover them).
- Change hit/crit/block math: edit `CombatResolver`; keep the roll order or expect seeded tests in
  `MonsterCombatTests` and the skill tests to change outcomes.
- Add an item family: add its prefix to both maps in `LegacyItemCode`, grid sizes to `ItemGridSizes`, and equipment
  metadata to `ItemEquipmentMetadata` if equippable; restrictions in `ItemClassRestrictions`. Check
  `WorldDataFactory` and the importer ([world-import.md](world-import.md)).
- Add an `ItemKind`: append it, update `ItemDefinition.IsWeapon`, `WeaponDamage` favoured kinds,
  `PlayerAttackReach` and the client item presentation.
- Add a class: new `Classes/<Name>Class.cs`, register it in `CharacterClassRegistry`, add `PlayerClass`/`JobBitCode`,
  head patterns in `CharacterHeadModels`, the favoured weapon in `WeaponDamage`, restrictions in
  `ItemClassRestrictions`, and the skill data ([skills.md](skills.md)).
- Change level rewards or training: `PlayerCharacter.LevelUp`, `ExperienceTable`, `SkillLearning`; tests in
  `ProgressionDataTests`, `ProgressionTransactionTests`.
- Verify with the Release build and `dotnet run --project tests/PristonTale.Tests -c Release --no-build`
  (`--filter <text>` narrows cases); see [testing.md](testing.md).

## Pitfalls

- Reading `PlayerCharacter.Derived` can mutate `Life`/`Mana`/`Stamina` (recompute clamps them). Read it on the
  owning thread only.
- `CloneForInventoryOperation` copies no timed effects; the clone's derived stats lack buffs. Do not read buffed
  values from a transaction candidate, and commit copies only inventory, equipment and histories.
- `Equip` throws on an invalid item; use `CanEquip`/`TryEquipFromInventory` for client input.
- Items with `UpgradeLevel > 0` cannot be equipped.
- Player absorption is flat, monster absorption is percent; the monster hit check uses `>` while the player check
  uses `<`. Both match the original receivers.
- `TrySplit` gives the split stack a new `InstanceId`; `TryAdd` keeps the id only for the first new stack.
- `Compatible` stacking uses record equality of `ItemDefinition`; two catalogs with equal data compare equal.
- `ItemEquipmentMetadata` and `ItemGridSizes` are generated data tables; edit entries, not structure.
- `CombatAntiCheat` and `PlayerCompositionFactory` are not used by any project; do not assume they are enforced.
- Item resistances, rolled-stat ranges, random job masks and special bonuses are loaded but have no effect.

## Related

- [architecture.md](architecture.md) - project layering and threading
- [skills.md](skills.md) - skill data, passives, server skill resolution
- [world.md](world.md) - monsters, AI, loot, spawning, NPC shops and warehouse
- [application.md](application.md) - authoritative tick, transactions, `FieldCombat`
- [persistence.md](persistence.md), [world-import.md](world-import.md) - item and character data sources
- [protocol.md](protocol.md) - how owner state, items and effects travel on the wire
- [glossary.md](glossary.md)
