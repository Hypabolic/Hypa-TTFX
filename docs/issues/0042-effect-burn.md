# 0042 — Effect: `burn`

**Labels:** `enhancement`, `ready-for-agent`

**Wave:** Wave 3 — heavy machinery (spanning trees, particles, clocks)  
**Reference:** `src/effects/burn.rs` (380 lines, 6 options)  
**Inherited parity cases:** 5

## What to build

Port the `burn` effect end-to-end: its option table, its registry entry, and the
effect itself, until its parity cases are byte-identical to the reference.

Transcribe from the Rust, not from the Python — the Rust has already resolved the
upstream semantics questions and its comments cite the upstream lines. Keep function
names and internal structure so a side-by-side diff is possible at review.

Read `docs/translation-checklist.md` for this file before starting.

### Inherited parity cases

| Case | Input | Arguments |
|---|---|---|
| `burn-basic` | `basic.txt` | `burn` |
| `burn-custom` | `paragraph.txt` | `burn --starting-color 404040 --burn-colors ff0000 ffa500 ffff00 --smoke-chance 1.0 --final-gradient-stops 00ff00 0000ff --final-gradient-steps 8 --final-gradient-direction horizontal` |
| `burn-dynamic` | `colored.txt` | `--existing-color-handling dynamic burn` |
| `burn-nosmoke` | `basic.txt` | `burn --smoke-chance 0.0` |
| `burn-xterm-anchored` | `paragraph.txt` | `--xterm-colors --canvas-width 60 --canvas-height 20 --anchor-canvas c --anchor-text c burn` |

### Known traps in this effect

- **Spanning tree** — uses a generator from `spanning_tree.rs`, which has three RNG-indexed removals (`:88, :93, :192`) and a `remove(0)` at `:317`. Get those right before this effect.
- **Particle emission with a value payload** — `burn.rs:178-185` registers a callback carrying `CallbackValue::Int(emission_id)` *inside a loop*. This is the canonical case for why callbacks keep an immutable `{ id, args }` record instead of a closure: a C# lambda would capture the loop variable.
- `remove(0)` at `:367`.
- `--smoke-chance 0.0` and `1.0` are both covered by inherited cases — the RNG draw happens either way.

## Acceptance criteria

- [ ] All 5 `burn-*` parity cases byte-identical to `ref_dump` at seeds 42 and 1337
- [ ] The same cases pass **unbounded to natural completion**, with matching total frame counts
- [ ] All 6 options are present with the reference's exact names, defaults, and validation
- [ ] The Unicode fixture case passes for this effect
- [ ] The effect's registry entry preserves the 37-name enumeration order
- [ ] Every trap listed above is addressed, with a comment citing the reference line
- [ ] AOT publish stays warning-free

## Blocked by

- 0011 — First effect end-to-end: `wipe`
- Spanning-tree generators must land with the first of `burn` / `smoke` / `laseretch`
