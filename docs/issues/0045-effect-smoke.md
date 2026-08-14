# 0045 — Effect: `smoke`

**Labels:** `enhancement`, `ready-for-agent`

**Wave:** Wave 3 — heavy machinery (spanning trees, particles, clocks)  
**Reference:** `src/effects/smoke.rs` (264 lines, 7 options)  
**Inherited parity cases:** 5

## What to build

Port the `smoke` effect end-to-end: its option table, its registry entry, and the
effect itself, until its parity cases are byte-identical to the reference.

Transcribe from the Rust, not from the Python — the Rust has already resolved the
upstream semantics questions and its comments cite the upstream lines. Keep function
names and internal structure so a side-by-side diff is possible at review.

Read `docs/translation-checklist.md` for this file before starting.

### Inherited parity cases

| Case | Input | Arguments |
|---|---|---|
| `smoke-basic` | `basic.txt` | `smoke` |
| `smoke-custom` | `paragraph.txt` | `smoke --starting-color 333333 --smoke-symbols + @ % --smoke-gradient-stops 111111 eeeeee --final-gradient-stops ff0000 00ff00 --final-gradient-steps 6 --final-gradient-direction horizontal` |
| `smoke-dynamic` | `colored.txt` | `--existing-color-handling dynamic smoke` |
| `smoke-wholecanvas` | `basic.txt` | `smoke --use-whole-canvas` |
| `smoke-xterm-anchored` | `paragraph.txt` | `--xterm-colors --canvas-width 60 --canvas-height 20 --anchor-canvas c --anchor-text c smoke` |

### Known traps in this effect

- **Spanning tree** — same generator dependency as `burn`.
- Ships as a separate effect but shares machinery with `burn`; port them adjacently.

## Acceptance criteria

- [ ] All 5 `smoke-*` parity cases byte-identical to `ref_dump` at seeds 42 and 1337
- [ ] The same cases pass **unbounded to natural completion**, with matching total frame counts
- [ ] All 7 options are present with the reference's exact names, defaults, and validation
- [ ] The Unicode fixture case passes for this effect
- [ ] The effect's registry entry preserves the 37-name enumeration order
- [ ] Every trap listed above is addressed, with a comment citing the reference line
- [ ] AOT publish stays warning-free

## Blocked by

- 0011 — First effect end-to-end: `wipe`
- Spanning-tree generators must land with the first of `burn` / `smoke` / `laseretch`
