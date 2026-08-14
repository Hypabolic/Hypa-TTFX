# 0023 — Effect: `spray`

**Labels:** `enhancement`, `ready-for-agent`

**Wave:** Wave 1 — motion and scene basics  
**Reference:** `src/effects/spray.rs` (241 lines, 7 options)  
**Inherited parity cases:** 4

## What to build

Port the `spray` effect end-to-end: its option table, its registry entry, and the
effect itself, until its parity cases are byte-identical to the reference.

Transcribe from the Rust, not from the Python — the Rust has already resolved the
upstream semantics questions and its comments cite the upstream lines. Keep function
names and internal structure so a side-by-side diff is possible at review.

Read `docs/translation-checklist.md` for this file before starting.

### Inherited parity cases

| Case | Input | Arguments |
|---|---|---|
| `spray-basic` | `basic.txt` | `spray` |
| `spray-dynamic` | `colored.txt` | `--existing-color-handling dynamic spray` |
| `spray-nw-slow` | `paragraph.txt` | `spray --spray-position nw --spray-volume 0.02 --movement-speed-range 0.3-0.8 --movement-easing in_out_quad` |
| `spray-xterm-anchored` | `paragraph.txt` | `--xterm-colors --canvas-width 60 --canvas-height 20 --anchor-canvas c --anchor-text c spray --spray-position center` |

### Known traps in this effect

- Float truncation at `spray.rs:222` — `(pending.len() as f64 * spray_volume) as i64` sets the volume.

## Acceptance criteria

- [ ] All 4 `spray-*` parity cases byte-identical to `ref_dump` at seeds 42 and 1337
- [ ] The same cases pass **unbounded to natural completion**, with matching total frame counts
- [ ] All 7 options are present with the reference's exact names, defaults, and validation
- [ ] The Unicode fixture case passes for this effect
- [ ] The effect's registry entry preserves the 37-name enumeration order
- [ ] Every trap listed above is addressed, with a comment citing the reference line
- [ ] AOT publish stays warning-free

## Blocked by

- 0011 — First effect end-to-end: `wipe`
