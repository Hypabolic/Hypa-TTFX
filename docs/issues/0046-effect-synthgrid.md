# 0046 — Effect: `synthgrid`

**Labels:** `enhancement`, `ready-for-agent`

**Wave:** Wave 3 — heavy machinery (spanning trees, particles, clocks)  
**Reference:** `src/effects/synthgrid.rs` (546 lines, 10 options)  
**Inherited parity cases:** 6

## What to build

Port the `synthgrid` effect end-to-end: its option table, its registry entry, and the
effect itself, until its parity cases are byte-identical to the reference.

Transcribe from the Rust, not from the Python — the Rust has already resolved the
upstream semantics questions and its comments cite the upstream lines. Keep function
names and internal structure so a side-by-side diff is possible at review.

Read `docs/translation-checklist.md` for this file before starting.

### Inherited parity cases

| Case | Input | Arguments |
|---|---|---|
| `synthgrid-always-xterm` | `colored.txt` | `--existing-color-handling always --xterm-colors synthgrid` |
| `synthgrid-anchored` | `paragraph.txt` | `--canvas-width 60 --canvas-height 20 --anchor-canvas c --anchor-text c synthgrid` |
| `synthgrid-basic` | `basic.txt` | `synthgrid` |
| `synthgrid-custom` | `paragraph.txt` | `synthgrid --grid-gradient-stops CC00CC ffffff --grid-gradient-steps 6 --grid-gradient-direction horizontal --text-gradient-stops ff0000 00ff00 --text-gradient-steps 8 --text-gradient-direction diagonal --grid-row-symbol = --grid-column-symbol : --text-generation-symbols # @ % --max-active-blocks 0.5` |
| `synthgrid-dynamic` | `colored.txt` | `--existing-color-handling dynamic synthgrid` |
| `synthgrid-single` | `single.txt` | `synthgrid` |

### Known traps in this effect

- **Callback payload captured in a loop** — `synthgrid.rs:454-462` registers a callback carrying the group number. Same capture hazard as `burn`.
- `remove(0)` at `:102, :117, :506`.
- Grid-block generation interacts with canvas dimensions; cover anchored and clipped canvases.

## Acceptance criteria

- [ ] All 6 `synthgrid-*` parity cases byte-identical to `ref_dump` at seeds 42 and 1337
- [ ] The same cases pass **unbounded to natural completion**, with matching total frame counts
- [ ] All 10 options are present with the reference's exact names, defaults, and validation
- [ ] The Unicode fixture case passes for this effect
- [ ] The effect's registry entry preserves the 37-name enumeration order
- [ ] Every trap listed above is addressed, with a comment citing the reference line
- [ ] AOT publish stays warning-free

## Blocked by

- 0011 — First effect end-to-end: `wipe`
