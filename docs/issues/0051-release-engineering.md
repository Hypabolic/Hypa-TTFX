# 0051 — Release engineering: CI, RIDs, and honest claims

**Labels:** `enhancement`, `ready-for-agent`

## What to build

CI, multi-RID publishing, and — importantly — scoping the parity claim to what has actually
been tested.

**`bin/test`** grown to its final shape, in dependency order: prerequisite probe, zero-package
check, unit goldens, AOT publish, CLI corpus, signal tests, then the oracle-dependent suites
behind a platform gate:

```sh
if [ "$(uname -s)" = "Linux" ]; then
  tools/parity/fetch_reference.sh    # inside the gate — macOS shouldn't need a Rust toolchain
  tools/parity/m0_matrix.sh
  tools/parity/run_suite.sh
  tools/parity/tty_compare.sh
  python3 tools/tests/resize_behavior.py
fi
```

The fetch belongs **inside** the gate: only the byte-exact suites need the oracle, and putting
it outside makes macOS CI require `cargo` to run tests it then skips.

**The platform gate stays** unless issue 0009's per-RID measurement removed it. macOS CI runs
the probe, zero-package check, unit goldens (with the boundary-tolerant assertions), the AOT
publish, the CLI corpus, and the signal tests.

**Per-RID publish** for `osx-arm64`, `osx-x64`, `linux-x64`, `linux-arm64`. AOT cross-compilation
needs a matching toolchain, so each builds on its own runner rather than cross-linking.

**Scope the claim honestly.** The parity contract is one machine, one binary pair. Publishing
four RIDs under a claim tested on one is overclaiming — maths-library behavior, AOT codegen,
signal delivery and the `winsize` layout can all differ by architecture, and the `linux-arm64`
measurement cannot be run from a macOS host. Either add a parity job per target RID, or the
README says byte-exact parity is **verified on `linux-x64`** (plus whichever others get a
runner) and **expected but unverified** elsewhere.

**Benchmarks** — extend the inherited `bench_full.py` with a third column, comparing startup and
full-canvas throughput against both the Rust binary and Python TTE. Expect 1–5 ms startup
against the Rust binary's 0.5 ms and Python's ~65 ms. Measure; do not chase.

## Acceptance criteria

- [ ] `bin/test` runs green on `ubuntu-latest` and `macos-latest`, with the platform gate
      applied and the reference fetch inside it
- [ ] The gate's scope matches issue 0009's measurement result
- [ ] All four RIDs publish and the artifacts are uploaded
- [ ] Binary size and startup time are recorded for each RID
- [ ] `bench_full.py` reports hypa-ttfx alongside ttfx and Python TTE
- [ ] The README states which RIDs byte-exact parity is *verified* on, and which are
      *expected but unverified*
- [ ] The reference build is cached by commit hash; a warm CI run does not rebuild it

## Blocked by

- All effect issues (0013–0048)
- 0050 — Coverage hardening
