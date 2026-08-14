# 0020 — Effect: `scattered`

**Labels:** `enhancement`, `ready-for-agent`

**Wave:** Wave 1 — motion and scene basics  
**Reference:** `src/effects/scattered.rs` (194 lines, 6 options)  
**Inherited parity cases:** 0

## What to build

Port the `scattered` effect end-to-end: its option table, its registry entry, and the
effect itself, until its parity cases are byte-identical to the reference.

Transcribe from the Rust, not from the Python — the Rust has already resolved the
upstream semantics questions and its comments cite the upstream lines. Keep function
names and internal structure so a side-by-side diff is possible at review.

Read `docs/translation-checklist.md` for this file before starting.

### ⚠️ No inherited parity cases — you must author them

`scattered` is one of **three effects** (`expand`, `scattered`, `slice`) with no entry in
the inherited `cases.txt`. The 'no effect merges with a failing parity case' gate is
vacuous here, because there is no case to fail. Author them in the same shape as the
others before claiming this issue done:

- `scattered-basic|basic.txt|scattered`
- `scattered-custom|paragraph.txt|scattered <non-default options exercising this effect>`
- `scattered-dynamic|colored.txt|--existing-color-handling dynamic scattered`
- `scattered-xterm-anchored|paragraph.txt|--xterm-colors --canvas-width 60 --canvas-height 20 --anchor-canvas c --anchor-text c scattered`

## Acceptance criteria

- [ ] Parity cases authored for `scattered` and committed to `cases.txt`
- [ ] Those cases are byte-identical to `ref_dump` at seeds 42 and 1337
- [ ] The same cases pass **unbounded to natural completion**, with matching total frame counts
- [ ] All 6 options are present with the reference's exact names, defaults, and validation
- [ ] The Unicode fixture case passes for this effect
- [ ] The effect's registry entry preserves the 37-name enumeration order
- [ ] AOT publish stays warning-free

## Blocked by

- 0011 — First effect end-to-end: `wipe`
