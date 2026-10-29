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
  | Special chance multiplier | ×1 (multiplies everyone's special item chance) |
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

## Match mutators

When the match starts, **2 random mutators** are rolled and apply all match
(shown above the shop): Big Shops (8 slots), Special Frenzy (special chance ×2),
Shirtless (no shirts in shops), Armed Start (random generic weapon), Old Money
(+50 $ start), Hardcore (−2 lives, at least 2), Long Haul (+2 lives), Augment
Feast (4 augment choices + an extra augment at round 12), Chaos (a round
modifier every round from round 1), Quick Death (overtime 20 s, sudden death
40 s), Paper Bodies (start HP ×0.6), Master Smith (max item level 7), Free
Spirits (free kit changes), Compound Interest (interest cap 10 $), Double
Trouble (every 3rd round all damage ×2), Arena Masters (every fight has an
arena). Defined in `json/mutators.json`.

## Augments

At **rounds 3, 6 and 9** the shop offers 3 augments; pick 1 (before the fight,
or it's lost). Augments last all match and stay hidden from others until the
end. Each offer rolls one tier for all 3 choices: **Silver** 60 %, **Gold** 30 %,
**Prismatic** 10 %. You never get an augment twice. 32 augments in
`json/augments.json`, built from these mods:

| Mod | Effect |
|---|---|
| `weapon_damage_mult`, `weapon_cooldown_mult`, `crit_damage_mult`, `max_hp_mult` | Multiply that stat in fights |
| `crit_chance_add`, `damage_reduction_add`, `dodge_add` | Add to that stat (DR and dodge before their caps) |
| `lifesteal`, `regen`, `thorns`, `start_shield` | Heal % of damage dealt, % max HP per second, reflect % of weapon damage taken, shield of % max HP at fight start |
| `ult_charge_mult`, `skill_cooldown_mult` | Kit ultimate charge speed, skill cooldowns |
| `execute`, `first_strike`, `double_strike`, `second_wind` | +% damage vs targets under 30 % HP, first weapon hit multiplier, chance to strike twice, survive one lethal hit per fight |
| `stun_duration_mult`, `opening_stun` | Longer stuns, stun the enemy at fight start |
| `income_add`, `interest_cap_add`, `rerolls_add`, `win_bonus_add`, `shop_discount`, `special_chance_mult` | Economy |
| `money_now`, `life_now`, `dynamic_slots_add` | Instant: money, a life, extra buff/charm/passive slots |

## Mystery events

About 25 % of shops (not in augment rounds) also offer one mystery event, each
player rolling their own:

- **Chest**: pay 25 $ for a random item (10 % special), free to take.
- **Shrine**: sacrifice 1 life for one of 3 special items (only above 1 life).
- **Gamble Table**: bet up to half your money, 50 % double or lose it.
- **Blacksmith**: pay 15 $ to level up one of your items (specials up to 3).
- **Fountain**: pay 40 $ for +1 life (up to your starting lives).
- **Library**: +10 $ and your next kit change is free.

## Arenas

Every fight rolls its own arena (40 % plain): Lava Pit (1 % max HP burn per
second), Frozen Hall (cooldowns ×1.15), Sanctuary (1 % max HP heal per second),
Stormy Peak (lightning for 8 % max HP every 5 s), Windy Cliffs (+12 % dodge),
The Forge (+10 % crit, crit damage ×1.2), Colosseum (+5 $ to the winner),
The Void (ultimates charge ×2). Defined in `json/arenas.json`.

## Bounties

A player on a **3+ win streak** has a bounty of 10 $ per streak win, visible to
everyone. Whoever beats them collects it. Losses and draws end a streak.

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
and take: dealing + taking 75 % of your max HP fills it. Charge and cooldowns
reset every fight. Stunned players can't use abilities (except Cleanse).

| Kit | Q | W | E | R (ultimate) |
|---|---|---|---|---|
| Duelist | Parry (7 s): blocks the next hit within 0.6 s, reflects 50 % | Lunge (7 s): instant extra weapon attack | Feint (12 s): enemy's next weapon attack misses | Flurry: 5 extra weapon attacks over 1.5 s |
| Berserker | Rage (10 s): +60 % damage dealt, +15 % taken, 4 s | Bloodlust (12 s): heal 40 % of damage dealt, 5 s | Shout (8 s): stun 0.5 s | Rampage: weapon cooldown ×0.5 + stun immune 6 s, breaks stuns |
| Guardian | Shield (12 s): absorb 9 % max HP, 4 s | Brace (12 s): +20 % damage reduction (past the item cap), 3 s | Cleanse (12 s): remove stun + enemy debuffs, works while stunned | Bulwark: 2 s, blocks 50 % of incoming damage and throws the blocked part back at the attacker (not invulnerable; Headshot is blocked but not thrown back) |
| Trickster | Smoke (8 s): +30 % dodge, 3 s | Hex (11 s): enemy weapon cooldown ×1.6, 4 s | Blink (5 s): next weapon attack crits | Mirror: copy the enemy's active upsides for 6 s (copied stacks capped at ×1.5) |
| Gambler | Coin Flip (3.5 s): next hit ×3 or ×0.75 | Double Down (10 s): +50 % crit chance, 5 s | Jackpot (12 s): 25 % stun 1.5 s, else +3 $ | All In: next 4 hits each 65 % to deal ×3.5, else miss |
| Sniper | Aim (8 s): next hit +125 % | Mark (12 s): enemy takes +20 % damage, 5 s | Retreat (12 s): +30 % dodge, 2 s | Headshot: 2 s wind-up, then 40 % of the enemy's current HP |

- Ability dodge and damage reduction can go past the item caps, but never past
  the hard caps: **50 % dodge** and **75 % damage reduction** in total.
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

### Empty Hands (special event)

- **Empty Hands** is a special lobby event that is only active in some matches
  (30 %, `chance` in `json/events.json`). When it is active, everyone gets a
  banner in rounds 1 and 2 and it shows in the Lobby events list.
- If you fought **both** round 1 and round 2 without a weapon, shop 3 also offers
  a random **special weapon and its pack's buff** (e.g. Pepf's Guitar + Pepf's
  Pick) at a third of their price (rounded up), as two separate offers.
- Buying them doesn't lock the roll. They count as obtained specials (and can
  claim Lucky Find). The offer disappears after shop 3.
- Only specials you could still get are offered (per-lobby mode respected). If
  no special weapon is left there is no offer; if the buff is gone, only the
  weapon is offered.
- Without the event there is no weaponless offer.

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
  60 % and 40 % (abilities can add more, up to the hard caps of 75 % and 50 %).
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

Each event can be claimed once per lobby (special events like Empty Hands
are only active in some matches, see above). Everyone sees a banner when it is
claimed and the list of open/claimed events. Defined in `json/events.json`.

| Event | Condition | Reward |
|---|---|---|
| First Blood | First to win a fight | +15 $ |
| Lucky Find | First to obtain a special item | +10 $ |
| Pack Master | First to own all 5 items of one pack | ×1.1 weapon damage (permanent) |
| Untouchable | First flawless victory (not round 1) | +20 $ |
| Dragon Tamer | First to own The Dragon | Title |
| Tycoon | First to hold 250 $ | Title |

## Bots

- **Solo vs bots** (menu) starts a private match against 1–7 bots right away.
- Hosts can **add bots** to empty seats in any lobby.
- Easy bots buy randomly and press abilities at random; Normal bots commit to
  packs, level items, keep a reserve and use abilities when ready; Hard bots
  also reroll for good items, save for interest and time defensive skills.
- Matches with fewer than 2 humans are kept in your history but don't count
  for the leaderboard or kit mastery. XP and achievements are earned in all
  matches.

## Levels, achievements & cosmetics

- XP per match: 100, +50 per placement above last, +10 per fight won, +25 per
  new achievement. Each level needs 10 % more XP than the last (max level 50).
- 40 achievements (`json/achievements.json`), with the share of players who
  have each.
- Cosmetics (`json/cosmetics.json`) unlock with levels or achievements. Purely
  visual, no gameplay effect:
  - **Name colors** (many animated: Ember Glow, Blood Moon, Spectral, Molten
    Gold, Hellfire, …) and **name borders** (Iron Plate, Runed, Inferno,
    Prismatic, …).
  - **Titles**: unlocked title cosmetics, any achievement name or your kit
    mastery, shown next to your name.
  - **Portrait borders** around your kit portrait (lobby, player list, fight
    panel, podium, profile) and **fighter frames** around your fight panel.
  - **Generic weapon skins** (Rusted, Obsidian, Frostforged, Emberforged,
    Gilded, Voidtouched, Prismatic, …): recolor and add a glow to generic
    weapons (Sword, Hammer, Frying Pan, Bow) in the shop, your build and fights.
  - **Victory effects** played in the arena when you win a fight (Confetti,
    Fireworks, Lightning, Meteor, Soul Harvest, Ash Storm, …).
  - **Admin set "Pepf Prime"** (only for `ASHEN_ADMINS`, hidden from everyone
    else): Pepf Prime name color, Tidecrown name border, "The Creator"
    title, Pepf Aura portrait border, Pepf Tide fighter frame, Pepf Teal weapon
    skin and the Encore victory effect. Wearing any piece also gives the
    player a turquoise aura in fights (light column, rolling tide, orbiting
    stars, drifting notes and a pixel crown), highlighted rows in the lobby,
    match sidebar and standings, light rays and a turquoise podium, a
    deep-sea profile banner, and an "has arrived" announcement with a chime
    when they join a lobby. The menu's demo duel sometimes shows the set off.
- Click any player name to see their profile.

## Fights on screen

- Fights play out in an animated pixel arena matching the rolled arena (Lava
  Pit, Frozen Hall, Storm, Forge, Colosseum, Void, …). Each player's items
  float on their side: the weapon flies over to strike (bows shoot arrows, the
  Guitar shoots notes, the Whip lashes), shirts and charms hover and orbit,
  and an item pulses whenever its effect fires. The loser's items fall and
  crumble to ash.
- Settings → **Effects & sound**: visual effects Full / Normal / Reduced (for
  slow PCs) and the sound volume, saved per device.

## Friends, history & replays

- Add friends by username; see who's online, in a lobby or in a match; invite
  friends to your lobby or join theirs.
- Every match is stored in history; the fights of the newest 100 matches can
  be replayed (pause, 1×/2×/4×) from the match details.

## Keybinds

Skills, ultimate, reroll, ready, +HP and buying shop slots 1–6 have keybinds
(defaults Q W E R, D, Space, H, 1–6). Change them in Settings (click, press a
key); they're saved to your account and shown next to every button.

## Kit mastery

Match wins with each kit count on your account: 5 / 20 / 50 wins give
"<Kit> Adept / Expert / Master". Your best title is shown in lobbies, matches and
on the leaderboard.

## Accounts & stats

- Unique username (3–20 letters, digits, `_`) and password (≥ 6 characters).
  The display name can be changed any time and doesn't need to be unique.
- Every finished match is stored with placements and final builds.
- Leaderboard: ranked by wins, then win rate.
