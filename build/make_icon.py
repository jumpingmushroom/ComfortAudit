"""Thunderstore icon: 256x256 PNG. A gable roof over a hearth flame — shelter and fire,
the two things the mod is actually about. Bold shapes only, so it survives 64px listings."""
from PIL import Image, ImageDraw, ImageFilter
import math

S = 256
SS = 4                     # supersample for clean diagonals
W = S * SS

img = Image.new("RGBA", (W, W), (0, 0, 0, 0))
d = ImageDraw.Draw(img)

WOOD_DARK  = (38, 27, 19)
WOOD_MID   = (74, 53, 36)
CREAM      = (242, 233, 216)
EMBER      = (255, 161, 60)
EMBER_HOT  = (255, 214, 130)

# rounded-square ground, slightly lighter towards the top
r = 44 * SS
d.rounded_rectangle([0, 0, W - 1, W - 1], radius=r, fill=WOOD_DARK)
glow = Image.new("RGBA", (W, W), (0, 0, 0, 0))
ImageDraw.Draw(glow).ellipse(
    [W * 0.10, W * 0.34, W * 0.90, W * 1.22], fill=(WOOD_MID[0], WOOD_MID[1], WOOD_MID[2], 190))
glow = glow.filter(ImageFilter.GaussianBlur(26 * SS))
mask = Image.new("L", (W, W), 0)
ImageDraw.Draw(mask).rounded_rectangle([0, 0, W - 1, W - 1], radius=r, fill=255)
img = Image.alpha_composite(img, Image.composite(glow, Image.new("RGBA", (W, W), (0, 0, 0, 0)), mask))
d = ImageDraw.Draw(img)

# gable roof: two thick beams meeting at an apex
apex = (W * 0.50, W * 0.20)
left = (W * 0.13, W * 0.53)
right = (W * 0.87, W * 0.53)
beam = int(15 * SS)
d.line([left, apex], fill=CREAM, width=beam, joint="curve")
d.line([apex, right], fill=CREAM, width=beam, joint="curve")
d.ellipse([apex[0] - beam / 2, apex[1] - beam / 2, apex[0] + beam / 2, apex[1] + beam / 2], fill=CREAM)

# (no ridge post: a vertical stroke into the apex reads as an arrowhead, not a roof)

# hearth flame under the roof
def flame(cx, base_y, height, width, colour):
    pts = []
    steps = 90
    for i in range(steps + 1):
        t = i / steps
        y = base_y - height * t
        # teardrop: wide at the base, pinched to a tip, with a slight lean
        w = width * math.sin(math.pi * (t ** 0.62)) * (1 - t * 0.12)
        pts.append((cx + w + math.sin(t * 3.0) * width * 0.10, y))
    for i in range(steps, -1, -1):
        t = i / steps
        y = base_y - height * t
        w = width * math.sin(math.pi * (t ** 0.62)) * (1 - t * 0.12)
        pts.append((cx - w + math.sin(t * 3.0) * width * 0.10, y))
    d.polygon(pts, fill=colour)

flame(W * 0.50, W * 0.83, W * 0.47, W * 0.150, EMBER)
flame(W * 0.50, W * 0.83, W * 0.29, W * 0.080, EMBER_HOT)

# hearth base
d.rounded_rectangle([W * 0.30, W * 0.825, W * 0.70, W * 0.875],
                    radius=int(10 * SS), fill=CREAM)

img = img.resize((S, S), Image.LANCZOS)
out = "/workspace/gamemods/Valheim/Hygge/thunderstore/icon.png"
img.save(out, "PNG")
print("wrote", out, img.size, img.mode)
