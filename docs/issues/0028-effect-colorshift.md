# 0028 — Effect: `colorshift`

**Labels:** `enhancement`, `ready-for-agent`

**Wave:** Wave 2 — gradients, synced scenes, sequence easers  
**Reference:** `src/effects/colorshift.rs` (275 lines, 12 options)  
**Inherited parity cases:** 6

## What to build

Port the `colorshift` effect end-to-end: its option table, its registry entry, and the
effect itself, until its parity cases are byte-identical to the reference.

Transcribe from the Rust, not from the Python — the Rust has already resolved the
upstream semantics questions and its comments cite the upstream lines. Keep function
names and internal structure so a side-by-side diff is possible at review.

Read `docs/translation-checklist.md` for this file before starting.

### Inherited parity cases

| Case | Input | Arguments |
|---|---|---|
| `colorshift-basic` | `basic.txt` | `colorshift` |
| `colorshift-custom` | `paragraph.txt` | `colorshift --gradient-stops ff0000 00ff00 0000ff --gradient-steps 6 --gradient-frames 3 --no-loop --travel-direction horizontal --reverse-travel-direction --cycles 1 --final-gradient-direction diagonal` |
| `colorshift-dynamic` | `colored.txt` | `--existing-color-handling dynamic colorshift --cycles 1` |
| `colorshift-notravel` | `paragraph.txt` | `colorshift --no-travel --cycles 2 --skip-final-gradient` |
| `colorshift-vertical` | `basic.txt` | `colorshift --travel-direction vertical --cycles 1` |
| `colorshift-xterm-anchored` | `paragraph.txt` | `--xterm-colors --canvas-width 60 --canvas-height 20 --anchor-canvas c --anchor-text c colorshift --cycles 1` |

### Known traps in this effect

- Float truncation at `colorshift.rs:167` — `(spectrum.len() as f64 * direction_index) as i64`.
- **`--cycles 0` never terminates by design** (`colorshift.rs:94`). Exclude it from the unbounded-completion gate by name, or the run hangs CI rather than failing.

## Acceptance criteria

- [ ] All 6 `colorshift-*` parity cases byte-identical to `ref_dump` at seeds 42 and 1337
- [ ] The same cases pass **unbounded to natural completion**, with matching total frame counts
- [ ] All 12 options are present with the reference's exact names, defaults, and validation
- [ ] The Unicode fixture case passes for this effect
- [ ] The effect's registry entry preserves the 37-name enumeration order
- [ ] Every trap listed above is addressed, with a comment citing the reference line
- [ ] AOT publish stays warning-free

## Blocked by

- 0011 — First effect end-to-end: `wipe`
