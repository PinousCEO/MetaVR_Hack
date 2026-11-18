# The arrow sticker that points to a new thing which is out of the user's sight (MrArrive):
#   MetaVRHack/Assets/LivingPartner/Art/Ui/arrow-sticker.png  (192 x 192, see-through round it)
# A round cream sticker with a brown ink rim and a honey arrow that points to the RIGHT (the app turns it).
# Run:  python tools/ui-art/build_arrow_sticker.py        (needs Pillow)
import os
from PIL import Image, ImageDraw, ImageFilter

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
DST = os.path.join(ROOT, "MetaVRHack", "Assets", "LivingPartner", "Art", "Ui", "arrow-sticker.png")
N, SS = 192, 6
CREAM, INK, HONEY, HONEY_DARK = (255, 248, 234, 255), (74, 47, 39, 255), (255, 196, 84, 255), (240, 160, 56, 255)


def main():
    n = N * SS
    img = Image.new("RGBA", (n, n), (0, 0, 0, 0))
    # a soft shadow under the sticker
    sh = Image.new("RGBA", (n, n), (0, 0, 0, 0))
    ImageDraw.Draw(sh).ellipse([n * 0.13, n * 0.16, n * 0.87, n * 0.90], fill=(74, 47, 39, 70))
    img = Image.alpha_composite(img, sh.filter(ImageFilter.GaussianBlur(n * 0.03)))
    d = ImageDraw.Draw(img)
    d.ellipse([n * 0.12, n * 0.12, n * 0.88, n * 0.88], fill=INK)
    d.ellipse([n * 0.145, n * 0.145, n * 0.855, n * 0.855], fill=CREAM)
    # the arrow: a thick shaft and a head, ink outline, honey fill
    def arrow(grow, fill):
        y0, y1 = 0.5 - 0.075 - grow, 0.5 + 0.075 + grow
        pts = [(0.27 - grow, y0), (0.50, y0), (0.50, 0.5 - 0.20 - grow * 1.6), (0.76 + grow * 1.5, 0.5),
               (0.50, 0.5 + 0.20 + grow * 1.6), (0.50, y1), (0.27 - grow, y1)]
        d.polygon([(x * n, y * n) for x, y in pts], fill=fill)
    arrow(0.022, INK)
    arrow(0.0, HONEY)
    d.polygon([(0.29 * n, 0.5 * n), (0.50 * n, 0.5 * n), (0.50 * n, 0.69 * n), (0.735 * n, 0.5 * n), (0.50 * n, 0.5 * n), (0.50 * n, 0.572 * n), (0.29 * n, 0.572 * n)], fill=HONEY_DARK)
    img.resize((N, N), Image.LANCZOS).save(DST)
    print("wrote", DST)


if __name__ == "__main__":
    main()
