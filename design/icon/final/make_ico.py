"""Renders the Weighing (scales) icon, transparent background, to a multi-size Windows .ico.
A dark outline keeps the light parts visible on light backgrounds. Large sizes use the full design;
small ones use thicker strokes so they stay readable."""
import sys
from pathlib import Path

from PIL import Image, ImageDraw, ImageFilter

OUT = Path(sys.argv[1])
OUT.mkdir(parents=True, exist_ok=True)
SS = 4  # supersampling: draw at 1024 for the 256 design space
W = 256 * SS


def hexrgb(h):
    h = h.lstrip("#")
    return tuple(int(h[i:i + 2], 16) for i in (0, 2, 4))


def render(post_w, base_h, beam_w, string_w, string_col, pan_y, pan_rx, pan_ry, knob_r, outline):
    s = lambda v: round(v * SS)
    img = Image.new("RGBA", (W, W), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    ink, accent, string = hexrgb("#E6E7EA"), hexrgb("#D97757"), hexrgb(string_col)

    def line(x1, y1, x2, y2, w, col):
        d.line([s(x1), s(y1), s(x2), s(y2)], fill=col, width=s(w))
        for x, y in ((x1, y1), (x2, y2)):
            d.ellipse([s(x - w / 2), s(y - w / 2), s(x + w / 2), s(y + w / 2)], fill=col)

    d.rounded_rectangle([s(128 - post_w / 2), s(62), s(128 + post_w / 2), s(202)], radius=s(post_w / 3), fill=ink)
    d.rounded_rectangle([s(80), s(196), s(176), s(196 + base_h)], radius=s(base_h / 2), fill=ink)
    line(58, 86, 198, 86, beam_w, accent)
    for cx in (62, 194):
        line(cx, 88, cx - 26, pan_y, string_w, string)
        line(cx, 88, cx + 26, pan_y, string_w, string)
        d.pieslice([s(cx - pan_rx), s(pan_y - pan_ry), s(cx + pan_rx), s(pan_y + pan_ry)], 0, 180, fill=ink)
    d.ellipse([s(128 - knob_r), s(58 - knob_r), s(128 + knob_r), s(58 + knob_r)], fill=accent)

    # Rounded dark outline under everything: blur the alpha, threshold it, soften the edge again
    grow = s(outline)
    halo = img.getchannel("A").filter(ImageFilter.GaussianBlur(grow / 2)).point(lambda v: 255 if v > 6 else 0)
    halo = halo.filter(ImageFilter.GaussianBlur(SS / 2))
    out = Image.new("RGBA", (W, W), hexrgb("#1A1B1F") + (0,))
    out.putalpha(halo)
    out.alpha_composite(img)

    # Fit the drawing to the canvas with a small margin
    content = out.crop(out.getchannel("A").getbbox())
    margin = round(W * 0.03)
    scale = (W - 2 * margin) / max(content.size)
    content = content.resize((round(content.width * scale), round(content.height * scale)), Image.LANCZOS)
    final = Image.new("RGBA", (W, W), (0, 0, 0, 0))
    final.alpha_composite(content, ((W - content.width) // 2, (W - content.height) // 2))
    return final


# Large sizes match design/icon/5-scales.svg; small ones thicken the strokes that would vanish below 1 px.
full = render(post_w=14, base_h=18, beam_w=13, string_w=5, string_col="#9A9DA6", pan_y=152, pan_rx=34, pan_ry=24, knob_r=15, outline=6)
medium = render(post_w=20, base_h=24, beam_w=20, string_w=9, string_col="#B4B7BE", pan_y=150, pan_rx=38, pan_ry=28, knob_r=20, outline=9)
small = render(post_w=26, base_h=30, beam_w=26, string_w=13, string_col="#C9CACF", pan_y=148, pan_rx=42, pan_ry=32, knob_r=24, outline=12)

sizes = {256: full, 128: full, 64: full, 48: full, 32: medium, 24: medium, 16: small}
frames = {n: img.resize((n, n), Image.LANCZOS) for n, img in sizes.items()}
for n, f in frames.items():
    f.save(OUT / f"scales-{n}.png")

ico = OUT / "app.ico"
frames[256].save(ico, format="ICO", sizes=[(n, n) for n in frames],
                 append_images=[frames[n] for n in frames if n != 256])
check = Image.open(ico)
print("ico sizes:", sorted(check.info["sizes"]))

# Check sheet: every frame on dark and on light, plus the small ones at 4x
order = sorted(frames, reverse=True)
width = sum(order) + 8 * (len(order) + 1) + 4 * (32 + 24 + 16) + 3 * 8
sheet = Image.new("RGBA", (width, 2 * 264), (0, 0, 0, 255))
for row, bg in enumerate(((11, 12, 14, 255), (243, 243, 243, 255))):
    ImageDraw.Draw(sheet).rectangle([0, row * 264, width, row * 264 + 263], fill=bg)
    x = 8
    for n in order:
        sheet.alpha_composite(frames[n], (x, row * 264 + 4))
        x += n + 8
    for n in (32, 24, 16):
        sheet.alpha_composite(frames[n].resize((n * 4, n * 4), Image.NEAREST), (x, row * 264 + 4))
        x += n * 4 + 8
sheet.save(OUT / "frames-check.png")
