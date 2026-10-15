#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
LOG_FILE="${TMPDIR:-/tmp}/hmistudio-server.log"
HEALTH_URL="http://127.0.0.1:5080/api/health"

# Reuse a server that is already running after a workspace restart.
if curl --silent --fail --max-time 2 "$HEALTH_URL" >/dev/null 2>&1; then
  echo "HMIStudio is already running at port 5080."
  exit 0
fi

cd "$ROOT"
export HMISTUDIO_URLS="http://0.0.0.0:5080"
nohup dotnet run --project src/HMI.Server --no-launch-profile >"$LOG_FILE" 2>&1 &
SERVER_PID=$!
echo "$SERVER_PID" > "${TMPDIR:-/tmp}/hmistudio-server.pid"

for _ in $(seq 1 45); do
  if curl --silent --fail --max-time 2 "$HEALTH_URL" >/dev/null 2>&1; then
    echo "HMIStudio is ready on port 5080. Forwarded browser preview should open automatically."
    echo "Server log: $LOG_FILE"
    exit 0
  fi
  if ! kill -0 "$SERVER_PID" 2>/dev/null; then
    echo "HMIStudio server exited during startup. Recent log:"
    tail -n 80 "$LOG_FILE" || true
    exit 1
  fi
  sleep 2
done

echo "HMIStudio did not become ready. Recent log:"
tail -n 80 "$LOG_FILE" || true
exit 1
