#!/usr/bin/env bash
# CLI contract corpus: exit codes and stream routing
# (0 success; 1 runtime errors — no-input/file errors on STDOUT,
# unsupported-ANSI on STDERR; 2 usage errors).
set -u
cd "$(dirname "$0")/../.."
RUST=./artifacts/ttfx
export COLUMNS=80 LINES=24
export PATH="/usr/local/bin:${PATH:-}"
export DOTNET_ROOT="${DOTNET_ROOT:-/usr/local/Cellar/dotnet/10.0.400/libexec}"
if [ ! -x "$RUST" ]; then
  echo "cli_corpus: $RUST is missing. Run bin/build first." >&2
  exit 1
fi
pass=0; fail=0; failed=()

check() {
  local name="$1" expected_rc="$2"; shift 2
  "$@" > /tmp/claude-cli-out 2>/tmp/claude-cli-err
  local rc=$?
  if [ "$rc" -eq "$expected_rc" ]; then pass=$((pass+1)); else fail=$((fail+1)); failed+=("$name rc=$rc want=$expected_rc"); fi
}

# usage errors -> 2
check unknown-subcommand 2 bash -c "printf x | $RUST nosucheffect"
check unknown-option 2 bash -c "printf x | $RUST --no-such-option wipe"
check bad-value 2 bash -c "printf x | $RUST --frame-rate -- -1 wipe"
check bad-tab-width 2 bash -c "printf x | $RUST --tab-width 0 wipe"
check root-opt-after-subcommand 2 bash -c "printf x | $RUST wipe --no-color"
check include-exclude-conflict 2 bash -c "printf x | $RUST -R --include-effects a --exclude-effects b"
check bad-easing 2 bash -c "printf x | $RUST wipe --wipe-ease not_an_ease"

# runtime errors -> 1
check no-input 1 bash -c "printf '' | $RUST --parity-dump --seed 1 wipe"
check whitespace-input 1 bash -c "printf '  \n  ' | $RUST --parity-dump --seed 1 wipe"
check missing-file 1 bash -c "$RUST -i /nonexistent/file wipe"
check no-effect 1 bash -c "printf x | $RUST"
check bad-ansi-input 1 bash -c "printf 'a\x1b[2Jb' | $RUST --parity-dump --seed 1 wipe"
check bad-utf8-file 1 bash -c "printf '\xff\xfe' > /tmp/claude-bad-utf8; $RUST -i /tmp/claude-bad-utf8 wipe"

# stream routing
printf '' | $RUST --parity-dump --seed 1 wipe > /tmp/claude-cli-out 2>/tmp/claude-cli-err
grep -q "NO INPUT." /tmp/claude-cli-out && pass=$((pass+1)) || { fail=$((fail+1)); failed+=("no-input-on-stdout"); }
$RUST -i /nonexistent/file wipe > /tmp/claude-cli-out 2>/tmp/claude-cli-err
[ -s /tmp/claude-cli-out ] && pass=$((pass+1)) || { fail=$((fail+1)); failed+=("file-error-on-stdout"); }
printf 'a\x1b[2Jb' | $RUST --parity-dump --seed 1 wipe > /tmp/claude-cli-out 2>/tmp/claude-cli-err
grep -qi "unsupported ansi" /tmp/claude-cli-err && pass=$((pass+1)) || { fail=$((fail+1)); failed+=("ansi-error-on-stderr"); }

# success -> 0
check success 0 bash -c "printf 'hi' | $RUST --parity-dump --seed 1 --max-frames 5 wipe"
check success-negative-canvas 0 bash -c "printf 'hi' | $RUST --canvas-width -1 --parity-dump --seed 1 --max-frames 2 wipe"
check success-multi-stops 0 bash -c "printf 'hi' | $RUST --parity-dump --seed 1 --max-frames 2 wipe --final-gradient-stops ff0000 00ff00 0000ff"

# --version / -v
check version-long 0 bash -c "$RUST --version | grep -q '^ttfx '"
check version-short 0 bash -c "$RUST -v | grep -q '^ttfx '"

# --print-completion: non-empty, syntax-valid, mentions all 37 effects
$RUST --print-completion bash > /tmp/claude-cli-bash 2>/tmp/claude-cli-err
if [ -s /tmp/claude-cli-bash ] && bash -n /tmp/claude-cli-bash 2>/dev/null; then
  pass=$((pass+1))
else
  fail=$((fail+1)); failed+=("bash-completion-syntax")
fi
for effect in beams binarypath blackhole bouncyballs bubbles burn colorshift crumble decrypt errorcorrect expand fireworks highlight laseretch matrix middleout orbittingvolley overflow pour print rain randomsequence rings scattered slice slide smoke spotlights spray swarm sweep synthgrid thunderstorm unstable vhstape waves wipe; do
  grep -q "$effect" /tmp/claude-cli-bash && pass=$((pass+1)) || { fail=$((fail+1)); failed+=("bash-missing-$effect"); }
done
$RUST --print-completion zsh > /tmp/claude-cli-zsh 2>/tmp/claude-cli-err
if [ -s /tmp/claude-cli-zsh ]; then
  if command -v zsh >/dev/null 2>&1; then
    if zsh -n /tmp/claude-cli-zsh 2>/dev/null; then pass=$((pass+1)); else fail=$((fail+1)); failed+=("zsh-completion-syntax"); fi
  else
    echo "cli_corpus: zsh not installed; skipping zsh -n" >&2
    pass=$((pass+1))
  fi
  for effect in beams binarypath blackhole bouncyballs bubbles burn colorshift crumble decrypt errorcorrect expand fireworks highlight laseretch matrix middleout orbittingvolley overflow pour print rain randomsequence rings scattered slice slide smoke spotlights spray swarm sweep synthgrid thunderstorm unstable vhstape waves wipe; do
    grep -q "$effect" /tmp/claude-cli-zsh && pass=$((pass+1)) || { fail=$((fail+1)); failed+=("zsh-missing-$effect"); }
  done
else
  fail=$((fail+1)); failed+=("zsh-completion-empty")
fi

# --random-effect filtering
check random-empty-filter 1 bash -c "printf x | $RUST --random-effect --include-effects nosucheffect"
check random-filter 0 bash -c "printf 'hi' | $RUST --parity-dump --seed 42 --max-frames 3 --random-effect --include-effects wipe beams"

echo "cli corpus: $pass passed, $fail failed"
if [ $fail -gt 0 ]; then printf 'FAILED: %s\n' "${failed[@]}"; exit 1; fi
