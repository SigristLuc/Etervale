# Special items

Items a player uses from the inventory with a right click: premium timers (EXP potions, Third Eyes, packages,
vampiric and mana items, cost reductions, Might of Awell, pets), stones that reset stats or skills, hair tints, the
big-head potion, scrolls, force orbs, skill and aging masters, the Gravity Scroll, the Teleport Core and the ether
cores, firecrackers and the mobile shop. The server decides every outcome; the client sends the item and draws the
result.

## Read this when

- You add a usable item, change what one does, or change a duration or percentage.
- You touch experience, extra drop rolls, per-attack life or mana, skill costs or carrying capacity from items.
- You work on the premium timer column, the Teleport Core window, item tooltips or the item effects in the world.
- You change the `premium` character column or the item-use messages.

## Code map

| Path | Responsibility |
| --- | --- |
| `Shared/PristonTale.Simulation/Items/Usable/UsableItemCatalog.cs` | Catalog key to `UsableItemRule` (action, argument, premium grants, conflict, level, village ban) |
| `.../Usable/PremiumTimers.cs` | `PremiumEffectKind`, `PremiumTimer`, the character's running timers, Gravity Scroll count and big-head look |
| `.../Usable/PremiumRules.cs` | Experience, extra drop chance, vampiric and mana-recharging amounts, cost reductions, weight bonus |
| `.../Usable/PremiumEffects.cs` | Mana Reduce, Terry and Mute as permanent system modifiers |
| `.../Usable/PremiumPets.cs` | Pet damage, elements, helper chance, strike timings, Nepsys slow |
| `.../Usable/UsableItemEffects.cs` | The character part of a use (timers, resets, mastery, aging, Gravity Scroll, big head) |
| `.../Usable/ItemScrolls.cs`, `ForceOrbs.cs`, `TeleportCoreDestinations.cs` | Scroll effects, force orb tables, the 33 Teleport Core fields |
| `.../Usable/ItemUseCommand.cs`, `ItemUseMessages.cs`, `ItemEffectKind.cs` | Command, statuses, outcome and history; the effect kinds sent to clients |
| `Shared/PristonTale.Application/Services/ItemUseAppService.cs` | Prepare, store, commit under the session's `SaveGate` |
| `Shared/PristonTale.Application/World/ItemUseTransactions.cs` | Tick-side checks and the candidate of a use |
| `Shared/PristonTale.Application/World/WorldServer.Items.cs` | `PrepareItemUseAsync`, `CommitItemUseAsync` and the world part of a use |
| `Shared/PristonTale.Application/World/FieldPremium.cs`, `FieldInstance.Premium.cs` | Timer expiry, per-attack life and mana, pets |
| `Shared/PristonTale.Application/World/FieldItemEvents.cs`, `FieldSkills.Items.cs` | Item effects published per tick; item status effects |
| `Shared/PristonTale.Application/World/MobileShopTransactions.cs` | The NPC-less shop a mobile shop item opens |
| `Persistence/PristonTale.Persistence.Abstractions/Characters/PremiumStateCodec.cs` | `SavedPremium` in the `premium` column |
| `Shared/PristonTale.Protocol/Packets/ItemUsePackets.cs` | `ItemUseCommand`, `ItemUseResult`, `PremiumTimers`, `ItemEffects` |
| `Client/PristonTale.Gameplay/UI/Inventory/HudItemUseController.cs`, `HudTeleportCore.cs`, `HudUsableTooltip.cs` | Right-click use, Teleport Core window, confirmations, tooltips |
| `Client/PristonTale.Gameplay/UI/Widgets/HudPremiumTimers.cs` | The premium timer column |
| `Client/PristonTale.Gameplay/Effects/LegacyItemEffects.cs` | Firecrackers and item sounds on the legacy particle engine |
| `Client/PristonTale.Gameplay/Effects/ItemVfx.cs`, `ForceOrbPresentation.cs` | Skill-learn light, force orb weapon glow and hit spark |
| `Client/PristonTale.Gameplay/World/Remote/PremiumPetPresentation.cs` | Pet actors, their animation and strikes |
| `Client/PristonTale.Gameplay/World/GameScene.Items.cs` | Item use, timers and item effects in the scene |

## Use flow

1. Right click on a carried item whose catalog key has a rule (`HudInventoryController.UseRequested`). The Teleport
   Core opens its field list first and asks to confirm the field; the mobile shop asks to confirm the use.
2. `ItemUseCommand {OperationId, ExpectedRevision, ItemId, Argument, Text}` goes to the server
   (`ItemUseAppService.UseAsync`). `Argument` is the Teleport Core destination index.
3. Tick (`ItemUseTransactions.Prepare`): replay check (the same id answers its recorded outcome), inventory revision,
   carried item with a rule, then the rule's checks (dead, village ban, level, conflicts, field, target). A refusal is
   final and consumes nothing. Otherwise a candidate consumes one item, applies the character part and records the use.
4. The candidate's save snapshot is stored (hair tints also store `hair_index`), then `CommitItemUseAsync` applies the
   inventory, re-applies the character part to the live character with the same clock, and the world part: scroll and
   force orb effects, hair, the mobile shop flag, item effects and the field change of travel items.
5. The result (`ItemUseResult`) carries `ItemUseStatus`; a field change also sends a forced `WARPFIELD`.

| Status | Meaning |
| --- | --- |
| `AlreadyRunning` | The same timer, scroll or force orb runs, or a package's timers run |
| `SameAttribute` | Vampiric Cuspid and its Super version exclude each other; one helper pet at a time |
| `NotHere` | Village for scrolls and force orbs, a room field, Bellatra or the quest arena for travel, towns and the castle for the mobile shop, Fatal Edge against a running Avert Scroll and the reverse |
| `LevelTooLow` | Helper pets need level 10; Teleport Core destinations and force orbs have their own levels |
| `NothingToChange` | Nothing to reset or train, the same hair, no aged item worn, five Gravity Scrolls used |

## Items and effects

Durations run on the real clock (Unix seconds) and keep running while the character is offline. A new timer of a
running kind is refused, so timers never stack.

| Items | Effect |
| --- | --- |
| Third Eyes BI112/114/178 (1 d, 7 d, 1 h) | 50% chance of one extra main drop roll from a monster that drops at all |
| EXP potions BI113/115 (30%), BI124/125/179 (50%), BI143/144/150/180 (100%) | Kill experience + the percent |
| Bronze/Silver/Gold/Superior and Ultra packages (BI126/127, 130/131, 135, 201-212, 223-226, 245-247) | Third Eyes 20/30/40/50% and EXP 10/20/30/40% together for 3 h, 1 d, 7 d or 30 d |
| Vampiric Cuspid BI119/120/181, Super Vampiric Cuspid BI186-188 | Life per attack: `int((STR + P/10)/20 * (1 + H/10) * H + 1)`, Super `/15` and `H/7`; H monsters hit, P average damage |
| Mana Recharging BI121/122/182 | Mana per attack: `int((SPI + P/10)/13 * (1 + H/10) * H + 1)` |
| Mana Reduce BI128/129/183/258, Stamina Reduce BI191/192/238/259 | Skill mana and stamina costs -30% |
| Phoenix BI132-134/151/177 | EXP +20%, extra drop chance +50; strikes the owner's target within 300 units once a second for `L/2+1..L*3/2+1` |
| Terry, Nepsys, Io, Mute BI152-155/165-172 | Join owner attacks (21% to level 5, 24% to 9, 100% to 120): Terry fire, Nepsys ice `L/2..L*95/100` (slow 10 s), Io `L*4/5+1..L*9/5+2`, Mute `L/2..L*39/20`; Terry -20% skill mana, Mute +10% dmg |
| Blue/Red/Green stones BI101-103, (E) stones BI319/320 | Stats back to the class start values, skill ranks refunded, or both |
| Strength..Health stones BI145-149 | One attribute back to its start value |
| Eternal Life BI105 | 30 s no damage taken, damage dealt -50% |
| Fatal Edge BI106, Avert Scroll BI107 | 1 h critical chance +15, evasion +15 |
| Force orbs FO101-112, magic FO121-132 | Added power on single-target hits (magic: on spell power), with +10/15/20% of the average attack for the strongest orbs |
| Teleport Core BI108 | One of 33 fields by level; ether cores EC101/102/104 return to Ricarten, Navisko, Pillai |
| Hair tints BI116-118, BI213-222 | Hair 0..12, refused for the current hair |
| Skill masters BI139-141 | Learned skills of class tier 1..3 fully trained |
| Aging masters BI136-138, BI162-164 | The worn aged item (weapon, shield or orb, armor, armlet, gloves, boots) gains one level |
| Gravity Scroll BI184 | Carrying capacity +50, at most five |
| Big-head potion BI109 | Big head for 600 s |
| Firecracker BI123 | Four rockets half a second apart; the roll picks each burst's shape |
| Mobile shop BI142 | A shop that buys and repairs until closed (NPC serial 0) |

Aging and mixing materials (BI110, 111, 160, 161, 185, 189, 190) and the passive resurrection scroll are not used
from the inventory. Items without a rule keep the generic tooltip.

## Client presentation

- Premium timer column: x 8, y 80 + 36 n, the original icons and grey covers measured against the 1 h, 3 h, 1 d,
  7 d and 30 d tiers, fade-in, ten-second blink, one-pixel-per-frame climb and the hover box with name, effect and
  `Time left: hh:mm:ss`. The column follows the `PremiumTimers` message, which the server sends when the timers change.
- Scrolls and force orbs are status effects (codes `0xF0000001..3`, `0xF0000200 + icon`) and show in the
  running-effects bar with their keep icons.
- Item effects (`ItemEffects` message): firecracker bursts with the original particle scripts and sounds, the
  skill-learn light of hair tints and force orbs (`ItemVfx.Learn`, rising streaks registered in code), the agony glow
  of skill masters, and pet strikes.
- A running force orb keeps a flare glow in the orb's colour on the holder's weapon bone, renewed every 29 s until the
  status ends, and each hit the holder lands bursts the weapon-hit spark on the target with a white light.
- The big head replaces the head model with the oversized one of the class (tier-0 head prefix, hair 1..3, look `b`
  or `t`); the descriptor carries it, so every client sees it.
- Pets are client actors driven by the player's descriptor flags (`CharacterInfo.TempFlags`: Phoenix bit, helper pet
  in bits 8-11, big head in bits 12-13); they follow their owner and play the strike the server announces.

## Pitfalls

- `PremiumTimers.Expire` runs every tick before the rules read the timers; reading them elsewhere sees the state of
  the last tick.
- The commit re-applies the character part with the prepared clock and roll; it must not re-apply aging (the
  inventory commit already carries the upgraded item).
- Package timers share one expiry; a single item of a kind a package runs is refused while the package runs.
- `HudNpcServiceController` accepts NPC serial 0 only for the shop: that is the mobile shop.

## Related

- [application.md](application.md) - durable operations and the tick
- [protocol.md](protocol.md) - the item-use opcodes
- [persistence.md](persistence.md) - the `premium` column
- [client.md](client.md) - HUD and scene
- [testing.md](testing.md) - `SpecialItemTests`, `--capture-item-hud`, `--capture-item-effects`
