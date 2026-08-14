#!/usr/bin/env bash
# 0003 subset of the CLI contract: parser + stream routing that do not need
# wipe animation, --m0-dump frames, or a full ANSI input parser.
# Do not edit tools/tests/cli_corpus.sh (verbatim copy).
set -u
cd "$(dirname "$0")/../.."
BIN=./artifacts/ttfx
if [ ! -x "$BIN" ]; then
  echo "cli_parser_corpus: $BIN is missing. Run bin/build first." >&2
  exit 1
fi
export COLUMNS=80 LINES=24
pass=0; fail=0; failed=()

check() {
  local name="$1" expected_rc="$2"; shift 2
  "$@" > /tmp/hypa-cli-out 2>/tmp/hypa-cli-err
  local rc=$?
  if [ "$rc" -eq "$expected_rc" ]; then pass=$((pass+1)); else fail=$((fail+1)); failed+=("$name rc=$rc want=$expected_rc"); fi
}

# usage errors -> 2
check unknown-subcommand 2 bash -c "printf x | $BIN nosucheffect"
check unknown-option 2 bash -c "printf x | $BIN --no-such-option wipe"
check bad-value 2 bash -c "printf x | $BIN --frame-rate -- -1 wipe"
check bad-tab-width 2 bash -c "printf x | $BIN --tab-width 0 wipe"
check root-opt-after-subcommand 2 bash -c "printf x | $BIN wipe --no-color"
check include-exclude-conflict 2 bash -c "printf x | $BIN -R --include-effects a --exclude-effects b"
check bad-easing 2 bash -c "printf x | $BIN wipe --wipe-ease not_an_ease"

# runtime errors -> 1
check no-input 1 bash -c "printf '' | $BIN --parity-dump --seed 1 wipe"
check whitespace-input 1 bash -c "printf '  \n  ' | $BIN --parity-dump --seed 1 wipe"
check missing-file 1 bash -c "$BIN -i /nonexistent/file wipe"
check no-effect 1 bash -c "printf x | $BIN"
check bad-ansi-input 1 bash -c "printf 'a\x1b[2Jb' | $BIN --parity-dump --seed 1 wipe"
check bad-utf8-file 1 bash -c "printf '\xff\xfe' > /tmp/hypa-bad-utf8; $BIN -i /tmp/hypa-bad-utf8 wipe"
# Effect resolution precedes ANSI validation (main.rs). Combined case must
# report the missing effect, not unsupported ANSI.
check ansi-no-effect 1 bash -c "printf 'a\x1b[2Jb' | $BIN"

# stream routing
printf '' | $BIN --parity-dump --seed 1 wipe > /tmp/hypa-cli-out 2>/tmp/hypa-cli-err
grep -q "NO INPUT." /tmp/hypa-cli-out && pass=$((pass+1)) || { fail=$((fail+1)); failed+=("no-input-on-stdout"); }
$BIN -i /nonexistent/file wipe > /tmp/hypa-cli-out 2>/tmp/hypa-cli-err
[ -s /tmp/hypa-cli-out ] && pass=$((pass+1)) || { fail=$((fail+1)); failed+=("file-error-on-stdout"); }
printf 'a\x1b[2Jb' | $BIN --parity-dump --seed 1 wipe > /tmp/hypa-cli-out 2>/tmp/hypa-cli-err
grep -qi "unsupported ansi" /tmp/hypa-cli-err && pass=$((pass+1)) || { fail=$((fail+1)); failed+=("ansi-error-on-stderr"); }
printf 'a\x1b[2Jb' | $BIN > /tmp/hypa-cli-out 2>/tmp/hypa-cli-err
if grep -q "No effect specified." /tmp/hypa-cli-err && ! grep -qi "unsupported ansi" /tmp/hypa-cli-err; then
  pass=$((pass+1))
else
  fail=$((fail+1)); failed+=("ansi-no-effect-reports-missing-effect")
fi

# parse-success paths that do not need wipe animation (0003 exits 0 with no frames)
check success 0 bash -c "printf 'hi' | $BIN --parity-dump --seed 1 --max-frames 5 wipe"
check success-negative-canvas 0 bash -c "printf 'hi' | $BIN --canvas-width -1 --parity-dump --seed 1 --max-frames 2 wipe"

echo "cli parser corpus: $pass passed, $fail failed"
if [ $fail -gt 0 ]; then printf 'FAILED: %s\n' "${failed[@]}"; exit 1; fi
