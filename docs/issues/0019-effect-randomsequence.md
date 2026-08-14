# 0019 — Effect: `randomsequence`

**Labels:** `enhancement`, `ready-for-agent`

**Wave:** Wave 1 — motion and scene basics  
**Reference:** `src/effects/random_sequence.rs` (194 lines, 5 options)  
**Inherited parity cases:** 8

## What to build

Port the `randomsequence` effect end-to-end: its option table, its registry entry, and the
effect itself, until its parity cases are byte-identical to the reference.

Transcribe from the Rust, not from the Python — the Rust has already resolved the
upstream semantics questions and its comments cite the upstream lines. Keep function
names and internal structure so a side-by-side diff is possible at review.

Read `docs/translation-checklist.md` for this file before starting.

### Inherited parity cases

| Case | Input | Arguments |
|---|---|---|
| `randomsequence-anchored` | `paragraph.txt` | `--canvas-width 60 --canvas-height 20 --anchor-canvas c --anchor-text c randomsequence --speed 0.03` |
| `randomsequence-basic` | `basic.txt` | `randomsequence` |
| `randomsequence-colored-always` | `colored.txt` | `--existing-color-handling always randomsequence` |
| `randomsequence-colored-dynamic` | `colored.txt` | `--existing-color-handling dynamic randomsequence` |
| `randomsequence-fast` | `basic.txt` | `randomsequence --speed 0.2 --final-gradient-frames 3` |
| `randomsequence-nocolor` | `basic.txt` | `--no-color randomsequence` |
| `randomsequence-single` | `single.txt` | `randomsequence` |
| `randomsequence-xterm` | `paragraph.txt` | `--xterm-colors randomsequence --speed 0.05` |

### Known traps in this effect

- Float truncation at `random_sequence.rs:72` — `(speed * input_len as f64) as i64`. This sets the per-tick character count, so rounding instead of truncating changes the draw sequence.

## Acceptance criteria

- [ ] All 8 `randomsequence-*` parity cases byte-identical to `ref_dump` at seeds 42 and 1337
- [ ] The same cases pass **unbounded to natural completion**, with matching total frame counts
- [ ] All 5 options are present with the reference's exact names, defaults, and validation
- [ ] The Unicode fixture case passes for this effect
- [ ] The effect's registry entry preserves the 37-name enumeration order
- [ ] Every trap listed above is addressed, with a comment citing the reference line
- [ ] AOT publish stays warning-free

## Blocked by

- 0011 — First effect end-to-end: `wipe`
