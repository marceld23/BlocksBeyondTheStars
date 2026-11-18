#!/usr/bin/env bash
# Watchdog for the CI test step (see .github/workflows/tests.yml).
#
#   bash scripts/ci-test-watchdog.sh <limit-seconds> <done-file> &
#
# Why: a shard of the full suite has twice sat in its Test step for 40 minutes (normal: 10-15). It was not a single
# stuck test - the hang detection (--blame-hang, an inactivity timer) never fired, so tests still trickled through -
# and the process did not react to a cancel for minutes. Cancelling from outside is useless: a cancelled job leaves
# no log and uploads nothing, so the reason stayed unknown both times.
#
# What it does:
#   * every minute one line into the step's log: how long the step has run, the test host's memory and CPU, the
#     runner's free memory, swap and load. In a normal run that is a dozen lines; in a crawl it shows what ran out.
#   * when the step has run <limit-seconds>: the runner's busiest processes, the managed stacks of every test host
#     (dotnet-stack, installed only now), then it kills the test hosts. `dotnet test` then ends by itself with an
#     error, names the tests that were running (the blame collector) and writes the .trx of what had finished - the
#     job fails normally, so its log and its artifacts survive.
#   * it leaves quietly as soon as <done-file> exists (the step touches it when the tests are through).
set -u

# It stops EVERY test host on the machine - fine on a CI runner that exists for one job, never on a developer's
# computer, where another checkout's tests may be running.
if [ "${GITHUB_ACTIONS:-}" != "true" ]; then
  echo "ci-test-watchdog.sh runs on the CI runner only: it stops every test host on the machine." >&2
  exit 2
fi

limit="$1"
done_file="$2"
start=$(date +%s)

stamp() {
  local s=$(( $(date +%s) - start ))
  printf '%02d:%02d' $(( s / 60 )) $(( s % 60 ))
}

hosts() {
  pgrep -f 'testhost' 2>/dev/null || true
}

beat() {
  local pids rss cpu mem swap load
  pids=$(hosts | tr '\n' ' ')
  rss=0
  cpu=0
  if [ -n "$pids" ]; then
    # shellcheck disable=SC2086
    rss=$(ps -o rss= -p $pids 2>/dev/null | awk '{s += $1} END {printf "%d", s / 1024}')
    # shellcheck disable=SC2086
    cpu=$(ps -o pcpu= -p $pids 2>/dev/null | awk '{s += $1} END {printf "%d", s}')
  fi
  mem=$(free -m 2>/dev/null | awk '/^Mem:/ {printf "used %d MB, available %d MB", $3, $7}')
  swap=$(free -m 2>/dev/null | awk '/^Swap:/ {printf "%d MB", $3}')
  load=$(cut -d' ' -f1-3 /proc/loadavg 2>/dev/null || echo '?')
  echo "[watchdog $(stamp)] test host: ${rss} MB, ${cpu}% CPU | runner memory: ${mem:-?}, swap used ${swap:-?} | load ${load}"
}

next_beat=$(( start + 60 ))
while :; do
  [ -e "$done_file" ] && exit 0
  now=$(date +%s)
  if [ "$now" -ge $(( start + limit )) ]; then
    break
  fi
  if [ "$now" -ge "$next_beat" ]; then
    beat
    next_beat=$(( now + 60 ))
  fi
  sleep 5
done

echo "::error::The test step has run $(( limit / 60 )) minutes. The watchdog prints what the test host is doing and stops it, so the step fails by itself and this log survives."
beat
echo "===== busiest processes ====="
ps -eo pid,ppid,etimes,pcpu,pmem,rss,nlwp,args --sort=-pcpu 2>/dev/null | cut -c1-220 | head -n 12
echo "===== memory ====="
free -m 2>/dev/null || true

pids=$(hosts)
if [ -z "$pids" ]; then
  echo "No test host process is running - the step hangs outside the tests."
  exit 0
fi

tool="$HOME/.dotnet/tools/dotnet-stack"
if [ ! -x "$tool" ]; then
  timeout 120 dotnet tool install --global dotnet-stack >/dev/null 2>&1 || echo "(dotnet-stack could not be installed - no stacks)"
fi

for pid in $pids; do
  echo "===== managed stacks of test host $pid (first 3000 lines) ====="
  if [ -x "$tool" ]; then
    timeout 180 "$tool" report -p "$pid" 2>&1 | head -n 3000 || true
  fi
done

echo "===== stopping the test host(s): $(echo "$pids" | tr '\n' ' ') ====="
# shellcheck disable=SC2086
kill -9 $pids 2>/dev/null || true
exit 0
