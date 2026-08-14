# 0003 — CLI parser core, root options, and the token-edge corpus

**Labels:** `enhancement`, `ready-for-agent`

## What to build

A hand-rolled, table-driven argument parser — no `System.CommandLine` (a package), no
reflection (hostile to AOT and to startup). This is new code with no clap underneath it, and
every later phase runs on top of it, so its edge cases are settled here rather than in polish.

The 15 root terminal options with ttfx's exact names and defaults (`--tab-width 4`,
`--frame-rate 60`, `--canvas-width -1`, `--anchor-canvas sw`, …), the value parsers mirroring
`argutils` (`PositiveInt`, `NonNegativeInt`, `CanvasDimension ≥ -1`, `Ratio`, `ColorArg`,
`Anchor`, `EasingName`), and the two-phase scan: root options **before** the effect name,
effect options after.

Also ships `--probe`, the no-op that emits blank frames forever, used by the signal and resize
tests before any real effect exists. **It must be a root flag, not a registry entry** —
`--random-effect` selects by `ChoiceIndex(names.Count)` over the registry, so a 38th name would
change every random selection for a given seed.

Spec shape:

```csharp
sealed record OptionSpec(
    string Long, char? Short, string MetaVar, string Help,
    OptionArity Arity,            // Flag | One | AtLeastOne | Exactly(n)
    string? Default,
    Func<string, object> Parse);  // throws UsageError with an argparse-shaped message
```

**The token-edge corpus is the point of this issue.** An inherited parity case already breaks a
naive parser: `cases.txt:2` contains `--beam-row-symbols - =` — a multi-value option whose
first value is a lone `-`. A parser that treats a leading dash as an option marker, or stops an
`AtLeastOne` scan at the first dash-prefixed token, rejects a case the suite requires to pass.

Numeric grammars are **not** `double.Parse`. Rust reaches these values through
`str::parse::<f64>()` (`effects/common.rs:6-20`, `:47-64`, `:135-145`), which differs from
.NET's invariant parse on `inf`/`NaN` spellings, leading `+`, and — importantly —
**surrounding whitespace, which Rust rejects and `long.Parse` accepts**. Generate the
acceptance/rejection corpus by running the *reference* parser over a token list and asserting
the C# parser agrees; do not hand-write a list of examples.

Exit codes: **2** for usage errors, **1** for runtime errors, with upstream's stream routing —
file errors and `NO INPUT.` to **stdout**, unsupported-ANSI to **stderr**.

Integer widths are pinned to Rust's: `i64` → `long`, `u64` → `ulong` (seeds, `max_frames`).

## Acceptance criteria

- [ ] All 15 root options parse with ttfx's exact names, defaults, and validation
- [ ] `--beam-row-symbols - =` parses as a two-value option — the lone `-` is a value
- [ ] `--` terminates option scanning; negative numbers parse as values; option-looking symbol
      values (`.`, `:`, `=`) are accepted where the spec expects values
- [ ] Whitespace-padded numbers are **rejected** (`--tab-width ' 4 '` fails), matching Rust
- [ ] The numeric acceptance corpus is generated from the reference parser, not hand-written,
      and the C# parser agrees on every token
- [ ] Usage errors exit 2; runtime errors exit 1; stream routing matches (file errors and
      `NO INPUT.` on stdout, unsupported-ANSI on stderr)
- [ ] `--probe` is a root flag and does not appear in the effect registry
- [ ] Root-before-subcommand ordering is enforced and tested
- [ ] No reflection anywhere in the parser; AOT publish stays warning-free

## Blocked by

- 0001 — Repo scaffold, AOT publish, prerequisite probe
