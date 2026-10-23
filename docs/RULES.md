# Ashen – Rules

This file is the source of truth for how a match of Ashen plays. The server
implements exactly these rules; numbers marked *(setting)* can be changed by the
lobby host.

## Lobby

- 2–8 players. Logging in (account) is required to play.
- Lobbies have a 5-letter code. Public lobbies show up under **Browse**, private
  ones can only be joined by code.
- The creator is the host. If the host leaves, the next player becomes host.
  The host can kick players before the match starts.
- A match can't be joined once it is running; you can only rejoin your own seat.
- Lobby and match share a simple text chat (last 50 messages, 200 characters).
- Host settings:
  | Setting | Default |
  |---|---|
  | Public | yes |
  | Starting lives | 5 |
  | Shop timer | 60 s |
  | Starting money | 100 $ |
  | Income per round | 20 $ |
  | Win bonus | 10 $ |
  | Special items unique | per player (or per lobby) |
  | Starting HP | 100 |
  | HP upgrade multiplier | ×1.2 |
  | HP upgrade base cost | 10 $ |
  | HP upgrade cost increase | +5 $ per purchase |
- After a match, everybody returns to the same lobby so the host can start a
  rematch.

## Match flow

A match is a loop of **Shop → Combat → Results** until one player is left.

1. Every player starts with *starting money* and *starting lives*, no items,
   and the **kit** they picked in the lobby (see Kits).
2. **Shop phase** lasts the *shop timer*, or ends early when every alive player
   is ready. Disconnected players count as ready.
3. **Combat phase**: alive players are paired (see Pairing). After a
   "3, 2, 1, Fight!" countdown all fights run **live** on the server at the same
   time, and players use their kit abilities with Q/W/E/R or the buttons.
4. **Results**: once the last fight ends, 5 more seconds.
5. The next shop starts (see Money for what it pays).

### Spectating

- Eliminated players, and players whose own fight is already over, can pick any
  fight of the round and watch it live.

### Pairing

- Pairing rotates through everyone: you fight the opponents you have faced
  least often, so you meet everyone before meeting anyone twice.
- With an odd number of alive players, one player fights a **ghost**: a copy of
  a random other alive player's current build. Only the real player's result
  counts.

### Winning, losing, lives

- The loser of a fight loses 1 life. A draw costs both players a life.
- The winner gets the *win bonus*.
- A **flawless victory** (winning without taking damage) gives +5 $ (not in
  round 1).
- At 0 lives you are eliminated. Players eliminated in the same round are
  ranked by the HP % they ended their fight with, then by damage dealt; if that
  is equal too, they share the placement.
- The last player alive wins. If everyone left dies at once, the tie-break
  above decides 1st place.
- Other players only see your name, whether you are ready, and whether you are
  alive. Lives, money and builds stay hidden (builds are visible to your
  opponent during a fight). Everything is revealed on the results screen at
  the end of the match.

### Disconnects and leaving

- Disconnecting keeps your seat reserved; log in again (same account) to rejoin,
  even mid-fight. While you are gone you count as ready, and a bot uses your
  kit abilities in fights. Ghosts use the same bot.
- Clicking **Forfeit** eliminates you immediately (you take the lowest open
  placement) and frees your seat.

## Money

When a shop opens (from round 2 on) you get:

- **Income** (*income per round*, doubled during Gold Rush).
- **Interest**: +1 $ per 10 $ you held before income, up to +5 $.
- **Loss streak bonus**: after 2 losses in a row +10 $, then +5 $ more for every
  further loss in a row (3 → +15 $, 4 → +20 $, …). A win or draw resets it.
- During **Tax**, you then lose 10 % of your money.

The shop shows what you got. Fights also pay the *win bonus*, the flawless bonus
and any money items (Coin, Chip, Crown) earned.

## Round modifiers

From round 2 on, 70 % of rounds get a random lobby-wide modifier (30 % are calm).
It applies to that round's shop and fights and is shown in the shop and fight.

| Modifier | Effect |
|---|---|
| Blood Moon | Crit damage ×1.5 |
| Overclock | All cooldowns ×0.8 (weapons, item effects, kit skills) |
| Fog | +15 % dodge chance for everyone (before the cap) |
| Gold Rush | Double income |
| Clearance | All shop items 30 % cheaper |
| Lucky Day | Special item chance ×3 |
| Glass Cannon | All damage ×1.5, max HP ×0.7 |
| Rust | Weapon damage ×0.75 |
| Fragile | Damage reduction halved |
| Unlucky | No special items in the shop |
| Tax | Lose 10 % of your money when the shop opens |

Modifiers live in `json/modifiers.json` (name/description); their effects are in
the server code.

## Kits

Everyone picks a kit in the lobby (free). In any shop you can switch for 15 $.
Several players can pick the same kit. Each kit has three skills (**Q**, **W**,
**E**) on cooldowns and an **ultimate** (**R**) that charges from damage you deal
and take: dealing + taking 40 % of your max HP fills it. Charge and cooldowns
reset every fight. Stunned players can't use abilities (except Cleanse).

| Kit | Q | W | E | R (ultimate) |
|---|---|---|---|---|
| Duelist | Parry (6 s): blocks the next hit within 0.6 s, reflects 50 % | Lunge (8 s): instant extra weapon attack | Feint (10 s): enemy's next weapon attack misses | Flurry: 5 extra weapon attacks over 1.5 s |
| Berserker | Rage (10 s): +50 % damage dealt, +25 % taken, 4 s | Bloodlust (12 s): heal 30 % of damage dealt, 5 s | Shout (9 s): stun 0.5 s | Rampage: weapon cooldown ×0.5 + stun immune 6 s, breaks stuns |
| Guardian | Shield (8 s): absorb 15 % max HP, 4 s | Brace (10 s): +30 % damage reduction (past the cap), 3 s | Cleanse (12 s): remove stun + enemy debuffs, works while stunned | Bulwark: invulnerable 2.5 s, reflects 100 % of blocked damage |
| Trickster | Smoke (9 s): +40 % dodge, 3 s | Hex (11 s): enemy weapon cooldown ×1.5, 4 s | Blink (7 s): next weapon attack crits | Mirror: copy the enemy's active buffs for 6 s |
| Gambler | Coin Flip (6 s): next hit ×2 or ×0.5 | Double Down (12 s): +30 % crit chance, 4 s | Jackpot (15 s): 10 % stun 2 s, else +2 $ | All In: next 3 hits ×3 or miss (50/50 each) |
| Sniper | Aim (8 s): next hit +100 % | Mark (12 s): enemy takes +20 % damage, 5 s | Retreat (10 s): +50 % dodge, 2 s | Headshot: 2 s wind-up, then 40 % of the enemy's current HP |

- Ability dodge and damage reduction can go past the normal caps (up to 90 %
  dodge and 95 % damage reduction).
- Headshot ignores damage reduction and overtime, but invulnerability, Parry
  and Shield still work against it.
- All numbers are in `json/kits.json`.

## Shop

- 6 slots per roll. 5 free rerolls per shop phase.
- Once you buy an item from a roll, that roll is **locked**: no more rerolls
  this shop phase, but you can still buy the other items of that roll.
- Every slot rolls independently:
  - With your **special chance** (base 1 %) it rolls a special item, otherwise a
    generic item.
  - Generic: every generic item has the same chance. Generics you own at max
    level don't appear. Duplicates inside one roll are possible.
  - Special: weighted pick from the remaining special pool. Every special has
    weight 1, The Dragon has weight 0.25. Specials you already obtained (or, in
    *per lobby* mode, that anyone obtained) and specials already in this roll
    are removed from the pool. If the pool is empty the slot becomes generic.
  - Special items can only be obtained once. Discarding one does not put it
    back into the pool.
- Special chance buffs (Celestial Shirt ×1.5, Dark Clover ×2.5) multiply:
  both together give 3.75 %.

### Pack synergy

- Owning 2 / 3 / 4+ different items of the same special pack gives your weapon
  +5 % / +10 % / +15 % damage. This stacks with the Pack Master event reward.

### Buying

- **Duplicate = level up.** Buying an item you already own raises its level by 1
  (max level 5). Its `level_stats` are added per level above 1, and its effect
  values grow by +50 % of their base strength per level above 1
  (e.g. Knight ×0.9 → ×0.85 → ×0.8, Coin 2 $ → 3 $ → 4 $).
- Slots: 1 weapon, 1 shirt, 4 dynamic slots (buff, charm, passive).
- No selling. Buying a different weapon/shirt replaces the old one (asks for
  confirmation; levels are lost). With all 4 dynamic slots full you pick a
  dynamic to replace.

### Weaponless bonus

- If you fought **both** round 1 and round 2 without a weapon, shop 3 also offers
  a random **special weapon and its pack's buff** (e.g. Pepf's Guitar + Pepf's
  Pick) at a third of their price (rounded up), as two separate offers.
- Buying them doesn't lock the roll. They count as obtained specials (and can
  claim Lucky Find). The offer disappears after shop 3.
- Only specials you could still get are offered (per-lobby mode respected). If
  no special weapon is left there is no offer; if the buff is gone, only the
  weapon is offered.

### HP upgrades

- In the shop you can buy **+HP**: multiplies your max HP by the *HP upgrade
  multiplier*. The first costs the *base cost*, each further one costs
  *cost increase* more (10 $, 15 $, 20 $, …). Unlimited, and it does not lock
  the roll.

## Combat

- Simulated at 20 ticks per second. HP starts full every fight; every buff and
  stack resets every fight.
- Your weapon attacks whenever its cooldown is ready. Each attack:
  1. the defender may **dodge** (dodge chance),
  2. the attacker may **crit** (crit chance → damage × crit damage),
  3. damage is reduced by the defender's **damage reduction**,
  4. invulnerability blocks it completely.
- Damage reduction and dodge chance are the sum over all your items, capped at
  80 % and 60 %.
- Weapon cooldown can't go below 0.1 s, crit chance is capped at 100 %.
- A stunned player's weapon and item cooldowns stop. Stuns hitting an already
  stunned player add their duration. Invulnerability blocks new stuns.
- **Overtime**: after 30 s all damage is increased by +10 % per second. From 60 s
  on (if **neither** fighter has a weapon: overtime from 5 s, sudden death from
  10 s), **sudden death** deals 1 damage per tick to both players, doubling every
  second, ignoring damage reduction, dodge and invulnerability. If both die on
  the same tick, the player with more HP before that tick wins; equal = draw.

### Effects

- **Triggers**: no trigger = always active (static); `cooldown` = fires every
  *cooldown* seconds (first time after one full cooldown); `round-start` = at the
  start of the fight; `<item>.hit` = when that weapon lands a hit (dodged or
  blocked attacks don't count); `<item>.crit` = when that weapon lands a crit.
- **Conditions**: `hp_below` — active only while below that HP fraction;
  `every_n_hits` — only every n-th trigger.
- **Targets**: `type.weapon` = your weapon, an item ID = that item if you own
  it, an effect ID = that effect (e.g. its cooldown or damage),
  `special_chance` = your special item chance in the shop.
- **Stacking**: stacks of the *same effect* add up
  (5 Guitar stacks of ×1.125 → ×1.625), *different effects* multiply
  (Pepf Shirt ×0.85 and Cyan Sigil ×0.9 → ×0.765). Flat `add` values always add.
- Timed stacking buffs refresh all stacks whenever a new stack is gained; when
  the duration runs out, all stacks drop. Timed buffs without `max_stacks` have
  at most 1 stack. Untimed buffs without `max_stacks` stack without limit.
- `attack` effects (Staff Smash) can't be dodged or crit, are reduced by damage
  reduction, scaled by overtime and blocked by invulnerability.
- `stun` effects from a weapon roll that weapon's crit chance; a crit doubles
  the stun duration.
- `money` (Coin) and `gamble` (Black Market Chip) money is paid out when the
  fight ends. The Chip starts at its payout and doubles or halves every
  cooldown. `interest` (Crown) pays a share of the money you hold when the
  fight starts.

## Lobby events (first to …)

Each event can be claimed once per lobby. Everyone sees a banner when it is
claimed and the list of open/claimed events. Defined in `json/events.json`.

| Event | Condition | Reward |
|---|---|---|
| First Blood | First to win a fight | +15 $ |
| Lucky Find | First to obtain a special item | +10 $ |
| Pack Master | First to own all 5 items of one pack | ×1.1 weapon damage (permanent) |
| Untouchable | First flawless victory (not round 1) | +20 $ |
| Dragon Tamer | First to own The Dragon | Title |
| Tycoon | First to hold 250 $ | Title |

## Accounts & stats

- Unique username (3–20 letters, digits, `_`) and password (≥ 6 characters).
  The display name can be changed any time and doesn't need to be unique.
- Every finished match is stored with placements and final builds.
- Leaderboard: ranked by wins, then win rate.
