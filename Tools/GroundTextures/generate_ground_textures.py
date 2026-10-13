"""Рисует бесшовные текстуры земли Emberhold в мультяшном стиле: трава, камень, земля.

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


if __name__ == "__main__":
    OUT.mkdir(parents=True, exist_ok=True)
    grass()
    rock()
    dirt()
    print("Готово:", OUT)
