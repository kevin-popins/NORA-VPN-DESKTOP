#!/usr/bin/env python3
"""Build shipping backgrounds from separately stored PNG masters.

Requires Pillow 11.3.0 (pip install Pillow==11.3.0). Example:
  python scripts/optimize-location-assets.py --masters <master-directory> --output <empty-directory>

Keep masters outside assets and version control. Never recompress derivatives.
The output directory must be empty, so old extensions cannot remain unnoticed.
Review derivatives before replacing assets/locations. Runtime needs only Windows'
built-in JPEG/PNG decoder; Pillow is a build-time tool only.
"""

import argparse
import hashlib
import json
from pathlib import Path

from PIL import Image, __version__, features

QUALITY = 95


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--masters', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--report', type=Path, help='Optional JSON manifest outside shipping assets')
    args = parser.parse_args()
    if __version__ != '11.3.0':
        parser.error('Use Pillow==11.3.0 for the verified encoder settings.')
    if features.version_codec('jpg') != '8.0' or features.version_feature('libjpeg_turbo') != '3.1.1':
        parser.error('Use the Pillow 11.3.0 Windows wheel with libjpeg-turbo 3.1.1 / JPEG API 8.0.')
    source = args.masters.resolve(strict=True)
    output = args.output.resolve()
    if source == output or source in output.parents or output in source.parents:
        parser.error('Masters and output must be separate directories.')
    masters = sorted(source.glob('*.png'), key=lambda path: path.name.casefold())
    if not masters:
        parser.error('No PNG masters found.')
    if len({path.stem.casefold() for path in masters}) != len(masters):
        parser.error('Master names must be unique on case-insensitive filesystems.')
    if output.exists() and any(output.iterdir()):
        parser.error('Output directory must be empty; review generated assets before replacing shipped files.')
    output.mkdir(parents=True, exist_ok=True)
    rows = []
    for master in masters:
        with Image.open(master) as original:
            original.load()
            rgba = original.convert('RGBA')
            transparent = rgba.getchannel('A').getextrema()[0] < 255
            suffix = '.png' if transparent else '.jpg'
            target = output / (master.stem + suffix)
            metadata = {}
            if original.info.get('icc_profile'):
                metadata['icc_profile'] = original.info['icc_profile']
            if transparent:
                rgba.save(target, format='PNG', optimize=True, compress_level=9, **metadata)
            else:
                original.convert('RGB').save(target, format='JPEG', quality=QUALITY,
                                             subsampling=0, optimize=True, progressive=False,
                                             **metadata)
            with Image.open(target) as decoded:
                decoded.load()
                if decoded.size != original.size:
                    raise RuntimeError(f'Dimension mismatch: {master.name}')
                if transparent and decoded.convert('RGBA').tobytes() != rgba.tobytes():
                    raise RuntimeError(f'Lossless RGBA verification failed: {master.name}')
            rows.append({'source': master.name, 'output': target.name,
                         'width': original.width, 'height': original.height,
                         'transparent': transparent, 'source_bytes': master.stat().st_size,
                         'output_bytes': target.stat().st_size,
                         'source_sha256': digest(master), 'output_sha256': digest(target)})
    report = {'pillow': __version__, 'jpeg_api': features.version_codec('jpg'),
              'libjpeg_turbo': features.version_feature('libjpeg_turbo'),
              'jpeg_quality': QUALITY, 'jpeg_subsampling': '4:4:4',
              'resized': False, 'files': rows,
              'source_bytes': sum(row['source_bytes'] for row in rows),
              'output_bytes': sum(row['output_bytes'] for row in rows)}
    report['reduction_percent'] = round(100 * (1 - report['output_bytes'] / report['source_bytes']), 2)
    if args.report:
        args.report.parent.mkdir(parents=True, exist_ok=True)
        args.report.write_text(json.dumps(report, indent=2) + '\n', encoding='utf-8')
    print(json.dumps({key: value for key, value in report.items() if key != 'files'}, indent=2))


if __name__ == '__main__':
    main()
