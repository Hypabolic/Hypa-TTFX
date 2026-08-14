# 0021 — Effect: `slice`

**Labels:** `enhancement`, `ready-for-agent`

**Wave:** Wave 1 — motion and scene basics  
**Reference:** `src/effects/slice.rs` (267 lines, 6 options)  
**Inherited parity cases:** 0

## What to build

Port the `slice` effect end-to-end: its option table, its registry entry, and the
effect itself, until its parity cases are byte-identical to the reference.

Transcribe from the Rust, not from the Python — the Rust has already resolved the
upstream semantics questions and its comments cite the upstream lines. Keep function
names and internal structure so a side-by-side diff is possible at review.

Read `docs/translation-checklist.md` for this file before starting.

### ⚠️ No inherited parity cases — you must author them

`slice` is one of **three effects** (`expand`, `scattered`, `slice`) with no entry in
the inherited `cases.txt`. The 'no effect merges with a failing parity case' gate is
vacuous here, because there is no case to fail. Author them in the same shape as the
others before claiming this issue done:

- `slice-basic|basic.txt|slice`
- `slice-custom|paragraph.txt|slice <non-default options exercising this effect>`
- `slice-dynamic|colored.txt|--existing-color-handling dynamic slice`
- `slice-xterm-anchored|paragraph.txt|--xterm-colors --canvas-width 60 --canvas-height 20 --anchor-canvas c --anchor-text c slice`

### Known traps in this effect

- `remove(0)` FIFO drains at `slice.rs:230, :241` — `List.RemoveAt(0)`, not `Remove(value)`.

## Acceptance criteria

- [ ] Parity cases authored for `slice` and committed to `cases.txt`
- [ ] Those cases are byte-identical to `ref_dump` at seeds 42 and 1337
- [ ] The same cases pass **unbounded to natural completion**, with matching total frame counts
- [ ] All 6 options are present with the reference's exact names, defaults, and validation
- [ ] The Unicode fixture case passes for this effect
- [ ] The effect's registry entry preserves the 37-name enumeration order
- [ ] Every trap listed above is addressed, with a comment citing the reference line
- [ ] AOT publish stays warning-free

## Blocked by

- 0011 — First effect end-to-end: `wipe`
