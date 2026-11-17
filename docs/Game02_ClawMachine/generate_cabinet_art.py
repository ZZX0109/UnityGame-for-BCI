"""Original cabinet typography; run before generate_prototype.py (Pillow)."""
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parents[2]
TARGET = ROOT / 'Assets/ClawMachine/Textures/CabinetMarquee.png'


def build():
    font_path = Path('C:/Windows/Fonts/segoeuib.ttf')
    if not font_path.is_file():
        raise SystemExit('Supply the local Segoe UI Bold font for original cabinet lettering.')
    image = Image.new('RGB', (2048, 256), '#142c32')
    draw = ImageDraw.Draw(image)
    gold = '#d4b47b'
    draw.rounded_rectangle((24, 24, 2024, 232), radius=28, outline=gold, width=3)
    draw.line((128, 68, 128, 178), fill=gold, width=5)
    draw.line((92, 103, 128, 80, 164, 103), fill=gold, width=5)
    draw.arc((78, 100, 178, 190), 0, 180, fill=gold, width=5)
    draw.text((232, 25), 'CLAW ATELIER', font=ImageFont.truetype(str(font_path), 130), fill='#f3eee2', stroke_width=0)
    draw.text((1652, 70), '01 / 03', font=ImageFont.truetype(str(font_path), 56), fill=gold)
    TARGET.parent.mkdir(parents=True, exist_ok=True)
    image.save(TARGET)
    print('Authored original cabinet lettering:', TARGET.name)


if __name__ == '__main__':
    build()
