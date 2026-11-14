#!/usr/bin/env python3
"""Restore ignored Asset Store dependencies from an owned complete Unity project."""
import argparse
import shutil
from pathlib import Path

DEPENDENCIES = (
    'Assets/Adventures_woman', 'Assets/Adventures_woman.meta',
    'Assets/Kevin Iglesias', 'Assets/Kevin Iglesias.meta',
    'Assets/Package Ready Assets', 'Assets/Package Ready Assets.meta',
    'Assets/VolumetricLines', 'Assets/VolumetricLines.meta',
    'Assets/Resources/Generated/BladeVolumetric.mat',
    'Assets/Resources/Generated/BladeVolumetric.mat.meta',
)

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source', required=True, type=Path, help='Complete Unity project root')
    parser.add_argument('--overwrite', action='store_true', help='Replace existing dependency files')
    args = parser.parse_args()
    source = args.source.expanduser().resolve()
    destination = Path(__file__).resolve().parent.parent
    if source == destination:
        parser.error('Source must be a different complete Unity project.')
    missing = [p for p in DEPENDENCIES if not (source / p).exists()]
    if missing:
        parser.error('Source is missing required paths:\n' + '\n'.join(missing))
    count = skipped = 0
    for relative in DEPENDENCIES:
        entry = source / relative
        files = (p for p in entry.rglob('*') if p.is_file()) if entry.is_dir() else [entry]
        for file in files:
            target = destination / file.relative_to(source)
            if target.exists() and not args.overwrite:
                skipped += 1
                continue
            target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(file, target)
            count += 1
    print(f'Restored {count} files; skipped {skipped} existing files. Assets remain ignored by Git.')

if __name__ == '__main__':
    main()
