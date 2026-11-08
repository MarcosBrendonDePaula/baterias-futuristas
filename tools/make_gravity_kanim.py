"""Builds the gravity_battery kanim (png, build.bytes, anim.bytes) from the source art."""
import struct
import sys
from pathlib import Path

from PIL import Image

ROOT = Path(sys.argv[1])
ART = ROOT / "art/gravity_battery"
OUT = ROOT / "anim/assets/gravity_battery"
NAME = "gravity_battery"
ATLAS_SIZE = (2048, 2048)
TOWER_HEIGHT = 1200
WEIGHT_WIDTH = 200
CABLE_SIZE = (20, 400)
LED_SIZE = 24
TARGET_SIZE = 4
CABLE_TOP_Y = -947
WEIGHT_CENTER_EMPTY_Y = -306
WEIGHT_CENTER_FULL_Y = -764
LED_Y = -110
METER_FRAMES = 459
FPS = 30.0


def sdbm(text):
    value = 0
    for char in text.lower():
        value = (ord(char) + (value << 6) + (value << 16) - value) & 0xFFFFFFFF
    return value - (1 << 32) if value >= 1 << 31 else value


def fit(path, height=None, width=None):
    image = Image.open(path).convert("RGBA")
    image = image.crop(image.getchannel("A").getbbox())
    if height is not None:
        size = (round(image.width * height / image.height), height)
    else:
        size = (width, round(image.height * width / image.width))
    return image.resize(size, Image.LANCZOS)


def cable_sprite():
    concept = Image.open(ART / "concept.png").convert("RGBA")
    return concept.crop((503, 360, 523, 750)).resize(CABLE_SIZE, Image.LANCZOS)


def led_sprite(color):
    image = Image.new("RGBA", (LED_SIZE, LED_SIZE), (0, 0, 0, 0))
    pixels = image.load()
    center = (LED_SIZE - 1) / 2
    for y in range(LED_SIZE):
        for x in range(LED_SIZE):
            distance = ((x - center) ** 2 + (y - center) ** 2) ** 0.5
            if distance <= center - 3:
                pixels[x, y] = color
            elif distance <= center:
                pixels[x, y] = (20, 25, 30, 255)
    return image


class Atlas:
    def __init__(self):
        self.image = Image.new("RGBA", ATLAS_SIZE, (0, 0, 0, 0))
        self.symbols = {}
        self.cursor = (2, 2)
        self.row_height = 0

    def add(self, symbol, sprite, pivot):
        x, y = self.cursor
        if x + sprite.width + 2 > ATLAS_SIZE[0]:
            x, y = 2, y + self.row_height + 2
            self.row_height = 0
        self.image.paste(sprite, (x, y))
        self.symbols.setdefault(symbol, []).append((x, y, sprite.width, sprite.height, pivot))
        self.cursor = (x + sprite.width + 2, y)
        self.row_height = max(self.row_height, sprite.height)


def string(text):
    data = text.encode("utf8")
    return struct.pack("<i", len(data)) + data


def table(names):
    unique = sorted(set(names))
    data = struct.pack("<i", len(unique))
    for name in unique:
        data += struct.pack("<i", sdbm(name)) + string(name)
    return data


def build_bytes(atlas):
    frame_total = sum(len(frames) for frames in atlas.symbols.values())
    data = b"BILD" + struct.pack("<iii", 10, len(atlas.symbols), frame_total) + string(NAME)
    for symbol, frames in atlas.symbols.items():
        data += struct.pack("<iiiii", sdbm(symbol), sdbm(symbol), 0, 0, len(frames))
        for index, (x, y, width, height, (pivot_x, pivot_y)) in enumerate(frames):
            data += struct.pack("<iii", index, 1, 0)
            data += struct.pack("<ffff", pivot_x, pivot_y, width, height)
            data += struct.pack(
                "<ffff",
                x / ATLAS_SIZE[0],
                y / ATLAS_SIZE[1],
                (x + width) / ATLAS_SIZE[0],
                (y + height) / ATLAS_SIZE[1],
            )
    return data + table(atlas.symbols)


def element(symbol, frame=0, scale_y=1.0, x=0.0, y=0.0):
    return struct.pack("<iiii", sdbm(symbol), frame, sdbm(symbol), 0) + struct.pack(
        "<11f", 1, 1, 1, 1, 1, 0, 0, scale_y, x, y, 0
    )


def anim_entry(name, frames):
    bounds = struct.pack("<ffff", 0, -TOWER_HEIGHT / 2, 600, TOWER_HEIGHT)
    data = string(name) + struct.pack("<ifi", sdbm(name), FPS, len(frames))
    for elements in frames:
        data += bounds + struct.pack("<i", len(elements)) + b"".join(elements)
    return data


def weight_center(index):
    progress = index / (METER_FRAMES - 1)
    return WEIGHT_CENTER_EMPTY_Y + (WEIGHT_CENTER_FULL_Y - WEIGHT_CENTER_EMPTY_Y) * progress


def anim_bytes(weight_height):
    body = [element("tower"), element("meter_target"), element("logicmeter_target")]
    meter = []
    for index in range(METER_FRAMES):
        center = weight_center(index)
        cable_bottom = center - weight_height / 2 + 30
        length = cable_bottom - CABLE_TOP_Y
        meter.append([
            element("weight", y=center),
            element("cable", scale_y=length / CABLE_SIZE[1], y=(CABLE_TOP_Y + cable_bottom) / 2),
        ])
    anims = [(name, [body]) for name in ("off", "on", "working_pre", "working_loop", "working_pst", "place")]
    anims.append(("ui", [[element("ui")]]))
    anims.append(("meter", meter))
    anims.append(("logicmeter", [[element("logic_led", 0, y=LED_Y)], [element("logic_led", 1, y=LED_Y)]]))
    frame_total = sum(len(frames) for _, frames in anims)
    element_total = sum(len(frame) for _, frames in anims for frame in frames)
    data = b"ANIM" + struct.pack("<iiii", 5, element_total, frame_total, len(anims))
    data += b"".join(anim_entry(name, frames) for name, frames in anims)
    data += struct.pack("<i", 3)
    names = [name for name, _ in anims] + ["tower", "weight", "cable", "meter_target", "logicmeter_target", "logic_led"]
    return data + table(names)


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    tower = fit(ART / "tower.png", height=TOWER_HEIGHT)
    weight = fit(ART / "weight.png", width=WEIGHT_WIDTH)
    atlas = Atlas()
    atlas.add("tower", tower, (0.0, -TOWER_HEIGHT / 2))
    atlas.add("ui", tower, (0.0, -TOWER_HEIGHT / 2))
    atlas.add("weight", weight, (0.0, 0.0))
    atlas.add("cable", cable_sprite(), (0.0, 0.0))
    target = Image.new("RGBA", (TARGET_SIZE, TARGET_SIZE), (0, 0, 0, 0))
    atlas.add("meter_target", target, (0.0, 0.0))
    atlas.add("logicmeter_target", target, (0.0, 0.0))
    atlas.add("logic_led", led_sprite((225, 70, 60, 255)), (0.0, 0.0))
    atlas.add("logic_led", led_sprite((95, 220, 90, 255)), (0.0, 0.0))
    atlas.image.save(OUT / f"{NAME}.png")
    (OUT / f"{NAME}_build.bytes").write_bytes(build_bytes(atlas))
    (OUT / f"{NAME}_anim.bytes").write_bytes(anim_bytes(weight.height))
    print("tower", tower.size, "weight", weight.size)


main()
