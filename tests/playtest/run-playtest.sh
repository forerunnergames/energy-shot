#!/usr/bin/env bash
# Automated multiplayer playtest: launches a host + shooter + victim headless &
# verifies join, replication, movement, kills, respawn, spawn armor, fire-rate
# cap & full-auto. Requires GODOT_BIN. Exits 0 only if all three roles pass.
set -u

GODOT_BIN="${GODOT_BIN:?Set GODOT_BIN to the Godot .NET binary}"
DIR="$(cd "$(dirname "$0")/../.." && pwd)"
LOGS="$DIR/reports/playtest"
mkdir -p "$LOGS"

# Is anything already bound to this UDP port? Whichever tool the machine has wins;
# no tool at all just means every candidate looks free (the PID seed still keeps
# concurrent runs apart).
port_in_use() {
  if command -v lsof > /dev/null 2>&1; then
    lsof -nP -iUDP:"$1" > /dev/null 2>&1
    return $?
  fi
  if command -v ss > /dev/null 2>&1; then
    ss -lun 2>/dev/null | grep -qE "[:.]$1([[:space:]]|\$)"
    return $?
  fi
  netstat -an 2>/dev/null | grep -qE "^udp.*[.:]$1([[:space:]]|\$)"
}

# Issue #144: this used to be one hardcoded port, so two runs on one machine (e.g.
# parallel agents in separate worktrees) joined each OTHER's host - seen as RPC
# checksum mismatches & instances landing in the wrong game. 49152-65535 is the
# dynamic/private range & $$ is unique among live runs, so two runs never start
# from the same candidate; anything already listening is stepped over.
pick_port() {
  candidate=$((49152 + $$ % 16000))
  attempt=0
  while port_in_use "$candidate" && [ "$attempt" -lt 200 ]; do
    candidate=$((49152 + (candidate - 49152 + 1) % 16000))
    attempt=$((attempt + 1))
  done
  echo "$candidate"
}

# Overridable so a run can be pinned to a known port (firewall rules, tcpdump).
PORT="${PLAYTEST_PORT:-$(pick_port)}"

# Issue #140: each instance must leave positive evidence that it really ran - it
# started the scenario, it asserted something, & it finished. A crashed --import
# step once let instances exit 0 without ever loading the main scene, and a silent
# log was reported as a PASS.
verify_log() {
  role="$1"
  log="$LOGS/$role.log"
  [ -s "$log" ] || { echo "$role: log is missing or empty - the instance never ran"; return 1; }
  grep -q "PLAYTEST: starting role \[$role\]" "$log" || { echo "$role: no 'starting role' line - the main scene never loaded"; return 1; }
  grep -q "PLAYTEST OK" "$log" || { echo "$role: no 'PLAYTEST OK' assertion - the scenario never ran"; return 1; }
  grep -q "PLAYTEST PASS \[$role\]" "$log" || { echo "$role: no 'PLAYTEST PASS' marker - the role never finished"; return 1; }
  return 0
}

echo "== Importing & building =="
# A failed import is a failed run (issue #140): the instances launched afterwards
# can exit 0 with a half-imported project & nothing would have been tested.
"$GODOT_BIN" --headless --path "$DIR" --import > "$LOGS/import.log" 2>&1 || { echo "IMPORT FAILED"; tail -20 "$LOGS/import.log"; exit 1; }
dotnet build "$DIR" > "$LOGS/build.log" 2>&1 || { echo "BUILD FAILED"; tail -20 "$LOGS/build.log"; exit 1; }

echo "== Launching host + 2 clients on port $PORT =="
# Admin messages (issue #158): the host runs with the operator file channel & a
# version file; the driver writes an announcement mid-run & every role asserts it.
ADMIN_FILE="$LOGS/admin-message"
VERSION_FILE="$LOGS/server-version"
: > "$ADMIN_FILE"
echo "v9.9.9-playtest" > "$VERSION_FILE"
"$GODOT_BIN" --headless --path "$DIR" -- --playtest host --port "$PORT" --admin-message-file "$ADMIN_FILE" --version-file "$VERSION_FILE" > "$LOGS/host.log" 2>&1 &
HOST=$!
sleep 5
"$GODOT_BIN" --headless --path "$DIR" -- --playtest victim --port "$PORT" > "$LOGS/victim.log" 2>&1 &
VICTIM=$!
sleep 3
"$GODOT_BIN" --headless --path "$DIR" -- --playtest shooter --port "$PORT" > "$LOGS/shooter.log" 2>&1 &
SHOOTER=$!

# Watchdog: kill everything if the scenario hangs.
( sleep 600; kill $HOST $VICTIM $SHOOTER 2>/dev/null ) &   # 600s: the end-of-run coverage phases (2026-08-21 features) added ~2 min
WATCHDOG=$!

FAIL=0
for role in shooter victim host; do
  case "$role" in
    shooter) pid=$SHOOTER ;;
    victim) pid=$VICTIM ;;
    host) pid=$HOST ;;
  esac
  wait "$pid"
  code=$?
  echo "$role exited with $code"
  [ "$code" -ne 0 ] && FAIL=1
  # Exit 0 is not proof the role ran (issue #140); the log has to show it did.
  verify_log "$role" || FAIL=1
done

kill $WATCHDOG 2>/dev/null
wait $WATCHDOG 2>/dev/null

if [ "$FAIL" -ne 0 ]; then
  echo "== PLAYTEST FAILED (port $PORT) - logs =="
  for f in host shooter victim; do
    echo "--- $f.log (last 40 lines) ---"
    tail -40 "$LOGS/$f.log" 2>/dev/null
  done
  exit 1
fi

echo "== PLAYTEST PASSED (host + shooter + victim) =="
