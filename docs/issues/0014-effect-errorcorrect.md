# 0014 — Effect: `errorcorrect`

**Labels:** `enhancement`, `ready-for-agent`

**Wave:** Wave 1 — motion and scene basics  
**Reference:** `src/effects/errorcorrect.rs` (445 lines, 8 options)  
**Inherited parity cases:** 4

## What to build

Port the `errorcorrect` effect end-to-end: its option table, its registry entry, and the
effect itself, until its parity cases are byte-identical to the reference.

Transcribe from the Rust, not from the Python — the Rust has already resolved the
upstream semantics questions and its comments cite the upstream lines. Keep function
names and internal structure so a side-by-side diff is possible at review.

Read `docs/translation-checklist.md` for this file before starting.

### Inherited parity cases

| Case | Input | Arguments |
|---|---|---|
| `errorcorrect-basic` | `basic.txt` | `errorcorrect` |
| `errorcorrect-dynamic` | `colored.txt` | `--existing-color-handling dynamic errorcorrect --error-pairs 0.5` |
| `errorcorrect-heavy` | `paragraph.txt` | `errorcorrect --error-pairs 0.4 --swap-delay 2 --error-color ff8800 --correct-color 00ff88 --movement-speed 0.4` |
| `errorcorrect-xterm-anchored` | `paragraph.txt` | `--xterm-colors --canvas-width 60 --canvas-height 20 --anchor-canvas c --anchor-text c errorcorrect` |

### Known traps in this effect

- Float truncation at `errorcorrect.rs:381` — `(error_pairs * characters.len() as f64) as i64`.
- **Two RNG-indexed removals in sequence** at `:387` and `:389`, drawn at `:386` and `:388` — the list shrinks between the draws, so the second draw's range depends on the first removal having shifted correctly.
- `remove(0)` at `:430`.

## Acceptance criteria

- [ ] All 4 `errorcorrect-*` parity cases byte-identical to `ref_dump` at seeds 42 and 1337
- [ ] The same cases pass **unbounded to natural completion**, with matching total frame counts
- [ ] All 8 options are present with the reference's exact names, defaults, and validation
- [ ] The Unicode fixture case passes for this effect
- [ ] The effect's registry entry preserves the 37-name enumeration order
- [ ] Every trap listed above is addressed, with a comment citing the reference line
- [ ] AOT publish stays warning-free

## Blocked by

- 0011 — First effect end-to-end: `wipe`
