# 0002 — Parity oracle: fetch, build, and adapt the Rust reference

**Labels:** `enhancement`, `ready-for-agent`

## What to build

The thing every later issue's acceptance criteria depends on: a locally-built `ttfx` binary
that can be invoked as a byte-exact reference, plus the adapter that lets the inherited parity
scripts call it.

**`tools/parity/fetch_reference.sh`** — clone ttfx at the commit pinned in `REFERENCE.md`,
`cargo build --release`, cache the built binary by commit hash so the ~80 s build is paid once.
Nothing inherited does this: the reference tree's own `fetch_reference.sh` fetches *Python
TTE*, not a Rust checkout.

**`tools/parity/reference.sh`** — the adapter, exposing three functions the suites call instead
of the Python drivers they currently invoke:

- `ref_dump` — wraps the reference binary with `--parity-dump` and the case's arguments. The
  flag is **required and easy to miss**: the Python driver had dump behavior built in, so a
  naive "swap the binary path" produces ordinary tty bytes instead of length-prefixed frames.
- `ref_m0` — wraps `--m0-dump`.
- `ref_tty` — a pty launcher for the reference. The inherited `tty_compare.sh` drives its
  reference side through `tools/parity/tty_run.py`, a Python pty launcher, so this one has to
  be written rather than repointed.

**Binary naming.** The harness runs both binaries in one process tree. They must live at
distinct, explicitly-referenced paths (`artifacts/ttfx`, `reference/ttfx`) and never be
resolved through `PATH` — both are named `ttfx`.

**`rngdump.rs`** — a small `examples/` program dropped into the fetched checkout by the fetch
script, exporting RNG draw sequences. The shipped binary has no RNG dump mode and `next_u64`
and `randbelow` are private, so the vectors issue 0008 needs cannot otherwise be produced.

Also copy in, unchanged: `cases.txt` (177 executable cases — line 78 is a comment header),
the four golden fixtures, `cli_corpus.sh`, `sigterm_behavior.py`, `resize_behavior.py`,
`bench_full.py`, and `docs/ordering-inventory.md`.

## Acceptance criteria

- [ ] `fetch_reference.sh` on a clean machine produces a working reference binary; a second run
      hits the cache and does not rebuild
- [ ] `REFERENCE.md` records the pinned ttfx commit and the upstream TTE commit
- [ ] `ref_dump` emits length-prefixed frames and `frames=N` on stderr, for a known case
- [ ] `ref_m0` emits a single preprocessed frame
- [ ] `ref_tty` drives the reference under a pty and captures the full byte stream
- [ ] `cargo run --example rngdump` in the fetched checkout emits reproducible draw sequences
- [ ] The two binaries never collide: both paths are explicit, neither is found via `PATH`
- [ ] `cases.txt` and the four golden fixtures are copied in and their provenance recorded

## Blocked by

- 0001 — Repo scaffold, AOT publish, prerequisite probe
