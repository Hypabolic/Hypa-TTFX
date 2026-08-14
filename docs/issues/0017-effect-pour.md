# 0017 — Effect: `pour`

**Labels:** `enhancement`, `ready-for-agent`

**Wave:** Wave 1 — motion and scene basics  
**Reference:** `src/effects/pour.rs` (279 lines, 10 options)  
**Inherited parity cases:** 6

## What to build

Port the `pour` effect end-to-end: its option table, its registry entry, and the
effect itself, until its parity cases are byte-identical to the reference.

Transcribe from the Rust, not from the Python — the Rust has already resolved the
upstream semantics questions and its comments cite the upstream lines. Keep function
names and internal structure so a side-by-side diff is possible at review.

Read `docs/translation-checklist.md` for this file before starting.

### Inherited parity cases

| Case | Input | Arguments |
|---|---|---|
| `pour-always-xterm` | `colored.txt` | `--existing-color-handling always --xterm-colors pour --pour-direction right` |
| `pour-anchored` | `paragraph.txt` | `--canvas-width 60 --canvas-height 20 --anchor-canvas c --anchor-text c pour` |
| `pour-basic` | `basic.txt` | `pour` |
| `pour-dynamic` | `colored.txt` | `--existing-color-handling dynamic pour` |
| `pour-left` | `paragraph.txt` | `pour --pour-direction left --starting-color ff0000 --final-gradient-frames 3` |
| `pour-up-fast` | `paragraph.txt` | `pour --pour-direction up --pour-speed 4 --gap 0 --movement-speed-range 0.3-0.9 --movement-easing out_bounce` |

### Known traps in this effect

- `remove(0)` FIFO drains at `pour.rs:251, :258, :264`.
- Pour direction and the grouped character sort interact — cover more than the default direction.

## Acceptance criteria

- [ ] All 6 `pour-*` parity cases byte-identical to `ref_dump` at seeds 42 and 1337
- [ ] The same cases pass **unbounded to natural completion**, with matching total frame counts
- [ ] All 10 options are present with the reference's exact names, defaults, and validation
- [ ] The Unicode fixture case passes for this effect
- [ ] The effect's registry entry preserves the 37-name enumeration order
- [ ] Every trap listed above is addressed, with a comment citing the reference line
- [ ] AOT publish stays warning-free

## Blocked by

- 0011 — First effect end-to-end: `wipe`
