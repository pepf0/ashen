#!/usr/bin/env bash
# Installs or updates Ashen on a Raspberry Pi (64-bit Raspberry Pi OS / Debian).
#   curl -fsSL https://raw.githubusercontent.com/pepf0/ashen/ccode/deploy/install.sh | sudo bash
# Re-running the same command updates to the latest commit of the branch.
set -euo pipefail

REPO="${ASHEN_REPO:-https://github.com/pepf0/ashen.git}"
BRANCH="${ASHEN_BRANCH:-ccode}"
PORT="${ASHEN_PORT:-6769}"
TRUSTED_PROXIES="${ASHEN_TRUSTED_PROXIES:-10.10.0.1}"
ADMINS="${ASHEN_ADMINS:-pepf}"
PREFIX="/opt/ashen"
DATA="/var/lib/ashen"
DOTNET_DIR="/opt/dotnet"
SERVICE="ashen"

log() { printf '\033[1;36m==>\033[0m %s\n' "$*"; }
die() { printf '\033[1;31mError:\033[0m %s\n' "$*" >&2; exit 1; }

[ "$(id -u)" -eq 0 ] || die "Run as root (pipe into 'sudo bash')."

log "Installing packages (git, curl, sqlite3)"
export DEBIAN_FRONTEND=noninteractive
apt-get update -qq
apt-get install -y -qq git curl ca-certificates sqlite3 >/dev/null

# Refuse to take a port another program already uses.
if ! systemctl is-active --quiet "$SERVICE" && ss -ltn "( sport = :$PORT )" | grep -q ":$PORT"; then
    die "Port $PORT is already in use. Re-run with another port, e.g.  ... | sudo ASHEN_PORT=6770 bash"
fi

if ! "$DOTNET_DIR/dotnet" --list-sdks 2>/dev/null | grep -q '^10\.'; then
    log "Installing the .NET 10 SDK to $DOTNET_DIR (this takes a few minutes on a Pi)"
    curl -fsSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh
    bash /tmp/dotnet-install.sh --channel 10.0 --install-dir "$DOTNET_DIR" >/dev/null
    rm -f /tmp/dotnet-install.sh
fi
export DOTNET_ROOT="$DOTNET_DIR" DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1

if ! id ashen >/dev/null 2>&1; then
    log "Creating system user 'ashen'"
    useradd --system --home-dir "$DATA" --shell /usr/sbin/nologin ashen
fi
mkdir -p "$PREFIX" "$DATA/backups"
chown -R ashen:ashen "$DATA"

log "Fetching $REPO ($BRANCH)"
if [ -d "$PREFIX/src/.git" ]; then
    git -C "$PREFIX/src" fetch --quiet origin "$BRANCH"
    git -C "$PREFIX/src" checkout --quiet -B "$BRANCH" "origin/$BRANCH"
    git -C "$PREFIX/src" reset --quiet --hard "origin/$BRANCH"
else
    git clone --quiet --branch "$BRANCH" "$REPO" "$PREFIX/src"
fi

log "Building"
rm -rf "$PREFIX/app.new"
"$DOTNET_DIR/dotnet" publish "$PREFIX/src/server/server/server.csproj" -c Release -o "$PREFIX/app.new" --nologo -v quiet
rm -rf "$PREFIX/app.old"
[ -d "$PREFIX/app" ] && mv "$PREFIX/app" "$PREFIX/app.old"
mv "$PREFIX/app.new" "$PREFIX/app"

log "Installing systemd service on port $PORT"
cat > "/etc/systemd/system/$SERVICE.service" <<UNIT
[Unit]
Description=Ashen game server
After=network-online.target
Wants=network-online.target

[Service]
User=ashen
Group=ashen
WorkingDirectory=$PREFIX/app
ExecStart=$DOTNET_DIR/dotnet $PREFIX/app/server.dll
Environment=ASHEN_ROOT=$PREFIX/src
Environment=ASHEN_DB=$DATA/ashen.db
Environment=ASPNETCORE_URLS=http://0.0.0.0:$PORT
Environment=ASHEN_TRUSTED_PROXIES=$TRUSTED_PROXIES
Environment=ASHEN_ADMINS=$ADMINS
Environment=DOTNET_ROOT=$DOTNET_DIR
Environment=DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1
Environment=DOTNET_CLI_TELEMETRY_OPTOUT=1
Environment=HOME=$DATA
Restart=always
RestartSec=3
NoNewPrivileges=true
ProtectSystem=strict
ProtectHome=true
PrivateTmp=true
ReadWritePaths=$DATA

[Install]
WantedBy=multi-user.target
UNIT

log "Installing daily database backup (keeps 14 days)"
cat > "$PREFIX/backup.sh" <<'BACKUP'
#!/usr/bin/env bash
set -euo pipefail
DATA=/var/lib/ashen
[ -f "$DATA/ashen.db" ] || exit 0
sqlite3 "$DATA/ashen.db" ".backup '$DATA/backups/ashen-$(date +%F).db'"
find "$DATA/backups" -name 'ashen-*.db' -mtime +14 -delete
BACKUP
chmod 755 "$PREFIX/backup.sh"
cat > "/etc/systemd/system/$SERVICE-backup.service" <<UNIT
[Unit]
Description=Back up the Ashen database

[Service]
Type=oneshot
User=ashen
ExecStart=$PREFIX/backup.sh
UNIT
cat > "/etc/systemd/system/$SERVICE-backup.timer" <<UNIT
[Unit]
Description=Daily Ashen database backup

[Timer]
OnCalendar=*-*-* 04:30:00
Persistent=true

[Install]
WantedBy=timers.target
UNIT

systemctl daemon-reload
systemctl enable --quiet "$SERVICE" "$SERVICE-backup.timer"
systemctl restart "$SERVICE"
systemctl start "$SERVICE-backup.timer"

log "Waiting for the server to come up"
for _ in $(seq 1 30); do
    if curl -fsS "http://127.0.0.1:$PORT/api/lobbies" >/dev/null 2>&1; then
        rm -rf "$PREFIX/app.old"
        log "Ashen is running on port $PORT ($(git -C "$PREFIX/src" log -1 --format='%h %s'))"
        echo "    Logs:    journalctl -u $SERVICE -f"
        echo "    Update:  re-run the same install command"
        exit 0
    fi
    sleep 1
done
journalctl -u "$SERVICE" -n 40 --no-pager || true
die "The server did not start. See the log above."
