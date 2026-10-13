"""Рисует бесшовные текстуры земли Emberhold в мультяшном стиле: трава, камень, земля
и трава трёх локаций: Топи, Мёртвый город, Горы.

RGB — рисунок крупными пятнами в 3–4 тона (видно с высоты камеры), A — мелкая деталь в серых тонах
около 0,5: шейдер Ground.shader накладывает её мельче и только вблизи камеры.

Запуск из корня репозитория: python3 Tools/GroundTextures/generate_ground_textures.py
Нужны numpy и Pillow. Зерно фиксировано, повторный запуск даёт те же файлы.
"""
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFilter

SIZE = 512
OUT = Path(__file__).resolve().parents[2] / "Assets/_Project/Art/Textures/Ground"


# ---------- бесшовный шум ----------

def value_noise(rng, cells, warp=None):
    """Бесшовный шум значений: решётка cells × cells, плавная интерполяция, период = размер текстуры.
    warp — сдвиг координат в пикселях (2, SIZE, SIZE): ломает прямые линии решётки."""
    lattice = rng.random((cells, cells))
    yy, xx = np.mgrid[0:SIZE, 0:SIZE].astype(np.float64)
    if warp is not None:
        xx = xx + warp[0]
        yy = yy + warp[1]
    tx = (xx * cells / SIZE) % cells
    ty = (yy * cells / SIZE) % cells
    x0 = np.floor(tx).astype(int)
    y0 = np.floor(ty).astype(int)
    fx = tx - x0
    fy = ty - y0
    fx = fx * fx * (3 - 2 * fx)
    fy = fy * fy * (3 - 2 * fy)
    x1 = (x0 + 1) % cells
    y1 = (y0 + 1) % cells
    a, b = lattice[y0, x0], lattice[y0, x1]
    c, d = lattice[y1, x0], lattice[y1, x1]
    return (a * (1 - fx) + b * fx) * (1 - fy) + (c * (1 - fx) + d * fx) * fy


def fbm(rng, base_cells, octaves, gain=0.5, warp=None):
    total, amp, norm = np.zeros((SIZE, SIZE)), 1.0, 0.0
    for o in range(octaves):
        total += value_noise(rng, base_cells * 2 ** o, warp) * amp
        norm += amp
        amp *= gain
    return total / norm


def warp_field(rng, cells, strength):
    """Сдвиг координат для fbm: пятна получаются извилистыми, без следов квадратной решётки."""
    return np.stack([(fbm(rng, cells, 2) - 0.5) * strength, (fbm(rng, cells, 2) - 0.5) * strength])


def normalize(x):
    return (x - x.min()) / max(x.max() - x.min(), 1e-6)


def worley(rng, count, stretch=1.0):
    """Бесшовные ячейки Вороного: расстояния до ближайшей (f1) и второй (f2) точки, номер ячейки.
    stretch > 1 сплющивает ячейки по вертикали: плиты ложатся слоями."""
    pts = rng.random((count, 2)) * SIZE
    yy, xx = np.mgrid[0:SIZE, 0:SIZE].astype(np.float32)
    f1 = np.full((SIZE, SIZE), 1e9, np.float32)
    f2 = np.full((SIZE, SIZE), 1e9, np.float32)
    idx = np.zeros((SIZE, SIZE), np.int32)
    for k, (px, py) in enumerate(pts):
        dx = np.abs(xx - px)
        dx = np.minimum(dx, SIZE - dx)
        dy = np.abs(yy - py)
        dy = np.minimum(dy, SIZE - dy) * stretch
        d = np.sqrt(dx * dx + dy * dy)
        closer = d < f1
        f2 = np.where(closer, f1, np.minimum(f2, d))
        idx = np.where(closer, k, idx)
        f1 = np.where(closer, d, f1)
    return f1, f2, idx, pts


def bands(x, levels):
    """Постеризация 0..1 в levels тонов: мультяшные пятна с чёткими краями."""
    return np.clip(np.floor(x * levels), 0, levels - 1) / (levels - 1)


def palette(t, colors):
    """t в 0..1 → цвет по списку тонов от тёмного к светлому."""
    colors = np.asarray(colors, np.float32)
    n = len(colors) - 1
    pos = np.clip(t, 0, 1) * n
    i = np.clip(np.floor(pos).astype(int), 0, n - 1)
    f = (pos - i)[..., None]
    return colors[i] * (1 - f) + colors[i + 1] * f


def soften(rgb, radius=0.8):
    """Чуть смягчить края пятен, чтобы на мипах не было лесенки."""
    img = Image.fromarray(np.clip(rgb, 0, 255).astype(np.uint8))
    return np.asarray(img.filter(ImageFilter.GaussianBlur(radius)), np.float32)


def wrapped_draw(draw_fn):
    """Рисует на холсте 3×3 плитки и вырезает центр: штрихи у края переходят на другую сторону."""
    big = Image.new("L", (SIZE * 3, SIZE * 3), 0)
    draw_fn(ImageDraw.Draw(big))
    tile = np.zeros((SIZE, SIZE), np.float32)
    arr = np.asarray(big, np.float32) / 255
    for oy in range(3):
        for ox in range(3):
            tile = np.maximum(tile, arr[oy * SIZE:(oy + 1) * SIZE, ox * SIZE:(ox + 1) * SIZE])
    return tile


def save(name, rgb, detail):
    a = np.clip(detail * 255, 0, 255).astype(np.uint8)
    rgba = np.dstack([np.clip(rgb, 0, 255).astype(np.uint8), a])
    Image.fromarray(rgba, "RGBA").save(OUT / name, optimize=True)


# ---------- трава ----------

def grass():
    rng = np.random.default_rng(7501)
    # Приглушённая оливковая трава с холодным оттенком: огонь костра на ней будет заметнее.
    tones = [(58, 74, 50), (74, 92, 58), (90, 108, 66), (108, 124, 76)]
    clumps = bands(normalize(fbm(rng, 3, 4, 0.5, warp_field(rng, 4, 260))) * 0.999, 4)
    rgb = palette(clumps, tones)

    # Пучки травы: короткие светлые штрихи-«галочки» крупного размера.
    def tufts(d):
        for _ in range(260):
            x, y = rng.random(2) * SIZE + SIZE
            h = rng.uniform(10, 18)
            for s in (-1, 0, 1):
                d.line([(x + s * 4, y), (x + s * 7 + rng.uniform(-2, 2), y - h * (1 - 0.25 * abs(s)))],
                       fill=255, width=3)
    tuft = wrapped_draw(tufts)
    rgb = rgb * (1 - tuft[..., None] * 0.6) + np.asarray(tones[3], np.float32) * 1.12 * tuft[..., None] * 0.6
    rgb = soften(rgb)

    # Деталь вблизи: частые тонкие травинки.
    def blades(d):
        for _ in range(2600):
            x, y = rng.random(2) * SIZE + SIZE
            h = rng.uniform(5, 10)
            d.line([(x, y), (x + rng.uniform(-3, 3), y - h)], fill=int(rng.uniform(120, 255)), width=1)
    detail = 0.5 + (wrapped_draw(blades) - 0.35) * 0.55 + (fbm(rng, 32, 2) - 0.5) * 0.25
    save("GroundGrass.png", rgb, np.clip(detail, 0, 1))


# ---------- камень ----------

def rock():
    rng = np.random.default_rng(7502)
    # Сине-серые плиты с тёмными трещинами и светлым краем: холодный камень под туман.
    tones = [(76, 82, 94), (96, 103, 115), (118, 125, 136), (142, 148, 156)]
    f1, f2, idx, pts = worley(rng, 16, stretch=2.2)
    cell_tone = rng.integers(0, 3, len(pts))
    base = cell_tone[idx] / 3.0
    # Внутри плиты — пара крупных пятен, тоже ступенями.
    blot = bands(normalize(fbm(rng, 4, 2, 0.5, warp_field(rng, 4, 160))) * 0.999, 3)
    t = np.clip(base + (blot - 0.5) * 0.35, 0, 1)
    # Свет сверху-слева: сторона плиты к свету светлее, к тени темнее (рисованный объём).
    yy, xx = np.mgrid[0:SIZE, 0:SIZE].astype(np.float32)
    cx, cy = pts[idx, 0], pts[idx, 1]
    ddx = (xx - cx + SIZE / 2) % SIZE - SIZE / 2
    ddy = (yy - cy + SIZE / 2) % SIZE - SIZE / 2
    edge = np.clip(1 - (f2 - f1) / 18, 0, 1)
    lit = (-ddx - ddy) / np.maximum(np.hypot(ddx, ddy), 1)
    t = np.clip(t + edge * lit * 0.45, 0, 1)
    rgb = palette(bands(t * 0.999, 4), tones)
    # Трещины по границам плит.
    crack = (f2 - f1) < 3.6
    rgb[crack] = (52, 56, 68)
    rgb = soften(rgb, 0.7)

    # Деталь вблизи: мелкие сколы и зерно.
    f1d, f2d, _, _ = worley(rng, 220)
    grain = np.clip((f2d - f1d) / 6, 0, 1)
    detail = 0.42 + grain * 0.16 + (fbm(rng, 48, 2) - 0.5) * 0.3
    save("GroundRock.png", rgb, np.clip(detail, 0, 1))


# ---------- земля ----------

def dirt():
    rng = np.random.default_rng(7503)
    # Приглушённая серо-коричневая земля: теплее камня, но без рыжины, чтобы не спорить с огнём.
    tones = [(70, 58, 50), (88, 73, 60), (106, 89, 72), (124, 106, 86)]
    t = bands(normalize(fbm(rng, 3, 4, 0.5, warp_field(rng, 4, 220))) * 0.999, 4)
    rgb = palette(t, tones)
    # Камешки: светлые серые пятна с тёмной нижней кромкой.
    f1, f2, idx, pts = worley(rng, 70)
    size = rng.uniform(3, 7, len(pts))
    keep = rng.random(len(pts)) < 0.55
    r = size[idx]
    stone = (f1 < r) & keep[idx]
    yy, xx = np.mgrid[0:SIZE, 0:SIZE].astype(np.float32)
    ddy = (yy - pts[idx, 1] + SIZE / 2) % SIZE - SIZE / 2
    shadow = stone & (ddy > r * 0.35)
    rgb[stone] = (132, 128, 120)
    rgb[shadow] = (82, 76, 70)
    rgb = soften(rgb, 0.8)

    detail = 0.5 + (fbm(rng, 40, 3) - 0.5) * 0.6
    save("GroundDirt.png", rgb, np.clip(detail, 0, 1))


# ---------- общее для травы ----------

def stones(rng, rgb, count, size_range, keep, color, shadow_color):
    """Камешки или пятна: светлое пятно с тёмной нижней кромкой (рисованный объём). Возвращает маску."""
    f1, f2, idx, pts = worley(rng, count)
    size = rng.uniform(size_range[0], size_range[1], len(pts))
    kept = rng.random(len(pts)) < keep
    r = size[idx]
    blob = (f1 < r) & kept[idx]
    yy, xx = np.mgrid[0:SIZE, 0:SIZE].astype(np.float32)
    ddy = (yy - pts[idx, 1] + SIZE / 2) % SIZE - SIZE / 2
    rgb[blob] = color
    if shadow_color is not None:
        rgb[blob & (ddy > r * 0.35)] = shadow_color
    return blob


def draw_tufts(rng, count, height, spread=(4, 7), width=3):
    """Пучки травы: штрихи-«галочки» по три травинки."""
    def tufts(d):
        for _ in range(count):
            x, y = rng.random(2) * SIZE + SIZE
            h = rng.uniform(*height)
            for s in (-1, 0, 1):
                d.line([(x + s * spread[0], y), (x + s * spread[1] + rng.uniform(-2, 2), y - h * (1 - 0.25 * abs(s)))],
                       fill=255, width=width)
    return wrapped_draw(tufts)


def blade_detail(rng, count, height):
    """Деталь вблизи: частые тонкие травинки."""
    def blades(d):
        for _ in range(count):
            x, y = rng.random(2) * SIZE + SIZE
            h = rng.uniform(*height)
            d.line([(x, y), (x + rng.uniform(-3, 3), y - h)], fill=int(rng.uniform(120, 255)), width=1)
    return wrapped_draw(blades)


# ---------- трава Топей ----------

def grass_swamp():
    rng = np.random.default_rng(7504)
    # Тёмная мокрая трава, холоднее луговой, с подушками мха.
    tones = [(40, 56, 46), (50, 68, 53), (61, 80, 59), (72, 92, 64)]
    clumps = bands(normalize(fbm(rng, 3, 4, 0.5, warp_field(rng, 4, 260))) * 0.999, 4)
    rgb = palette(clumps, tones)
    # Мох: округлые светлые жёлто-зелёные подушки ступенями.
    moss = normalize(fbm(rng, 6, 3, 0.5, warp_field(rng, 6, 120)))
    rgb[moss > 0.72] = (88, 104, 62)
    rgb[moss > 0.82] = (104, 118, 70)
    # Редкие тёмные пучки осоки.
    tuft = draw_tufts(rng, 140, (12, 20))
    rgb = rgb * (1 - tuft[..., None] * 0.55) + np.asarray((34, 48, 40), np.float32) * tuft[..., None] * 0.55
    # Маленькие лужицы неровной формы: тёмная вода с кромкой грязи и бликом у верхнего левого края.
    pools = normalize(fbm(rng, 9, 2, 0.5, warp_field(rng, 8, 90)))
    water = pools > 0.8
    # Кромка — вода, расширенная на пару пикселей (с переносом через край текстуры).
    grown = np.zeros_like(water)
    for oy in range(-3, 4):
        for ox in range(-3, 4):
            if ox * ox + oy * oy <= 9:
                grown |= np.roll(np.roll(water, oy, axis=0), ox, axis=1)
    rim = grown
    # Блик: вода, у которой сверху-слева на несколько пикселей ещё вода, а не берег.
    lit = water & np.roll(np.roll(water, 5, axis=0), 5, axis=1) & ~np.roll(np.roll(water, 9, axis=0), 9, axis=1)
    rgb[rim] = (52, 50, 42)
    rgb[water] = (44, 58, 66)
    rgb[lit] = (84, 102, 110)
    rgb = soften(rgb)

    detail = 0.5 + (blade_detail(rng, 1800, (4, 8)) - 0.35) * 0.45 + (fbm(rng, 36, 3) - 0.5) * 0.35
    detail[water] = 0.5 + (fbm(rng, 24, 2)[water] - 0.5) * 0.1
    save("GroundGrassSwamp.png", rgb, np.clip(detail, 0, 1))


# ---------- трава Мёртвого города ----------

def grass_dead():
    rng = np.random.default_rng(7505)
    # Сухая выгоревшая трава: пыльная олива к соломе, без сочной зелени.
    tones = [(96, 90, 64), (104, 98, 68), (112, 105, 72), (121, 113, 77)]
    # Пятна травы мельче и ближе по тону, чем у обычной травы: крупных пятен на плитку нет, повтор не читается.
    clumps = bands(normalize(fbm(rng, 6, 3, 0.5, warp_field(rng, 6, 160))) * 0.999, 4)
    rgb = palette(clumps, tones)
    # Проплешины голой земли: мелкие, частые, разбросаны ровно по плитке, по тону близки к траве.
    bare = normalize(fbm(rng, 12, 2, 0.5, warp_field(rng, 10, 70)))
    rgb[bare > 0.7] = (104, 93, 74)
    rgb[bare > 0.82] = (97, 86, 70)
    # Пепел: серые пятна и тёмные угольки.
    ash = normalize(fbm(rng, 8, 3, 0.5, warp_field(rng, 8, 90)))
    rgb[ash > 0.86] = (114, 111, 103)
    stones(rng, rgb, 90, (1.5, 3.5), 0.5, (54, 52, 50), None)
    # Редкие сухие пучки.
    tuft = draw_tufts(rng, 110, (8, 15))
    rgb = rgb * (1 - tuft[..., None] * 0.6) + np.asarray((146, 136, 96), np.float32) * tuft[..., None] * 0.6
    rgb = soften(rgb)

    detail = 0.5 + (blade_detail(rng, 900, (4, 8)) - 0.35) * 0.4 + (fbm(rng, 44, 3) - 0.5) * 0.45
    save("GroundGrassDead.png", rgb, np.clip(detail, 0, 1))


# ---------- трава Гор ----------

def grass_mountain():
    rng = np.random.default_rng(7506)
    # Короткая жёсткая трава, холодная сизая зелень.
    tones = [(60, 76, 62), (73, 89, 70), (87, 102, 78), (100, 114, 86)]
    clumps = bands(normalize(fbm(rng, 4, 4, 0.5, warp_field(rng, 4, 200))) * 0.999, 4)
    rgb = palette(clumps, tones)
    # Короткие частые пучки.
    tuft = draw_tufts(rng, 420, (6, 10), spread=(3, 5), width=2)
    rgb = rgb * (1 - tuft[..., None] * 0.55) + np.asarray((112, 124, 92), np.float32) * tuft[..., None] * 0.55
    # Камешки и лишайник (бледно-жёлтые и рыжеватые пятнышки).
    stones(rng, rgb, 80, (3, 6), 0.5, (128, 130, 128), (78, 80, 84))
    stones(rng, rgb, 60, (2, 4), 0.6, (150, 146, 100), None)
    stones(rng, rgb, 40, (2, 3.5), 0.5, (142, 118, 88), None)
    rgb = soften(rgb)

    detail = 0.5 + (blade_detail(rng, 3200, (3, 6)) - 0.35) * 0.5 + (fbm(rng, 40, 2) - 0.5) * 0.25
    save("GroundGrassMountain.png", rgb, np.clip(detail, 0, 1))


if __name__ == "__main__":
    OUT.mkdir(parents=True, exist_ok=True)
    grass()
    rock()
    dirt()
    grass_swamp()
    grass_dead()
    grass_mountain()
    print("Готово:", OUT)
