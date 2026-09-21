#!/usr/bin/env bash
# ---------------------------------------------------------------------------
# Play FGO Arcade (Cloud23333 local platform) on Linux under Wine.
#
# One script, safe to run again and again:
#   1. adds the cabinet virtual-LAN addresses and lowers the privileged-port
#      floor so the title server can listen on 777
#   2. starts MariaDB and ARTEMiS under Wine if they are not already up
#   3. waits for the server's health check ("Service OK")
#   4. applies the Wine compatibility patches to the install if needed
#      (ago.exe byte patches, English overlay merge, in-binary English text)
#   5. launches the game with the content hook and a movable window, and if it
#      comes up on the known AMDaemon error screen, restarts it automatically
#
# Usage:
#   ./play-fgo.sh                 # launch (default install path below)
#   FGO_INSTALL=/path ./play-fgo.sh
#   ./play-fgo.sh --stop          # close the game (leaves the server running)
#   ./play-fgo.sh --status        # show what is running
#   ./play-fgo.sh --revert        # undo the patches in the install
#   FGO_DESKTOP=1 ./play-fgo.sh   # run inside a Wine desktop window (easier to
#                                 # focus on compositors that ignore raise)
# ---------------------------------------------------------------------------
set -uo pipefail

INSTALL="${FGO_INSTALL:-$HOME/.cache/fgoac-game/install}"
APP="$INSTALL/App"
SERVER="$INSTALL/Server"
ARTEMIS="$SERVER/artemis"
LOGS="$INSTALL/logs"
RUNTIME_INI="$INSTALL/DEVICE/runtime/segatools.runtime.ini"
MARIA_INI="$SERVER/mariadb.ini"
MARIA_BIN="$SERVER/mariadb-10.11.16-winx64"
PYTHON="$SERVER/python/python.exe"
DB_PORT=8888
TITLE_PORT=777
BILLING_PORT=9999
AIME_PORT=7777
MAX_ATTEMPTS=3

C_OK=$'\033[32m'; C_WARN=$'\033[33m'; C_ERR=$'\033[31m'; C_DIM=$'\033[2m'; C_OFF=$'\033[0m'
say()  { printf '%s==>%s %s\n' "$C_OK" "$C_OFF" "$*"; }
warn() { printf '%s!!%s  %s\n' "$C_WARN" "$C_OFF" "$*"; }
die()  { printf '%s!!%s  %s\n' "$C_ERR" "$C_OFF" "$*" >&2; exit 1; }
dim()  { printf '%s   %s%s\n' "$C_DIM" "$*" "$C_OFF"; }

command -v wine >/dev/null || die "wine is not installed"
[ -d "$APP" ] || die "install not found at $INSTALL (set FGO_INSTALL=/path/to/install)"
mkdir -p "$LOGS"

port_open() { python3 -c "
import socket, sys
s = socket.socket(); s.settimeout(1)
sys.exit(0 if s.connect_ex(('127.0.0.1', $1)) == 0 else 1)
" 2>/dev/null; }

wait_port() { local i; for ((i=0; i<$2; i++)); do port_open "$1" && return 0; sleep 1; done; return 1; }

wine_pids() { python3 - "$1" <<'PY' 2>/dev/null
import os, sys
needle = sys.argv[1]
for pid in os.listdir('/proc'):
    if not pid.isdigit():
        continue
    try:
        cl = open('/proc/%s/cmdline' % pid, 'rb').read().decode('latin1')
    except Exception:
        continue
    if needle in cl:
        print(pid)
PY
}

# The game process only; the injector wrapper carries "ago.exe" on its command
# line too, so it has to be excluded.
game_pids() { python3 - <<'PY' 2>/dev/null
import os
for pid in os.listdir('/proc'):
    if not pid.isdigit():
        continue
    try:
        cl = open('/proc/%s/cmdline' % pid, 'rb').read().decode('latin1')
    except Exception:
        continue
    if 'ago.exe' in cl and 'inject.exe' not in cl:
        print(pid)
PY
}

# --- 1. network -------------------------------------------------------------
ensure_lan() {
  local missing=""
  ip -4 addr show lo | grep -q '192\.168\.100\.1/'  || missing="192.168.100.1/24"
  ip -4 addr show lo | grep -q '192\.168\.100\.11/' || missing="$missing 192.168.100.11/24"
  if [ -n "$missing" ]; then
    say "adding cabinet virtual LAN addresses ($missing)"
    sudo ip addr add 192.168.100.1/24 dev lo 2>/dev/null || true
    sudo ip addr add 192.168.100.11/24 dev lo 2>/dev/null || true
  fi
  local floor
  floor=$(cat /proc/sys/net/ipv4/ip_unprivileged_port_start 2>/dev/null || echo 1024)
  if [ "$floor" -gt 700 ]; then
    say "lowering privileged port floor so the title server can bind $TITLE_PORT"
    sudo sysctl -w net.ipv4.ip_unprivileged_port_start=700 >/dev/null 2>&1 || \
      warn "could not lower the port floor; the title server may not start"
  fi
}

# --- 2. server --------------------------------------------------------------
start_db() {
  port_open $DB_PORT && { dim "database already up"; return 0; }
  say "starting MariaDB"
  ( cd "$SERVER" && nohup wine "$MARIA_BIN/bin/mariadbd.exe" \
      "--defaults-file=Z:$MARIA_INI" \
      "--basedir=Z:$MARIA_BIN" \
      "--datadir=Z:$SERVER/data/mariadb" \
      "--pid-file=Z:$SERVER/state/mariadb-engine.pid" \
      "--log-error=Z:$LOGS/mariadb.log" --console \
      >"$LOGS/mariadb-stdout.log" 2>&1 & )
  wait_port $DB_PORT 40 || warn "MariaDB did not come up (see $LOGS/mariadb.log)"
}

start_artemis() {
  if port_open $TITLE_PORT && port_open $BILLING_PORT && port_open $AIME_PORT; then
    dim "title, billing and aimedb already up"; return 0
  fi
  say "starting ARTEMiS"
  ( cd "$ARTEMIS" && nohup wine "$PYTHON" index.py --config config \
      >"$LOGS/artemis-stdout.log" 2>"$LOGS/artemis-stderr.log" & )
  wait_port $TITLE_PORT 60 || warn "ARTEMiS did not open $TITLE_PORT (see $LOGS/artemis-stderr.log)"
  wait_port $BILLING_PORT 20 || true
  wait_port $AIME_PORT 20 || true
}

restart_artemis() {
  # A freshly started title server is what makes the ALL.Net handshake succeed;
  # the database is left alone so account data is never at risk.
  say "restarting the title server for a clean attempt"
  for p in $(wine_pids 'index.py'); do kill -9 "$p" 2>/dev/null; done
  sleep 4
  start_artemis
  wait_healthy
}

wait_healthy() {
  local i
  for ((i=0; i<60; i++)); do
    if curl -s -m 3 "http://192.168.100.1:$TITLE_PORT/" 2>/dev/null | grep -q "Service OK"; then
      say "server reports ready"
      return 0
    fi
    sleep 1
  done
  warn "server never reported ready; starting the game anyway"
}

# --- 3. patches -------------------------------------------------------------
patch_ini() {
  python3 - "$RUNTIME_INI" <<'PY'
import re, sys
path = sys.argv[1]
raw = open(path, 'rb').read()
text = raw.decode('utf-16') if raw[:2] in (b'\xff\xfe', b'\xfe\xff') else raw.decode('utf-8-sig')
def set_ini(section, key, value):
    global text
    m = re.search(r'(?m)^\[' + re.escape(section) + r'\]\s*$', text)
    if not m:
        text = text.rstrip() + '\r\n\r\n[%s]\r\n%s=%s\r\n' % (section, key, value); return
    start = m.end()
    nxt = re.search(r'(?m)^\[[^\]]+\]\s*$', text[start:])
    end = start + nxt.start() if nxt else len(text)
    body = text[start:end]
    km = re.search(r'(?m)^(\s*' + re.escape(key) + r'\s*=).*$', body)
    if km:
        body = body[:km.start()] + km.group(1) + value + body[km.end():]
    else:
        body = '\r\n%s=%s' % (key, value) + body
    text = text[:start] + body + text[end:]
set_ini('gfx', 'windowed', '1')
set_ini('gfx', 'framed', '1')
open(path, 'w', encoding='utf-16').write(text)
PY
}

ensure_touchshim() {
  # Mouse clicks have to become WM_TOUCH: Wine stubs the touch APIs and the
  # title screen waits for a touch that never comes. See touchshim.c.
  local src dll
  src="$(cd "$(dirname "$0")" && pwd)/touchshim.c"
  dll="$APP/touchshim.dll"
  [ -f "$dll" ] && return 0
  if command -v x86_64-w64-mingw32-gcc >/dev/null 2>&1 && [ -f "$src" ]; then
    say "building the touch shim"
    x86_64-w64-mingw32-gcc -O2 -shared -o "$dll" "$src" -luser32 \
      || warn "touch shim build failed; clicks may not register"
  else
    warn "no touch shim and no mingw compiler; clicks may not register"
  fi
}

apply_patches() {
  say "checking Wine patches"
  python3 - "$APP" <<'PY'
import json, os, shutil, sys

app = sys.argv[1]
exe = os.path.join(app, 'ago.exe')
if not os.path.isfile(exe):
    print('   ago.exe not found'); raise SystemExit(0)

backup = exe + '.wine-orig'
if not os.path.isfile(backup):
    shutil.copy2(exe, backup)

data = bytearray(open(exe, 'rb').read())
changed = 0
for name, off, want, repl in (
        ('robust-access flag cleared', 0x27AF7D, bytes.fromhex('94200000'), bytes.fromhex('00000000')),
        ('SetWindowFeedbackSetting stubbed', 0x27CB8D, bytes.fromhex('ff150df40301'), bytes.fromhex('b80100000090'))):
    cur = bytes(data[off:off+len(want)])
    if cur == repl:
        continue
    if cur != want:
        print('   %s: unexpected bytes, skipped' % name); continue
    data[off:off+len(want)] = repl
    changed += 1
if changed:
    open(exe, 'wb').write(data)
print('   ago.exe patches: %d applied' % changed)

zh_rom = os.path.join(app, 'zh', 'rom')
rom = os.path.join(app, 'rom')
rom_backup = os.path.join(app, 'rom.wine-orig')
copied = 0
if os.path.isdir(zh_rom):
    for base, _dirs, files in os.walk(zh_rom):
        for name in files:
            src = os.path.join(base, name)
            rel = os.path.relpath(src, zh_rom)
            dst = os.path.join(rom, rel)
            keep = os.path.join(rom_backup, rel)
            if os.path.isfile(dst) and open(src, 'rb').read() == open(dst, 'rb').read():
                continue
            if os.path.isfile(dst) and not os.path.isfile(keep):
                os.makedirs(os.path.dirname(keep), exist_ok=True)
                shutil.copy2(dst, keep)
            os.makedirs(os.path.dirname(dst), exist_ok=True)
            shutil.copy2(src, dst)
            copied += 1
print('   English overlay: %d file(s) updated' % copied)

table = os.path.join(app, 'zh', 'executable-text.json')
applied = 0
if os.path.isfile(table):
    entries = json.load(open(table, encoding='utf-8-sig'))
    data = bytearray(open(exe, 'rb').read())
    for e in entries:
        ident = e.get('id', '')
        if '||' not in ident:
            continue
        name, _, off = ident.rpartition('||')
        if not name.lower().endswith('ago.exe'):
            continue
        try:
            off = int(off)
        except ValueError:
            continue
        src = e.get('原文', '').encode('utf-8')
        dst = e.get('译文', '').encode('utf-8')
        if not dst or len(dst) > len(src):
            continue
        if bytes(data[off:off+len(src)]) != src:
            continue
        data[off:off+len(src)] = dst + b'\x00' * (len(src) - len(dst))
        applied += 1
    if applied:
        open(exe, 'wb').write(data)
print('   in-binary English text: %d string(s) applied' % applied)
PY
  patch_ini
}

revert_patches() {
  say "reverting patches"
  python3 - "$APP" <<'PY'
import os, shutil, sys
app = sys.argv[1]
exe = os.path.join(app, 'ago.exe')
if os.path.isfile(exe + '.wine-orig'):
    shutil.copy2(exe + '.wine-orig', exe)
    print('   ago.exe restored')
rom_backup = os.path.join(app, 'rom.wine-orig')
rom = os.path.join(app, 'rom')
n = 0
if os.path.isdir(rom_backup):
    for base, _d, files in os.walk(rom_backup):
        for name in files:
            src = os.path.join(base, name)
            shutil.copy2(src, os.path.join(rom, os.path.relpath(src, rom_backup)))
            n += 1
print('   rom originals restored: %d' % n)
PY
}

# --- 4. game ----------------------------------------------------------------
GAME_PID=""

stop_game() {
  local pids
  pids=$(game_pids)
  if [ -n "$pids" ]; then
    say "closing the running game"
    for p in $pids; do kill -9 "$p" 2>/dev/null; done
  fi
  for p in $(wine_pids 'inject.exe -d -k fgohook'); do kill -9 "$p" 2>/dev/null; done
  GAME_PID=""
}

launch_game() {
  stop_game
  local shim_args=""
  [ -f "$APP/touchshim.dll" ] && shim_args="-k touchshim.dll"
  say "launching the game"
  ( cd "$APP" && \
    GALLIUM_DRIVER=zink \
    MESA_LOADER_DRIVER_OVERRIDE=zink \
    FGO_ZH_ENABLED=0 \
    SEGATOOLS_CONFIG_PATH="$RUNTIME_INI" \
    WAYLAND_DISPLAY= \
    nohup wine ${FGO_DESKTOP:+explorer /desktop=FGOA,1280x720} inject.exe -d ${shim_args} -k fgohook.dll ago.exe -hdtv720 -w \
      >"$LOGS/play-fgo.log" 2>&1 & )
  local i
  GAME_PID=""
  for ((i=0; i<60; i++)); do
    GAME_PID=$(game_pids | head -n1)
    [ -n "$GAME_PID" ] && break
    sleep 1
  done
  if [ -z "$GAME_PID" ]; then
    warn "the game did not start; see $LOGS/play-fgo.log"
    return 1
  fi
  dim "game pid $GAME_PID"
  return 0
}

window_for_pid() {
  python3 - "$1" <<'PY' 2>/dev/null
import subprocess, sys
pid = sys.argv[1]
for i in subprocess.run(['xdotool', 'search', '--name', 'FGOA'], capture_output=True, text=True).stdout.split():
    p = subprocess.run(['xprop', '-id', i, '_NET_WM_PID'], capture_output=True, text=True).stdout
    if pid in p:
        print(i); break
PY
}

screen_state() {
  local win="$1" tmp
  command -v magick >/dev/null || { echo unknown; return; }
  tmp=$(mktemp /tmp/fgo-screen.XXXXXX.png)
  if ! magick import -window "$win" "$tmp" 2>/dev/null; then rm -f "$tmp"; echo unknown; return; fi
  python3 - "$tmp" <<'PY'
import sys
try:
    from PIL import Image
    im = Image.open(sys.argv[1]).convert('RGB')
except Exception:
    print('unknown'); raise SystemExit
w, h = im.size
band = im.crop((0, 0, w, 30))
data = band.tobytes()
blue = sum(data[2::3]) / max(1, len(data) // 3)
# The arcade UI has a blue top bar (blue > 30); the error screen is black.
print('title' if blue > 30 else 'error')
PY
  rm -f "$tmp"
}

launch_with_retry() {
  local attempt=1 win state
  while :; do
    launch_game || return 1
    say "waiting for the title screen (about two minutes)"
    sleep 170
    win=$(window_for_pid "$GAME_PID")
    if [ -z "$win" ]; then warn "no game window found"; return 1; fi
    state=$(screen_state "$win")
    if [ "$state" = "title" ]; then
      say "game is up: 'Please touch the screen'"
      say "click the window to start; it is a normal movable window"
      dim "log: $LOGS/play-fgo.log"
      return 0
    fi
    if [ "$state" = "error" ] || [ "$state" = "unknown" ]; then
      if [ "$attempt" -lt "$MAX_ATTEMPTS" ]; then
        attempt=$((attempt+1))
        warn "AMDaemon did not come up cleanly (known Wine issue); restarting, attempt $attempt of $MAX_ATTEMPTS"
        stop_game; sleep 4; restart_artemis; continue
      fi
      warn "still not at the title after $MAX_ATTEMPTS attempts"
      dim "the game is running; try closing it and running this script again"
      return 1
    fi
    return 0
  done
}

status() {
  printf 'install : %s\n' "$INSTALL"
  for pair in "database:$DB_PORT" "title:$TITLE_PORT" "billing:$BILLING_PORT" "aimedb:$AIME_PORT"; do
    if port_open "${pair#*:}"; then printf '  %-8s up (%s)\n' "${pair%%:*}" "${pair#*:}"
    else printf '  %-8s down\n' "${pair%%:*}"; fi
  done
  local pid
  pid=$(game_pids | head -n1)
  [ -n "$pid" ] && printf '  game     running (pid %s)\n' "$pid" || printf '  game     not running\n'
}

case "${1:-}" in
  --stop)   stop_game ;;
  --status) status ;;
  --revert) stop_game; revert_patches ;;
  --help|-h) sed -n '2,21p' "$0" ;;
  "")       ensure_lan; start_db; start_artemis; wait_healthy; ensure_touchshim; apply_patches; launch_with_retry ;;
  *)        die "unknown option: $1 (try --help)" ;;
esac
