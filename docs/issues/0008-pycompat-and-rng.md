# 0008 — PyCompat helpers and the RNG

**Labels:** `enhancement`, `ready-for-agent`

## What to build

The numeric foundation. Get this green before anything downstream, because every later parity
failure is otherwise indistinguishable from an RNG or rounding bug.

**`PyCompat`** — the helpers that every transcribed expression routes through, so each call
site is greppable and covered:

- `TruncToI64(double)` — Rust `as i64` on a float **truncates toward zero**. Not `Math.Round`
  (banker's), not `Convert.ToInt64` (rounds). **18 enumerated sites** in
  `docs/translation-checklist.md` §1, most of which compute a *count* — so rounding instead of
  truncating changes how many RNG draws follow and desynchronizes the rest of the run.
  Special case: `easing.rs:356-357` is `as i64 as usize`, where a negative eased value
  truncates negative then **wraps** to a huge `usize`. Transcribe both steps with `unchecked`.
- `FloorDiv` — Python `//` floors; C# `/` truncates toward zero. They differ for negative
  operands.
- `RoundHalfEven` — `Math.Round(double)` already defaults to `MidpointRounding.ToEven`, so this
  is a thin wrapper. "Thin" is not "untested": pin it against the reference across NaN, ±∞,
  magnitudes beyond `long`, negative zero, and exact halves, where the two may legitimately
  differ (Rust's helper returns `i64` and saturates).
- `FMin` / `FMax` — Rust's `f64::min`/`max` return the **non-NaN** operand; .NET's
  `Math.Min`/`Math.Max` **propagate** NaN, and they differ on signed zero. Nine enumerated
  float sites in checklist §2. Integer `Math.Min`/`Max` are fine.

**`Rng`** — xoshiro256++ with SplitMix64 seed expansion, reimplemented bit-exactly. Pure
integer arithmetic: `wrapping_add`/`wrapping_mul` → `unchecked`, `rotate_left` →
`BitOperations.RotateLeft`, `leading_zeros` → `BitOperations.LeadingZeroCount`.

Helper semantics are the parity contract and must match exactly: `randbelow` by bit-mask
rejection; `randint(a,b)` inclusive; `randrange(a,b)` half-open; `choice = seq[randbelow(len)]`;
`uniform(a,b) = a + (b-a)*random()`; `shuffle` = Fisher–Yates from the top.

**It is an instance on `EngineWorld`, never a static** — the run loop carries RNG state
*forward* across a resize rebuild rather than reseeding.

**Vectors come from `rngdump`** (issue 0002), not from the shipped binary: `next_u64` and
`randbelow` are private and there is no dump flag.

**Cover the rejection loop explicitly.** `randbelow` retries when the masked draw exceeds `n`,
so non-power-of-two ranges consume a *variable* number of `next_u64` calls. A test that only
samples `random()` sequentially will not catch a wrong mask width, and a wrong mask width
desynchronizes every effect.

Unseeded runs use `RandomNumberGenerator.Fill` — BCL, AOT-clean, and never compared.

## Acceptance criteria

- [ ] First 10k draws of every helper match the `rngdump` vectors for several seeds
- [ ] Rejection-loop coverage: non-power-of-two ranges (e.g. `randint(0, 2)`) match draw-for-draw
- [ ] `shuffle` matches CPython's loop order on a known sequence
- [ ] `TruncToI64` truncates toward zero, verified against negative and fractional inputs
- [ ] `easing.rs`'s two-step `as i64 as usize` cast is reproduced, including negative wrap
- [ ] `FloorDiv` floors for negative operands where `/` would truncate
- [ ] `RoundHalfEven` pinned across NaN, ±∞, out-of-`long` magnitudes, negative zero, halves
- [ ] `FMin`/`FMax` return the non-NaN operand and match on signed zero
- [ ] The RNG is an instance; a test proves state continues across a simulated rebuild
- [ ] All 18 truncation sites from checklist §1 route through `TruncToI64` (grep-enforced)

## Blocked by

- 0002 — Parity oracle
