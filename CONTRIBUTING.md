# Contributing to hypa-ttfx

Thanks for wanting to help. This repo is a standalone C# / .NET 10 Native-AOT
CLI and library (`Hypa.Ttfx`). It began as a port of ttfx / TerminalTextEffects
and now evolves on its own.

## How to help

- Bugs and regressions: open an issue with a repro (effect, flags, seed, input).
- Features and API changes: open an issue first if the change is user-visible.
- Docs, examples, and CI: pull requests are welcome without an issue.
- Security: see [SECURITY.md](SECURITY.md), not a public issue.

Please follow the [Code of Conduct](CODE_OF_CONDUCT.md).

## Prerequisites

- .NET SDK 10 (`global.json` pins the feature band)
- On Linux, the Native AOT toolchain (`clang`, `zlib1g-dev`, `binutils`)
- Optional: a `reference/ttfx` binary for the oracle parity suites
  (`tools/parity/fetch_reference.sh`, or `HYPA_TTFX_ORACLE=1` to require it)

This repository does not take NuGet `PackageReference`s. Everything we compile
comes from `Microsoft.NETCore.App`. Downstream apps consume `Hypa.Ttfx` as a
package; that is the supported way to reuse the engine.

## Build, test, pack

```sh
./bin/build              # AOT publish to artifacts/ttfx (host RID)
./bin/test               # the CI gate
./bin/pack               # Hypa.Ttfx + Hypa.Ttfx.Tool → artifacts/nuget
```

`./bin/test` is what CI runs: unit goldens, AOT publish, CLI corpus, signal
tests, and (on Linux, or locally when `reference/ttfx` is present) frame-parity
suites. A green bounded prefix is not enough for effect changes — completion,
final-gradient, and reset behavior have to match the intended contract.

Unit tests alone:

```sh
dotnet run --project tests/Ttfx.Tests -c Release --no-launch-profile
```

## Layout

| Path | Role |
|---|---|
| `src/Ttfx` | Packable library (`Hypa.Ttfx`) |
| `src/Ttfx.Cli` | Native-AOT CLI and `dotnet tool` (`Hypa.Ttfx.Tool`) |
| `tests/Ttfx.Tests` | In-repo unit harness (no xUnit / NUnit) |
| `tools/parity` | Optional byte-compare suites against pinned ttfx |
| `bin/` | `build`, `test`, `pack` |

Public library entry point: `Ttfx.TextEffects` / `Ttfx.TextEffectOptions`.

## House rules

These prevent changes that look like improvements and silently break callers
or the remaining parity suites.

1. **No `PackageReference` in this repo.** `bin/test` greps for it.
2. **Invariant culture.** `Parse`, `ToLower`, `StartsWith`, and `IndexOf` need
   `InvariantCulture` / `Ordinal` (or a char overload). Defaults are culture-sensitive.
3. **One codepoint is one cell.** Use `Rune`, not `char`. Rust `str::len()` is
   bytes, not `String.Length`.
4. **Float → int goes through `PyCompat.TruncToI64`.** `as i64` truncates toward
   zero; `Math.Round` / `Convert.ToInt64` do not. A rounded count changes the
   RNG draw sequence.
5. **Do not substitute "equivalent" float functions.** Not `x * x` for
   `powf(2)`, not `Math.Sqrt` for `powf(0.5)`, not `hypot` via `Sqrt(x*x+y*y)`,
   not .NET `Min`/`Max` (NaN handling differs). No epsilon on transcribed
   float comparisons.
6. **Event actions run inline** at the emission point, reentrantly. A deferred
   queue changes observable frames even with identical RNG draws.
7. **Ordering is behavior.** `Dictionary` iteration is lookup-only.
   `List.Sort` is unstable; use a stable sort. Sort keys read `CharacterId`,
   never a list index.
8. **Do not "fix" inherited quirks** (truncated bezier arc length, banker's
   rounding, integer floor-division gradients, looping scenes that report
   complete) without an issue that treats the change as intentional.

Keep function names and structure close to the existing code so reviews stay
side-by-side. Comments that cite historical line numbers can stay; they are
not a promise that we still match those sources.

## Pull requests

1. Fork and branch from `main`.
2. Keep the change focused. Do not mix a feature with an unrelated refactor.
3. Run `./bin/test` (or the unit harness plus `./bin/build` if you cannot run
   the full gate locally).
4. Fill in the PR template. Link the issue when there is one.
5. Do not add `Co-authored-by: Cursor` or similar tool attribution trailers.

Maintainers cut releases by bumping `Version` in `Directory.Build.props` and
pushing a `vX.Y.Z` tag. See the Releasing section in [README.md](README.md).

## License

By contributing, you agree that your contribution is licensed under the MIT
License in [LICENSE](LICENSE).
