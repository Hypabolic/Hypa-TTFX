# 0036 — Effect: `rings`

**Labels:** `enhancement`, `ready-for-agent`

**Wave:** Wave 2 — gradients, synced scenes, sequence easers  
**Reference:** `src/effects/rings.rs` (590 lines, 9 options)  
**Inherited parity cases:** 4

## What to build

Port the `rings` effect end-to-end: its option table, its registry entry, and the
effect itself, until its parity cases are byte-identical to the reference.

Transcribe from the Rust, not from the Python — the Rust has already resolved the
upstream semantics questions and its comments cite the upstream lines. Keep function
names and internal structure so a side-by-side diff is possible at review.

Read `docs/translation-checklist.md` for this file before starting.

### Inherited parity cases

| Case | Input | Arguments |
|---|---|---|
| `rings-basic` | `basic.txt` | `rings` |
| `rings-custom` | `paragraph.txt` | `rings --ring-colors ff0000 00ff00 --ring-gap 0.2 --spin-duration 50 --spin-speed 0.5-2.0 --disperse-duration 50 --spin-disperse-cycles 2 --final-gradient-direction horizontal` |
| `rings-dynamic` | `colored.txt` | `--existing-color-handling dynamic rings --spin-duration 60 --disperse-duration 60` |
| `rings-xterm-anchored` | `paragraph.txt` | `--xterm-colors --canvas-width 60 --canvas-height 20 --anchor-canvas c --anchor-text c rings --spin-duration 80 --disperse-duration 40` |

### Known traps in this effect

- **Keyed removal from an insertion-ordered map** at `rings.rs:117` — `motion.paths.remove("disperse")`. This is the one place where a `Dictionary`'s order-breaks-on-removal behavior would actually bite, since `motion.paths` is iterated elsewhere.
- `VecDeque` at `:423` — `pop_front` only, FIFO.
- RNG-indexed access at `:113`.
- Iterates `rings.values()` — dict insertion order is behavior.

## Acceptance criteria

- [ ] All 4 `rings-*` parity cases byte-identical to `ref_dump` at seeds 42 and 1337
- [ ] The same cases pass **unbounded to natural completion**, with matching total frame counts
- [ ] All 9 options are present with the reference's exact names, defaults, and validation
- [ ] The Unicode fixture case passes for this effect
- [ ] The effect's registry entry preserves the 37-name enumeration order
- [ ] Every trap listed above is addressed, with a comment citing the reference line
- [ ] AOT publish stays warning-free

## Blocked by

- 0011 — First effect end-to-end: `wipe`
