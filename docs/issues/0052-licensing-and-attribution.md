# 0052 — Licensing, attribution, and README

**Labels:** `enhancement`, `ready-for-human`

## What to build

This is a port of a port, and the attribution chain has to be right before anything is
published. Flagged `ready-for-human` because licence text is a judgement call, not an agent
task.

**The chain:** TerminalTextEffects (ChrisBuilds, MIT) → ttfx (a Rust parity port, MIT,
preserving the original copyright in its `LICENSE` and `NOTICE`) → this C# port.

- **`LICENSE`** must carry the original TerminalTextEffects copyright, ttfx's copyright, and
  ours. Follow ttfx's own file as the model — it already solved the two-party version of this
  problem.
- **`NOTICE`** carries the attribution in full, naming both upstreams and what each contributed.
- **`README`** leads with credit, as ttfx's does: every effect, the animation engine, and the
  CLI are ChrisBuilds' design; ttfx translated them to Rust; this project translates that to C#
  and adds nothing to the art. Effect *ideas* belong upstream, where they came from.
- **`REFERENCE.md`** pins both commits — the ttfx commit this port targets and the upstream TTE
  commit ttfx targets.

**Also document the deliberate divergences** in the README, so they are stated rather than
discovered:

- RNG is xoshiro256++, not CPython's Mersenne Twister — `--seed` is reproducible within this
  port and against ttfx, but not against Python TTE (inherited from ttfx).
- Broken-pipe exit status is 0, not 141 (plan §8.2), unless issue 0012's errno route worked.
- SIGTERM exit status, if issue 0012 could not match it.
- Shell completions are hand-written rather than generated, so their text differs.
- No Python plugin effects; no wide-character (`wcwidth`) handling — one codepoint is one cell,
  faithfully reproducing upstream.
- Byte-exact parity is verified on the RIDs issue 0051 names, not all four.

## Acceptance criteria

- [ ] `LICENSE` carries all three copyrights, modelled on ttfx's
- [ ] `NOTICE` states the full attribution chain
- [ ] `README` leads with credit to ChrisBuilds and to ttfx, and links both
- [ ] `REFERENCE.md` pins both commits
- [ ] Every deliberate divergence is listed in the README with a one-line reason
- [ ] The parity claim in the README is scoped to tested RIDs
- [ ] A human has reviewed the attribution before first publication

## Blocked by

None — can start immediately, but must be complete before the repo is made public.
