# 0031 — Effect: `fireworks`

**Labels:** `enhancement`, `ready-for-agent`

**Wave:** Wave 2 — gradients, synced scenes, sequence easers  
**Reference:** `src/effects/fireworks.rs` (421 lines, 9 options)  
**Inherited parity cases:** 4

## What to build

Port the `fireworks` effect end-to-end: its option table, its registry entry, and the
effect itself, until its parity cases are byte-identical to the reference.

Transcribe from the Rust, not from the Python — the Rust has already resolved the
upstream semantics questions and its comments cite the upstream lines. Keep function
names and internal structure so a side-by-side diff is possible at review.

Read `docs/translation-checklist.md` for this file before starting.

### Inherited parity cases

| Case | Input | Arguments |
|---|---|---|
| `fireworks-basic` | `basic.txt` | `fireworks` |
| `fireworks-custom` | `paragraph.txt` | `fireworks --explode-anywhere --firework-colors ff0000 00ff00 0000ff --firework-symbol * --firework-volume 0.12 --launch-delay 10 --explode-distance 0.4 --final-gradient-direction diagonal` |
| `fireworks-dynamic` | `colored.txt` | `--existing-color-handling dynamic fireworks` |
| `fireworks-xterm-anchored` | `paragraph.txt` | `--xterm-colors --canvas-width 60 --canvas-height 20 --anchor-canvas c --anchor-text c fireworks --launch-delay 5` |

### Known traps in this effect

- Float truncation at `fireworks.rs:413` — `(launch_delay as f64 * rng.uniform(0.5, 1.5)) as i64`.

## Acceptance criteria

- [ ] All 4 `fireworks-*` parity cases byte-identical to `ref_dump` at seeds 42 and 1337
- [ ] The same cases pass **unbounded to natural completion**, with matching total frame counts
- [ ] All 9 options are present with the reference's exact names, defaults, and validation
- [ ] The Unicode fixture case passes for this effect
- [ ] The effect's registry entry preserves the 37-name enumeration order
- [ ] Every trap listed above is addressed, with a comment citing the reference line
- [ ] AOT publish stays warning-free

## Blocked by

- 0011 — First effect end-to-end: `wipe`
