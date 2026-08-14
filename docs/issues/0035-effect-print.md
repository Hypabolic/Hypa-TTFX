# 0035 — Effect: `print`

**Labels:** `enhancement`, `ready-for-agent`

**Wave:** Wave 2 — gradients, synced scenes, sequence easers  
**Reference:** `src/effects/print_effect.rs` (399 lines, 6 options)  
**Inherited parity cases:** 6

## What to build

Port the `print` effect end-to-end: its option table, its registry entry, and the
effect itself, until its parity cases are byte-identical to the reference.

Transcribe from the Rust, not from the Python — the Rust has already resolved the
upstream semantics questions and its comments cite the upstream lines. Keep function
names and internal structure so a side-by-side diff is possible at review.

Read `docs/translation-checklist.md` for this file before starting.

### Inherited parity cases

| Case | Input | Arguments |
|---|---|---|
| `print-always-xterm` | `colored.txt` | `--existing-color-handling always --xterm-colors print` |
| `print-anchored` | `paragraph.txt` | `--canvas-width 60 --canvas-height 20 --anchor-canvas c --anchor-text c print` |
| `print-basic` | `basic.txt` | `print` |
| `print-dynamic` | `colored.txt` | `--existing-color-handling dynamic print` |
| `print-fast` | `paragraph.txt` | `print --print-speed 4 --print-head-return-speed 3 --print-head-easing out_quad --final-gradient-direction vertical` |
| `print-single` | `single.txt` | `print --print-speed 1 --print-head-return-speed 0.5` |

### Known traps in this effect

- `remove(0)` FIFO drains at `print_effect.rs:73, :288, :318`.
- Iterator `.min()`/`.max()` at `:120, :339` — integer `Ord`, LINQ is fine, but they `unwrap()` on empty.

## Acceptance criteria

- [ ] All 6 `print-*` parity cases byte-identical to `ref_dump` at seeds 42 and 1337
- [ ] The same cases pass **unbounded to natural completion**, with matching total frame counts
- [ ] All 6 options are present with the reference's exact names, defaults, and validation
- [ ] The Unicode fixture case passes for this effect
- [ ] The effect's registry entry preserves the 37-name enumeration order
- [ ] Every trap listed above is addressed, with a comment citing the reference line
- [ ] AOT publish stays warning-free

## Blocked by

- 0011 — First effect end-to-end: `wipe`
