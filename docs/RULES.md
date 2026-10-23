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
  | Starting HP | 250 |
  | HP upgrade multiplier | ×1.2 |
  | HP upgrade base cost | 10 $ |
  | HP upgrade cost increase | +5 $ per purchase |
- After a match, everybody returns to the same lobby so the host can start a
  rematch.

## Match flow

A match is a loop of **Shop → Combat → Results** until one player is left.

1. Every player starts with *starting money* and *starting lives*, no items.
2. **Shop phase** lasts the *shop timer*, or ends early when every alive player
   is ready. Disconnected players count as ready.
3. **Combat phase**: alive players are paired (see Pairing) and every fight is
   simulated by the server. Every player watches their own fight replay in real
   time (no skipping, no speed-up).
4. **Results**: shown until every connected player has finished their replay,
   then 5 more seconds.
5. The next shop starts. Every shop except the first pays *income*.

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

- Disconnecting keeps your seat reserved; log in again (same account) to rejoin.
  While you are gone you count as ready and fight with your current build.
- Clicking **Forfeit** eliminates you immediately (you take the lowest open
  placement) and frees your seat.

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

### Buying

- **Duplicate = level up.** Buying an item you already own raises its level by 1
  (max level 5). Its `level_stats` are added per level above 1, and its effect
  values grow by +50 % of their base strength per level above 1
  (e.g. Knight ×0.9 → ×0.85 → ×0.8, Coin 2 $ → 3 $ → 4 $).
- Slots: 1 weapon, 1 shirt, 4 dynamic slots (buff, charm, passive).
- No selling. Buying a different weapon/shirt replaces the old one (asks for
  confirmation; levels are lost). With all 4 dynamic slots full you pick a
  dynamic to replace.

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
  on, **sudden death** deals 1 damage per tick to both players, doubling every
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
