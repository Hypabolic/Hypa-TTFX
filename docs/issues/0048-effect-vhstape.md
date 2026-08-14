# 0048 — Effect: `vhstape`

**Labels:** `enhancement`, `ready-for-agent`

**Wave:** Wave 3 — heavy machinery (spanning trees, particles, clocks)  
**Reference:** `src/effects/vhstape.rs` (620 lines, 9 options)  
**Inherited parity cases:** 4

## What to build

Port the `vhstape` effect end-to-end: its option table, its registry entry, and the
effect itself, until its parity cases are byte-identical to the reference.

Transcribe from the Rust, not from the Python — the Rust has already resolved the
upstream semantics questions and its comments cite the upstream lines. Keep function
names and internal structure so a side-by-side diff is possible at review.

Read `docs/translation-checklist.md` for this file before starting.

### Inherited parity cases

| Case | Input | Arguments |
|---|---|---|
| `vhstape-basic` | `basic.txt` | `vhstape` |
| `vhstape-dynamic` | `colored.txt` | `--existing-color-handling dynamic vhstape --total-glitch-time 150` |
| `vhstape-fast` | `paragraph.txt` | `vhstape --total-glitch-time 200 --glitch-line-chance 0.1 --noise-chance 0.01` |
| `vhstape-xterm-anchored` | `paragraph.txt` | `--xterm-colors --canvas-width 60 --canvas-height 20 --anchor-canvas c --anchor-text c vhstape --total-glitch-time 150` |

### Known traps in this effect

- 620 lines with no single dominant trap — the risk is volume and the glitch-line RNG ordering.
- Iterates glitch line colours in reverse at `vhstape.rs:209` — `.rev()` order is behavior.

## Acceptance criteria

- [ ] All 4 `vhstape-*` parity cases byte-identical to `ref_dump` at seeds 42 and 1337
- [ ] The same cases pass **unbounded to natural completion**, with matching total frame counts
- [ ] All 9 options are present with the reference's exact names, defaults, and validation
- [ ] The Unicode fixture case passes for this effect
- [ ] The effect's registry entry preserves the 37-name enumeration order
- [ ] Every trap listed above is addressed, with a comment citing the reference line
- [ ] AOT publish stays warning-free

## Blocked by

- 0011 — First effect end-to-end: `wipe`
