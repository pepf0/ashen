# Ashen

A browser auto-battler for 2–8 friends. Buy items in the shop, build around a
pack, and let the server fight it out. Last player standing wins.

The full rules are in **[docs/RULES.md](docs/RULES.md)**.

## How to play

1. Register an account and log in.
2. Create a lobby and share the 5-letter code (or make it public so it shows up
   under *Browse*). The host can change the match settings.
3. Each round you get a **shop** with 6 items and 5 free rerolls. Buying an item
   locks the roll (no more rerolls, but you can still buy the rest of it).
   Buying an item you already own levels it up.
4. When everyone is ready (or the timer runs out) you fight another player. The
   fight is simulated on the server and replayed in real time.
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

Run the tests:

```bash
cd server
dotnet test server.Tests
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
| `match_start` | match starts | lobby, settings, players |
| `snapshot` | every shop opens (after income) | money, lives, max HP, HP upgrades, special chance, W/L, titles, build |
| `roll` | every shop roll and reroll | the 6 items, reroll number, special chance |
| `pity_offer` | shop 3, weaponless players | offered items and prices |
| `purchase` | every buy | item, price, new/level-up/replace, money before/after, rerolls used, time left |
| `hp_upgrade` | every HP upgrade | cost, new max HP |
| `fight` | every fight | both builds, max HP, DR, dodge, winner, duration, damage, earnings, attacks, crits, dodges, stuns, sudden death |
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

## Editing game data

Items, effects and lobby events live in `json/`. The server validates every
reference on startup (unknown effect IDs or event items stop it with a clear
error), and the compendium reads the same files. Restart the service after
changing them.
