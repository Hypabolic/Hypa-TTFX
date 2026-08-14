# 0013 — Effect: `bouncyballs`

**Labels:** `enhancement`, `ready-for-agent`

**Wave:** Wave 1 — motion and scene basics  
**Reference:** `src/effects/bouncyballs.rs` (253 lines, 8 options)  
**Inherited parity cases:** 4

## What to build

Port the `bouncyballs` effect end-to-end: its option table, its registry entry, and the
effect itself, until its parity cases are byte-identical to the reference.

Transcribe from the Rust, not from the Python — the Rust has already resolved the
upstream semantics questions and its comments cite the upstream lines. Keep function
names and internal structure so a side-by-side diff is possible at review.

Read `docs/translation-checklist.md` for this file before starting.

### Inherited parity cases

| Case | Input | Arguments |
|---|---|---|
| `bouncyballs-basic` | `basic.txt` | `bouncyballs` |
| `bouncyballs-custom` | `paragraph.txt` | `bouncyballs --ball-colors ff0000 00ff00 0000ff --ball-symbols o . x --ball-delay 0 --movement-speed 1.2 --movement-easing out_quad --final-gradient-direction horizontal` |
| `bouncyballs-dynamic` | `colored.txt` | `--existing-color-handling dynamic bouncyballs` |
| `bouncyballs-xterm-anchored` | `paragraph.txt` | `--xterm-colors --canvas-width 60 --canvas-height 20 --anchor-canvas c --anchor-text c bouncyballs` |

### Known traps in this effect

- Float truncation at `bouncyballs.rs:187` — `(canvas_top as f64 * rng.uniform(1.0, 1.5)) as i64`, and the `uniform` draw ordering matters.
- RNG-indexed removal at `bouncyballs.rs:239` (index drawn at `:238`).
- Stable sort at `bouncyballs.rs:215`.

## Acceptance criteria

- [ ] All 4 `bouncyballs-*` parity cases byte-identical to `ref_dump` at seeds 42 and 1337
- [ ] The same cases pass **unbounded to natural completion**, with matching total frame counts
- [ ] All 8 options are present with the reference's exact names, defaults, and validation
- [ ] The Unicode fixture case passes for this effect
- [ ] The effect's registry entry preserves the 37-name enumeration order
- [ ] Every trap listed above is addressed, with a comment citing the reference line
- [ ] AOT publish stays warning-free

## Blocked by

- 0011 — First effect end-to-end: `wipe`
