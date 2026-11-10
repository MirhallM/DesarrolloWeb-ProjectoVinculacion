# catalog-analysis

Herramienta del equipo para probar las reglas de categorías y el nombre para mostrar contra una exportación real de Aspel SAE. **No se despliega**; el backend implementa la misma lógica en C#.

| Archivo | Qué hace |
|---|---|
| `catalog.py` | Implementación de referencia: `Categorizer` y `DisplayNamer`, que leen `backend/src/backend/Data/Seed/*.json`. |
| `analyze.py` | Mide la cobertura por categoría y lista las palabras más comunes en Otros. |
| `parity_cases.py` | Genera los casos de la prueba de paridad con el backend en C# (ver más abajo). |

Requisitos: Python 3.10 o superior, sin dependencias externas.

## Exportar el inventario desde SSMS

En la base `SAE70Empre01`:

```sql
SELECT i.CVE_ART, i.DESCR, i.EXIST, i.STATUS, p.PRECIO
FROM dbo.INVE01 AS i
LEFT JOIN dbo.PRECIO_X_PROD01 AS p
    ON p.CVE_ART = i.CVE_ART
   AND p.CVE_PRECIO = 1;
```

Resultados → clic derecho → **Guardar resultados como…** → CSV. SSMS lo guarda separado por `;`, sin encabezado y en UTF-8.

## Uso

```bash
cd tools/catalog-analysis
python analyze.py data/inventario.csv
```

Genera `resultados/productos-categorizados.csv` (abre bien en Excel).

> El inventario tiene precios y existencias del negocio. Va en `data/` o `resultados/`, que están en `.gitignore`. **No se sube al repositorio.**

## Probar una descripción

```python
from catalog import Categorizer, DisplayNamer
Categorizer().categorize('LECHE ENT 1LT')   # ('lacteos', 'LECHE')
DisplayNamer()('LECHE ENT 1LT')             # 'Leche Entera 1 L'
```

Las reglas y el algoritmo se explican en [docs/catalog-categories.md](../../docs/catalog-categories.md).

## Casos de paridad con el backend

El backend en C# debe dar la misma categoría y el mismo nombre que `catalog.py`. Las pruebas comparan ambos con un archivo de casos que se genera aquí:

```bash
cd tools/catalog-analysis
python parity_cases.py data/inventario.csv
```

Escribe `backend/tests/backend.Tests/Catalog/Data/catalog-parity-cases.jsonl`: un JSON por línea con la descripción de Aspel, la categoría y el nombre para mostrar. **No lleva código, precio ni existencia** y no repite descripciones. Ese archivo sí se sube al repositorio.

Hay que regenerarlo y subirlo en el mismo PR cada vez que cambien `categories.json`, `display-dictionary.json` o el algoritmo de `catalog.py`; si no, la prueba de paridad falla.
