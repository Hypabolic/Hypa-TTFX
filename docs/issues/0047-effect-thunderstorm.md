# 0047 — Effect: `thunderstorm`

**Labels:** `enhancement`, `ready-for-agent`

**Wave:** Wave 3 — heavy machinery (spanning trees, particles, clocks)  
**Reference:** `src/effects/thunderstorm.rs` (922 lines, 12 options)  
**Inherited parity cases:** 4

## What to build

Port the `thunderstorm` effect end-to-end: its option table, its registry entry, and the
effect itself, until its parity cases are byte-identical to the reference.

Transcribe from the Rust, not from the Python — the Rust has already resolved the
upstream semantics questions and its comments cite the upstream lines. Keep function
names and internal structure so a side-by-side diff is possible at review.

Read `docs/translation-checklist.md` for this file before starting.

### Inherited parity cases

| Case | Input | Arguments |
|---|---|---|
| `thunderstorm-basic` | `basic.txt` | `thunderstorm --storm-time 2` |
| `thunderstorm-custom` | `paragraph.txt` | `thunderstorm --storm-time 3 --text-glow-time 1 --spark-glow-time 1 --lightning-color ffff00` |
| `thunderstorm-dynamic` | `colored.txt` | `--existing-color-handling dynamic thunderstorm --storm-time 2` |
| `thunderstorm-xterm-anchored` | `paragraph.txt` | `--xterm-colors --canvas-width 60 --canvas-height 20 --anchor-canvas c thunderstorm --storm-time 2` |

### Known traps in this effect

- **The largest effect at 922 lines**, and clock-dependent — reads monotonic time for the storm budget, so it is only reproducible under the virtual clock.
- `remove(0)` at `:513`.
- 12 options. Budget this as the single biggest effect issue.

## Acceptance criteria

- [ ] All 4 `thunderstorm-*` parity cases byte-identical to `ref_dump` at seeds 42 and 1337
- [ ] The same cases pass **unbounded to natural completion**, with matching total frame counts
- [ ] All 12 options are present with the reference's exact names, defaults, and validation
- [ ] The Unicode fixture case passes for this effect
- [ ] The effect's registry entry preserves the 37-name enumeration order
- [ ] Every trap listed above is addressed, with a comment citing the reference line
- [ ] AOT publish stays warning-free

## Blocked by

- 0011 — First effect end-to-end: `wipe`
