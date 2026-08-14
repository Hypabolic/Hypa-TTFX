# 0018 — Effect: `rain`

**Labels:** `enhancement`, `ready-for-agent`

**Wave:** Wave 1 — motion and scene basics  
**Reference:** `src/effects/rain.rs` (251 lines, 7 options)  
**Inherited parity cases:** 4

## What to build

Port the `rain` effect end-to-end: its option table, its registry entry, and the
effect itself, until its parity cases are byte-identical to the reference.

Transcribe from the Rust, not from the Python — the Rust has already resolved the
upstream semantics questions and its comments cite the upstream lines. Keep function
names and internal structure so a side-by-side diff is possible at review.

Read `docs/translation-checklist.md` for this file before starting.

### Inherited parity cases

| Case | Input | Arguments |
|---|---|---|
| `rain-basic` | `basic.txt` | `rain` |
| `rain-custom` | `paragraph.txt` | `rain --rain-colors 00ff00 ff0000 --rain-symbols . , --movement-speed 0.8-1.2 --movement-easing out_bounce --final-gradient-direction vertical` |
| `rain-dynamic` | `colored.txt` | `--existing-color-handling dynamic rain` |
| `rain-xterm-anchored` | `paragraph.txt` | `--xterm-colors --canvas-width 60 --canvas-height 20 --anchor-canvas c --anchor-text c rain` |

### Known traps in this effect

- RNG-indexed removal at `rain.rs:241` (index drawn at `:240`) — `RemoveAt(i)`; a swap-removal or `Remove(value)` desynchronizes every later draw.
- Stable sort at `rain.rs:219` — `sort_by_key` on input row. `List.Sort` is unstable.

## Acceptance criteria

- [ ] All 4 `rain-*` parity cases byte-identical to `ref_dump` at seeds 42 and 1337
- [ ] The same cases pass **unbounded to natural completion**, with matching total frame counts
- [ ] All 7 options are present with the reference's exact names, defaults, and validation
- [ ] The Unicode fixture case passes for this effect
- [ ] The effect's registry entry preserves the 37-name enumeration order
- [ ] Every trap listed above is addressed, with a comment citing the reference line
- [ ] AOT publish stays warning-free

## Blocked by

- 0011 — First effect end-to-end: `wipe`
