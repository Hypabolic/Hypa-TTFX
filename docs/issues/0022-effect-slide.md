# 0022 — Effect: `slide`

**Labels:** `enhancement`, `ready-for-agent`

**Wave:** Wave 1 — motion and scene basics  
**Reference:** `src/effects/slide.rs` (309 lines, 10 options)  
**Inherited parity cases:** 6

## What to build

Port the `slide` effect end-to-end: its option table, its registry entry, and the
effect itself, until its parity cases are byte-identical to the reference.

Transcribe from the Rust, not from the Python — the Rust has already resolved the
upstream semantics questions and its comments cite the upstream lines. Keep function
names and internal structure so a side-by-side diff is possible at review.

Read `docs/translation-checklist.md` for this file before starting.

### Inherited parity cases

| Case | Input | Arguments |
|---|---|---|
| `slide-always-xterm` | `colored.txt` | `--existing-color-handling always --xterm-colors slide --grouping column` |
| `slide-anchored` | `paragraph.txt` | `--canvas-width 60 --canvas-height 20 --anchor-canvas c --anchor-text c slide --grouping diagonal --merge` |
| `slide-basic` | `basic.txt` | `slide` |
| `slide-column-merge` | `paragraph.txt` | `slide --grouping column --merge --gap 0 --movement-speed 0.5` |
| `slide-diagonal-reverse` | `paragraph.txt` | `slide --grouping diagonal --reverse-direction --movement-easing out_bounce --final-gradient-frames 3` |
| `slide-dynamic` | `colored.txt` | `--existing-color-handling dynamic slide` |

### Known traps in this effect

- `remove(0)` FIFO drains at `slide.rs:290, :297`.
- Grouped `get_characters_grouped` variants — exact grouping (diagonal bands, row/column) is behavior.

## Acceptance criteria

- [ ] All 6 `slide-*` parity cases byte-identical to `ref_dump` at seeds 42 and 1337
- [ ] The same cases pass **unbounded to natural completion**, with matching total frame counts
- [ ] All 10 options are present with the reference's exact names, defaults, and validation
- [ ] The Unicode fixture case passes for this effect
- [ ] The effect's registry entry preserves the 37-name enumeration order
- [ ] Every trap listed above is addressed, with a comment citing the reference line
- [ ] AOT publish stays warning-free

## Blocked by

- 0011 — First effect end-to-end: `wipe`
