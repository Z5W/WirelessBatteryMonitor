from PIL import Image, ImageDraw
from pathlib import Path

asset_dir = Path(__file__).resolve().parents[1] / "design" / "assets"
asset_dir.mkdir(parents=True, exist_ok=True)

S = 1024


def rr(draw, box, radius, fill=None, outline=None, width=1):
    draw.rounded_rectangle(box, radius=radius, fill=fill, outline=outline, width=width)


img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
d = ImageDraw.Draw(img)

# App tile
rr(d, (32, 32, S - 32, S - 32), 220, fill=(15, 18, 24, 255), outline=(44, 52, 66, 255), width=10)

# Soft inner glow
glow = Image.new("RGBA", (S, S), (0, 0, 0, 0))
gd = ImageDraw.Draw(glow)
gd.ellipse((90, 120, 930, 900), fill=(48, 180, 255, 18))
img = Image.alpha_composite(img, glow)
d = ImageDraw.Draw(img)

# Battery shell
rr(d, (160, 330, 760, 700), 72, fill=(23, 28, 37, 255), outline=(232, 240, 252, 255), width=22)
rr(d, (760, 442, 830, 588), 20, fill=(232, 240, 252, 255))

# Battery fill gradient (cyan -> green)
fill_w = 548
fill_h = 326
grad = Image.new("RGBA", (fill_w, fill_h), (0, 0, 0, 0))
gpx = grad.load()
for x in range(fill_w):
    t = x / max(1, fill_w - 1)
    r = int(76 + (54 - 76) * t)
    g = int(194 + (211 - 194) * t)
    b = int(255 + (153 - 255) * t)
    for y in range(fill_h):
        gpx[x, y] = (r, g, b, 255)
mask = Image.new("L", (fill_w, fill_h), 0)
md = ImageDraw.Draw(mask)
md.rounded_rectangle((0, 0, fill_w - 1, fill_h - 1), radius=48, fill=255)
img.paste(grad, (196, 352), mask)

# Live status dot, top-right inside tile
d = ImageDraw.Draw(img)
d.ellipse((774, 116, 890, 232), fill=(76, 194, 255, 40))
d.ellipse((794, 136, 870, 212), fill=(76, 194, 255, 255))
d.ellipse((818, 160, 846, 188), fill=(15, 18, 24, 255))

img.save(asset_dir / "logo-256.png")
img.resize((256, 256), Image.LANCZOS).save(asset_dir / "logo-256-small.png")
img.save(
    asset_dir / "app.ico",
    sizes=[(16, 16), (20, 20), (24, 24), (32, 32), (40, 40), (48, 48), (64, 64), (128, 128), (256, 256)],
)
print("icons written")
