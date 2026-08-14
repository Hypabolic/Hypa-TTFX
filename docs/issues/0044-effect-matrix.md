# 0044 — Effect: `matrix`

**Labels:** `enhancement`, `ready-for-agent`

**Wave:** Wave 3 — heavy machinery (spanning trees, particles, clocks)  
**Reference:** `src/effects/matrix.rs` (659 lines, 13 options)  
**Inherited parity cases:** 6

## What to build

Port the `matrix` effect end-to-end: its option table, its registry entry, and the
effect itself, until its parity cases are byte-identical to the reference.

Transcribe from the Rust, not from the Python — the Rust has already resolved the
upstream semantics questions and its comments cite the upstream lines. Keep function
names and internal structure so a side-by-side diff is possible at review.

Read `docs/translation-checklist.md` for this file before starting.

### Inherited parity cases

| Case | Input | Arguments |
|---|---|---|
| `matrix-basic` | `basic.txt` | `matrix` |
| `matrix-custom` | `paragraph.txt` | `matrix --rain-time 2 --highlight-color ffffff --rain-color-gradient ff0000 00ff00 --rain-symbols a b c 1 2 3 --rain-fall-delay-range 1-5 --rain-column-delay-range 2-4 --symbol-swap-chance 0.05 --color-swap-chance 0.02 --resolve-delay 1 --final-gradient-stops ff0000 0000ff --final-gradient-steps 8 --final-gradient-frames 2 --final-gradient-direction vertical` |
| `matrix-dynamic` | `colored.txt` | `--existing-color-handling dynamic matrix --rain-time 1` |
| `matrix-short` | `basic.txt` | `matrix --rain-time 1` |
| `matrix-single` | `single.txt` | `matrix --rain-time 1` |
| `matrix-xterm-anchored` | `paragraph.txt` | `--xterm-colors --canvas-width 60 --canvas-height 20 --anchor-canvas c --anchor-text c matrix --rain-time 2` |

### Known traps in this effect

- **Clock-dependent.** Reads wall time for rain-phase transitions, so it is only reproducible under the virtual clock. With `--frame-rate 0` the virtual `dt` is `1/60` (not a division by zero) — if that guard is wrong, this effect's frame count is wrong.
- Float truncation at `matrix.rs:165`.
- RNG-indexed removal at `:221` (index drawn at `:220`).
- `remove(0)` at `:182, :228, :541, :546`.
- 13 options; 659 lines.

## Acceptance criteria

- [ ] All 6 `matrix-*` parity cases byte-identical to `ref_dump` at seeds 42 and 1337
- [ ] The same cases pass **unbounded to natural completion**, with matching total frame counts
- [ ] All 13 options are present with the reference's exact names, defaults, and validation
- [ ] The Unicode fixture case passes for this effect
- [ ] The effect's registry entry preserves the 37-name enumeration order
- [ ] Every trap listed above is addressed, with a comment citing the reference line
- [ ] AOT publish stays warning-free

## Blocked by

- 0011 — First effect end-to-end: `wipe`
