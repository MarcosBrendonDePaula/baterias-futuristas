"""Builds the kanims of the modular gravity battery: base, rail and motor."""
import math
import struct
import sys
from pathlib import Path

from PIL import Image

ROOT = Path(sys.argv[1])
ART = ROOT / "art/gravity_battery"
ASSETS = ROOT / "anim/assets"
ATLAS_SIZE = (2048, 2048)
TILE = 200
TOWER_HEIGHT = 1200
WEIGHT_WIDTH = 200
CABLE_SIZE = (20, 400)
LED_SIZE = 24
LED_Y = -110
TARGET_SIZE = 4
CABLE_MAX_UNITS = 21000
CABLE_FRAMES = 2101
RAIL_CROP = (391, 565)
BASE_TOP_BAR_CENTER = 729
LEFT_PULLEY = (140.2, 127.3)
RIGHT_PULLEY = (407.0, 127.3)
PULLEY_RADIUS = 51
PULLEY_HUB_RADIUS = 22
VENT_X = (-55.0, 50.6)
VENT_Y = -205.0
VENT_SIZE = (54, 29)
MOTOR_FRAMES = 30
ICON_MAX_SIZE = 128
THERMAL_HEIGHT = 1600
THERMAL_GLOW_SIZE = (290, 749)
THERMAL_GLOW_X = 36.6
THERMAL_GLOW_Y = -792.4
SIMPLE_BATTERIES = (
    ("flywheel", 5, (329, 436, 126)),
    ("compressed_air", 7, None),
    ("flow_battery", 8, None),
    ("molten_salt", 10, None),
)
THERMAL_FRAMES = 101
# Glowing regions in source-image pixels: left, top, right, bottom, RGB.
PULSE_REGIONS = {
    "thermal_sand": ((412, 600, 615, 880, (255, 90, 20)),),
    "compressed_air": ((855, 590, 893, 1080, (255, 170, 40)), (125, 600, 170, 1060, (190, 235, 255))),
    "flow_battery": ((190, 320, 390, 935, (210, 120, 255)), (630, 320, 845, 935, (255, 235, 120))),
    "molten_salt": ((430, 560, 590, 950, (255, 200, 110)),),
    "heat_compressor": ((340, 450, 690, 790, (255, 110, 30)),),
    "expansion_valve": ((484, 430, 540, 630, (120, 230, 255)),),
}
BASE_VARIANTS = (("gravity_base", "weight.png"), ("gravity_hydraulic_base", "tank.png"))
FPS = 30.0
STATIC_ANIMS = ("off", "on", "working_pre", "working_loop", "working_pst", "place")


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
        if y + sprite.height + 2 > ATLAS_SIZE[1]:
            raise ValueError(f"atlas overflow at {symbol}")
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


def build_bytes(name, atlas):
    frame_total = sum(len(frames) for frames in atlas.symbols.values())
    data = b"BILD" + struct.pack("<iii", 10, len(atlas.symbols), frame_total) + string(name)
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


def element(symbol, frame=0, scale_y=1.0, y=0.0, x=0.0, degrees=0.0, alpha=1.0):
    radians = math.radians(degrees)
    cosine, sine = math.cos(radians), math.sin(radians)
    return struct.pack("<iiii", sdbm(symbol), frame, sdbm(symbol), 0) + struct.pack(
        "<11f", alpha, 1, 1, 1, cosine, sine, -sine, cosine * scale_y, x, y, 0
    )


def pulley_sprite(motor, center):
    size = 2 * PULLEY_RADIUS
    left, top = round(center[0]) - PULLEY_RADIUS, round(center[1]) - PULLEY_RADIUS
    sprite = motor.crop((left, top, left + size, top + size))
    pixels = sprite.load()
    middle = (size - 1) / 2
    for y in range(size):
        for x in range(size):
            distance = ((x - middle) ** 2 + (y - middle) ** 2) ** 0.5
            if distance < PULLEY_HUB_RADIUS or distance > PULLEY_RADIUS:
                pixels[x, y] = (0, 0, 0, 0)
    return sprite


def motor_frames(width, direction, glow_base, glow_swing):
    left = (LEFT_PULLEY[0] - width / 2, LEFT_PULLEY[1] - 2 * TILE)
    right = (RIGHT_PULLEY[0] - width / 2, RIGHT_PULLEY[1] - 2 * TILE)
    frames = []
    for index in range(MOTOR_FRAMES):
        turn = direction * 360.0 * index / MOTOR_FRAMES
        glow = glow_base + glow_swing * math.sin(math.radians(abs(turn) * 2))
        frames.append([
            element("vent_glow", x=VENT_X[0], y=VENT_Y, alpha=glow),
            element("vent_glow", x=VENT_X[1], y=VENT_Y, alpha=glow),
            element("pulley_left", x=left[0], y=left[1], degrees=turn),
            element("pulley_right", x=right[0], y=right[1], degrees=turn),
            element("structure"),
        ])
    return frames


def anim_bytes(anims, symbols):
    entries = b""
    element_total = 0
    frame_total = 0
    max_visible = 0
    for name, bounds, frames in anims:
        entries += string(name) + struct.pack("<ifi", sdbm(name), FPS, len(frames))
        for elements in frames:
            entries += struct.pack("<ffff", *bounds) + struct.pack("<i", len(elements)) + b"".join(elements)
            element_total += len(elements)
            frame_total += 1
            max_visible = max(max_visible, len(elements))
    header = b"ANIM" + struct.pack("<iiii", 5, element_total, frame_total, len(anims))
    names = [name for name, _, _ in anims] + list(symbols)
    return header + entries + struct.pack("<i", max_visible) + table(names)


def write_kanim(name, atlas, anims):
    folder = ASSETS / name
    folder.mkdir(parents=True, exist_ok=True)
    atlas.image.save(folder / f"{name}.png")
    (folder / f"{name}_build.bytes").write_bytes(build_bytes(name, atlas))
    (folder / f"{name}_anim.bytes").write_bytes(anim_bytes(anims, atlas.symbols))


def thumbnail(sprite):
    factor = ICON_MAX_SIZE / max(sprite.size)
    return sprite.resize((round(sprite.width * factor), round(sprite.height * factor)), Image.LANCZOS)


def structure(name, sprite, extra_body=(), extra_anims=(), extra_symbols=(), working_frames=None):
    atlas = Atlas()
    pivot = (0.0, -sprite.height / 2)
    atlas.add("structure", sprite, pivot)
    atlas.add("ui", thumbnail(sprite), (0.0, 0.0))
    for symbol, image, symbol_pivot in extra_symbols:
        atlas.add(symbol, image, symbol_pivot)
    bounds = (0, -sprite.height / 2, max(600, sprite.width), sprite.height)
    body = [element("structure"), *extra_body]
    anims = [(anim, bounds, [body]) for anim in STATIC_ANIMS if anim != "working_loop" or working_frames is None]
    if working_frames is not None:
        anims.append(("working_loop", bounds, working_frames))
    anims.append(("ui", bounds, [[element("ui")]]))
    anims.extend(extra_anims)
    write_kanim(name, atlas, anims)


def pulse_symbols(name, art_path, sprite_height):
    source = Image.open(art_path).convert("RGBA")
    left, top, right, bottom = source.getchannel("A").getbbox()
    scale = sprite_height / (bottom - top)
    width = (right - left) * scale
    symbols = []
    placements = []
    for index, (x0, y0, x1, y1, color) in enumerate(PULSE_REGIONS.get(name, ())):
        size = (max(1, round((x1 - x0) * scale)), max(1, round((y1 - y0) * scale)))
        symbol = f"pulse_{index}"
        symbols.append((symbol, Image.new("RGBA", size, (*color, 255)), (0.0, 0.0)))
        center_x = ((x0 + x1) / 2 - left) * scale - width / 2
        center_y = ((y0 + y1) / 2 - top) * scale - sprite_height
        placements.append((symbol, center_x, center_y))
    return symbols, placements


def pulse_elements(placements, frame):
    turn = math.radians(360.0 * frame / MOTOR_FRAMES)
    return [
        element(symbol, x=x, y=y, alpha=0.30 + 0.25 * math.sin(turn + index * math.pi))
        for index, (symbol, x, y) in enumerate(placements)
    ]


def make_thermal(target):
    silo = fit(ROOT / "art/thermal_sand/silo.png", height=THERMAL_HEIGHT)
    glow = Image.new("RGBA", THERMAL_GLOW_SIZE, (255, 120, 20, 220))
    frames = [
        [element("glow", x=THERMAL_GLOW_X, y=THERMAL_GLOW_Y, alpha=index / (THERMAL_FRAMES - 1))]
        for index in range(THERMAL_FRAMES)
    ]
    bounds = (0, -THERMAL_HEIGHT / 2, 1100, THERMAL_HEIGHT)
    pulses, placements = pulse_symbols("thermal_sand", ROOT / "art/thermal_sand/silo.png", THERMAL_HEIGHT)
    symbols = [("glow", glow, (0.0, 0.0)), ("meter_target", target, (0.0, 0.0)), *pulses]
    working = [
        [*pulse_elements(placements, index), element("structure"), element("meter_target")]
        for index in range(MOTOR_FRAMES)
    ]
    structure("thermal_sand", silo, [element("meter_target")], [("meter", bounds, frames)], symbols, working)


def make_simple_batteries(target):
    for name, height_tiles, spinner in SIMPLE_BATTERIES:
        height = height_tiles * TILE
        sprite = fit(ROOT / "art" / name / "building.png", height=height)
        bounds = (0, -height / 2, max(600, sprite.width), height)
        symbols = [
            ("meter_target", target, (0.0, 0.0)),
            ("logicmeter_target", target, (0.0, 0.0)),
            ("logic_led", led_sprite((225, 70, 60, 255)), (0.0, 0.0)),
            ("logic_led", led_sprite((95, 220, 90, 255)), (0.0, 0.0)),
        ]
        extras = [
            ("meter", bounds, [[element("meter_target")], [element("meter_target")]]),
            ("logicmeter", bounds, [[element("logic_led", 0, y=LED_Y)], [element("logic_led", 1, y=LED_Y)]]),
        ]
        pulses, placements = pulse_symbols(name, ROOT / "art" / name / "building.png", height)
        symbols.extend(pulses)
        working = [
            [*pulse_elements(placements, index), element("structure"),
             element("meter_target"), element("logicmeter_target")]
            for index in range(MOTOR_FRAMES)
        ]
        if spinner:
            center_x, center_y, radius = spinner
            disc = disc_sprite(sprite, center_x, center_y, radius)
            symbols.append(("disc", disc, (0.0, 0.0)))
            x = center_x - sprite.width / 2
            y = center_y - height
            working = [
                [element("disc", x=x, y=y, degrees=360.0 * index / MOTOR_FRAMES), element("structure"),
                 element("meter_target"), element("logicmeter_target")]
                for index in range(MOTOR_FRAMES)
            ]
        structure(name, sprite, [element("meter_target"), element("logicmeter_target")], extras, symbols, working)


def make_heat_pump_pieces():
    for name, height_tiles, spinner in (("heat_compressor", 2, (232, 98, 43)), ("expansion_valve", 2, None)):
        height = height_tiles * TILE
        art = ROOT / "art" / name / "building.png"
        sprite = fit(art, height=height)
        symbols, placements = pulse_symbols(name, art, height)
        extra = []
        if spinner:
            center_x, center_y, radius = spinner
            symbols.append(("disc", disc_sprite(sprite, center_x, center_y, radius), (0.0, 0.0)))
            extra = [("disc", center_x - sprite.width / 2, center_y - height)]
        working = []
        for index in range(MOTOR_FRAMES):
            spin = [element(s, x=x, y=y, degrees=360.0 * index / MOTOR_FRAMES) for s, x, y in extra]
            working.append([*pulse_elements(placements, index), *spin, element("structure")])
        structure(name, sprite, (), (), symbols, working)


def disc_sprite(sprite, center_x, center_y, radius):
    size = 2 * radius
    left, top = round(center_x) - radius, round(center_y) - radius
    disc = sprite.crop((left, top, left + size, top + size))
    pixels = disc.load()
    middle = (size - 1) / 2
    for y in range(size):
        for x in range(size):
            if ((x - middle) ** 2 + (y - middle) ** 2) ** 0.5 > radius:
                pixels[x, y] = (0, 0, 0, 0)
    return disc


def main():
    tower = fit(ART / "tower.png", height=TOWER_HEIGHT)
    width = tower.width

    # Every seam is cut at the center of a crossbar so stacked pieces continue each other.
    motor = tower.crop((0, 0, width, RAIL_CROP[0])).resize((width, 2 * TILE), Image.LANCZOS)
    base = tower.crop((0, BASE_TOP_BAR_CENTER, width, TOWER_HEIGHT)).resize((width, 2 * TILE), Image.LANCZOS)
    rail = tower.crop((0, RAIL_CROP[0], width, RAIL_CROP[1])).resize((width, TILE), Image.LANCZOS)
    motor_symbols = [
        ("pulley_left", pulley_sprite(motor, LEFT_PULLEY), (0.0, 0.0)),
        ("pulley_right", pulley_sprite(motor, RIGHT_PULLEY), (0.0, 0.0)),
        ("vent_glow", Image.new("RGBA", VENT_SIZE, (255, 150, 40, 200)), (0.0, 0.0)),
    ]
    motor_bounds = (0, -TILE, 600, 2 * TILE)
    discharge = [("discharge_loop", motor_bounds, motor_frames(width, -1, 0.15, 0.10))]
    charge_frames = motor_frames(width, 1, 0.55, 0.40)
    structure("gravity_motor", motor, extra_anims=discharge, extra_symbols=motor_symbols, working_frames=charge_frames)
    structure("gravity_rail", rail)

    target = Image.new("RGBA", (TARGET_SIZE, TARGET_SIZE), (0, 0, 0, 0))
    cable_frames = []
    for index in range(CABLE_FRAMES):
        length = max(1.0, CABLE_MAX_UNITS * index / (CABLE_FRAMES - 1))
        cable_frames.append([element("cable", scale_y=length / CABLE_SIZE[1], y=length / 2)])
    led_frames = [[element("logic_led", 0, y=LED_Y)], [element("logic_led", 1, y=LED_Y)]]
    meter_bounds = (0, -TILE, 600, 2 * TILE)
    body = [element("meter_target"), element("logicmeter_target")]
    for kanim_name, art_file in BASE_VARIANTS:
        load = fit(ART / art_file, width=WEIGHT_WIDTH)
        extras = [
            ("meter", meter_bounds, [[element("meter_target")], [element("meter_target")]]),
            ("logicmeter", meter_bounds, led_frames),
            ("weight", (0, 0, 2 * WEIGHT_WIDTH, 2 * load.height), [[element("weight")]]),
            ("cable", (0, CABLE_MAX_UNITS / 2, 200, CABLE_MAX_UNITS), cable_frames),
        ]
        symbols = [
            ("weight", load, (0.0, 0.0)),
            ("cable", cable_sprite(), (0.0, 0.0)),
            ("meter_target", target, (0.0, 0.0)),
            ("logicmeter_target", target, (0.0, 0.0)),
            ("logic_led", led_sprite((225, 70, 60, 255)), (0.0, 0.0)),
            ("logic_led", led_sprite((95, 220, 90, 255)), (0.0, 0.0)),
        ]
        structure(kanim_name, base, body, extras, symbols)
    make_thermal(target)
    make_simple_batteries(target)
    make_heat_pump_pieces()
    print("base", base.size, "rail", rail.size, "motor", motor.size)


main()
