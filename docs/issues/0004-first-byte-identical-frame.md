# 0004 — First byte-identical frame: `--m0-dump` for plain ASCII

**Labels:** `enhancement`, `ready-for-agent`

## What to build

The first real tracer bullet: text goes in one end, bytes come out the other, and they are
byte-identical to the reference. Every layer is involved except the effect engine.

Path: stdin or `--input-file` → strict UTF-8 decode → ANSI-aware input parser → character grid
→ canvas sizing and anchoring → fill characters → neighbours → renderer → frame bytes →
stdout. Scoped to **plain ASCII input at default options**; colour is 0005, anchoring is 0006,
non-ASCII is 0007.

**Frames are bytes, not strings.** Build directly into a pooled `byte[]` / `ArrayBufferWriter`
and write to the raw stream from `Console.OpenStandardOutput()`. `Console.Write` does encoding,
`TextWriter` synchronisation, and autoflush, and is the easiest way to make this slow. Building
bytes also makes `--parity-dump`'s length prefix trivially correct — it is a **byte** count.

**Strict UTF-8 means a throwing decoder.** `Encoding.UTF8.GetString`, `File.ReadAllText`, and
`Console.InputEncoding` all replace invalid bytes with U+FFFD and never throw; Rust's
`String::from_utf8` rejects. Use
`new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true)` on both
input paths. `cli_corpus.sh:33` already ships a `bad-utf8-file` case asserting exit 1.

**Renderer specifics that are easy to get subtly wrong:**

- Frame dimensions are `visible_top × visible_right` — absolute terminal-space extents after
  anchoring and clipping — **not** canvas width/height.
- SGR order is bold, italic, underline, blink, reverse, hidden, strike, fg, bg, symbol,
  `\x1b[0m`; `dim` is stored but never emitted.
- Rows joined with `"\n"`, top row first; a bare symbol when unformatted.
- The painter does **not** sort. It walks `visible_characters` and keeps the max
  `(layer, character_id)` per cell.
- `visible_characters` removal is `swap_remove` (swap-with-last), not a shifting `RemoveAt`.

**Terminal size is three file descriptors**, not one: stdout, then stderr, then stdin, first
that is a tty *and* reports positive rows and columns; else the `COLUMNS`/`LINES` env vars
(both must parse, with per-axis override); else `(80, 24)`. `ioctl(1, …)` alone diverges the
moment stdout is redirected while stderr is a terminal — exactly the shape of a harness run.
Verify `TIOCGWINSZ` and `sizeof(struct winsize)` against the platform headers with a two-line C
program per target; do not trust a recalled constant. This is a *different* decision from the
`isatty` check that gates the tty lifecycle — keep them separate.

`character_id` allocation must consume exactly as the reference parser does: ids are allocated
for characters later overwritten by cursor movement, popped as trailing whitespace, or cropped
by the canvas, so surviving characters have id **gaps**. Keep the id an explicit field —
once orphans are dropped, list index ≠ id, and every ordering key must read the field.

## Acceptance criteria

- [ ] `--m0-dump` output is byte-identical to `ref_m0` for the ASCII fixtures at default options
- [ ] Frame dimensions derive from `visible_top`/`visible_right`, verified against a case where
      they differ from canvas dimensions
- [ ] SGR component order matches; `dim` is never emitted
- [ ] Empty or whitespace-only input prints `NO INPUT.` on **stdout** and exits 1
- [ ] Malformed UTF-8 is **rejected** (message, exit 1), not silently replaced — `bad-utf8-file`
      from the CLI corpus passes
- [ ] Unsupported ANSI sequences error to **stderr** with exit 1
- [ ] Terminal size honours the stdout→stderr→stdin cascade, then `COLUMNS`/`LINES`, then
      `(80, 24)`; `TIOCGWINSZ` is asserted against platform headers on macOS and Linux
- [ ] `character_id` gaps are preserved; a test pins ids across an input with cursor-movement
      overwrites and trailing whitespace
- [ ] Frames are built as bytes and written to the raw stdout stream

## Blocked by

- 0002 — Parity oracle
- 0003 — CLI parser core
