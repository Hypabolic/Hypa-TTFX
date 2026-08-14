# 0027 — Effect: `bubbles`

**Labels:** `enhancement`, `ready-for-agent`

**Wave:** Wave 2 — gradients, synced scenes, sequence easers  
**Reference:** `src/effects/bubbles.rs` (522 lines, 10 options)  
**Inherited parity cases:** 5

## What to build

Port the `bubbles` effect end-to-end: its option table, its registry entry, and the
effect itself, until its parity cases are byte-identical to the reference.

Transcribe from the Rust, not from the Python — the Rust has already resolved the
upstream semantics questions and its comments cite the upstream lines. Keep function
names and internal structure so a side-by-side diff is possible at review.

Read `docs/translation-checklist.md` for this file before starting.

### Inherited parity cases

| Case | Input | Arguments |
|---|---|---|
| `bubbles-basic` | `basic.txt` | `bubbles` |
| `bubbles-bottom` | `paragraph.txt` | `bubbles --pop-condition bottom --bubble-colors ff0000 00ff00 --pop-color 00ffff --movement-easing out_bounce` |
| `bubbles-dynamic` | `colored.txt` | `--existing-color-handling dynamic bubbles --bubble-delay 5` |
| `bubbles-rainbow-anywhere` | `paragraph.txt` | `bubbles --rainbow --pop-condition anywhere --bubble-speed 0.8 --bubble-delay 8` |
| `bubbles-xterm-anchored` | `paragraph.txt` | `--xterm-colors --canvas-width 60 --canvas-height 20 --anchor-canvas c --anchor-text c bubbles` |

### Known traps in this effect

- `remove(0)` FIFO drains at `bubbles.rs:475, :493`.
- Iterator `.min()` at `:156` — `unwrap()` on empty must throw, not default.

## Acceptance criteria

- [ ] All 5 `bubbles-*` parity cases byte-identical to `ref_dump` at seeds 42 and 1337
- [ ] The same cases pass **unbounded to natural completion**, with matching total frame counts
- [ ] All 10 options are present with the reference's exact names, defaults, and validation
- [ ] The Unicode fixture case passes for this effect
- [ ] The effect's registry entry preserves the 37-name enumeration order
- [ ] Every trap listed above is addressed, with a comment citing the reference line
- [ ] AOT publish stays warning-free

## Blocked by

- 0011 — First effect end-to-end: `wipe`
