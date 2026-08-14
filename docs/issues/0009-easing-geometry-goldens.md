# 0009 — Easing, geometry, and goldens on the published binary

**Labels:** `enhancement`, `ready-for-agent`

## What to build

The 31 named easings plus `MakeEasing`'s cubic-bezier Newton–Raphson solve, and the geometry
module — verified against the Python-derived golden fixtures, which are the strongest available
evidence that the C# matches *Python*, not merely the Rust.

**Bugs to reproduce, not fix:**

- `find_length_of_bezier_curve` omits the final t=0.9→1.0 span (a 10-sample loop bug). Path
  lengths are systematically short and `max_steps` depends on it.
- Row deltas are doubled in path distances, circle x-offsets are doubled, diagonal/radial
  gradient maths doubles rows. Copy each call site's choice.
- `find_normalized_distance_from_center` **rejects** out-of-rectangle coordinates with an error
  rather than clamping.

**Float functions that must not be "simplified"** (checklist §2 — 47 enumerated sites):

- `.powf(2.0)` → `Math.Pow(x, 2.0)`, **never** `x * x`. `.powf(0.5)` → `Math.Pow(x, 0.5)`,
  **never** `Math.Sqrt`. `Math.Sqrt` is correctly rounded and `Math.Pow` is not; they differ in
  the last ulp, and `geometry.rs:88` / `:233-234` feed coordinate quantization where that flips
  a cell.
- `f64::hypot` (`geometry.rs:204, 206`) → `double.Hypot`, never `Math.Sqrt(x*x + y*y)`.
- The ten exact `p == 0.0` / `p == 1.0` guards in the exponential and elastic easings
  (checklist §3) stay exact — no epsilon.

**The goldens must run under the same compiler as the product.** `dotnet run` on a test console
app compiles with **RyuJIT**; `bin/build` compiles with **ILC**, and they can differ on exactly
what this port is sensitive to — `Math.Pow` strength reduction, constant folding, FMA
contraction. ttfx documents its own instance: optimized Rust const-folds some `powf` calls and
its release tests tolerate 1 ulp on `CubicBezier` for that reason. So M1 can be green under JIT
and the next phase fail on identical source. Either publish the test project AOT for the target
RID, or have the *published* binary emit the golden dumps through a hidden flag.

**Assert at ttfx's tolerances, not `cmp`.** The reference is bit-exact on Linux/glibc *except*
`CubicBezier` (1 ulp), and uses `1e-15` absolute elsewhere including macOS
(`easing_goldens.rs:60-72`). "Consume the fixtures verbatim" means the fixture *data*, not
bit-exact assertions.

**Per-RID transcendental measurement.** Dump easing at 1e-3 steps and the geometry lattice from
both binaries and compare, per RID. The meaningful comparison target is the **quantized integer
lattice** — a single legitimate ulp on a raw float would wrongly freeze the platform gate. The
result decides where byte-exact parity CI can run (plan §7.7).

## Acceptance criteria

- [ ] `easing_goldens.bin` and `geometry_goldens.txt` pass, asserted with ttfx's tolerance
      schedule (bit-exact on Linux/glibc except `CubicBezier` at 1 ulp; `1e-15` elsewhere)
- [ ] Goldens run against an **AOT-published** binary, not `dotnet run`
- [ ] All 31 easings plus `MakeEasing` are covered
- [ ] The truncated bezier arc-length bug is reproduced (a test pins the short length)
- [ ] Doubled row deltas match at every call site
- [ ] `find_normalized_distance_from_center` throws for out-of-rectangle input
- [ ] No `x*x` or `Math.Sqrt` substitution for `powf`; grep-enforced
- [ ] The per-RID measurement runs and its result is recorded in the plan, deciding the
      platform gate for byte-exact CI

## Blocked by

- 0001 — Repo scaffold, AOT publish, prerequisite probe
- 0008 — PyCompat helpers and the RNG
