# 0029 — Effect: `crumble`

**Labels:** `enhancement`, `ready-for-agent`

**Wave:** Wave 2 — gradients, synced scenes, sequence easers  
**Reference:** `src/effects/crumble.rs` (476 lines, 3 options)  
**Inherited parity cases:** 6

## What to build

Port the `crumble` effect end-to-end: its option table, its registry entry, and the
effect itself, until its parity cases are byte-identical to the reference.

Transcribe from the Rust, not from the Python — the Rust has already resolved the
upstream semantics questions and its comments cite the upstream lines. Keep function
names and internal structure so a side-by-side diff is possible at review.

Read `docs/translation-checklist.md` for this file before starting.

### Inherited parity cases

| Case | Input | Arguments |
|---|---|---|
| `crumble-always-xterm` | `colored.txt` | `--existing-color-handling always --xterm-colors crumble` |
| `crumble-anchored` | `paragraph.txt` | `--canvas-width 60 --canvas-height 20 --anchor-canvas c --anchor-text c crumble` |
| `crumble-basic` | `basic.txt` | `crumble` |
| `crumble-custom` | `paragraph.txt` | `crumble --final-gradient-stops ff0000 00ff00 0000ff --final-gradient-steps 6 --final-gradient-direction horizontal` |
| `crumble-dynamic` | `colored.txt` | `--existing-color-handling dynamic crumble` |
| `crumble-single` | `single.txt` | `crumble` |

### Known traps in this effect

- `remove(0)` FIFO drains at `crumble.rs:418, :443`.

## Acceptance criteria

- [ ] All 6 `crumble-*` parity cases byte-identical to `ref_dump` at seeds 42 and 1337
- [ ] The same cases pass **unbounded to natural completion**, with matching total frame counts
- [ ] All 3 options are present with the reference's exact names, defaults, and validation
- [ ] The Unicode fixture case passes for this effect
- [ ] The effect's registry entry preserves the 37-name enumeration order
- [ ] Every trap listed above is addressed, with a comment citing the reference line
- [ ] AOT publish stays warning-free

## Blocked by

- 0011 — First effect end-to-end: `wipe`
