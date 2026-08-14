# 0039 — Effect: `sweep`

**Labels:** `enhancement`, `ready-for-agent`

**Wave:** Wave 2 — gradients, synced scenes, sequence easers  
**Reference:** `src/effects/sweep.rs` (257 lines, 6 options)  
**Inherited parity cases:** 5

## What to build

Port the `sweep` effect end-to-end: its option table, its registry entry, and the
effect itself, until its parity cases are byte-identical to the reference.

Transcribe from the Rust, not from the Python — the Rust has already resolved the
upstream semantics questions and its comments cite the upstream lines. Keep function
names and internal structure so a side-by-side diff is possible at review.

Read `docs/translation-checklist.md` for this file before starting.

### Inherited parity cases

| Case | Input | Arguments |
|---|---|---|
| `sweep-basic` | `basic.txt` | `sweep` |
| `sweep-center` | `paragraph.txt` | `sweep --first-sweep-direction center_to_outside --second-sweep-direction outside_to_center` |
| `sweep-custom` | `paragraph.txt` | `sweep --sweep-symbols # @ % --first-sweep-direction row_top_to_bottom --second-sweep-direction diagonal_bottom_left_to_top_right --final-gradient-stops ff0000 00ff00 --final-gradient-steps 12 --final-gradient-direction horizontal` |
| `sweep-dynamic` | `colored.txt` | `--existing-color-handling dynamic sweep` |
| `sweep-xterm-anchored` | `paragraph.txt` | `--xterm-colors --canvas-width 60 --canvas-height 20 --anchor-canvas c --anchor-text c sweep` |

## Acceptance criteria

- [ ] All 5 `sweep-*` parity cases byte-identical to `ref_dump` at seeds 42 and 1337
- [ ] The same cases pass **unbounded to natural completion**, with matching total frame counts
- [ ] All 6 options are present with the reference's exact names, defaults, and validation
- [ ] The Unicode fixture case passes for this effect
- [ ] The effect's registry entry preserves the 37-name enumeration order
- [ ] AOT publish stays warning-free

## Blocked by

- 0011 — First effect end-to-end: `wipe`
