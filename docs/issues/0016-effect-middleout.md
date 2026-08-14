# 0016 — Effect: `middleout`

**Labels:** `enhancement`, `ready-for-agent`

**Wave:** Wave 1 — motion and scene basics  
**Reference:** `src/effects/middleout.rs` (264 lines, 9 options)  
**Inherited parity cases:** 5

## What to build

Port the `middleout` effect end-to-end: its option table, its registry entry, and the
effect itself, until its parity cases are byte-identical to the reference.

Transcribe from the Rust, not from the Python — the Rust has already resolved the
upstream semantics questions and its comments cite the upstream lines. Keep function
names and internal structure so a side-by-side diff is possible at review.

Read `docs/translation-checklist.md` for this file before starting.

### Inherited parity cases

| Case | Input | Arguments |
|---|---|---|
| `middleout-always-xterm` | `colored.txt` | `--existing-color-handling always --xterm-colors middleout` |
| `middleout-anchored` | `paragraph.txt` | `--canvas-width 60 --canvas-height 20 --anchor-canvas c --anchor-text c middleout --expand-direction horizontal` |
| `middleout-basic` | `basic.txt` | `middleout` |
| `middleout-dynamic` | `colored.txt` | `--existing-color-handling dynamic middleout` |
| `middleout-horizontal` | `paragraph.txt` | `middleout --expand-direction horizontal --starting-color 00ff00 --center-movement-speed 0.3 --full-movement-speed 0.9 --center-easing out_bounce --full-easing in_quad` |

### Known traps in this effect

- **Set iteration is ordered.** `middleout.rs`'s full-phase activation loop iterates a rebuilt `active_characters` set; canonical order is ascending `CharacterId` (see `docs/ordering-inventory.md`). This is one of only two effect-level set iterations in the whole port.

## Acceptance criteria

- [ ] All 5 `middleout-*` parity cases byte-identical to `ref_dump` at seeds 42 and 1337
- [ ] The same cases pass **unbounded to natural completion**, with matching total frame counts
- [ ] All 9 options are present with the reference's exact names, defaults, and validation
- [ ] The Unicode fixture case passes for this effect
- [ ] The effect's registry entry preserves the 37-name enumeration order
- [ ] Every trap listed above is addressed, with a comment citing the reference line
- [ ] AOT publish stays warning-free

## Blocked by

- 0011 — First effect end-to-end: `wipe`
