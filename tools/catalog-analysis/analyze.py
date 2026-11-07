"""Analiza una exportación de INVE01 y mide cómo se comportan las reglas.

Uso:
    python analyze.py inventario.csv [--out resultados]

El CSV viene de SSMS (separado por ";", sin encabezado) con las columnas:
CVE_ART; DESCR; EXIST; STATUS; PRECIO (lista 1)
"""
import argparse, collections, csv, random
from pathlib import Path
from catalog import Categorizer, DisplayNamer, tokens


def load(path):
    rows = []
    with open(path, encoding='utf-8-sig', newline='') as f:
        for r in csv.reader(f, delimiter=';', quotechar='"'):
            if not r:
                continue
            r += [''] * (5 - len(r))
            num = lambda x: None if x.strip() in ('', 'NULL') else float(x)
            rows.append(dict(code=r[0], descr=r[1], exist=num(r[2]), status=r[3].strip(), price=num(r[4])))
    return rows


MAX_PRICE = 100_000  # precios iguales o mayores son errores de captura (docs/business-rules.md, sección 2)


def visible(r):
    d = r['descr'].strip()
    return r['status'] == 'A' and 0 < (r['price'] or 0) < MAX_PRICE and d and d.upper() != 'NULL'


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('csv')
    ap.add_argument('--out', default='resultados')
    a = ap.parse_args()
    rows = load(a.csv)
    cat, name = Categorizer(), DisplayNamer()
    vis = [r for r in rows if visible(r)]
    out = Path(a.out); out.mkdir(exist_ok=True)

    counts = collections.Counter(); otros = collections.Counter()
    with open(out / 'productos-categorizados.csv', 'w', encoding='utf-8-sig', newline='') as f:
        w = csv.writer(f, delimiter=';')
        w.writerow(['codigo', 'descripcion_aspel', 'nombre_para_mostrar', 'categoria', 'regla', 'disponible'])
        for r in vis:
            slug, kw = cat.categorize(r['descr'])
            counts[slug] += 1
            if slug == 'otros':
                otros.update(set(t for t in tokens(r['descr']) if t.isalpha() and len(t) > 2))
            w.writerow([r['code'], r['descr'].strip(), name(r['descr']), slug, kw or '',
                        'Disponible' if (r['exist'] or 0) > 0 else 'Agotado'])

    total = len(vis)
    print(f'Productos visibles: {total}')
    print(f'Categorizados: {total - counts["otros"]} ({100 * (total - counts["otros"]) / total:.1f} %)')
    for c in cat.categories:
        print(f'  {c["name"]:32} {counts[c["slug"]]:6}')
    print('\nPalabras más comunes en "Otros" (candidatas a reglas nuevas):')
    print(', '.join(f'{t} ({n})' for t, n in otros.most_common(60)))
    print(f'\nDetalle por producto: {out / "productos-categorizados.csv"}')


if __name__ == '__main__':
    main()
