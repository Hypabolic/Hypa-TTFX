# 0049 — CLI surface: random-effect, filtering, completions

**Labels:** `enhancement`, `ready-for-agent`

## What to build

The remaining user-facing CLI surface, plus the two behaviors that have **zero coverage on
either side** of the parity harness today.

- **`--random-effect` / `-R`**, with `--include-effects` and `--exclude-effects` filtering
  (mutually exclusive with each other as upstream has them). Selection is
  `ChoiceIndex(names.Count)` over the registry, so a given seed must select the **same effect**
  as the reference — which means the 37-name enumeration order is part of the contract.
  Reproduce the quirk that a randomly selected effect runs with **pure default** config;
  effect-specific CLI args are ignored on that path.
- **`--print-completion bash|zsh`** — the reference generates these from its clap model via
  `clap_complete`. With no packages these become two hand-written script templates driven by
  the registry. Checked: `cli_corpus.sh` asserts *nothing* about completion output, so exact
  parity is not required — but that also means completions are currently untested on both
  sides, so this issue adds the coverage.
- **`--version` / `-v`**, replacing any default version flag so the short form matches.

### The two uncovered behaviors

Grepping the reference's `tools/` confirms `--random-effect` appears in no harness at all, and
`resize_behavior.py` checks that a resize *restarts*, not what the RNG did across it. Both are
places where a mistake produces output that looks entirely plausible.

1. **Registry enumeration order.** For ~20 seeds, `--random-effect` must select the same effect
   as the reference. Compare the frame streams byte-for-byte — a differing selection shows up
   immediately as a total mismatch.
2. **RNG continuity across a resize rebuild.** The obvious test does not work: the inherited
   driver fires `SIGWINCH` after a wall-clock delay (`resize_behavior.py:62-80`), so the two
   binaries may legitimately have emitted different frame counts before the signal, leaving
   their RNG states different *even when both are correct*. Comparing total output length
   instead would miss a reset. This needs a **deterministic trigger**: have `--parity-dump`
   accept a "rebuild after frame N" hook exercising the same rebuild path without a real
   signal, and compare the full stream. Real `SIGWINCH` delivery is tested separately as
   behavior (issue 0012), not as byte parity.

## Acceptance criteria

- [ ] `--random-effect` selects the same effect as the reference for ~20 seeds, verified by
      byte-identical frame streams
- [ ] A randomly selected effect runs with pure default config; effect args are ignored
- [ ] `--include-effects` / `--exclude-effects` filter correctly and conflict as upstream does
- [ ] Filtering to an empty set errors with exit 1
- [ ] RNG continuity across a rebuild is verified through a deterministic frame-N trigger, not a
      timed signal
- [ ] `--print-completion bash` and `zsh` emit non-empty scripts that pass `bash -n` / `zsh -n`
      and mention all 37 effect names (the zsh check skips with a notice if zsh is absent)
- [ ] `--version` / `-v` prints and exits 0
- [ ] `cli_corpus.sh` passes in full

## Blocked by

- 0011 — First effect end-to-end: `wipe`
- 0012 — Signals and the resize debounce
