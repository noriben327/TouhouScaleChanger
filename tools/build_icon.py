from pathlib import Path

from PIL import Image


ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / "Assets" / "TouhouScalePad-icon.png"
OUTPUT = ROOT / "Assets" / "TouhouScalePad.ico"
SIZES = [(16, 16), (20, 20), (24, 24), (32, 32), (40, 40), (48, 48), (64, 64), (128, 128), (256, 256)]


def main() -> None:
    with Image.open(SOURCE) as opened:
        image = opened.convert("RGBA")

    alpha = image.getchannel("A")
    if alpha.getbbox() is None:
        raise RuntimeError("Icon source is fully transparent.")
    if any(alpha.getpixel(point) != 0 for point in ((0, 0), (image.width - 1, 0),
                                                     (0, image.height - 1),
                                                     (image.width - 1, image.height - 1))):
        raise RuntimeError("Icon source corners must be transparent.")

    image.save(OUTPUT, format="ICO", sizes=SIZES)
    print(f"Wrote {OUTPUT}")


if __name__ == "__main__":
    main()
