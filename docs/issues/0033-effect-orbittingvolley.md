# 0033 — Effect: `orbittingvolley`

**Labels:** `enhancement`, `ready-for-agent`

**Wave:** Wave 2 — gradients, synced scenes, sequence easers  
**Reference:** `src/effects/orbittingvolley.rs` (395 lines, 12 options)  
**Inherited parity cases:** 4

## What to build

Port the `orbittingvolley` effect end-to-end: its option table, its registry entry, and the
effect itself, until its parity cases are byte-identical to the reference.

Transcribe from the Rust, not from the Python — the Rust has already resolved the
upstream semantics questions and its comments cite the upstream lines. Keep function
names and internal structure so a side-by-side diff is possible at review.

Read `docs/translation-checklist.md` for this file before starting.

### Inherited parity cases

| Case | Input | Arguments |
|---|---|---|
| `orbittingvolley-basic` | `basic.txt` | `orbittingvolley` |
| `orbittingvolley-custom` | `paragraph.txt` | `orbittingvolley --top-launcher-symbol T --right-launcher-symbol R --bottom-launcher-symbol B --left-launcher-symbol L --launcher-movement-speed 1.4 --character-movement-speed 0.8 --volley-size 0.1 --launch-delay 10 --character-easing in_out_quad --final-gradient-direction vertical` |
| `orbittingvolley-dynamic` | `colored.txt` | `--existing-color-handling dynamic orbittingvolley --launch-delay 5` |
| `orbittingvolley-xterm-anchored` | `paragraph.txt` | `--xterm-colors --canvas-width 60 --canvas-height 20 --anchor-canvas c --anchor-text c orbittingvolley --volley-size 0.2` |

### Known traps in this effect

- **Four float truncations** at `orbittingvolley.rs:158, :163, :168, :368` — three launcher-position computations plus the volley size.
- `remove(0)` at `:138`.

## Acceptance criteria

- [ ] All 4 `orbittingvolley-*` parity cases byte-identical to `ref_dump` at seeds 42 and 1337
- [ ] The same cases pass **unbounded to natural completion**, with matching total frame counts
- [ ] All 12 options are present with the reference's exact names, defaults, and validation
- [ ] The Unicode fixture case passes for this effect
- [ ] The effect's registry entry preserves the 37-name enumeration order
- [ ] Every trap listed above is addressed, with a comment citing the reference line
- [ ] AOT publish stays warning-free

## Blocked by

- 0011 — First effect end-to-end: `wipe`
