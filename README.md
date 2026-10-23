# Ashen

A browser auto-battler for 2–8 friends. Buy items in the shop, build around a
pack, and let the server fight it out. Last player standing wins.

The full rules are in **[docs/RULES.md](docs/RULES.md)**.

## How to play

1. Register an account and log in.
2. Create a lobby and share the 5-letter code (or make it public so it shows up
   under *Browse*). The host can change the match settings, and everyone picks a
   kit.
3. Each round you get a **shop** with 6 items and 5 free rerolls. Buying an item
   locks the roll (no more rerolls, but you can still buy the rest of it).
   Buying an item you already own levels it up.
4. When everyone is ready (or the timer runs out) you fight another player live.
   Use your kit's abilities with **Q W E R** (or the buttons).
5. Losing costs a life. At 0 lives you're out.

## Project layout

```
index.html, css/, js/   Frontend (plain HTML/CSS/JS modules, no build step)
img/                    Sprites
json/                   items.json, effects.json, events.json (game data)
server/server/          C# server (ASP.NET Core, .NET 10)
  Combat/                 Fight simulation
  Game/                   Lobbies, shop, match loop, lobby events
  Data/                   SQLite accounts, sessions, match history
  Item/                   Item/effect models and JSON loading
server/server.Tests/    xUnit tests (combat, shop odds, data)
deploy/                 Raspberry Pi installer + nginx snippet
docs/RULES.md           Every rule of the game
```

The server serves the frontend, the REST API (`/api/...`) and the game
WebSocket (`/ws`) on one port. All frontend URLs are relative, so it runs at
`/` locally and under `/ashen/` behind nginx.

## Running locally

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```bash
cd server/server
dotnet run
```

Open <http://localhost:5067>. The database is created at `data/ashen.db` in the
repository (ignored by git).

Environment variables:

| Variable | Default | Meaning |
|---|---|---|
| `ASPNETCORE_URLS` | `http://0.0.0.0:5067` | Address/port to listen on |
| `ASHEN_ROOT` | auto-detected | Folder that contains `index.html` and `json/` |
| `ASHEN_DB` | `<root>/data/ashen.db` | SQLite database file |
| `ASHEN_TRUSTED_PROXIES` | – | Comma-separated proxy IPs whose `X-Forwarded-For` is trusted |
| `ASHEN_LOGS` | `logs/` next to the database | Folder for the balancing logs |
| `ASHEN_ADMINS` | `pepf` | Comma-separated usernames with access to the Admin tab |
| `ASHEN_DATA_OVERRIDES` | `json-overrides/` next to the database | Where admin-edited data files are stored |

Run the tests:

```bash
cd server
dotnet test server.Tests
```

Balance report (thousands of simulated fights: weapons, support items, kits by
weapon, full packs, random builds):

```bash
cd server
ASHEN_SIM=1 dotnet test server.Tests --filter BalanceSim --logger "console;verbosity=detailed"
```

## Deploying (Raspberry Pi behind the pepf.net VPS)

**On the Pi** (64-bit Raspberry Pi OS), run:

```bash
curl -fsSL https://raw.githubusercontent.com/pepf0/ashen/ccode/deploy/install.sh | sudo bash
```

This installs .NET 10, clones the `ccode` branch to `/opt/ashen/src`, builds it,
and runs it as the `ashen` systemd service on port **6769** with the database
at `/var/lib/ashen/ashen.db` (backed up daily to `/var/lib/ashen/backups`,
14 days kept). Run the same command again to update. If 6769 is taken, the
script stops; pick another port with `... | sudo ASHEN_PORT=6770 bash` (and use
it in nginx too).

**On the VPS**, run

```bash
curl -fsSL https://raw.githubusercontent.com/pepf0/ashen/ccode/deploy/nginx-setup.py | sudo python3 -
```

It adds the two `location` blocks from
[`deploy/nginx-ashen.conf`](deploy/nginx-ashen.conf) to the `pepf.net` server
block (the HTTPS one if there are several), backs the file up to
`/root/nginx-backups`, runs `nginx -t` (restoring the backup if it fails) and
reloads nginx. The game is then at <https://pepf.net/ashen/>.

### Balancing logs

Every match writes a JSON Lines file to `/var/lib/ashen/logs/`
(`<date>-<time>-<lobby>.jsonl`, kept forever; set `ASHEN_LOGS` to move it).
Each line is one object with a `type`:

| type | When | Contents |
|---|---|---|
| `match_start` | match starts | lobby, settings, mutators, active events, players |
| `snapshot` | every shop opens (after income) | money, income breakdown, kit, loss streak, lives, max HP, HP upgrades, special chance, W/L, titles, build |
| `roll` | every shop roll and reroll | the 6 items, reroll number, special chance |
| `pity_offer` | shop 3, weaponless players (Empty Hands event) | offered items and prices |
| `purchase` | every buy | item, price, new/level-up/replace, money before/after, rerolls used, time left |
| `hp_upgrade` | every HP upgrade | cost, new max HP |
| `round` | every shop opens | round number, round modifier, players alive |
| `fight` | every fight | both builds, kits, max HP, DR, dodge, winner, duration, damage, earnings, attacks, crits, dodges, stuns, abilities used, healing, shield absorbed, round modifier, sudden death |
| `kit_change` | kit switched in a shop | from, to, cost |
| `augment_offer` / `augment_pick` | augment rounds | offered ids / picked id and tier |
| `mystery_offer` / `mystery_use` | mystery events | kind, choices / what was done and the result |
| `bounty` | a bounty is collected | collector, victim, amount |
| `event_claimed` | first-to events | event, player |
| `elimination` | player knocked out | placement, final build |
| `match_end` | match ends | standings |

Copy them to your PC with e.g.
`scp -r pepf@<pi>:/var/lib/ashen/logs ./ashen-logs`
(or `sudo cp -r /var/lib/ashen/logs ~` first if your user can't read them).

Useful commands on the Pi:

```bash
journalctl -u ashen -f          # live logs
sudo systemctl restart ashen    # restart
ls /var/lib/ashen/backups       # database backups
```

## Features at a glance

- Live fights with kits (Q/W/E/R, rebindable), augments, round modifiers,
  match mutators, arenas, mystery events, bounties and first-to events.
- Solo vs bots and bot seats in lobbies (Easy / Normal / Hard).
- Accounts with levels, 40 achievements, cosmetics (name colors, frames,
  victory effects, titles), kit mastery and profiles.
- Friends with invites, leaderboard, match history with per-round fight
  replays (newest 100 matches).
- Admin tab for `ASHEN_ADMINS`: live lobbies, bans, a validated JSON editor
  with hot reload, statistics.

## Editing game data

Items, effects, lobby events, kits (`kits.json`), round modifiers
(`modifiers.json`), augments (`augments.json`), match mutators
(`mutators.json`), arenas (`arenas.json`), achievements and cosmetics live
in `json/`. On the live server you can also edit them in the **Admin** tab:
edits are validated, stored in `/var/lib/ashen/json-overrides/` (so updates
don't overwrite them) and reloaded as soon as no match is running. The server validates every
reference on startup (unknown effect IDs or event items stop it with a clear
error), and the compendium reads the same files. Restart the service after
changing them.
