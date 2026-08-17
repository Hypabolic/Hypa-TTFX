#!/usr/bin/env python3
"""Render every effect against hypa-logo.txt into docs/examples/*.gif."""

from __future__ import annotations

import argparse
import os
import shutil
import subprocess
import sys
import tempfile
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parents[1]
BIN = ROOT / "artifacts" / "ttfx"
LOGO = ROOT / "hypa-logo.txt"
OUT_DIR = ROOT / "docs" / "examples"

EFFECTS = [
    "beams",
    "binarypath",
    "blackhole",
    "bouncyballs",
    "bubbles",
    "burn",
    "colorshift",
    "crumble",
    "decrypt",
    "errorcorrect",
    "expand",
    "fireworks",
    "highlight",
    "laseretch",
    "matrix",
    "middleout",
    "orbittingvolley",
    "overflow",
    "pour",
    "print",
    "rain",
    "randomsequence",
    "rings",
    "scattered",
    "slice",
    "slide",
    "smoke",
    "spotlights",
    "spray",
    "swarm",
    "sweep",
    "synthgrid",
    "thunderstorm",
    "unstable",
    "vhstape",
    "waves",
    "wipe",
]

CANVAS_WIDTH = 51
CANVAS_HEIGHT = 16
MAX_DUMP_FRAMES = 180
MAX_GIF_FRAMES = 72
SEED = "42"

BG = (13, 17, 23)
FG = (230, 237, 243)
PAD = 10
CELL_W = 10
CELL_H = 18
FONT_SIZE = 14
FONT_PATH = "/System/Library/Fonts/Menlo.ttc"

# Standard xterm 256-color cube + grayscale.
_XTERM = [
    (0, 0, 0),
    (205, 0, 0),
    (0, 205, 0),
    (205, 205, 0),
    (0, 0, 238),
    (205, 0, 205),
    (0, 205, 205),
    (229, 229, 229),
    (127, 127, 127),
    (255, 0, 0),
    (0, 255, 0),
    (255, 255, 0),
    (92, 92, 255),
    (255, 0, 255),
    (0, 255, 255),
    (255, 255, 255),
]
for r in range(6):
    for g in range(6):
        for b in range(6):
            _XTERM.append(
                (
                    0 if r == 0 else 55 + 40 * r,
                    0 if g == 0 else 55 + 40 * g,
                    0 if b == 0 else 55 + 40 * b,
                )
            )
for i in range(24):
    v = 8 + 10 * i
    _XTERM.append((v, v, v))


def xterm(code: int) -> tuple[int, int, int]:
    return _XTERM[code] if 0 <= code < 256 else FG


def iter_dump_frames(data: bytes):
    i = 0
    n = len(data)
    while i < n:
        nl = data.find(b"\n", i)
        if nl < 0:
            break
        length = int(data[i:nl])
        start = nl + 1
        yield data[start : start + length]
        i = start + length
        if i < n and data[i : i + 1] == b"\n":
            i += 1


class Pen:
    __slots__ = ("fg", "bg", "bold", "reverse", "hidden")

    def __init__(self) -> None:
        self.reset()

    def reset(self) -> None:
        self.fg = FG
        self.bg = BG
        self.bold = False
        self.reverse = False
        self.hidden = False

    def apply(self, params: list[int]) -> None:
        if not params:
            params = [0]
        i = 0
        while i < len(params):
            p = params[i]
            if p == 0:
                self.reset()
            elif p == 1:
                self.bold = True
            elif p == 22:
                self.bold = False
            elif p == 7:
                self.reverse = True
            elif p == 27:
                self.reverse = False
            elif p == 8:
                self.hidden = True
            elif p == 28:
                self.hidden = False
            elif p == 39:
                self.fg = FG
            elif p == 49:
                self.bg = BG
            elif 30 <= p <= 37:
                self.fg = xterm(p - 30)
            elif 90 <= p <= 97:
                self.fg = xterm(p - 90 + 8)
            elif 40 <= p <= 47:
                self.bg = xterm(p - 40)
            elif 100 <= p <= 107:
                self.bg = xterm(p - 100 + 8)
            elif p in (38, 48):
                dest = "fg" if p == 38 else "bg"
                if i + 1 < len(params) and params[i + 1] == 5 and i + 2 < len(params):
                    color = xterm(params[i + 2])
                    i += 2
                elif i + 1 < len(params) and params[i + 1] == 2 and i + 4 < len(params):
                    color = (params[i + 2] & 255, params[i + 3] & 255, params[i + 4] & 255)
                    i += 4
                else:
                    i += 1
                    continue
                setattr(self, dest, color)
            i += 1

    def pair(self) -> tuple[tuple[int, int, int], tuple[int, int, int]]:
        fg, bg = self.fg, self.bg
        if self.bold and fg == FG:
            fg = (255, 255, 255)
        if self.reverse:
            fg, bg = bg, fg
        if self.hidden:
            fg = bg
        return fg, bg


def parse_frame(payload: bytes) -> list[list[tuple[str, tuple[int, int, int], tuple[int, int, int]]]]:
    text = payload.decode("utf-8")
    pen = Pen()
    rows: list[list[tuple[str, tuple[int, int, int], tuple[int, int, int]]]] = []
    row: list[tuple[str, tuple[int, int, int], tuple[int, int, int]]] = []
    i = 0
    while i < len(text):
        if text[i] == "\x1b" and i + 1 < len(text) and text[i + 1] == "[":
            end = text.find("m", i + 2)
            if end < 0:
                i += 1
                continue
            raw = text[i + 2 : end]
            params = [int(p) if p else 0 for p in raw.split(";")] if raw else [0]
            pen.apply(params)
            i = end + 1
            continue
        if text[i] == "\n":
            rows.append(row)
            row = []
            i += 1
            continue
        ch = text[i]
        fg, bg = pen.pair()
        row.append((ch, fg, bg))
        i += 1
    if row:
        rows.append(row)
    return rows


def subsample(frames: list[bytes]) -> list[bytes]:
    if len(frames) <= MAX_GIF_FRAMES:
        return frames
    last = frames[-1]
    # Evenly pick MAX_GIF_FRAMES-1 from the prefix, always keep the last.
    n = MAX_GIF_FRAMES - 1
    picked = [frames[round(i * (len(frames) - 2) / (n - 1))] for i in range(n)]
    if picked[-1] is not last:
        picked.append(last)
    return picked


def render_image(
    rows: list[list[tuple[str, tuple[int, int, int], tuple[int, int, int]]]],
    font: ImageFont.FreeTypeFont,
) -> Image.Image:
    width = CANVAS_WIDTH * CELL_W + PAD * 2
    height = CANVAS_HEIGHT * CELL_H + PAD * 2
    img = Image.new("RGB", (width, height), BG)
    draw = ImageDraw.Draw(img)
    for y, row in enumerate(rows[:CANVAS_HEIGHT]):
        for x, (ch, fg, bg) in enumerate(row[:CANVAS_WIDTH]):
            x0 = PAD + x * CELL_W
            y0 = PAD + y * CELL_H
            draw.rectangle((x0, y0, x0 + CELL_W - 1, y0 + CELL_H - 1), fill=bg)
            if ch != " " and fg != bg:
                draw.text((x0 + CELL_W / 2, y0 + CELL_H / 2 + 1), ch, font=font, fill=fg, anchor="mm")
    return img


def dump_effect(effect: str) -> bytes:
    env = os.environ.copy()
    env["COLUMNS"] = "80"
    env["LINES"] = "24"
    proc = subprocess.run(
        [
            str(BIN),
            "--parity-dump",
            "--frame-rate",
            "0",
            "--ignore-terminal-dimensions",
            "--canvas-width",
            str(CANVAS_WIDTH),
            "--canvas-height",
            str(CANVAS_HEIGHT),
            "--anchor-text",
            "c",
            "--seed",
            SEED,
            "--max-frames",
            str(MAX_DUMP_FRAMES),
            effect,
        ],
        input=LOGO.read_bytes(),
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        env=env,
        check=False,
    )
    if proc.returncode != 0:
        err = proc.stderr.decode("utf-8", "replace").strip()
        raise RuntimeError(f"{effect} exited {proc.returncode}: {err}")
    return proc.stdout


def encode_gif(images: list[Image.Image], dest: Path) -> None:
    dest.parent.mkdir(parents=True, exist_ok=True)
    # 20 fps, then hold the last frame for ~0.9s so the finished logo is readable.
    held = images + [images[-1]] * 18
    if shutil.which("ffmpeg"):
        with tempfile.TemporaryDirectory() as tmp:
            tmp_path = Path(tmp)
            for i, img in enumerate(held):
                img.save(tmp_path / f"{i:04d}.png")
            palette = tmp_path / "palette.png"
            frames = tmp_path / "%04d.png"
            subprocess.run(
                [
                    "ffmpeg",
                    "-y",
                    "-hide_banner",
                    "-loglevel",
                    "error",
                    "-framerate",
                    "20",
                    "-i",
                    str(frames),
                    "-vf",
                    "palettegen=max_colors=256:stats_mode=diff",
                    str(palette),
                ],
                check=True,
            )
            subprocess.run(
                [
                    "ffmpeg",
                    "-y",
                    "-hide_banner",
                    "-loglevel",
                    "error",
                    "-framerate",
                    "20",
                    "-i",
                    str(frames),
                    "-i",
                    str(palette),
                    "-lavfi",
                    "paletteuse=dither=sierra2_4a:diff_mode=rectangle",
                    "-gifflags",
                    "-offsetting",
                    str(dest),
                ],
                check=True,
            )
        return

    durations = [50] * (len(held) - 1) + [50]
    held[0].save(
        dest,
        save_all=True,
        append_images=held[1:],
        duration=durations,
        loop=0,
        optimize=True,
        disposal=2,
    )


def render_effect(effect: str, font: ImageFont.FreeTypeFont) -> tuple[int, int, int]:
    raw = dump_effect(effect)
    frames = list(iter_dump_frames(raw))
    if not frames:
        raise RuntimeError(f"{effect}: no frames")
    chosen = subsample(frames)
    images = [render_image(parse_frame(frame), font) for frame in chosen]
    dest = OUT_DIR / f"{effect}.gif"
    encode_gif(images, dest)
    return len(frames), len(chosen), dest.stat().st_size


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("effects", nargs="*", help="Subset of effect names (default: all 37)")
    args = parser.parse_args()
    wanted = args.effects or EFFECTS
    unknown = [name for name in wanted if name not in EFFECTS]
    if unknown:
        print(f"unknown effects: {', '.join(unknown)}", file=sys.stderr)
        return 2
    if not BIN.is_file():
        print(f"missing {BIN}; run ./bin/build first", file=sys.stderr)
        return 1
    if not LOGO.is_file():
        print(f"missing {LOGO}", file=sys.stderr)
        return 1

    font = ImageFont.truetype(FONT_PATH, FONT_SIZE)
    OUT_DIR.mkdir(parents=True, exist_ok=True)
    failed = []
    for name in wanted:
        try:
            dumped, kept, size = render_effect(name, font)
        except Exception as exc:
            print(f"FAIL {name}: {exc}", file=sys.stderr)
            failed.append(name)
            continue
        print(f"OK   {name:<16} dump={dumped:<4} gif={kept:<3} {size / 1024:7.1f} KiB")
    if failed:
        print(f"{len(failed)} failed: {', '.join(failed)}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
