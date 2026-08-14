# Reference pins

The parity oracle is a locally built Rust `ttfx` binary at a pinned commit.
`ttfx` is itself a port of Python `terminaltexteffects` (TTE) v0.15.0.

`tools/parity/fetch_reference.sh` reads the `ttfx_*` keys from the block below.

```
ttfx_commit=6e24dac78e3011d89bd7ff24d1ad91dd89e11d8a
ttfx_remote=https://github.com/omacom-io/ttfx.git
ttfx_remote_ssh=git@github.com:omacom-io/ttfx.git
tte_commit=7a91dd9ca6ee0c4f4b1484efee0ecac1bb84104e
tte_remote=https://github.com/ChrisBuilds/terminaltexteffects.git
```

## ttfx (Rust oracle)

- **Remote (HTTPS):** https://github.com/omacom-io/ttfx.git
- **Remote (SSH):** git@github.com:omacom-io/ttfx.git
- **Commit:** `6e24dac78e3011d89bd7ff24d1ad91dd89e11d8a`
- **Tag / version:** v0.3.1 (package version in that commit's `Cargo.toml`)
- **Optional local clone source:** `/Users/matthew/Development/reference-implementations/ttfx`
  (used only when that tree contains the pinned commit; a clean machine clones from GitHub)

The fetch script installs the release binary at `reference/ttfx` (gitignored) and
caches it at `reference/cache/<full-hash>/ttfx`. That path is distinct from the
C# AOT binary at `artifacts/ttfx`. Neither is placed on `PATH`.

## TerminalTextEffects (upstream Python)

- **Remote:** https://github.com/ChrisBuilds/terminaltexteffects.git
- **Commit:** `7a91dd9ca6ee0c4f4b1484efee0ecac1bb84104e`
- **Tag:** v0.15.0

This is the TTE revision `ttfx` @ `6e24dac` itself pins (see that tree's
`tools/parity/fetch_reference.sh` and `REFERENCE.md`). It is recorded here for
provenance; this issue does not fetch the Python tree.

## Copied assets

Every file below is a verbatim copy from `ttfx` @ `6e24dac78e3011d89bd7ff24d1ad91dd89e11d8a`.
Do not edit them in this repo to "fix" anything — they are the inherited contract.

| Path in ttfx @ 6e24dac | Path in this repo |
|---|---|
| `tools/parity/cases.txt` | `tools/parity/cases.txt` |
| `tests/fixtures/easing_goldens.bin` | `tests/Ttfx.Tests/fixtures/easing_goldens.bin` |
| `tests/fixtures/engine_traces.txt` | `tests/Ttfx.Tests/fixtures/engine_traces.txt` |
| `tests/fixtures/geometry_goldens.txt` | `tests/Ttfx.Tests/fixtures/geometry_goldens.txt` |
| `tests/fixtures/graphics_goldens.txt` | `tests/Ttfx.Tests/fixtures/graphics_goldens.txt` |
| `tools/tests/cli_corpus.sh` | `tools/tests/cli_corpus.sh` |
| `tools/tests/sigterm_behavior.py` | `tools/tests/sigterm_behavior.py` |
| `tools/tests/resize_behavior.py` | `tools/tests/resize_behavior.py` |
| `tools/tests/bench_full.py` | `tools/tests/bench_full.py` |
| `docs/ordering-inventory.md` | `docs/ordering-inventory.md` |

## Original files in this slice (not copies)

| Path | Role |
|---|---|
| `tools/parity/fetch_reference.sh` | Clone/build/cache the pinned `ttfx` binary |
| `tools/parity/reference.sh` | `ref_dump` / `ref_m0` / `ref_tty` adapter |
| `tools/parity/rngdump.rs` | Dropped into the fetched checkout as `examples/rngdump.rs` |
| `tools/parity/easingdump.rs` | Dropped into the fetched checkout as `examples/easingdump.rs` |
| `tools/parity/geometrydump.rs` | Dropped into the fetched checkout as `examples/geometrydump.rs` |
| `tools/parity/pty_launch.py` | Real-pty launcher used by `ref_tty` |
