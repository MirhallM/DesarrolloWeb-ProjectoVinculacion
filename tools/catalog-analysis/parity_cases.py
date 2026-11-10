"""Genera los casos de prueba de paridad entre catalog.py y el backend en C#.

Uso:
    python parity_cases.py data/inventario.csv
    python parity_cases.py data/inventario.csv --out otra/ruta.jsonl

Escribe un JSON por línea con la descripción de Aspel (sin recortar), la categoría y el nombre
para mostrar que calcula catalog.py. NO incluye código, precio ni existencia, y no repite
descripciones. El resultado va al proyecto de pruebas del backend (backend/tests/backend.Tests).
"""
import argparse, json
from pathlib import Path
from analyze import load
from catalog import Categorizer, DisplayNamer

DEFAULT_OUT = (Path(__file__).resolve().parents[2]
               / 'backend' / 'tests' / 'backend.Tests' / 'Catalog' / 'Data' / 'catalog-parity-cases.jsonl')


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('csv')
    ap.add_argument('--out', default=str(DEFAULT_OUT))
    a = ap.parse_args()

    descriptions = sorted({r['descr'] for r in load(a.csv)})
    cat, name = Categorizer(), DisplayNamer()

    out = Path(a.out)
    out.parent.mkdir(parents=True, exist_ok=True)
    with open(out, 'w', encoding='utf-8', newline='\n') as f:
        for d in descriptions:
            f.write(json.dumps({'description': d, 'category': cat.categorize(d)[0], 'displayName': name(d)},
                               ensure_ascii=False) + '\n')

    print(f'{len(descriptions)} descripciones únicas -> {out}')


if __name__ == '__main__':
    main()
