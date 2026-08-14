# 0037 — Effect: `spotlights`

**Labels:** `enhancement`, `ready-for-agent`

**Wave:** Wave 2 — gradients, synced scenes, sequence easers  
**Reference:** `src/effects/spotlights.rs` (371 lines, 8 options)  
**Inherited parity cases:** 6

## What to build

Port the `spotlights` effect end-to-end: its option table, its registry entry, and the
effect itself, until its parity cases are byte-identical to the reference.

Transcribe from the Rust, not from the Python — the Rust has already resolved the
upstream semantics questions and its comments cite the upstream lines. Keep function
names and internal structure so a side-by-side diff is possible at review.

Read `docs/translation-checklist.md` for this file before starting.

### Inherited parity cases

| Case | Input | Arguments |
|---|---|---|
| `spotlights-always-xterm` | `colored.txt` | `--existing-color-handling always --xterm-colors spotlights --search-duration 50` |
| `spotlights-anchored` | `paragraph.txt` | `--canvas-width 60 --canvas-height 20 --anchor-canvas c --anchor-text c spotlights --search-duration 60` |
| `spotlights-basic` | `basic.txt` | `spotlights --search-duration 60` |
| `spotlights-custom` | `paragraph.txt` | `spotlights --beam-width-ratio 1.5 --beam-falloff 0.5 --search-duration 40 --search-speed-range 0.5-1.0 --spotlight-count 5 --final-gradient-stops ff0000 00ff00 --final-gradient-steps 6 --final-gradient-direction horizontal` |
| `spotlights-dynamic` | `colored.txt` | `--existing-color-handling dynamic spotlights --search-duration 50` |
| `spotlights-single` | `single.txt` | `spotlights --search-duration 30 --spotlight-count 1` |

### Known traps in this effect

- Float truncation at `spotlights.rs:324`.
- Float `min`/`max` at `:235` (`.max(0.2)`) and `:324` — must be `PyCompat.FMin`/`FMax`, since .NET propagates NaN where Rust returns the non-NaN operand.

## Acceptance criteria

- [ ] All 6 `spotlights-*` parity cases byte-identical to `ref_dump` at seeds 42 and 1337
- [ ] The same cases pass **unbounded to natural completion**, with matching total frame counts
- [ ] All 8 options are present with the reference's exact names, defaults, and validation
- [ ] The Unicode fixture case passes for this effect
- [ ] The effect's registry entry preserves the 37-name enumeration order
- [ ] Every trap listed above is addressed, with a comment citing the reference line
- [ ] AOT publish stays warning-free

## Blocked by

- 0011 — First effect end-to-end: `wipe`
