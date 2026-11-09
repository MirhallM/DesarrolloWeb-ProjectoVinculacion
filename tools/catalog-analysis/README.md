# catalog-analysis

Herramienta del equipo para probar las reglas de categorías y el nombre para mostrar contra una exportación real de Aspel SAE. **No se despliega**; el backend implementa la misma lógica en C#.

| Archivo | Qué hace |
|---|---|
| `catalog.py` | Implementación de referencia: `Categorizer` y `DisplayNamer`, que leen `backend/Data/Seed/*.json`. |
| `analyze.py` | Mide la cobertura por categoría y lista las palabras más comunes en Otros. |

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
