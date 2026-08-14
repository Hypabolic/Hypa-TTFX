# 0041 — Effect: `waves`

**Labels:** `enhancement`, `ready-for-agent`

**Wave:** Wave 2 — gradients, synced scenes, sequence easers  
**Reference:** `src/effects/waves.rs` (282 lines, 10 options)  
**Inherited parity cases:** 5

## What to build

Port the `waves` effect end-to-end: its option table, its registry entry, and the
effect itself, until its parity cases are byte-identical to the reference.

Transcribe from the Rust, not from the Python — the Rust has already resolved the
upstream semantics questions and its comments cite the upstream lines. Keep function
names and internal structure so a side-by-side diff is possible at review.

Read `docs/translation-checklist.md` for this file before starting.

### Inherited parity cases

| Case | Input | Arguments |
|---|---|---|
| `waves-basic` | `basic.txt` | `waves` |
| `waves-center` | `paragraph.txt` | `waves --wave-direction center_to_outside --wave-count 1` |
| `waves-custom` | `paragraph.txt` | `waves --wave-symbols ~ - = --wave-gradient-stops ff0000 0000ff --wave-gradient-steps 4 8 --wave-count 2 --wave-length 1 --wave-direction row_top_to_bottom --wave-easing out_bounce --final-gradient-stops 00ff00 --final-gradient-steps 6 --final-gradient-direction vertical` |
| `waves-dynamic` | `colored.txt` | `--existing-color-handling dynamic waves --wave-count 1` |
| `waves-xterm-anchored` | `paragraph.txt` | `--xterm-colors --canvas-width 60 --canvas-height 20 --anchor-canvas c --anchor-text c waves --wave-count 1` |

### Known traps in this effect

- `remove(0)` FIFO drain at `waves.rs:271`.

## Acceptance criteria

- [ ] All 5 `waves-*` parity cases byte-identical to `ref_dump` at seeds 42 and 1337
- [ ] The same cases pass **unbounded to natural completion**, with matching total frame counts
- [ ] All 10 options are present with the reference's exact names, defaults, and validation
- [ ] The Unicode fixture case passes for this effect
- [ ] The effect's registry entry preserves the 37-name enumeration order
- [ ] Every trap listed above is addressed, with a comment citing the reference line
- [ ] AOT publish stays warning-free

## Blocked by

- 0011 — First effect end-to-end: `wipe`
