# 0050 — Coverage hardening: make the green suite mean something

**Labels:** `enhancement`, `ready-for-agent`

## What to build

The inherited suite is thinner than its size suggests, and this issue closes the gap.

354 checks sounds comprehensive. What it actually is: **177 cases truncated at 400 frames, at
2 seeds, over four ASCII fixtures** (`basic.txt`, `single.txt`, `colored.txt`,
`paragraph.txt`), all well-formed. The tty suite concentrates its variants on `randomsequence`.
So the suite can go green while the port is still wrong.

Additions, each closing a class of error permanently:

- **Malformed and adversarial ANSI.** Cursor-movement overwrites, ignored SGR parameter values,
  private modes, truncated CSI. The reference's own plan lists the corpus; the shipped suite
  barely samples it. Note the two quirks this must pin: unsupported SGR *parameter values* are
  silently ignored while malformed *sequences* raise, and `_input_colors_frequency` increments
  at character-creation time so colours of cells later overwritten still count.
- **Numeric edge cases** in option parsing — `0`, `-1`, values beyond `int`, `NaN`/`inf`
  spellings for ratio options, whitespace-padded numbers (which Rust rejects).
- **Every multi-value option**, since the hand-rolled parser is new code where the reference
  inherited clap's tested behavior.
- **Long runs.** 400 frames truncates the slow effects well before their final phase, so a port
  can match a prefix while being wrong about completion, the final gradient, the
  reset-to-final-appearance, or the stop condition. Run every case unbounded as well as bounded,
  comparing total frame count.
- **The paths the object-graph design actually touches** — path/scene replacement during
  reentrant dispatch, duplicate registrations with structurally equal payloads, `--max-frames 0`.
  These belong in the engine state traces (issue 0010), not the frame-parity suite, because
  frame parity cannot reach them.

**The unbounded runs need a watchdog.** Some configurations legitimately never terminate —
`colorshift --cycles 0` loops forever by design — and a port bug that leaks `active_characters`
(easy, given the looping-scene quirk) turns an unbounded run into a hung CI job rather than a
failure. Cap by wall clock and frame count, treat the cap as a failure, and exclude the
known-infinite configurations by name.

## Acceptance criteria

- [ ] An adversarial ANSI corpus is committed and every case matches the reference, including
      the silently-ignored SGR parameter values and the colour-frequency counting quirk
- [ ] Numeric edge cases covered for every option kind, generated against the reference parser
- [ ] Every multi-value option has a case
- [ ] Every effect runs unbounded to completion with a matching total frame count
- [ ] A watchdog caps unbounded runs; exceeding it fails rather than hangs
- [ ] Known-infinite configurations (`colorshift --cycles 0`) are excluded by name, not by
      timeout
- [ ] The new cases are wired into `bin/test` and run in CI

## Blocked by

- All effect issues (0013–0048)
