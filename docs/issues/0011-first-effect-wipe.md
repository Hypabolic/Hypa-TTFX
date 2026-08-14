# 0011 — First effect end-to-end: `wipe`

**Labels:** `enhancement`, `ready-for-agent`

## What to build

The slice that proves the whole architecture. Everything below `wipe` has been verified in
pieces; this is the first time a real animation runs from CLI through the engine to the
terminal and is compared frame-for-frame against the reference.

`wipe` is chosen because it is the smallest effect (187 lines, 7 options) and already has 5
parity cases. It is pulled ahead of the wave-1 batch precisely so this issue can demonstrate an
end-to-end byte-identical stream.

**Ships alongside the effect:**

- **`IEffect`** with three members — `Build`, `NextFrame`, and **`DispatchCallback`**. The third
  is the entry point for the `{ id, args }` callback design and is easy to omit; without it the
  engine cannot invoke the per-effect switch at all.
- **`EffectRegistry`** — a static array of specs (name, description, option table, factory).
  No reflection, no assembly scanning. **Enumeration order is observable**: `--random-effect`
  selects by `ChoiceIndex(names.Count)`, so the list must match the reference's order exactly
  (alphabetical, with `randomsequence` under R and `print` under P). Pin all 37 names in a test
  even though only `wipe` is implemented.
- **The tty output path** — `prep_canvas` (hide cursor, scroll to make room or reposition for
  `--reuse-canvas`, DEC save `\x1b7`), the per-frame restore/save + cursor-up preamble, and
  teardown (show cursor, EOL) honouring `--no-eol` / `--no-restore-cursor`. Teardown must run
  on error paths too.
- **Frame pacing** — `1/frame_rate` delay, monotonic check, sleep the remainder, timestamp taken
  **after** the sleep so drift accumulates (faithful), `--frame-rate 0` disables.
- **The harness flags**: `--parity-dump` (length-prefixed frames, forces the virtual clock,
  suppresses the tty path), `--virtual-clock`, `--max-frames`.

**Two clock details that are easy to get wrong:**

1. Virtual `dt` is **not** simply `1/frame_rate`. `ctx.rs:50-52` substitutes `1/60` whenever the
   rate is nonpositive — and the oracle contract runs at `--frame-rate 0`, so this is *the* case
   the suite exercises, not an edge case.
2. The clock advances **inside `frame()`**, not once per `NextFrame` return
   (`ctx.rs:697-702`). An effect can call `frame()` more than once while returning a single
   frame — `unstable.rs` has five call sites with fall-through paths that discard the earlier
   string but keep its clock advance. Hoisting the advance to the run loop desynchronizes
   virtual time, and the effect that notices is `matrix` or `thunderstorm`, phases later.

**`--max-frames N` emits the frame *before* checking the limit** (`effect.rs:92-101`), so
`--max-frames 0` still produces one frame. A `while (count < max)` pre-check yields zero and
silently shifts every bounded comparison in the suite by one.

Render ordering: tick `active_characters` in ascending `CharacterId` (snapshot first, as the
reference does).

## Acceptance criteria

- [ ] All 5 `wipe-*` parity cases are byte-identical to `ref_dump` at both seeds (42, 1337)
- [ ] The same cases pass **unbounded to natural completion**, with matching total frame counts
- [ ] `tty_compare` passes for `wipe`, including `--reuse-canvas`, `--no-eol`, and
      `--no-restore-cursor` variants
- [ ] `IEffect.DispatchCallback` exists and is exercised
- [ ] The registry lists exactly 37 names in reference order; `--probe` is absent
- [ ] `--frame-rate 0` under the virtual clock uses `dt = 1/60`
- [ ] The clock advances inside `frame()`; a test covers an effect calling `frame()` twice for
      one returned frame
- [ ] `--max-frames 0` emits one frame; `--max-frames 1` emits one; a value past completion
      emits all
- [ ] Teardown restores the cursor on the error path as well as the success path
- [ ] `cli_corpus.sh` passes end-to-end (it needs `wipe` and `--parity-dump` to run at all)

## Blocked by

- 0005 — Colour through the same path
- 0010 — Tick machinery
