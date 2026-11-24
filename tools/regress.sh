#!/usr/bin/env bash
# Full regression: unit and integration tests, every scene self-test headless, two self-tests
# with a window, plc:50 and api against the fake PLC, and plc:50 against the built-in test PLC. Takes about 18 minutes.
#
#   bash tools/regress.sh            (Git Bash, from anywhere)
#
# Prints one PASS/FAIL line per test. Anything with FAILED or FAIL needs a look.
cd "$(dirname "$0")/.." || exit 1
G="${GODOT:-../tools/godot-4.7.2/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe}"
TMP="${TMPDIR:-/tmp}/factorysim-regress"
mkdir -p "$TMP"

echo "== dotnet build"; dotnet build FactorySim.sln 2>&1 | grep -E "rror\(s\)"
echo "== dotnet test"; dotnet test FactorySim.sln 2>&1 | grep -E "Passed!|Failed!|Failed "
if [ -f tools/public/export_public.py ]; then     # optional local check
  echo "== public check"; py -X utf8 tools/public/export_public.py --check 2>&1 | grep -E "LEAK|PUBLIC CHECK"
fi

for m in blocks snap decor timescale trace stats bidir tank robot assembly sensors transport crane machining ui palletizer lift station heavy sorters panel sort:20 controls editor line; do
  echo "== $m"
  "$G" --headless --path src/FactorySim.App -- --selftest=$m 2>&1 | grep -E "SELFTEST RESULT|FAILED"
done

echo "== plc:50 and api (fake PLC)"
py tools/fake_plc/fake_plc.py --mode sorter --port 5020 > /dev/null 2>&1 & P=$!
sleep 2
"$G" --headless --path src/FactorySim.App -- --selftest=plc:50 --plc-port=5020 2>&1 | grep -E "SELFTEST RESULT|FAILED|low=|errors"
echo "== api"
"$G" --headless --path src/FactorySim.App -- --selftest=api --plc-port=5020 2>&1 | grep -E "SELFTEST RESULT|FAILED"
kill $P

echo "== plc:50 (built-in test PLC, no Python)"
"$G" --headless --path src/FactorySim.App -- --selftest=plc:50 --test-plc 2>&1 | grep -E "SELFTEST RESULT|FAILED|low=|errors|test PLC"

# With a window: timing differs from headless, and screenshots only work here.
for m in blocks snap decor ui editor machining crane assembly robot timescale trace; do
  echo "== $m (window)"
  "$G" --path src/FactorySim.App -- --selftest=$m "--screenshot=$TMP/$m.png" --screenshot-at=9999 2>&1 | grep -E "SELFTEST RESULT|FAILED"
done
echo "== done (screenshots in $TMP)"
