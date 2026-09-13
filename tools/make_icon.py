"""Redraw the Lexicycle app icon as SVG.

    python tools/make_icon.py

Rewrites `Resources/AppIcon/appicon{,fg}.svg` and the Google Play listing PNG. Run it
only when the mark itself changes; the SVGs are checked in, so an ordinary build never
needs this script, fontTools, Inkscape or a CJK font.

Everything is computed rather than hand-placed, so the two arcs, the two arrowheads and
the two book pages are exact mirrors. That is the point: the raster this was traced from
(docs/images/lexicycle_icon.png) has a malformed blue arrowhead -- one flank longer than
the other and a tip off the tangent -- which is visible once the icon is scaled up.

Requires `pip install fonttools`; Inkscape is optional and only used for the PNG.
"""

from __future__ import annotations

import math
import shutil
import subprocess
import sys
from pathlib import Path

from fontTools.pens.recordingPen import DecomposingRecordingPen
from fontTools.pens.svgPathPen import SVGPathPen
from fontTools.pens.transformPen import TransformPen
from fontTools.pens.boundsPen import BoundsPen
from fontTools.ttLib import TTFont
from fontTools.misc.transform import Transform

# ---------------------------------------------------------------- canvas

SIZE = 512
C = SIZE / 2  # 256 -- the centre everything is built around

BLUE = "#1CB2FF"
GREEN = "#B1DF5A"

# Android masks an adaptive icon down to a circle 66% of the layer, so nothing may
# stray past this radius. Google Play's 512px listing icon wants visible padding too;
# the same budget covers both.
SAFE_RADIUS = 160.0

# ---------------------------------------------------------------- the cycle ring

RING_R = 122.0  # centre-line of the stroke
RING_W = 27.0

HEAD_HALF_WIDTH = 30.0
HEAD_LENGTH = 47.0
HEAD_FLARE = 18.0  # degrees the head turns outward, as in the source drawing

# Angles are mathematical (0 = east, counter-clockwise positive) but the strokes are
# travelled *clockwise*, from the higher angle down to the lower one.
BLUE_ARC = (200.0, 50.0)
GREEN_ARC = (20.0, -145.0)


def on_ring(angle_deg: float, radius: float = RING_R) -> tuple[float, float]:
    a = math.radians(angle_deg)
    return C + radius * math.cos(a), C - radius * math.sin(a)


def clockwise_tangent(angle_deg: float) -> tuple[float, float]:
    """Unit vector pointing the way a clockwise traveller is heading."""
    a = math.radians(angle_deg)
    return math.sin(a), math.cos(a)


def outward_normal(angle_deg: float) -> tuple[float, float]:
    a = math.radians(angle_deg)
    return math.cos(a), -math.sin(a)


def arc_path(start_deg: float, end_deg: float) -> str:
    x0, y0 = on_ring(start_deg)
    x1, y1 = on_ring(end_deg)
    large = 1 if (start_deg - end_deg) > 180 else 0
    # sweep-flag 1 is clockwise on screen, which is the direction of travel.
    return f"M {x0:.2f},{y0:.2f} A {RING_R},{RING_R} 0 {large} 1 {x1:.2f},{y1:.2f}"


def arrow_head(angle_deg: float) -> str:
    """A symmetrical triangle sitting on the ring, aimed the way the arc travels."""
    tx, ty = clockwise_tangent(angle_deg)
    nx, ny = outward_normal(angle_deg)
    flare = math.radians(HEAD_FLARE)
    dx = tx * math.cos(flare) + nx * math.sin(flare)
    dy = ty * math.cos(flare) + ny * math.sin(flare)
    length = math.hypot(dx, dy)
    dx, dy = dx / length, dy / length
    px, py = -dy, dx  # base runs perpendicular to the aim, so both flanks match

    bx, by = on_ring(angle_deg)
    a = (bx + px * HEAD_HALF_WIDTH, by + py * HEAD_HALF_WIDTH)
    b = (bx - px * HEAD_HALF_WIDTH, by - py * HEAD_HALF_WIDTH)
    tip = (bx + dx * HEAD_LENGTH, by + dy * HEAD_LENGTH)
    return "M {:.2f},{:.2f} L {:.2f},{:.2f} L {:.2f},{:.2f} Z".format(*a, *tip, *b)


# ---------------------------------------------------------------- the open book

PAGE_TOP_SPINE = 200.0  # the spine sits higher than the outer edge at the top ...
PAGE_TOP_OUTER = 210.0
PAGE_BOTTOM_SPINE = 358.0  # ... and lower at the bottom, so the pages splay open
PAGE_BOTTOM_OUTER = 316.0
PAGE_OUTER_X = 161.0
TOP_CORNER = 9.0  # kept small: a domed top corner reads as a shield, not a page
BOTTOM_CORNER = 17.0
PAGE_SAG = 7.0  # how far the bottom edge bows below a straight line
SPINE_GAP_TOP = 8.0  # the white channel between the pages: wider at the top,
SPINE_GAP_BOTTOM = 6.0  # closing toward the binding, which is what reads as a fold


def left_page() -> str:
    """One splayed page: straight top, bowed bottom, near-vertical outer edge."""
    top_x = PAGE_OUTER_X + TOP_CORNER
    bottom_x = PAGE_OUTER_X + BOTTOM_CORNER
    inner_top, inner_bottom = C - SPINE_GAP_TOP, C - SPINE_GAP_BOTTOM
    return (
        f"M {inner_top:.2f},{PAGE_TOP_SPINE:.2f} "
        f"L {top_x:.2f},{PAGE_TOP_OUTER:.2f} "
        f"Q {PAGE_OUTER_X:.2f},{PAGE_TOP_OUTER + 1:.2f} "
        f"{PAGE_OUTER_X:.2f},{PAGE_TOP_OUTER + TOP_CORNER:.2f} "
        f"L {PAGE_OUTER_X:.2f},{PAGE_BOTTOM_OUTER - BOTTOM_CORNER:.2f} "
        f"Q {PAGE_OUTER_X:.2f},{PAGE_BOTTOM_OUTER:.2f} "
        f"{bottom_x:.2f},{PAGE_BOTTOM_OUTER + 2:.2f} "
        f"Q {(inner_bottom + bottom_x) / 2:.2f},"
        f"{(PAGE_BOTTOM_SPINE + PAGE_BOTTOM_OUTER) / 2 + PAGE_SAG:.2f} "
        f"{inner_bottom:.2f},{PAGE_BOTTOM_SPINE:.2f} Z"
    )


def mirror(path_d: str) -> str:
    """Reflect a path about the spine, so the right page is exactly the left one."""
    return f'<path d="{path_d}" transform="matrix(-1 0 0 1 {SIZE} 0)"'


# ---------------------------------------------------------------- the two letters

FONT = r"C:\Windows\Fonts\msyhbd.ttc"


def glyph_path(char: str, centre: tuple[float, float], height: float) -> str:
    """One glyph as a filled path, scaled to `height` and centred on `centre`.

    Outlines rather than a <text> element: the icon is rasterised at build time by
    SkiaSharp and at store-listing time by whatever opens the SVG, and neither can be
    relied on to have a CJK font installed.
    """
    font = TTFont(FONT, fontNumber=0)
    glyph_set = font.getGlyphSet()
    name = font.getBestCmap()[ord(char)]

    recorder = DecomposingRecordingPen(glyph_set)
    glyph_set[name].draw(recorder)

    bounds = BoundsPen(glyph_set)
    recorder.replay(bounds)
    x_min, y_min, x_max, y_max = bounds.bounds

    scale = height / (y_max - y_min)
    cx, cy = centre
    # y flips: font units grow upward, SVG user units grow downward.
    transform = Transform().translate(
        cx - scale * (x_min + x_max) / 2,
        cy + scale * (y_min + y_max) / 2,
    ).scale(scale, -scale)

    pen = SVGPathPen(glyph_set, ntos=lambda v: f"{v:.2f}")
    recorder.replay(TransformPen(pen, transform))
    return pen.getCommands()


LETTER_Y = 272.0
LETTER_X = 46.0  # offset from the spine, i.e. the middle of each page
LATIN_HEIGHT = 66.0
CJK_HEIGHT = 62.0


# ---------------------------------------------------------------- assembly

def furthest_point() -> float:
    """Worst-case distance from the centre, checked against the adaptive-icon mask."""
    worst = 0.0
    for angle in (BLUE_ARC[1], GREEN_ARC[1]):
        tx, ty = clockwise_tangent(angle)
        nx, ny = outward_normal(angle)
        flare = math.radians(HEAD_FLARE)
        dx, dy = tx * math.cos(flare) + nx * math.sin(flare), ty * math.cos(flare) + ny * math.sin(flare)
        n = math.hypot(dx, dy)
        dx, dy = dx / n, dy / n
        px, py = -dy, dx
        bx, by = on_ring(angle)
        for point in (
            (bx + px * HEAD_HALF_WIDTH, by + py * HEAD_HALF_WIDTH),
            (bx - px * HEAD_HALF_WIDTH, by - py * HEAD_HALF_WIDTH),
            (bx + dx * HEAD_LENGTH, by + dy * HEAD_LENGTH),
        ):
            worst = max(worst, math.hypot(point[0] - C, point[1] - C))
    worst = max(worst, RING_R + RING_W / 2)
    for corner in (
        (PAGE_OUTER_X, PAGE_TOP_OUTER),
        (PAGE_OUTER_X, PAGE_BOTTOM_OUTER),
        (C, PAGE_BOTTOM_SPINE),
        (C, PAGE_TOP_SPINE),
    ):
        worst = max(worst, math.hypot(corner[0] - C, corner[1] - C))
    return worst


def build(background: str | None, crop: float | None = None) -> str:
    """The mark. `crop` sets a square viewBox of that half-size around the centre.

    The icon keeps the full 512 canvas, because its empty margin is exactly what stops
    an adaptive-icon mask from clipping the arrowheads. A splash screen is not masked,
    so there the margin is dead space that shrinks the mark for no reason -- cropping
    about the centre rather than about the artwork's bounding box keeps it optically
    centred, which the bounding box would not.
    """
    page = left_page()
    if crop is None:
        view, extent = f"0 0 {SIZE} {SIZE}", SIZE
    else:
        view, extent = f"{C - crop:g} {C - crop:g} {2 * crop:g} {2 * crop:g}", 2 * crop

    parts = [
        f'<svg xmlns="http://www.w3.org/2000/svg" width="{extent:g}" '
        f'height="{extent:g}" viewBox="{view}">',
        "  <!-- Generated by tools/make_icon.py. Edit that, not this. -->",
    ]
    if background:
        parts.append(f'  <rect x="{C - extent / 2:g}" y="{C - extent / 2:g}" '
                     f'width="{extent:g}" height="{extent:g}" fill="{background}"/>')

    parts += [
        "  <!-- the cycle: blue over the top, green under the bottom, both clockwise -->",
        f'  <path d="{arc_path(*BLUE_ARC)}" fill="none" stroke="{BLUE}" '
        f'stroke-width="{RING_W}" stroke-linecap="butt"/>',
        f'  <path d="{arrow_head(BLUE_ARC[1])}" fill="{BLUE}"/>',
        f'  <path d="{arc_path(*GREEN_ARC)}" fill="none" stroke="{GREEN}" '
        f'stroke-width="{RING_W}" stroke-linecap="butt"/>',
        f'  <path d="{arrow_head(GREEN_ARC[1])}" fill="{GREEN}"/>',
        "  <!-- the open book, right page mirrored from the left -->",
        f'  <path d="{page}" fill="{BLUE}"/>',
        f'  {mirror(page)} fill="{GREEN}"/>',
        f'  <path d="{glyph_path("A", (C - LETTER_X, LETTER_Y), LATIN_HEIGHT)}" fill="#FFFFFF"/>',
        f'  <path d="{glyph_path("文", (C + LETTER_X, LETTER_Y), CJK_HEIGHT)}" fill="#FFFFFF"/>',
        "</svg>",
    ]
    return "\n".join(parts) + "\n"


REPO = Path(__file__).resolve().parent.parent
APP_ICON = REPO / "src/Lexicycle/LexicycleApp/Resources/AppIcon"
SPLASH = REPO / "src/Lexicycle/LexicycleApp/Resources/Splash/splash.svg"
PLAY_ICON = REPO / "docs/images/play-store-icon-512.png"

SPLASH_PAD = 6.0  # breathing room past the artwork, so nothing sits on the crop edge

# The mark is drawn on white, so that is the background layer. Google Play rejects a
# 512px listing icon with transparency, and this keeps the store tile, the adaptive
# icon and the pre-API-26 legacy icon all showing the same thing.
BACKGROUND = "#FFFFFF"


def main() -> int:
    reach = furthest_point()
    print(f"furthest point from centre: {reach:.1f}px (safe zone {SAFE_RADIUS:.0f}px)")
    if reach > SAFE_RADIUS:
        # An adaptive icon is masked to a circle 66% of the layer. Anything outside it
        # is simply gone on most launchers, with no warning at build time.
        print("artwork leaves the adaptive-icon safe zone", file=sys.stderr)
        return 1

    background = f'<svg xmlns="http://www.w3.org/2000/svg" width="{SIZE}" height="{SIZE}" viewBox="0 0 {SIZE} {SIZE}">\n  <rect width="{SIZE}" height="{SIZE}" fill="{BACKGROUND}"/>\n</svg>\n'
    (APP_ICON / "appicon.svg").write_text(background, encoding="utf-8")
    (APP_ICON / "appiconfg.svg").write_text(build(None), encoding="utf-8")
    print("wrote", APP_ICON / "appicon.svg")
    print("wrote", APP_ICON / "appiconfg.svg")

    SPLASH.write_text(build(None, crop=reach + SPLASH_PAD), encoding="utf-8")
    print("wrote", SPLASH)

    inkscape = shutil.which("inkscape")
    if not inkscape:
        print("inkscape not found -- skipped the Play Store PNG", file=sys.stderr)
        return 0

    flat = APP_ICON.parent / "_playicon.svg"
    flat.write_text(build(BACKGROUND), encoding="utf-8")
    try:
        subprocess.run(
            [inkscape, "--export-type=png", f"--export-filename={PLAY_ICON}",
             "-w", "512", "-h", "512", str(flat)],
            check=True, capture_output=True,
        )
    finally:
        flat.unlink()
    print("wrote", PLAY_ICON)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
