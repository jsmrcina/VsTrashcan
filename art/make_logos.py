"""
Builds the Mod DB logos (480x320) for VsWaypointSharing and VsTrashcan in the style of the popular mods on the
Mod DB: the game's own textures as pixel art, and a big outlined title in Almendra (the game's title font).

Everything except the title is drawn at 1/4 size (120x80) and scaled up 4x without smoothing, so all pixels sit on
the same grid as the game's 32px textures shown at 4x. Reads textures and fonts from the installed game.

    python3 make_logos.py <waypoint-out.png> <trashcan-out.png> [<trash-icon.png>]

The trash icon defaults to VsTrashcan's own resources/assets/textures/gui/icon.png next to this repo.
"""
import os
import sys
from PIL import Image, ImageDraw, ImageFilter, ImageFont

GAME = os.environ.get("VINTAGE_STORY", "/opt/vintagestory")
TEX = os.path.join(GAME, "assets/survival/textures")
FONT = os.path.join(GAME, "assets/game/fonts/Almendra-Bold.ttf")

W, H, SCALE = 480, 320, 4
LW, LH = W // SCALE, H // SCALE  # 120 x 80 low-res canvas


def tex(path, size=None):
    img = Image.open(os.path.join(TEX, path)).convert("RGBA")
    if size:
        img = img.crop((0, 0, size[0], size[1]))
    return img


def tile(texture, w, h, step=1):
    """Tiles a texture over w x h, taking every step-th pixel (step 2 = textures at half density)."""
    t = texture.resize((texture.width // step, texture.height // step), Image.NEAREST) if step > 1 else texture
    out = Image.new("RGBA", (w, h))
    for y in range(0, h, t.height):
        for x in range(0, w, t.width):
            out.paste(t, (x, y))
    return out


def shade(img, factor):
    """Darkens (factor < 1) or lightens (factor > 1) an image, keeping alpha."""
    r, g, b, a = img.split()
    rgb = Image.merge("RGB", (r, g, b)).point(lambda v: max(0, min(255, int(v * factor))))
    return Image.merge("RGBA", (*rgb.split(), a))


def vignette(img, strength=0.55):
    mask = Image.new("L", img.size, 0)
    d = ImageDraw.Draw(mask)
    w, h = img.size
    for i in range(12):
        d.rectangle([i, i, w - 1 - i, h - 1 - i], outline=int(255 * strength * (1 - i / 12)))
    dark = Image.new("RGBA", img.size, (12, 8, 5, 255))
    return Image.composite(dark, img, mask)


def masked(texture, mask_points, size):
    """A texture cut to a polygon (in low-res pixels), for item sprites like ingots and flint."""
    m = Image.new("L", size, 0)
    ImageDraw.Draw(m).polygon(mask_points, fill=255)
    t = tile(texture, *size)
    out = Image.new("RGBA", size, (0, 0, 0, 0))
    out.paste(t, (0, 0), m)
    return out


def outline(sprite, color=(30, 20, 12, 255)):
    """A 1px dark outline around a sprite, like the game's item icons."""
    a = sprite.split()[3]
    grown = a.filter(ImageFilter.MaxFilter(3))
    base = Image.new("RGBA", sprite.size, color)
    out = Image.new("RGBA", sprite.size, (0, 0, 0, 0))
    out.paste(base, (0, 0), grown)
    out.alpha_composite(sprite)
    return out


def curved_arrow(d, p0, p1, p2, color, dark, width=3, head=8):
    """A quadratic curve p0 -> p2 bent towards p1, with an arrowhead at p2 aimed along the curve."""
    import math
    pts = []
    for i in range(41):
        t = i / 40
        x = (1 - t) ** 2 * p0[0] + 2 * (1 - t) * t * p1[0] + t ** 2 * p2[0]
        y = (1 - t) ** 2 * p0[1] + 2 * (1 - t) * t * p1[1] + t ** 2 * p2[1]
        pts.append((x, y))
    body = pts[:-5]
    d.line(body, fill=dark, width=width + 2, joint="curve")
    d.line(body, fill=color, width=width, joint="curve")
    (x1, y1), (x2, y2) = pts[-6], pts[-1]
    ang = math.atan2(y2 - y1, x2 - x1)
    tip = (x2 + math.cos(ang) * 1.5, y2 + math.sin(ang) * 1.5)
    left = (x2 - math.cos(ang - 0.6) * head, y2 - math.sin(ang - 0.6) * head)
    right = (x2 - math.cos(ang + 0.6) * head, y2 - math.sin(ang + 0.6) * head)
    d.polygon([tip, left, right], fill=color, outline=dark)


def upscale(low):
    return low.resize((W, H), Image.NEAREST)  # low must be W/scale x H/scale for a whole-number scale


def title(img, text, y, size=58, fill=(244, 228, 184), stroke=(38, 24, 12)):
    d = ImageDraw.Draw(img)
    font = ImageFont.truetype(FONT, size)
    tw = d.textlength(text, font=font)
    x = (W - tw) / 2
    shadow = Image.new("RGBA", img.size, (0, 0, 0, 0))
    ImageDraw.Draw(shadow).text((x + 3, y + 5), text, font=font, fill=(0, 0, 0, 170), stroke_width=5, stroke_fill=(0, 0, 0, 170))
    img.alpha_composite(shadow.filter(ImageFilter.GaussianBlur(3)))
    d.text((x, y), text, font=font, fill=fill, stroke_width=5, stroke_fill=stroke)


def ingot_sprite(metal, w=14, h=7):
    pts = [(0, h - 1), (w - 1, h - 1), (w - 3, 0), (2, 0)]
    s = masked(shade(tex(f"block/metal/ingot/{metal}.png"), 1.1), pts, (w, h))
    d = ImageDraw.Draw(s)
    d.line([(3, 1), (w - 4, 1)], fill=(255, 235, 200, 140))
    return outline(s)


# ---------- Waypoint Sharing ----------

def pin_sprite(color, dark):
    """A map pin with a 1px margin all round, so its outline is never cut off."""
    s = Image.new("RGBA", (15, 21), (0, 0, 0, 0))
    d = ImageDraw.Draw(s)
    d.ellipse([1, 1, 13, 13], fill=color)
    d.polygon([(3, 10), (11, 10), (7, 18)], fill=color)
    d.ellipse([5, 5, 9, 9], fill=(246, 233, 207, 255))
    d.point([(4, 3), (5, 3), (3, 4)], fill=(255, 255, 255, 110))
    return outline(s, dark)


def bowed_arrow_sprite(length, bow, color, dark):
    """
    A pixel-art arrow pointing right: a 2px line bowed upwards by `bow` pixels, ending in a right-pointing head.
    Turned round (rotate 180) it bows downwards and points left. Stamped in whole pixels, then outlined.
    """
    import math
    pad = 3
    head = 4
    s = Image.new("RGBA", (length + head + pad * 2, bow + 2 + 8 + pad * 2), (0, 0, 0, 0))
    d = ImageDraw.Draw(s)
    base = pad + bow + 4
    x0, x1 = pad, pad + length - 1
    for x in range(x0, x1 + 1):
        y = round(base - bow * math.sin((x - x0) / (x1 - x0) * math.pi))
        d.rectangle([x, y, x + 1, y + 1], fill=color)
    # head: a right-pointing triangle centred on the line's end
    for col, half in enumerate([3, 2, 1, 0]):
        d.line([(x1 + 1 + col, base - half), (x1 + 1 + col, base + 1 + half)], fill=color)
    return outline(s, dark)


def waypoint_logo(out):
    low = Image.new("RGBA", (LW, LH))
    # oak plank frame with the map parchment inset, like a map pinned to a board
    low.paste(shade(tile(tex("block/wood/planks/oak1.png"), LW, LH, step=2), 0.8), (0, 0))
    low.paste(tile(tex("item/utility/map-blank.png"), 104, 64), (8, 5))
    d = ImageDraw.Draw(low)
    d.rectangle([7, 4, 112, 69], outline=(52, 34, 18, 255))

    # a little terrain, clear of the pins and arrows: a pond top-left, forest top-right and bottom-left
    d.ellipse([11, 7, 27, 15], fill=(96, 140, 160, 255), outline=(70, 104, 120, 255))
    d.line([(15, 10), (19, 10)], fill=(150, 190, 205, 255))
    for x, y in [(95, 9), (99, 11), (96, 13), (15, 49), (19, 51), (14, 53)]:
        d.ellipse([x - 2, y - 2, x + 2, y + 2], fill=(104, 124, 70, 255))

    # the two players' waypoints
    left_pin, right_pin = (22, 18), (83, 18)
    low.alpha_composite(pin_sprite((201, 68, 60, 255), (90, 26, 22, 255)), left_pin)
    low.alpha_composite(pin_sprite((60, 124, 201, 255), (24, 52, 96, 255)), right_pin)

    # sync arrows between them: over the top into the blue pin, and underneath back into the red pin
    gold, gold_dark = (241, 195, 91, 255), (120, 82, 20, 255)
    # placed so each head stops 2px short of its marker's outline
    arrow = bowed_arrow_sprite(34, 5, gold, gold_dark)
    low.alpha_composite(arrow, (39, 10))
    low.alpha_composite(arrow.rotate(180), (37, 23))

    img = vignette(upscale(low))
    title(img, "Waypoint Sharing", 226, size=54)
    img.convert("RGB").save(out)


# ---------- Trashcan ----------

def open_can_sprite(trash_icon):
    """The mod's own 32px trash can icon without its lid (rows 0-7), with a dark opening under the rim."""
    icon = Image.open(trash_icon).convert("RGBA")
    body = icon.crop((0, 8, 32, 32))
    d = ImageDraw.Draw(body)
    # the rim is row 0 of the body (x 6..23); just below it, show the inside of the can
    for y, (x0, x1), color in [(1, (7, 21), (28, 25, 24, 255)), (2, (8, 20), (40, 36, 34, 255)), (3, (9, 19), (72, 70, 68, 255))]:
        d.line([(x0, y), (x1, y)], fill=color)
    bbox = body.getbbox()
    return body.crop(bbox)


def trashcan_logo(out, trash_icon):
    # Drawn at 1/5 size (96x64), so the 32px can and the items keep the same pixel size as everything else
    lw, lh = W // 5, H // 5
    low = Image.new("RGBA", (lw, lh))
    floor = 44  # top of the plank floor; the title sits on the floor below the can
    low.paste(shade(tile(tex("block/stone/cobblestone/andesite1.png"), lw, lh, step=2), 0.62), (0, 0))
    low.paste(shade(tile(tex("block/wood/planks/oak1.png"), lw, lh - floor, step=2), 0.7), (0, floor))
    d = ImageDraw.Draw(low)
    d.line([(0, floor - 1), (lw, floor - 1)], fill=(30, 22, 14, 255))

    # the open can, centred, standing on the floor
    can = open_can_sprite(trash_icon)
    cx = (lw - can.width) // 2
    cy = floor + 1 - can.height
    shadow = Image.new("RGBA", (lw, lh), (0, 0, 0, 0))
    ImageDraw.Draw(shadow).ellipse([cx - 2, floor - 2, cx + can.width + 1, floor + 2], fill=(0, 0, 0, 120))
    low.alpha_composite(shadow)
    low.alpha_composite(can, (cx, cy))

    # items dropping straight into the opening
    mouth = cx + can.width // 2
    copper = ingot_sprite("copper").rotate(-14, expand=True, resample=Image.NEAREST)
    low.alpha_composite(copper, (mouth - copper.width + 3, cy - copper.height - 1))
    flint_pts = [(1, 3), (5, 0), (9, 2), (8, 7), (3, 8)]
    flint = outline(masked(shade(tex("block/stone/flint.png"), 1.2), flint_pts, (10, 9)))
    low.alpha_composite(flint, (mouth + 2, cy - flint.height - 6))
    for x, y in [(mouth - 8, cy - 15), (mouth - 4, cy - 17), (mouth + 7, cy - 21), (mouth + 10, cy - 19)]:
        if y > 0:
            d.line([(x, y), (x - 1, y + 2)], fill=(220, 210, 190, 140))

    img = vignette(low.resize((W, H), Image.NEAREST))
    title(img, "Trashcan", 238, size=62)
    img.convert("RGB").save(out)


if __name__ == "__main__":
    here = os.path.dirname(os.path.abspath(__file__))
    icon = sys.argv[3] if len(sys.argv) > 3 else os.path.join(here, "../../VsTrashcan/resources/assets/textures/gui/icon.png")
    waypoint_logo(sys.argv[1])
    trashcan_logo(sys.argv[2], icon)
