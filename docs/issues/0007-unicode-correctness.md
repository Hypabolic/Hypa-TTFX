# 0007 — Unicode correctness: the Rune pipeline

**Labels:** `enhancement`, `ready-for-agent`

## What to build

The highest-risk C#-specific divergence in the port, and one with **zero coverage** in the
inherited suite — every inherited fixture is ASCII.

Rust `str::chars()` yields Unicode scalar values; C# `foreach (char c in s)` yields UTF-16 code
units, splitting non-BMP characters into surrogate pairs. TTE and ttfx treat one codepoint as
one cell. So the parser's cell array is `Rune[]`, not `char[]`, and `input_symbol` is a string
(a Rune's UTF-16 form can be two chars).

The eight `.chars()` sites are enumerated in `docs/translation-checklist.md` §5. Two convert a
codepoint to a *number* and are the concrete traps:

- **`binarypath.rs:159`** — `symbol.chars().next() as u32` formatted `{:08b}`. Taking a UTF-16
  `char` yields the **high surrogate** for astral input, so the rendered binary string is
  wrong. Must be `Rune.Value`.
- **`swarm.rs:113`** — `c.to_digit(10)` on the first character of a path id. No direct C#
  equivalent; write a helper with Rust's semantics (ASCII digits only, `None` outside range).
  **Not** `char.GetNumericValue`, which accepts Unicode digit forms and returns `double`.

Rust's `char::is_ascii_digit` / `is_whitespace` / `is_alphanumeric` are ASCII-scoped and are
**not** interchangeable with `Rune.IsDigit` and friends, which are Unicode predicates with
different membership. Match per site.

Ordinal string comparison: the parser dispatches CSI sequences with `starts_with("\x1b[")`
(`input.rs:63`) and `starts_with('?')` (`:337`). C# `StartsWith(string)` is **culture-sensitive**
by default — use `StringComparison.Ordinal` at every byte-oriented compare. Likewise
`easing.rs:51`'s `to_lowercase()` must be `ToLowerInvariant`.

**New fixture** with astral-plane characters (which `binarypath` will turn into a binary string)
and combining marks, wired into `cases.txt` so every effect exercises it later.

A **lone surrogate cannot appear** in this fixture — input is strictly UTF-8 decoded before
parsing and a surrogate code point is not representable in UTF-8. Test malformed UTF-8 as a
rejection case instead (that lands in 0004).

## Acceptance criteria

- [ ] The input parser's cell array is rune-based; one codepoint occupies one cell
- [ ] A non-BMP + combining-mark fixture is committed and `--m0-dump` on it is byte-identical
      to `ref_m0`
- [ ] `binarypath`'s codepoint-to-binary conversion uses the full scalar value, verified on an
      astral character (covered again by the binarypath effect issue)
- [ ] The `to_digit(10)` helper matches Rust: ASCII digits only, rejects Unicode digit forms
- [ ] Every `StartsWith`/`EndsWith`/`IndexOf(string)` uses `StringComparison.Ordinal`
- [ ] `ToLowerInvariant` is used for easing-name parsing
- [ ] A grep check in `bin/test` fails on bare culture-sensitive `Parse`/`ToLower`/`StartsWith`
- [ ] The Unicode fixture is added to `cases.txt` so later effect issues inherit the coverage

## Blocked by

- 0004 — First byte-identical frame
