#!/usr/bin/env python3
"""Быстрая валидация исходного пакета Quarry_01 без сторонних библиотек.

Run: python validate_sources.py
"""
from __future__ import annotations
import json
import struct
from pathlib import Path

base = Path(__file__).resolve().parent
cfg = json.loads((base / 'quarry_01.json').read_text(encoding='utf-8'))
w = cfg['world']
n = int(w['terrain_heightmap_resolution'])
assert n == 1025, f'Неожиданное разрешение: {n}'
assert w['size_x_m'] == 1500 and w['size_z_m'] == 1500
assert w['height_max_m'] == 210 and w['height_min_m'] == 0
assert cfg['map_id'] == 'Quarry_01'
assert len(cfg['roads']['ring_roads']) == 4
assert cfg['seed'] == 17031

raw = (base / 'Quarry_01_heightmap_le.raw').read_bytes()
expected = n * n * 2
assert len(raw) == expected, f'RAW: {len(raw)} байт вместо {expected}'

# Без numpy/Pillow проверяем весь массив uint16 LE, диапазон и примерную глубину.
vals = struct.unpack(f'<{n*n}H', raw)
minval, maxval = min(vals), max(vals)
assert 0 <= minval < maxval <= 65535

center = vals[(n // 2) * n + (n // 2)]
corner = vals[0]
# Верх должен находиться заметно выше центра, без привязки к абсолютной высоте.
assert corner > center + 10000, 'Карта не похожа на карьер (угол не выше центра)'

for poi in cfg['points_of_interest']:
    assert poi['id'], 'POI без id'

print('OK: Quarry_01 источники согласованы')
print(f'RAW: {n}x{n}, little-endian uint16, {len(raw)} байт')
print(f'Raw min/max: {minval}/{maxval}; центр: {center}; угол: {corner}')
print('Это НЕ проверка проходимости дорог или запуска Unity.')
