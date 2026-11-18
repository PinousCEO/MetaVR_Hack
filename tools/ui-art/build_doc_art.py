# The stickers of a DOCUMENT the partner made (MR/MrDocs.cs, Windows/PcDocs.cs):
#   MetaVRHack/Assets/LivingPartner/Art/Ui/doc-pdf.png   a sheet of paper with a folded corner, lines of writing, a rose band "PDF"
#   MetaVRHack/Assets/LivingPartner/Art/Ui/doc-pic.png   the same sheet with a little picture on it, a sky band "PNG"
#   MetaVRHack/Assets/LivingPartner/Art/Ui/doc-page.png  a blank page (what the board shows until a page's picture has arrived)
# Storybook look: cream paper, brown ink line, pastel bands, a soft shadow. No brand marks.
# Run:  python tools/ui-art/build_doc_art.py        (needs Pillow)
import os
from PIL import Image, ImageDraw, ImageFilter, ImageFont

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
OUT = os.path.join(ROOT, "MetaVRHack", "Assets", "LivingPartner", "Art", "Ui")
FONT = os.path.join(ROOT, "MetaVRHack", "Assets", "LivingPartner", "Art", "Fonts", "Jua-Regular.ttf")
N, SS = 256, 4
CREAM, PAPER, INK = (255, 248, 234, 255), (255, 253, 246, 255), (74, 47, 39, 255)
ROSE, SKY, HONEY, SAGE, FOLD = (246, 181, 184, 255), (168, 214, 237, 255), (255, 214, 107, 255), (181, 219, 184, 255), (243, 226, 196, 255)


def sheet(kind):
    n = N * SS
    img = Image.new("RGBA", (n, n), (0, 0, 0, 0))
    x0, y0, x1, y1, c = 0.21 * n, 0.09 * n, 0.79 * n, 0.91 * n, 0.17 * n      # the sheet, and how much of its corner is folded
    r, lw = 0.035 * n, int(0.016 * n)
    outline = [(x0 + r, y0), (x1 - c, y0), (x1, y0 + c), (x1, y1 - r), (x1 - r, y1), (x0 + r, y1), (x0, y1 - r), (x0, y0 + r)]
    # a soft shadow under it
    sh = Image.new("RGBA", (n, n), (0, 0, 0, 0))
    ImageDraw.Draw(sh).polygon([(x + 0.012 * n, y + 0.022 * n) for x, y in outline], fill=(74, 47, 39, 90))
    img = Image.alpha_composite(img, sh.filter(ImageFilter.GaussianBlur(n * 0.022)))
    d = ImageDraw.Draw(img)
    # a cream die-cut edge, the ink line, the paper
    d.line(outline + [outline[0]], fill=CREAM, width=lw * 4, joint="curve")
    d.polygon(outline, fill=PAPER)
    d.line(outline + [outline[0], outline[1]], fill=INK, width=lw, joint="curve")
    # the folded corner
    fold = [(x1 - c, y0), (x1 - c, y0 + c), (x1, y0 + c)]
    d.polygon(fold, fill=FOLD)
    d.line(fold + [fold[0]], fill=INK, width=lw, joint="curve")
    if kind == "pdf":
        # lines of writing: a heading and four lines
        d.rounded_rectangle([x0 + 0.07 * n, y0 + 0.10 * n, x0 + 0.30 * n, y0 + 0.135 * n], radius=0.017 * n, fill=INK)
        for i, w in enumerate([0.44, 0.40, 0.44, 0.30]):
            y = y0 + (0.215 + i * 0.062) * n
            d.rounded_rectangle([x0 + 0.07 * n, y, x0 + (0.07 + w) * n, y + 0.022 * n], radius=0.011 * n, fill=(74, 47, 39, 120))
    else:
        # a little picture: sky, a sun, two hills
        px0, py0, px1, py1 = x0 + 0.07 * n, y0 + 0.21 * n, x1 - 0.07 * n, y0 + 0.50 * n
        d.rounded_rectangle([px0, py0, px1, py1], radius=0.02 * n, fill=(214, 236, 248, 255))
        d.ellipse([px1 - 0.13 * n, py0 + 0.035 * n, px1 - 0.05 * n, py0 + 0.115 * n], fill=HONEY)
        d.polygon([(px0, py1), (px0 + 0.15 * n, py0 + 0.11 * n), (px0 + 0.30 * n, py1)], fill=SAGE)
        d.polygon([(px0 + 0.17 * n, py1), (px0 + 0.31 * n, py0 + 0.16 * n), (px1, py1)], fill=(150, 200, 156, 255))
        d.rounded_rectangle([px0, py0, px1, py1], radius=0.02 * n, outline=INK, width=int(lw * 0.8))
        d.rounded_rectangle([x0 + 0.07 * n, y0 + 0.10 * n, x0 + 0.27 * n, y0 + 0.135 * n], radius=0.017 * n, fill=INK)
    # the band with the kind of file
    bx0, by0, bx1, by1 = x0 - 0.045 * n, y1 - 0.30 * n, x0 + 0.40 * n, y1 - 0.10 * n
    d.rounded_rectangle([bx0, by0, bx1, by1], radius=0.045 * n, fill=ROSE if kind == "pdf" else SKY, outline=INK, width=lw)
    font = ImageFont.truetype(FONT, int(0.135 * n))
    word = "PDF" if kind == "pdf" else "PNG"
    box = d.textbbox((0, 0), word, font=font)
    d.text(((bx0 + bx1) / 2 - (box[0] + box[2]) / 2, (by0 + by1) / 2 - (box[1] + box[3]) / 2), word, font=font, fill=INK)
    # a little star: the partner made this
    cx, cy, s = x1 - 0.085 * n, y1 - 0.105 * n, 0.062 * n
    import math
    pts = []
    for i in range(10):
        a, rad = -math.pi / 2 + i * math.pi / 5, s if i % 2 == 0 else s * 0.45
        pts.append((cx + rad * math.cos(a), cy + rad * math.sin(a)))
    d.polygon(pts, fill=HONEY)
    d.line(pts + [pts[0], pts[1]], fill=INK, width=int(lw * 0.7), joint="curve")
    img.resize((N, N), Image.LANCZOS).save(os.path.join(OUT, "doc-" + kind + ".png"))
    print("wrote doc-" + kind + ".png")


def blank_page():
    # A4 proportion, plain paper with the faintest lines: only seen for the moment before a page's picture is there
    w, h = 248, 351
    img = Image.new("RGBA", (w, h), PAPER)
    d = ImageDraw.Draw(img)
    d.rounded_rectangle([26, 34, 150, 44], radius=5, fill=(74, 47, 39, 46))
    for i in range(9):
        d.rounded_rectangle([26, 70 + i * 22, (222 if i % 4 != 3 else 160), 76 + i * 22], radius=3, fill=(74, 47, 39, 26))
    img.save(os.path.join(OUT, "doc-page.png"))
    print("wrote doc-page.png")


if __name__ == "__main__":
    sheet("pdf")
    sheet("pic")
    blank_page()
