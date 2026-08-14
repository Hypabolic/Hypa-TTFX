# 0043 — Effect: `laseretch`

**Labels:** `enhancement`, `ready-for-agent`

**Wave:** Wave 3 — heavy machinery (spanning trees, particles, clocks)  
**Reference:** `src/effects/laseretch.rs` (491 lines, 11 options)  
**Inherited parity cases:** 6

## What to build

Port the `laseretch` effect end-to-end: its option table, its registry entry, and the
effect itself, until its parity cases are byte-identical to the reference.

Transcribe from the Rust, not from the Python — the Rust has already resolved the
upstream semantics questions and its comments cite the upstream lines. Keep function
names and internal structure so a side-by-side diff is possible at review.

Read `docs/translation-checklist.md` for this file before starting.

### Inherited parity cases

| Case | Input | Arguments |
|---|---|---|
| `laseretch-basic` | `basic.txt` | `laseretch` |
| `laseretch-dynamic` | `colored.txt` | `--existing-color-handling dynamic laseretch` |
| `laseretch-fast` | `paragraph.txt` | `laseretch --etch-speed 3 --etch-delay 0 --cool-gradient-stops 00ffe6 0077ff --laser-gradient-stops ff0000 ffff00 --spark-gradient-stops ffffff ff7b00 1a0900 --spark-cooling-frames 3 --final-gradient-stops ff0000 00ff00 --final-gradient-steps 6 --final-gradient-direction horizontal` |
| `laseretch-group-quirk` | `basic.txt` | `laseretch --etch-pattern row_top_to_bottom` |
| `laseretch-single` | `single.txt` | `laseretch` |
| `laseretch-xterm-anchored` | `paragraph.txt` | `--xterm-colors --canvas-width 60 --canvas-height 20 --anchor-canvas c --anchor-text c laseretch --etch-speed 4 --etch-delay 0` |

### Known traps in this effect

- **Three hard dependencies at once**: a recursive-backtracker spanning tree, the particle pool, and bezier paths (whose arc-length calculation has the reproduced final-segment bug).
- **Deque rotation** at `laseretch.rs:166, :225-226` — `pop_front` then `push_back`. This is a rotation, not a drain; neither `Queue<T>` nor `Stack<T>` alone expresses it.
- `remove(0)` at `:461, :466`.
- 11 options, including the dual-type etch-pattern parser flagged as an odd CLI case.

## Acceptance criteria

- [ ] All 6 `laseretch-*` parity cases byte-identical to `ref_dump` at seeds 42 and 1337
- [ ] The same cases pass **unbounded to natural completion**, with matching total frame counts
- [ ] All 11 options are present with the reference's exact names, defaults, and validation
- [ ] The Unicode fixture case passes for this effect
- [ ] The effect's registry entry preserves the 37-name enumeration order
- [ ] Every trap listed above is addressed, with a comment citing the reference line
- [ ] AOT publish stays warning-free

## Blocked by

- 0011 — First effect end-to-end: `wipe`
- Spanning-tree generators must land with the first of `burn` / `smoke` / `laseretch`
