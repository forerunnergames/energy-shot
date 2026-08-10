#!/usr/bin/env bash
# Automated multiplayer playtest: launches a host + shooter + victim headless &
# verifies join, replication, movement, kills, respawn, spawn armor, fire-rate
# cap & full-auto. Requires GODOT_BIN. Exits 0 only if all three roles pass.
set -u

GODOT_BIN="${GODOT_BIN:?Set GODOT_BIN to the Godot .NET binary}"
DIR="$(cd "$(dirname "$0")/../.." && pwd)"
LOGS="$DIR/reports/playtest"
mkdir -p "$LOGS"

echo "== Importing & building =="
"$GODOT_BIN" --headless --path "$DIR" --import > "$LOGS/import.log" 2>&1
dotnet build "$DIR" > "$LOGS/build.log" 2>&1 || { echo "BUILD FAILED"; tail -20 "$LOGS/build.log"; exit 1; }

echo "== Launching host + 2 clients =="
"$GODOT_BIN" --headless --path "$DIR" -- --playtest host > "$LOGS/host.log" 2>&1 &
HOST=$!
sleep 5
"$GODOT_BIN" --headless --path "$DIR" -- --playtest victim > "$LOGS/victim.log" 2>&1 &
VICTIM=$!
sleep 3
"$GODOT_BIN" --headless --path "$DIR" -- --playtest shooter > "$LOGS/shooter.log" 2>&1 &
SHOOTER=$!

# Watchdog: kill everything if the scenario hangs.
( sleep 300; kill $HOST $VICTIM $SHOOTER 2>/dev/null ) &
WATCHDOG=$!

FAIL=0
for role in SHOOTER VICTIM HOST; do
  pid=${!role}
  wait "$pid"
  code=$?
  echo "$role exited with $code"
  [ "$code" -ne 0 ] && FAIL=1
done

kill $WATCHDOG 2>/dev/null
wait $WATCHDOG 2>/dev/null

if [ "$FAIL" -ne 0 ]; then
  echo "== PLAYTEST FAILED - logs =="
  for f in host shooter victim; do
    echo "--- $f.log (last 40 lines) ---"
    tail -40 "$LOGS/$f.log"
  done
  exit 1
fi

echo "== PLAYTEST PASSED (host + shooter + victim) =="
