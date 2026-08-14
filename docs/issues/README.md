# hypa-ttfx issues

52 issues breaking down [`plan.md`](../../plan.md) into independently-grabbable vertical
slices. Each slice cuts through every layer it touches and is verified by the parity oracle
rather than by inspection — a completed issue is demonstrably byte-identical to the reference,
not "looks right".

Every effect issue should be read alongside [`docs/translation-checklist.md`](../translation-checklist.md),
which enumerates the trap sites per file.

## Order of work

```
0001 scaffold + AOT publish
 ├── 0002 parity oracle ──┐
 └── 0003 CLI parser ─────┤
                          ├── 0004 first byte-identical frame
                          │    ├── 0005 colour ── 0006 anchoring
                          │    └── 0007 unicode
                          └── 0008 pycompat + RNG
                               └── 0009 easing/geometry goldens
                                    └── 0010 tick machinery
                                         └── 0011 wipe (first effect, proves everything)
                                              ├── 0012 signals + resize
                                              ├── 0013–0048  the other 36 effects  ← parallel
                                              └── 0049 CLI surface
                                                   └── 0050 coverage → 0051 release
0052 licensing (any time; before publication)
```

**0011 is the gate.** Everything before it is foundation; the 36 effect issues after it are
fully parallel and can be picked up independently.

## Foundation

| # | Issue | Blocked by |
|---|---|---|
| [0001](0001-repo-scaffold-and-aot-publish.md) | Repo scaffold, AOT publish, prerequisite probe | — |
| [0002](0002-parity-oracle.md) | Parity oracle: fetch, build, adapt the Rust reference | 0001 |
| [0003](0003-cli-parser-core.md) | CLI parser core, root options, token-edge corpus | 0001 |

## First tracer bullet — text in, correct bytes out

| # | Issue | Blocked by |
|---|---|---|
| [0004](0004-first-byte-identical-frame.md) | First byte-identical frame: `--m0-dump` for plain ASCII | 0002, 0003 |
| [0005](0005-colour-pipeline.md) | Colour through the same path | 0004 |
| [0006](0006-anchoring-matrix.md) | Anchoring: the real matrix, not the inherited sample | 0005 |
| [0007](0007-unicode-correctness.md) | Unicode correctness: the Rune pipeline | 0004 |

## Engine

| # | Issue | Blocked by |
|---|---|---|
| [0008](0008-pycompat-and-rng.md) | PyCompat helpers and the RNG | 0002 |
| [0009](0009-easing-geometry-goldens.md) | Easing, geometry, goldens on the published binary | 0001, 0008 |
| [0010](0010-tick-machinery.md) | Tick machinery: motion, scenes, events, particles | 0008, 0009 |
| [0011](0011-first-effect-wipe.md) | **First effect end-to-end: `wipe`** | 0005, 0010 |
| [0012](0012-signals-and-resize.md) | Signals and the resize debounce | 0003, 0011 |

## Effects — all blocked by 0011, otherwise parallel

Wave 1 — motion and scene basics:

| # | Effect | Lines | Options | Cases |
|---|---|---|---|---|
| [0013](0013-effect-bouncyballs.md) | `bouncyballs` | 253 | 8 | 4 |
| [0014](0014-effect-errorcorrect.md) | `errorcorrect` | 445 | 8 | 4 |
| [0015](0015-effect-expand.md) | `expand` | 202 | 5 | **0 — author them** |
| [0016](0016-effect-middleout.md) | `middleout` | 264 | 9 | 5 |
| [0017](0017-effect-pour.md) | `pour` | 279 | 10 | 6 |
| [0018](0018-effect-rain.md) | `rain` | 251 | 7 | 4 |
| [0019](0019-effect-randomsequence.md) | `randomsequence` | 194 | 5 | 8 |
| [0020](0020-effect-scattered.md) | `scattered` | 194 | 6 | **0 — author them** |
| [0021](0021-effect-slice.md) | `slice` | 267 | 6 | **0 — author them** |
| [0022](0022-effect-slide.md) | `slide` | 309 | 10 | 6 |
| [0023](0023-effect-spray.md) | `spray` | 241 | 7 | 4 |

Wave 2 — gradients, synced scenes, sequence easers:

| # | Effect | Lines | Options | Cases |
|---|---|---|---|---|
| [0024](0024-effect-beams.md) | `beams` | 435 | 13 | 4 |
| [0025](0025-effect-binarypath.md) | `binarypath` | 426 | 6 | 5 |
| [0026](0026-effect-blackhole.md) | `blackhole` | 616 | 5 | 6 |
| [0027](0027-effect-bubbles.md) | `bubbles` | 522 | 10 | 5 |
| [0028](0028-effect-colorshift.md) | `colorshift` | 275 | 12 | 6 |
| [0029](0029-effect-crumble.md) | `crumble` | 476 | 3 | 6 |
| [0030](0030-effect-decrypt.md) | `decrypt` | 367 | 5 | 6 |
| [0031](0031-effect-fireworks.md) | `fireworks` | 421 | 9 | 4 |
| [0032](0032-effect-highlight.md) | `highlight` | 176 | 6 | 5 |
| [0033](0033-effect-orbittingvolley.md) | `orbittingvolley` | 395 | 12 | 4 |
| [0034](0034-effect-overflow.md) | `overflow` | 280 | 6 | 6 |
| [0035](0035-effect-print.md) | `print` | 399 | 6 | 6 |
| [0036](0036-effect-rings.md) | `rings` | 590 | 9 | 4 |
| [0037](0037-effect-spotlights.md) | `spotlights` | 371 | 8 | 6 |
| [0038](0038-effect-swarm.md) | `swarm` | 471 | 8 | 6 |
| [0039](0039-effect-sweep.md) | `sweep` | 257 | 6 | 5 |
| [0040](0040-effect-unstable.md) | `unstable` | 479 | 8 | 6 |
| [0041](0041-effect-waves.md) | `waves` | 282 | 10 | 5 |

Wave 3 — heavy machinery (spanning trees, particles, clocks):

| # | Effect | Lines | Options | Cases |
|---|---|---|---|---|
| [0042](0042-effect-burn.md) | `burn` | 380 | 6 | 5 |
| [0043](0043-effect-laseretch.md) | `laseretch` | 491 | 11 | 6 |
| [0044](0044-effect-matrix.md) | `matrix` | 659 | 13 | 6 |
| [0045](0045-effect-smoke.md) | `smoke` | 264 | 7 | 5 |
| [0046](0046-effect-synthgrid.md) | `synthgrid` | 546 | 10 | 6 |
| [0047](0047-effect-thunderstorm.md) | `thunderstorm` | 922 | 12 | 4 |
| [0048](0048-effect-vhstape.md) | `vhstape` | 620 | 9 | 4 |

`wipe` (187 lines, 7 options, 5 cases) is issue 0011 — pulled ahead so the first end-to-end
byte-identical stream can be demonstrated.

## Completion

| # | Issue | Blocked by |
|---|---|---|
| [0049](0049-cli-surface-completion.md) | CLI surface: random-effect, filtering, completions | 0011, 0012 |
| [0050](0050-coverage-hardening.md) | Coverage hardening: make the green suite mean something | all effects |
| [0051](0051-release-engineering.md) | Release engineering: CI, RIDs, honest claims | all effects, 0050 |
| [0052](0052-licensing-and-attribution.md) | Licensing, attribution, README | — (before publication) |

## Notes

**Three effects have no inherited parity cases.** `expand`, `scattered` and `slice` are absent
from `cases.txt` entirely — 34 of 37 effects are covered. The "no effect merges with a failing
parity case" gate is vacuous for those three, so their issues require authoring the cases
first. This was found by sweeping the reference, not by review.

**Every issue is verified against the oracle**, under the scoped contract in plan §7.1:
`--parity-dump` (which forces the virtual clock and suppresses the tty path), explicit canvas
dimensions with `--ignore-terminal-dimensions`, `--frame-rate 0`, a fixed `--seed`, identical
`COLUMNS`/`LINES`, one machine, one pinned binary pair.

**Bounded and unbounded.** Every effect issue requires its cases to pass both truncated at 400
frames *and* unbounded to natural completion with matching frame counts — a prefix match is not
a pass.
