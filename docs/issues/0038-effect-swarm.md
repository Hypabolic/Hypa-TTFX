# 0038 — Effect: `swarm`

**Labels:** `enhancement`, `ready-for-agent`

**Wave:** Wave 2 — gradients, synced scenes, sequence easers  
**Reference:** `src/effects/swarm.rs` (471 lines, 8 options)  
**Inherited parity cases:** 6

## What to build

Port the `swarm` effect end-to-end: its option table, its registry entry, and the
effect itself, until its parity cases are byte-identical to the reference.

Transcribe from the Rust, not from the Python — the Rust has already resolved the
upstream semantics questions and its comments cite the upstream lines. Keep function
names and internal structure so a side-by-side diff is possible at review.

Read `docs/translation-checklist.md` for this file before starting.

### Inherited parity cases

| Case | Input | Arguments |
|---|---|---|
| `swarm-always-xterm` | `colored.txt` | `--existing-color-handling always --xterm-colors swarm` |
| `swarm-anchored` | `paragraph.txt` | `--canvas-width 60 --canvas-height 20 --anchor-canvas c --anchor-text c swarm` |
| `swarm-basic` | `basic.txt` | `swarm` |
| `swarm-custom` | `paragraph.txt` | `swarm --base-color ff0000 00ff00 --flash-color ffffff --swarm-size 0.2 --swarm-coordination 0.5 --swarm-area-count-range 3-6 --final-gradient-stops ff00ff 00ffff --final-gradient-steps 6 --final-gradient-direction vertical` |
| `swarm-dynamic` | `colored.txt` | `--existing-color-handling dynamic swarm` |
| `swarm-single` | `single.txt` | `swarm` |

### Known traps in this effect

- **Mutates a cached geometry return value.** `swarm` shuffles the list returned by `find_coords_on_circle`, so a later same-argument call observes the shuffled entry. The Rust port reproduces the cache at effect level with a persistent map whose entries carry the mutation — the naive 'recompute, it's a pure function' translation is wrong here.
- `chars().next().to_digit(10)` at `swarm.rs:113` on a path id — ASCII-digit helper, not `char.GetNumericValue`.
- Chains `motion.paths.values()` — insertion order is behavior, so this needs `OrderedMap`.

## Acceptance criteria

- [ ] All 6 `swarm-*` parity cases byte-identical to `ref_dump` at seeds 42 and 1337
- [ ] The same cases pass **unbounded to natural completion**, with matching total frame counts
- [ ] All 8 options are present with the reference's exact names, defaults, and validation
- [ ] The Unicode fixture case passes for this effect
- [ ] The effect's registry entry preserves the 37-name enumeration order
- [ ] Every trap listed above is addressed, with a comment citing the reference line
- [ ] AOT publish stays warning-free

## Blocked by

- 0011 — First effect end-to-end: `wipe`
