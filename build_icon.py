from pathlib import Path
from PIL import Image, ImageDraw

size = 256
image = Image.new("RGBA", (size, size), (0, 0, 0, 0))
draw = ImageDraw.Draw(image)
draw.rounded_rectangle((8, 8, 248, 248), radius=60, fill="#173029", outline="#4a977a", width=5)
draw.rounded_rectangle((103, 48, 153, 153), radius=25, fill="#80eac5")
draw.arc((68, 89, 188, 190), 0, 180, fill="#80eac5", width=14)
draw.line((128, 185, 128, 211), fill="#80eac5", width=13)
draw.line((92, 211, 164, 211), fill="#80eac5", width=13)
target = Path(__file__).parent / "Assets" / "VoicePTT.ico"
target.parent.mkdir(parents=True, exist_ok=True)
image.save(target, sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)])
