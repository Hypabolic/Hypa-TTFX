# 0024 — Effect: `beams`

**Labels:** `enhancement`, `ready-for-agent`

**Wave:** Wave 2 — gradients, synced scenes, sequence easers  
**Reference:** `src/effects/beams.rs` (435 lines, 13 options)  
**Inherited parity cases:** 4

## What to build

Port the `beams` effect end-to-end: its option table, its registry entry, and the
effect itself, until its parity cases are byte-identical to the reference.

Transcribe from the Rust, not from the Python — the Rust has already resolved the
upstream semantics questions and its comments cite the upstream lines. Keep function
names and internal structure so a side-by-side diff is possible at review.

Read `docs/translation-checklist.md` for this file before starting.

### Inherited parity cases

| Case | Input | Arguments |
|---|---|---|
| `beams-basic` | `basic.txt` | `beams` |
| `beams-custom` | `paragraph.txt` | `beams --beam-row-symbols - = --beam-column-symbols . : --beam-delay 2 --beam-row-speed-range 30-80 --beam-column-speed-range 12-20 --beam-gradient-stops ff0000 0000ff --beam-gradient-steps 4 --beam-gradient-frames 1 --final-gradient-direction horizontal --final-wipe-speed 5` |
| `beams-dynamic` | `colored.txt` | `--existing-color-handling dynamic beams` |
| `beams-xterm-anchored` | `paragraph.txt` | `--xterm-colors --canvas-width 60 --canvas-height 20 --anchor-canvas c --anchor-text c beams` |

### Known traps in this effect

- **13 options — the largest surface of any effect.** Includes `--beam-row-symbols - =`, whose lone `-` value is the token-edge case from issue 0003.
- Stable sorts at `beams.rs:134, :137` — `sort_by_key` on input column and row.
- `remove(0)` FIFO drains at `:149, :379, :417`.

## Acceptance criteria

- [ ] All 4 `beams-*` parity cases byte-identical to `ref_dump` at seeds 42 and 1337
- [ ] The same cases pass **unbounded to natural completion**, with matching total frame counts
- [ ] All 13 options are present with the reference's exact names, defaults, and validation
- [ ] The Unicode fixture case passes for this effect
- [ ] The effect's registry entry preserves the 37-name enumeration order
- [ ] Every trap listed above is addressed, with a comment citing the reference line
- [ ] AOT publish stays warning-free

## Blocked by

- 0011 — First effect end-to-end: `wipe`
