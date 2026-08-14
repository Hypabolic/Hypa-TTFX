# 0026 — Effect: `blackhole`

**Labels:** `enhancement`, `ready-for-agent`

**Wave:** Wave 2 — gradients, synced scenes, sequence easers  
**Reference:** `src/effects/blackhole.rs` (616 lines, 5 options)  
**Inherited parity cases:** 6

## What to build

Port the `blackhole` effect end-to-end: its option table, its registry entry, and the
effect itself, until its parity cases are byte-identical to the reference.

Transcribe from the Rust, not from the Python — the Rust has already resolved the
upstream semantics questions and its comments cite the upstream lines. Keep function
names and internal structure so a side-by-side diff is possible at review.

Read `docs/translation-checklist.md` for this file before starting.

### Inherited parity cases

| Case | Input | Arguments |
|---|---|---|
| `blackhole-always-xterm` | `colored.txt` | `--existing-color-handling always --xterm-colors blackhole` |
| `blackhole-anchored` | `paragraph.txt` | `--canvas-width 60 --canvas-height 20 --anchor-canvas c --anchor-text c blackhole` |
| `blackhole-basic` | `basic.txt` | `blackhole` |
| `blackhole-custom` | `paragraph.txt` | `blackhole --blackhole-color 00ffff --star-colors ff0000 00ff00 0000ff --final-gradient-stops ff0000 ffffff --final-gradient-steps 6 --final-gradient-direction horizontal` |
| `blackhole-dynamic` | `colored.txt` | `--existing-color-handling dynamic blackhole` |
| `blackhole-single` | `single.txt` | `blackhole` |

### Known traps in this effect

- RNG-indexed removal at `blackhole.rs:104` (index drawn at `:103`).
- `remove(0)` at `:287, :569`.
- `floor_div` at `:556` — Python `//`, not C# `/`.

## Acceptance criteria

- [ ] All 6 `blackhole-*` parity cases byte-identical to `ref_dump` at seeds 42 and 1337
- [ ] The same cases pass **unbounded to natural completion**, with matching total frame counts
- [ ] All 5 options are present with the reference's exact names, defaults, and validation
- [ ] The Unicode fixture case passes for this effect
- [ ] The effect's registry entry preserves the 37-name enumeration order
- [ ] Every trap listed above is addressed, with a comment citing the reference line
- [ ] AOT publish stays warning-free

## Blocked by

- 0011 — First effect end-to-end: `wipe`
