"""Draws Belie's own creature pack (creatures/*.png and pack.json).

Each creature is 24x24 pixel art with two walking frames, facing right. Run from the repository root:
    python scripts/make_creatures.py
"""
import json
import math
import os

from PIL import Image

SIZE = 24
OUT = "creatures"
INK = (28, 22, 34, 255)
WHITE = (255, 255, 255, 255)


def blank():
    return [[None] * SIZE for _ in range(SIZE)]


def put(grid, x, y, color):
    if 0 <= x < SIZE and 0 <= y < SIZE:
        grid[y][x] = color


def ellipse(grid, cx, cy, rx, ry, color):
    for y in range(SIZE):
        for x in range(SIZE):
            if ((x + .5 - cx) / rx) ** 2 + ((y + .5 - cy) / ry) ** 2 <= 1:
                grid[y][x] = color


def outline(grid):
    """Inks every empty pixel that touches a coloured one."""
    edge = []
    for y in range(SIZE):
        for x in range(SIZE):
            if grid[y][x] is not None:
                continue
            if any(0 <= x + dx < SIZE and 0 <= y + dy < SIZE and grid[y + dy][x + dx] not in (None, INK)
                   for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1))):
                edge.append((x, y))
    for x, y in edge:
        grid[y][x] = INK


def shade(grid, color, dark):
    """Darkens the lower-left of a body for a little volume."""
    for y in range(SIZE):
        for x in range(SIZE):
            if grid[y][x] == color and grid[y - 1][x] == color and (y + 1 >= SIZE or grid[y + 1][x] != color):
                grid[y][x] = dark


def eyes(grid, x, y, gap=4):
    for ex in (x, x + gap):
        put(grid, ex, y, INK)
        put(grid, ex, y + 1, INK)
        put(grid, ex, y, WHITE)


def feet(grid, y, xs, color, frame):
    for i, x in enumerate(xs):
        lift = 1 if (i + frame) % 2 else 0
        put(grid, x, y - lift, color)
        put(grid, x + 1, y - lift, color)


def mossbun(frame):
    g = blank()
    body, dark, leaf = (126, 201, 112, 255), (88, 160, 84, 255), (60, 140, 70, 255)
    ellipse(g, 12, 15, 7.5, 6.5, body)
    for x, y in ((12, 7), (13, 6), (14, 5), (11, 6), (10, 5), (12, 8)):
        put(g, x, y, leaf)
    feet(g, 22, (7, 14), dark, frame)
    shade(g, body, dark)
    eyes(g, 13, 13)
    put(g, 15, 16, (240, 140, 150, 255))
    outline(g)
    return g


def cindrel(frame):
    g = blank()
    body, dark, belly = (240, 140, 70, 255), (200, 100, 50, 255), (255, 214, 160, 255)
    ellipse(g, 12, 15, 6.5, 5.5, body)
    ellipse(g, 13, 17, 3, 2.5, belly)
    for x, y in ((8, 9), (9, 9), (8, 8), (15, 9), (16, 9), (16, 8)):
        put(g, x, y, body)
    flame = [(4, 13), (3, 12), (4, 11), (3, 10), (2, 11), (4, 12), (5, 13)]
    sway = 1 if frame else 0
    for i, (x, y) in enumerate(flame):
        put(g, x, y - sway * (i % 2), (255, 200, 60, 255) if i % 2 else (255, 110, 50, 255))
    feet(g, 21, (8, 14), dark, frame)
    shade(g, body, dark)
    eyes(g, 12, 13)
    outline(g)
    return g


def driplet(frame):
    g = blank()
    body, dark, shine = (100, 170, 240, 255), (70, 130, 210, 255), (200, 235, 255, 255)
    ellipse(g, 12, 16, 6.5, 5.5, body)
    for y in range(6, 12):
        half = (y - 6) // 2 + (1 if frame and y == 6 else 0)
        for x in range(12 - half, 13 + half):
            put(g, x, y, body)
    put(g, 9, 13, shine); put(g, 9, 14, shine)
    feet(g, 22, (8, 14), dark, frame)
    shade(g, body, dark)
    eyes(g, 12, 15)
    outline(g)
    return g


def voltmite(frame):
    g = blank()
    body, dark, stripe = (250, 214, 70, 255), (214, 170, 40, 255), (90, 70, 40, 255)
    for y in range(10, 20):
        for x in range(6, 18):
            put(g, x, y, body)
    for x in range(6, 18):
        put(g, x, 17, stripe)
    spark = (255, 255, 200, 255) if frame else (255, 240, 120, 255)
    for x, y in ((9, 9), (9, 8), (8, 7), (15, 9), (15, 8), (16, 7)):
        put(g, x, y, dark)
    put(g, 8, 6 - frame, spark); put(g, 16, 6 - (1 - frame), spark)
    feet(g, 21, (6, 10, 14), dark, frame)
    shade(g, body, dark)
    eyes(g, 11, 12)
    outline(g)
    return g


def gloomoth(frame):
    g = blank()
    body, dark, wing = (150, 110, 210, 255), (110, 80, 170, 255), (200, 170, 240, 255)
    lift = 1 if frame else 0
    ellipse(g, 6, 11 - lift, 4, 4.5, wing)
    ellipse(g, 18, 11 - lift, 4, 4.5, wing)
    ellipse(g, 12, 14, 4.5, 6, body)
    put(g, 6, 10 - lift, (255, 230, 120, 255)); put(g, 18, 10 - lift, (255, 230, 120, 255))
    for x, y in ((10, 7), (9, 6), (14, 7), (15, 6)):
        put(g, x, y, dark)
    feet(g, 21, (9, 13), dark, frame)
    shade(g, body, dark)
    eyes(g, 10, 12)
    outline(g)
    return g


def pebblet(frame):
    g = blank()
    body, dark, moss = (160, 160, 170, 255), (120, 120, 132, 255), (110, 180, 100, 255)
    ellipse(g, 12, 15, 8, 6, body)
    for x in range(7, 16):
        put(g, x, 9 + (1 if x in (7, 15) else 0), moss)
    put(g, 15, 17, dark); put(g, 16, 16, dark); put(g, 8, 18, dark)
    feet(g, 21, (7, 15), dark, frame)
    shade(g, body, dark)
    eyes(g, 12, 13, 3)
    outline(g)
    return g


CREATURES = [
    ("mossbun", "Mossbun", ["Leaf"], mossbun),
    ("cindrel", "Cindrel", ["Ember"], cindrel),
    ("driplet", "Driplet", ["Tide"], driplet),
    ("voltmite", "Voltmite", ["Spark", "Bug"], voltmite),
    ("gloomoth", "Gloomoth", ["Shade", "Air"], gloomoth),
    ("pebblet", "Pebblet", ["Stone"], pebblet),
]


def save(grid, path):
    image = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
    for y in range(SIZE):
        for x in range(SIZE):
            if grid[y][x] is not None:
                image.putpixel((x, y), grid[y][x])
    image.save(path, optimize=True)


# Each type's color on the Team panel; a pack may name its own types and colors.
COLORS = {"Leaf": "#7CCB6F", "Ember": "#F08A4B", "Tide": "#64AAF0", "Spark": "#F5D547",
          "Bug": "#B5C04A", "Shade": "#9A6ED2", "Air": "#B8A8F0", "Stone": "#A0A0AA",
          "Ground": "#E2BF65", "Flying": "#A98FF3"}

# Creatures whose frames are kept as they are in creatures/ instead of being drawn here. They come first,
# because a new team takes the first six creatures in the pack.
KEPT = [
    {"id": "gliscor", "name": "Gliscor", "types": ["Ground", "Flying"], "frames": ["gliscor-0.png", "gliscor-1.png"],
     "motion": "hang", "mouth": [0.47, 0.34]},
]


def main():
    os.makedirs(OUT, exist_ok=True)
    pack = {"name": "Belie critters", "colors": COLORS, "creatures": [dict(creature) for creature in KEPT]}
    for key, name, types, draw in CREATURES:
        frames = []
        for frame in (0, 1):
            file = f"{key}-{frame}.png"
            save(draw(frame), os.path.join(OUT, file))
            frames.append(file)
        pack["creatures"].append({"id": key, "name": name, "types": types, "frames": frames})
    with open(os.path.join(OUT, "pack.json"), "w", encoding="utf-8") as handle:
        json.dump(pack, handle, indent=2)
        handle.write("\n")


if __name__ == "__main__":
    main()
