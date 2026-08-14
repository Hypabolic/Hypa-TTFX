# 0005 — Colour through the same path

**Labels:** `enhancement`, `ready-for-agent`

## What to build

The same stdin→bytes path as 0004, now carrying colour. This is what makes the rest of the
`m0_matrix` evaluable, and it pulls `Graphics`, `Hexterm` and the static half of `Animation`
into M0 — the M0/M1 boundary is drawn by what a frame needs, not by file.

- **SGR parsing** in the input parser: the supported subset, bold bumping a pending standard
  foreground by +8, and the quirk that **unsupported SGR parameter values are silently
  ignored** (the SGR loop has no error fallback — only malformed or unsupported *sequences*
  raise). `_input_colors_frequency` increments at character-creation time, so colours of cells
  later overwritten by cursor movement still count.
- **`Color` / `ColorPair`** — equality is on the *original argument*: `Color(255) != Color("ffffff")`.
  Needed for `input_colors_frequency` keying.
- **`Gradient`** — channel deltas are **integer floor division** (`(end - start) // steps`),
  not float lerp. Python `//` floors; C# `/` truncates toward zero, and they differ for
  negative deltas. Includes the exact end-stop append per pair, the shared-stop skip, and
  `loop` appending stop[0]. `get_color_at_fraction` **rejects** out-of-range input rather than
  clamping.
- **`Hexterm`** — the 256-entry table and its nearest-match metric: minimum *mean absolute
  channel difference* via linear scan, first minimum wins. Not Euclidean, not perceptual.
- **`existing_color_handling`** — all three modes, with `always` overriding every colour and
  applying **at parse time**, plus the `preexisting_colors_present` scan.
- `--xterm-colors`, `--no-color`, `--terminal-background-color`.

**Hex parsing has two quirks** (`hexterm.rs:74-79`): a stripped length of **6 or 7** is valid,
and `parse_rgb` uses the first six. `Convert.FromHexString` throws on odd length and cannot be
used. The two `#`-strips also differ deliberately — `trim_start_matches('#')` for the length
check, `trim_matches('#')` for the radix parse.

**Truncation vs rounding is inconsistent upstream and must stay that way.**
`shift_color_towards` uses `int(x*255)` truncation (`graphics.rs:361`);
`adjust_color_brightness` uses `round(x*255)` half-to-even. And negative components format as
`"-3"`, not two's complement — C# `i.ToString("x2")` on a negative int gives `fffffffd`.

**HSL branches on exact float equality** (`animation.rs:593, 602, 604, 615`):
`max_val == min_val`, `max_val == normalized_red`, `saturation == 0.0`. An epsilon flips the
hue branch and the result feeds `round_half_even(channel * 255)` — a visible colour change.

See `docs/translation-checklist.md` §2, §3, §5 for the enumerated sites.

## Acceptance criteria

- [ ] The colour-bearing `m0_matrix` variants are byte-identical to `ref_m0`:
      `--xterm-colors`, `--no-color`, and all three `--existing-color-handling` modes
- [ ] `graphics_goldens.txt` passes, consuming the fixture data with ttfx's tolerance schedule
- [ ] Gradient construction uses integer floor division; a test covers a **negative** channel
      delta where floor and truncate differ
- [ ] `hex_to_xterm` matches the reference across a colour sweep, including ties (first minimum
      wins)
- [ ] Seven-digit hex is accepted and uses the first six digits; odd-length input does not throw
- [ ] `Color(255) != Color("ffffff")` — equality is on the original argument
- [ ] `get_color_at_fraction` throws outside [0,1] rather than clamping
- [ ] Negative colour components format as `-3`, not two's complement
- [ ] No epsilon appears in any transcribed float comparison
- [ ] Unsupported SGR parameter *values* are silently ignored while malformed *sequences* error

## Blocked by

- 0004 — First byte-identical frame
