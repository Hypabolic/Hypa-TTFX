# 0025 — Effect: `binarypath`

**Labels:** `enhancement`, `ready-for-agent`

**Wave:** Wave 2 — gradients, synced scenes, sequence easers  
**Reference:** `src/effects/binarypath.rs` (426 lines, 6 options)  
**Inherited parity cases:** 5

## What to build

Port the `binarypath` effect end-to-end: its option table, its registry entry, and the
effect itself, until its parity cases are byte-identical to the reference.

Transcribe from the Rust, not from the Python — the Rust has already resolved the
upstream semantics questions and its comments cite the upstream lines. Keep function
names and internal structure so a side-by-side diff is possible at review.

Read `docs/translation-checklist.md` for this file before starting.

### Inherited parity cases

| Case | Input | Arguments |
|---|---|---|
| `binarypath-basic` | `basic.txt` | `binarypath` |
| `binarypath-custom` | `paragraph.txt` | `binarypath --binary-colors ff0000 00ff00 0000ff --movement-speed 2 --active-binary-groups 0.3 --final-gradient-direction vertical` |
| `binarypath-dynamic` | `colored.txt` | `--existing-color-handling dynamic binarypath` |
| `binarypath-single` | `single.txt` | `binarypath` |
| `binarypath-xterm-anchored` | `paragraph.txt` | `--xterm-colors --canvas-width 60 --canvas-height 20 --anchor-canvas c --anchor-text c binarypath --active-binary-groups 0.15` |

### Known traps in this effect

- **Codepoint → binary at `binarypath.rs:159`** — `symbol.chars().next() as u32` formatted `{:08b}`. A UTF-16 `char` yields the *high surrogate* for astral input. Must be `Rune.Value`. This effect is the reason the Unicode fixture exists.
- Float truncations at `:206` and `:354`.
- RNG-indexed removal at `:367` (index drawn at `:366`).
- `remove(0)` at `:376, :403`.

## Acceptance criteria

- [ ] All 5 `binarypath-*` parity cases byte-identical to `ref_dump` at seeds 42 and 1337
- [ ] The same cases pass **unbounded to natural completion**, with matching total frame counts
- [ ] All 6 options are present with the reference's exact names, defaults, and validation
- [ ] The Unicode fixture case passes for this effect
- [ ] The effect's registry entry preserves the 37-name enumeration order
- [ ] Every trap listed above is addressed, with a comment citing the reference line
- [ ] AOT publish stays warning-free

## Blocked by

- 0011 — First effect end-to-end: `wipe`
