# 0030 — Effect: `decrypt`

**Labels:** `enhancement`, `ready-for-agent`

**Wave:** Wave 2 — gradients, synced scenes, sequence easers  
**Reference:** `src/effects/decrypt.rs` (367 lines, 5 options)  
**Inherited parity cases:** 6

## What to build

Port the `decrypt` effect end-to-end: its option table, its registry entry, and the
effect itself, until its parity cases are byte-identical to the reference.

Transcribe from the Rust, not from the Python — the Rust has already resolved the
upstream semantics questions and its comments cite the upstream lines. Keep function
names and internal structure so a side-by-side diff is possible at review.

Read `docs/translation-checklist.md` for this file before starting.

### Inherited parity cases

| Case | Input | Arguments |
|---|---|---|
| `decrypt-always-xterm` | `colored.txt` | `--existing-color-handling always --xterm-colors decrypt` |
| `decrypt-basic` | `basic.txt` | `decrypt` |
| `decrypt-custom` | `paragraph.txt` | `decrypt --typing-speed 3 --ciphertext-colors ff0000 00ff00 0000ff --final-gradient-stops eda000 00d1ff --final-gradient-direction horizontal` |
| `decrypt-dynamic` | `colored.txt` | `--existing-color-handling dynamic decrypt` |
| `decrypt-single` | `single.txt` | `decrypt --typing-speed 1` |
| `decrypt-xterm-anchored` | `paragraph.txt` | `--xterm-colors --canvas-width 60 --canvas-height 20 --anchor-canvas c --anchor-text c decrypt` |

### Known traps in this effect

- `remove(0)` FIFO drain at `decrypt.rs:334`.

## Acceptance criteria

- [ ] All 6 `decrypt-*` parity cases byte-identical to `ref_dump` at seeds 42 and 1337
- [ ] The same cases pass **unbounded to natural completion**, with matching total frame counts
- [ ] All 5 options are present with the reference's exact names, defaults, and validation
- [ ] The Unicode fixture case passes for this effect
- [ ] The effect's registry entry preserves the 37-name enumeration order
- [ ] Every trap listed above is addressed, with a comment citing the reference line
- [ ] AOT publish stays warning-free

## Blocked by

- 0011 — First effect end-to-end: `wipe`
