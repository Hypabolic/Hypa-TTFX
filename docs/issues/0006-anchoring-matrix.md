# 0006 — Anchoring: the real matrix, not the inherited sample

**Labels:** `enhancement`, `ready-for-agent`

## What to build

Canvas and text anchoring across the full option space, verified against the reference.

**The inherited `m0_matrix.sh` is not the cross-product it looks like.** It is 14 hand-picked
variants (`m0_matrix.sh:29-44`) touching only the `c`, `ne`, and one mixed `n`/`se` anchor
configuration — not all nine anchors, and with no cross-product between anchoring and the
colour or wrap options. Since anchoring is precisely where every frame gains leading blank
rows and columns, generate the real matrix here:

- all nine `--anchor-canvas` × all nine `--anchor-text` combinations
- crossed with clipped and unclipped canvas sizes
- keeping the 14 inherited variants on top as the option-interaction cases

Canvas maths to transcribe exactly: the centre formulas (`top//2` plus the odd adjustment), the
anchor offset computation, visible-bounds clamping, `outside_scope` random coordinates landing
exactly one cell beyond an edge, the `-1`/`0` canvas sizing semantics, and
`--ignore-terminal-dimensions` overwriting the terminal dims.

Also `--wrap-text` and `--tab-width` (a tab expands to N space characters), since both change
the grid the anchoring operates on.

## Acceptance criteria

- [ ] A generated 9×9 anchor matrix runs, and every combination is byte-identical to `ref_m0`
- [ ] Each of clipped and unclipped canvas sizes is covered for every anchor pair
- [ ] The 14 inherited option-interaction variants still pass
- [ ] `--canvas-width 0` / `-1` semantics match; `--ignore-terminal-dimensions` overrides
- [ ] `--wrap-text` and non-default `--tab-width` are covered in combination with anchoring
- [ ] Non-southwest anchors produce the expected leading blank rows/columns *inside* the frame
- [ ] The matrix generator is committed, so a reference bump re-runs it rather than re-deriving

## Blocked by

- 0005 — Colour through the same path
