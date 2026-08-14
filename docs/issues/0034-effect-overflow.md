# 0034 — Effect: `overflow`

**Labels:** `enhancement`, `ready-for-agent`

**Wave:** Wave 2 — gradients, synced scenes, sequence easers  
**Reference:** `src/effects/overflow.rs` (280 lines, 6 options)  
**Inherited parity cases:** 6

## What to build

Port the `overflow` effect end-to-end: its option table, its registry entry, and the
effect itself, until its parity cases are byte-identical to the reference.

Transcribe from the Rust, not from the Python — the Rust has already resolved the
upstream semantics questions and its comments cite the upstream lines. Keep function
names and internal structure so a side-by-side diff is possible at review.

Read `docs/translation-checklist.md` for this file before starting.

### Inherited parity cases

| Case | Input | Arguments |
|---|---|---|
| `overflow-always-xterm` | `colored.txt` | `--existing-color-handling always --xterm-colors overflow` |
| `overflow-anchored` | `paragraph.txt` | `--canvas-width 60 --canvas-height 20 --anchor-canvas c --anchor-text c overflow` |
| `overflow-basic` | `basic.txt` | `overflow` |
| `overflow-dynamic` | `colored.txt` | `--existing-color-handling dynamic overflow` |
| `overflow-fast` | `paragraph.txt` | `overflow --overflow-cycles-range 1-2 --overflow-speed 5 --overflow-gradient-stops ff0000 00ff00 --final-gradient-direction horizontal` |
| `overflow-single` | `single.txt` | `overflow --overflow-speed 1` |

### Known traps in this effect

- `VecDeque` at `overflow.rs:95` — `push_back` / `pop_front`, a true FIFO. `Queue<T>` is correct here.

## Acceptance criteria

- [ ] All 6 `overflow-*` parity cases byte-identical to `ref_dump` at seeds 42 and 1337
- [ ] The same cases pass **unbounded to natural completion**, with matching total frame counts
- [ ] All 6 options are present with the reference's exact names, defaults, and validation
- [ ] The Unicode fixture case passes for this effect
- [ ] The effect's registry entry preserves the 37-name enumeration order
- [ ] Every trap listed above is addressed, with a comment citing the reference line
- [ ] AOT publish stays warning-free

## Blocked by

- 0011 — First effect end-to-end: `wipe`
