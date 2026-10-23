#!/usr/bin/env python3
"""Adds the /ashen/ reverse proxy to the pepf.net server block on the VPS, tests nginx and reloads it.
    curl -fsSL https://raw.githubusercontent.com/pepf0/ashen/ccode/deploy/nginx-setup.py | sudo python3 -
Safe to run twice. If nginx -t fails, the original file is restored."""
import glob, os, re, shutil, subprocess, sys, time
BLOCK = """
    # Ashen game (added by the Ashen setup)
    location = /ashen {
        return 301 /ashen/;
    }

    location /ashen/ {
        proxy_pass http://10.10.0.2:6769/;
        proxy_http_version 1.1;
        proxy_set_header Upgrade $http_upgrade;
        proxy_set_header Connection "upgrade";
        proxy_set_header Host $host;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
        proxy_read_timeout 1h;
        proxy_send_timeout 1h;
    }
"""
TEST_MODE = len(sys.argv) > 1 and sys.argv[1] != "-"
DIRS = sys.argv[1:] if TEST_MODE else ["/etc/nginx/sites-enabled/*", "/etc/nginx/conf.d/*.conf", "/etc/nginx/nginx.conf"]

def server_blocks(text):
    """Yields (start, end) of every `server { ... }` block, end = index of its closing brace."""
    for m in re.finditer(r'(^|[\s;{}])server\s*\{', text):
        depth, i = 0, m.end() - 1
        while i < len(text):
            c = text[i]
            if c == '#':
                i = text.find('\n', i)
                if i < 0: break
                continue
            if c == '{': depth += 1
            elif c == '}':
                depth -= 1
                if depth == 0:
                    yield m.end() - 1, i
                    break
            i += 1

if not TEST_MODE and shutil.which("nginx") is None:
    sys.exit("nginx isn't installed on this machine. Run this on the VPS that runs nginx for pepf.net (ssh into it first).")
if not TEST_MODE:
    check = subprocess.run(["nginx", "-t"], capture_output=True, text=True)
    if check.returncode != 0:
        sys.exit("Your nginx config already fails `nginx -t`, so nothing was changed:\n" + check.stderr)

MARKER = "\n    # Ashen game (added by the Ashen setup)\n"
OUR_BLOCK = re.compile(re.escape(MARKER) + r".*?proxy_send_timeout 1h;\n    \}\n", re.S)

def names(body):
    """Exact server_name entries of a server block (so class.pepf.net doesn't count as pepf.net)."""
    out = set()
    for m in re.finditer(r'(?:^|[;{])\s*server_name\s+([^;]*);', body, re.M):
        out.update(m.group(1).split())
    return out

def is_pepf(body):
    return "pepf.net" in names(body)

def pepf_block_end(text):
    """Closing brace of the pepf.net server block in text, preferring the HTTPS (listen 443) one."""
    ends = [(re.search(r'listen[^;]*443', text[s:e]) is None, e) for s, e in server_blocks(text) if is_pepf(text[s:e])]
    return min(ends)[1] if ends else None

candidates, misplaced, seen_names = [], [], set()
for pattern in DIRS:
    for path in sorted(glob.glob(pattern)):
        try: text = open(path).read()
        except (IsADirectoryError, PermissionError): continue
        for start, end in server_blocks(text):
            body = text[start:end]
            seen_names.update(names(body))
            if "location /ashen/" in body:
                if is_pepf(body):
                    print(f"Ashen is already configured for pepf.net in {path}. Nothing to do.")
                    sys.exit(0)
                if MARKER in body:
                    misplaced.append(path)
                    continue
            if not is_pepf(body): continue
            https = re.search(r'listen[^;]*443', body) is not None
            candidates.append((not https, path, end))
if not candidates:
    sys.exit("Could not find a server block with exactly `server_name pepf.net`. Found: "
             + (", ".join(sorted(seen_names)) or "none") + ". Nothing was changed.")
candidates.sort()
_, path, _ = candidates[0]
# Keep backups outside sites-enabled, or nginx would load them as a second config.
backup_dir = "/root/nginx-backups" if not TEST_MODE else os.path.dirname(path) + "-backups"
os.makedirs(backup_dir, exist_ok=True)
backups = {}
for f in set(misplaced + [path]):
    backups[f] = os.path.join(backup_dir, f"{os.path.basename(f)}.{int(time.time())}")
    shutil.copy2(f, backups[f])

# Remove blocks an older version of this script put into the wrong site (e.g. class.pepf.net).
for f in set(misplaced):
    text = open(f).read()
    open(f, "w").write(OUR_BLOCK.sub("\n", text))
    print(f"Removed the misplaced /ashen/ proxy from {f}")

text = open(path).read()
end = pepf_block_end(text)
open(path, "w").write(text[:end].rstrip() + "\n" + BLOCK + text[end:])
print(f"Added the /ashen/ proxy to {path} (backups in {backup_dir})")
if TEST_MODE:
    sys.exit(0)
test = subprocess.run(["nginx", "-t"], capture_output=True, text=True)
if test.returncode != 0:
    for f, b in backups.items():
        shutil.copy2(b, f)
    sys.exit("nginx -t failed, so the change was reverted:\n" + test.stderr)
subprocess.run(["systemctl", "reload", "nginx"], check=True)
print("nginx reloaded. Open https://pepf.net/ashen/")
