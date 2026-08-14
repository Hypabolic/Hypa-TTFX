# 0032 — Effect: `highlight`

**Labels:** `enhancement`, `ready-for-agent`

**Wave:** Wave 2 — gradients, synced scenes, sequence easers  
**Reference:** `src/effects/highlight.rs` (176 lines, 6 options)  
**Inherited parity cases:** 5

## What to build

Port the `highlight` effect end-to-end: its option table, its registry entry, and the
effect itself, until its parity cases are byte-identical to the reference.

Transcribe from the Rust, not from the Python — the Rust has already resolved the
upstream semantics questions and its comments cite the upstream lines. Keep function
names and internal structure so a side-by-side diff is possible at review.

Read `docs/translation-checklist.md` for this file before starting.

### Inherited parity cases

| Case | Input | Arguments |
|---|---|---|
| `highlight-basic` | `basic.txt` | `highlight` |
| `highlight-center` | `paragraph.txt` | `highlight --highlight-direction center_to_outside --highlight-brightness 2.5` |
| `highlight-custom` | `paragraph.txt` | `highlight --highlight-brightness 0.5 --highlight-direction row_top_to_bottom --highlight-width 3 --final-gradient-stops ff0000 0000ff --final-gradient-steps 6 --final-gradient-direction horizontal` |
| `highlight-dynamic` | `colored.txt` | `--existing-color-handling dynamic highlight` |
| `highlight-xterm-anchored` | `paragraph.txt` | `--xterm-colors --canvas-width 60 --canvas-height 20 --anchor-canvas c --anchor-text c highlight` |

## Acceptance criteria

- [ ] All 5 `highlight-*` parity cases byte-identical to `ref_dump` at seeds 42 and 1337
- [ ] The same cases pass **unbounded to natural completion**, with matching total frame counts
- [ ] All 6 options are present with the reference's exact names, defaults, and validation
- [ ] The Unicode fixture case passes for this effect
- [ ] The effect's registry entry preserves the 37-name enumeration order
- [ ] AOT publish stays warning-free

## Blocked by

- 0011 — First effect end-to-end: `wipe`
