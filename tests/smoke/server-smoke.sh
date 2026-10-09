#!/usr/bin/env bash
#
# Smoke test against the real game: boots a throwaway dedicated server with the Release build of the mod and the
# test-only self-test mod (tests/selftest), which checks the filter rule, filter saving and the trash slots against
# every item and block in the game. Fails unless the mod loads cleanly and the self-test passes.
#
# Usage: VINTAGE_STORY=/path/to/game tests/smoke/server-smoke.sh   (keeps the work dir with KEEP=1)
#
set -euo pipefail

: "${VINTAGE_STORY:?set VINTAGE_STORY to the game directory (the one containing VintagestoryServer.dll)}"
repo="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
work="$(mktemp -d -t vstc-smoke.XXXXXX)"
timeout_s="${SMOKE_TIMEOUT:-240}"
# Not the default 42420, so it can run next to a real server; bound to localhost only
port="${SMOKE_PORT:-42499}"

cleanup() {
    [[ -n "${keepalive:-}" ]] && kill "$keepalive" 2>/dev/null || true
    [[ -n "${server:-}" ]] && kill "$server" 2>/dev/null || true
    if [[ "${KEEP:-0}" == 1 ]]; then echo "work dir kept: $work"; else rm -rf "$work"; fi
}
trap cleanup EXIT

echo "== Building Release"
dotnet build "$repo/VsTrashcan.csproj" -c Release -nologo -v quiet
dotnet build "$repo/tests/selftest/VsTrashcanSelfTest.csproj" -c Release -nologo -v quiet
mkdir -p "$work/mods" "$work/data"
cp "$repo/bin/Release/VsTrashcan.zip" "$work/mods/"
cp "$repo/tests/selftest/bin/Release/VsTrashcanSelfTest.zip" "$work/mods/"

echo "== Starting dedicated server (timeout ${timeout_s}s)"
mkfifo "$work/stdin"
sleep infinity > "$work/stdin" & keepalive=$!   # holds the fifo open so the server console doesn't see EOF
dotnet "$VINTAGE_STORY/VintagestoryServer.dll" --dataPath "$work/data" --addModPath "$work/mods" --ip 127.0.0.1 --port "$port" \
    < "$work/stdin" > "$work/console.log" 2>&1 & server=$!

for ((i = 0; i < timeout_s; i++)); do
    grep -q "Dedicated Server now running" "$work/console.log" 2>/dev/null && break
    kill -0 "$server" 2>/dev/null || { echo "FAIL: server exited during startup"; tail -30 "$work/console.log"; exit 1; }
    sleep 1
done
if ((i >= timeout_s)); then echo "FAIL: server did not finish starting"; tail -30 "$work/console.log"; exit 1; fi
echo "   up after ${i}s, stopping"
echo "/stop" > "$work/stdin"
wait "$server" || true
server=

log="$work/data/Logs/server-main.log"
fail=0
check() {
    if grep -q -- "$2" "$log"; then echo "PASS: $1"; else echo "FAIL: $1 (no '$2' in server-main.log)"; fail=1; fi
}
check "mod loaded"                 "Mod 'VsTrashcan.zip' (vstrashcan)"
check "server side started"        "VsTrashcan: ready"
check "self-test passed"           "VsTrashcan self-test: PASSED"
grep -h "VsTrashcan self-test" "$log" | sed 's/^/   /'

if grep -E "\[(Error|Fatal)\]" "$log" "$work/data/Logs/server-debug.log"; then
    echo "FAIL: errors in server logs (above)"
    fail=1
else
    echo "PASS: no errors in server logs"
fi

exit "$fail"
