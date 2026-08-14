# hypa-ttfx

A C# / .NET 10 Native-AOT port of [`ttfx`](https://github.com/…/ttfx) — a terminal
text-effects CLI with 37 effects. `ttfx` is itself a byte-exact parity port of the Python
package `terminaltexteffects` (TTE) v0.15.0.

**This is a parity port.** Success is not "it works" or "it looks right" — it is *byte-identical
frame output to the reference binary for the same input, config, and seed*. That single fact
determines almost every rule below.

## Current state

Nothing is built. `plan.md`, `docs/` and this file are the only contents.

**Start at [`docs/issues/README.md`](docs/issues/README.md)** — 52 issues with a dependency
graph. Issue `0001` is the first unblocked one. Issue `0011` (`wipe`) is the gate: everything
before it is foundation, and the 36 effect issues after it are fully parallel.

## Where to look

| You need | Read |
|---|---|
| What to work on next | `docs/issues/README.md`, then the numbered issue |
| Why a design decision is what it is | `plan.md` (§ numbers are referenced from the issues) |
| Traps in the specific file you're porting | `docs/translation-checklist.md` — **check this before writing any file** |
| The source of truth for behavior | The Rust reference, not the Python |

The reference checkout is at `~/Development/reference-implementations/ttfx` on this machine.
Once issue 0002 lands, `tools/parity/fetch_reference.sh` pins and builds it properly; until
then, read from that path directly.

## Non-negotiable rules

Each of these prevents a mistake that *looks like an improvement* and silently breaks parity.

1. **Transcribe from the Rust, not the Python.** The Rust already resolved every upstream
   semantics question and its comments cite the upstream lines. Keep function names and
   internal structure so a side-by-side diff works at review. Preserve its comments.
2. **Reproduce upstream's bugs deliberately.** The truncated bezier arc length, banker's
   rounding, integer floor-division gradients, looping scenes reporting themselves complete —
   these are the contract, not defects. Never "fix" one.
3. **Zero NuGet packages.** Everything from `Microsoft.NETCore.App`. Enforced by a grep in
   `bin/test`.
4. **No deferred event queue, ever.** Event actions execute inline at the emission point,
   reentrantly. A queue changes observable frame output even with identical RNG draws.
5. **Ordering is behavior.** `Dictionary` iteration order is not contractual — lookup only.
   `List.Sort` is unstable; the reference uses stable `sort_by` at all 11 sort sites. Sort keys
   read the explicit `CharacterId` field, never a list index.
6. **Every float→int cast goes through `PyCompat.TruncToI64`.** Rust `as i64` truncates toward
   zero; `Math.Round` and `Convert.ToInt64` round. 18 enumerated sites — most compute a count,
   so a rounded one changes the RNG draw sequence and desynchronizes the whole run.
7. **Never substitute an "equivalent" float function.** Not `x * x` for `powf(2.0)`, not
   `Math.Sqrt` for `powf(0.5)`, not `Math.Sqrt(x*x+y*y)` for `hypot`, not `Math.Min`/`Max` for
   Rust's (which return the non-NaN operand where .NET propagates NaN). No epsilon on a
   transcribed float comparison.
8. **One codepoint is one cell.** `Rune`, never `char`. Rust `str::len()` is bytes, not
   `String.Length`.
9. **Invariant everything.** `Parse`, `ToLower`, and `StartsWith`/`IndexOf` are all
   culture-sensitive by default and the reference's are not.

## Definition of done

A slice is done when its parity cases are **byte-identical to the reference**, under the scoped
contract (plan §7.1): `--parity-dump`, explicit canvas dimensions with
`--ignore-terminal-dimensions`, `--frame-rate 0`, fixed `--seed`, identical `COLUMNS`/`LINES`,
one machine, one pinned binary pair.

Effect issues must pass **both** bounded at 400 frames *and* unbounded to natural completion
with matching total frame counts. A matching prefix is not a pass — it hides wrong completion,
final-gradient, and reset behavior.

## When a parity case fails

1. Get the first divergent frame and byte offset from the differ, not by eyeballing frames.
2. Suspect the RNG draw *count* before the RNG itself — a wrong cast or a wrong collection
   operation changes how many draws happen, and every later frame then differs.
3. A divergence is not automatically our bug. Both implementations descend from the same
   Python; if ours agrees with Python and the Rust doesn't, that's a bug in the reference.
   Adjudicate against the Python source, report it upstream, don't "match" a defect.

## Don't

- Don't optimize. The reference's O(n²) algorithms are kept for fidelity and are fine at
  terminal scale. Its perf hacks (inline symbol buffers, bitmap sets, key interning) are
  explicitly optional — plan §5.8 lists which halves are semantics and which aren't.
- Don't widen scope. The only permitted divergences are enumerated in plan §1, §4.5, §5.8 and
  §8.2. Everything else is transcription.
- Don't claim a phase complete on a green bounded suite alone. See "Definition of done".
