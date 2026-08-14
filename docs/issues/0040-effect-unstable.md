# 0040 — Effect: `unstable`

**Labels:** `enhancement`, `ready-for-agent`

**Wave:** Wave 2 — gradients, synced scenes, sequence easers  
**Reference:** `src/effects/unstable.rs` (479 lines, 8 options)  
**Inherited parity cases:** 6

## What to build

Port the `unstable` effect end-to-end: its option table, its registry entry, and the
effect itself, until its parity cases are byte-identical to the reference.

Transcribe from the Rust, not from the Python — the Rust has already resolved the
upstream semantics questions and its comments cite the upstream lines. Keep function
names and internal structure so a side-by-side diff is possible at review.

Read `docs/translation-checklist.md` for this file before starting.

### Inherited parity cases

| Case | Input | Arguments |
|---|---|---|
| `unstable-always-xterm` | `colored.txt` | `--existing-color-handling always --xterm-colors unstable` |
| `unstable-anchored` | `paragraph.txt` | `--canvas-width 60 --canvas-height 20 --anchor-canvas c --anchor-text c unstable` |
| `unstable-basic` | `basic.txt` | `unstable` |
| `unstable-custom` | `paragraph.txt` | `unstable --unstable-color 00ffff --explosion-ease in_out_quad --explosion-speed 2 --reassembly-ease out_bounce --reassembly-speed 0.5 --final-gradient-direction horizontal` |
| `unstable-dynamic` | `colored.txt` | `--existing-color-handling dynamic unstable` |
| `unstable-single` | `single.txt` | `unstable --explosion-speed 0.4` |

### Known traps in this effect

- **Set iteration is ordered.** The explosion and reassembly tick loops iterate `active_characters`; canonical order is ascending `CharacterId`.
- RNG-indexed removal at `unstable.rs:167`.
- **Calls `ctx.frame()` at five sites** (`:382, :397, :433, :437, :470`) with fall-through paths that discard the earlier frame string but keep its clock advance. This is the effect that proves the clock must advance inside `frame()`, not per returned frame.

## Acceptance criteria

- [ ] All 6 `unstable-*` parity cases byte-identical to `ref_dump` at seeds 42 and 1337
- [ ] The same cases pass **unbounded to natural completion**, with matching total frame counts
- [ ] All 8 options are present with the reference's exact names, defaults, and validation
- [ ] The Unicode fixture case passes for this effect
- [ ] The effect's registry entry preserves the 37-name enumeration order
- [ ] Every trap listed above is addressed, with a comment citing the reference line
- [ ] AOT publish stays warning-free

## Blocked by

- 0011 — First effect end-to-end: `wipe`
