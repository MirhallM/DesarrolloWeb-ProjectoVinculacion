# Reglas comerciales

**Catálogo Web – Mini Super Nettes**  
Documento de la historia **US-04: Confirmar las reglas comerciales**.

Define cómo se interpretan los datos de Aspel SAE y qué ve el cliente en el catálogo. Las reglas marcadas como **pendiente de validación** requieren la confirmación del negocio.

## 1. Datos que se leen de Aspel SAE

| Dato | Fuente | Uso |
|---|---|---|
| Código de artículo | `INVE01.CVE_ART` | Identifica el producto. No se muestra al cliente. **Se guarda exactamente como viene, sin recortar espacios.** |
| Descripción | `INVE01.DESCR` | Base del nombre que ve el cliente (sección 5). |
| Existencia | `INVE01.EXIST` | Determina si está disponible. No se muestra la cantidad. |
| Estado | `INVE01.STATUS` | `A` = activo, `B` = baja. |
| Precio público | `PRECIO_X_PROD01.PRECIO` con `CVE_PRECIO = 1` | Precio mostrado, con ISV incluido. Se redondea a 2 decimales. |

No se usan:

- `INVE01.LIN_PROD`: los productos no tienen la línea asignada en Aspel. Las categorías las asigna la aplicación (sección 4).
- Fechas de última compra y última venta: son datos internos del negocio y no se sincronizan.

Detalles de los datos (inventario de noviembre de 2026, 18 460 artículos):

- **Códigos con espacios:** 14 códigos aparecen dos veces, una con espacios al inicio (`"  2050000117121"` y `"2050000117121"`). En Aspel son **artículos distintos**, con descripciones distintas. Si se recortan los espacios chocan con la restricción única de `code`.
- **Precio:** `PRECIO` es `float` en SQL Server, así que puede traer decimales de más (`34.999999`). Se redondea a 2 decimales al recibirlo.
- **Existencia:** también es `float`; 389 artículos tienen existencia con decimales (productos por libra).
- **Longitudes:** `DESCR` mide hasta 40 caracteres y `CVE_ART` hasta 16.

Consulta de referencia para el agente de sincronización (se verifica contra la base real en US-05):

```sql
SELECT i.CVE_ART, i.DESCR, i.EXIST, i.STATUS, p.PRECIO
FROM INVE01 AS i
LEFT JOIN PRECIO_X_PROD01 AS p
    ON p.CVE_ART = i.CVE_ART
   AND p.CVE_PRECIO = 1;
```

## 2. Estado y visibilidad

| Situación en Aspel | En la aplicación | ¿Se ve en el catálogo? |
|---|---|---|
| `STATUS = 'A'` | `is_active = true` | Sí, si tiene precio |
| `STATUS = 'B'` | `is_active = false` | No |
| El producto deja de venir de Aspel | `is_active = false` | No |
| Activo sin precio con `CVE_PRECIO = 1`, o con precio 0 | Se guarda con `public_price = 0` | No |
| Activo con descripción vacía o con el texto `NULL` | Se guarda; `display_name` vacío | No |
| Activo con precio de 100 000 o más | Se guarda tal cual; el panel lo señala como precio sospechoso | No. **Límite pendiente de validación.** |

**Bajas masivas por `STATUS`:** si en una corrida muchos productos que estaban activos pasan a `STATUS ≠ 'A'`, la sincronización **no se frena**: se aplica, se cuenta en `sync_runs.products_deactivated_by_status` y, desde 100 productos (`Sync:StatusDeactivationWarningCount`), se escribe una advertencia en el log. Un `B` es una decisión explícita en Aspel, la fuente de verdad, y es reversible (con `A` el producto reaparece en la siguiente corrida). Frenar detendría también los precios y las existencias de todo el catálogo. Lo que sí frena es que los productos **dejen de venir** (lectura vacía o parcial), que es lo que puede ocultar el catálogo por error ([architecture.md](architecture.md#51-protocolo-us-06)).

Los productos que no se ven **no se borran** de PostgreSQL: se conservan para no romper favoritos ni imágenes ya asociadas, y vuelven a mostrarse si cambian en Aspel.

Regla de visibilidad: **`is_active = true`, `0 < public_price < 100 000` y `display_name` no vacío**.

En el inventario de noviembre de 2026 hay 121 activos sin precio, 27 activos sin descripción y 2 precios imposibles (15 000 000 000 y 11 111 111 111). Los listados están en la carpeta de revisión para que el negocio los corrija en Aspel; al corregirlos, la sincronización los hace visibles sin hacer nada más.

## 3. Disponibilidad

| Condición | Se muestra como |
|---|---|
| Visible y `EXIST > 0` | **Disponible** |
| Visible y `EXIST <= 0` | **Agotado** |

- Los productos agotados **sí aparecen** en el catálogo, marcados como agotados.
- Al cliente se le muestra solo el estado, nunca la cantidad exacta.
- La existencia puede ser negativa en Aspel; se trata como agotado.
- **Existencias infladas:** 715 artículos activos tienen 10 000 unidades o más y 118 tienen un millón o más (por ejemplo, 500 000 000 000). El negocio sabe que son valores de relleno. No afectan al catálogo porque solo se usa `EXIST > 0`, pero el panel de administración no debe usar la cantidad para nada más (reportes, alertas de inventario).
- La disponibilidad es informativa y no reserva mercancía. Se muestra junto a la fecha de la última sincronización exitosa (US-12).

## 4. Categorías

Aspel no tiene categorías útiles, así que **la aplicación las asigna con reglas de palabras clave** sobre la descripción.

- **Lista de categorías:** la propone el equipo revisando descripciones reales y la valida el negocio. **Pendiente de validación.**
- **Reglas:** las mantiene el equipo en el código o en datos semilla; se cambian mediante un pull request. No hay pantalla de administración para esto.
- **Coincidencia:** se compara la descripción en mayúsculas y sin acentos contra cada palabra clave, como palabras completas y seguidas; se acepta el plural con `S` o `ES`.
- **Conflictos:** si varias reglas coinciden, gana la de mayor prioridad; si empatan, la palabra clave más larga (por ejemplo, `CHOCOLATE` antes que `LECHE` en "CHOCOLATE CON LECHE"); si siguen empatadas, la categoría que va primero.
- **Sin coincidencia:** el producto va a la categoría **Otros**.
- **Datos semilla:** `backend/src/backend/Data/Seed/categories.json`. La lista propuesta, el algoritmo detallado y los resultados con el inventario real están en [catalog-categories.md](catalog-categories.md).
- **Cuándo se aplican:** en dos momentos, y los dos llegan al mismo resultado.
  1. **Al sincronizar:** cada corrida con cambios calcula la categoría de cada artículo recibido y escribe solo los que difieren de lo guardado. Como el agente envía el inventario completo, una corrida es también una recategorización de todo lo que llega.
  2. **Al desplegar:** el comando `seed` (pre-deploy de Railway) deja las tablas `categories` y `category_rules` iguales a `categories.json` y **recategoriza todos los productos** con las reglas vigentes. Es idempotente: solo cambian (y solo cambian su `updated_at`) las filas cuya categoría o nombre cambió; `synced_at` no se toca, porque no es una sincronización. Una categoría quitada del JSON se borra y sus productos pasan a la que indiquen las reglas.
- **Si un contenedor anterior sincroniza durante el despliegue:** puede guardar productos con las reglas viejas después del `seed`. Esa corrida queda sin huella de reglas y la siguiente corrida es completa por fuerza (ver [architecture.md](architecture.md#51-protocolo-us-06)), así que se corrige sola en el siguiente ciclo (15 minutos).
- **Si cambia el código del categorizador sin cambiar ningún JSON** (por ejemplo, la normalización de texto): en Railway el despliegue ya cambia la huella (incluye el commit), pero hay que subir `CatalogRulesFingerprint.AlgorithmVersion` para que también ocurra en local o en otro entorno. El `seed` recalcula siempre, así que corrige los productos aunque no se suba.

## 5. Nombre que ve el cliente

Las descripciones de Aspel vienen en mayúsculas y abreviadas (por ejemplo, `LECHE ENT 1LT`). Sin modificar Aspel, la aplicación genera un nombre para mostrar:

1. Expande abreviaturas comunes con un diccionario (`ENT` → `Entera`, `LT` → `L`).
2. Convierte el resultado a tipo título.

`LECHE ENT 1LT` → `Leche Entera 1 L`

- El diccionario lo mantiene el equipo, igual que las reglas de categorías: `backend/src/backend/Data/Seed/display-dictionary.json`. Los pasos completos están en [catalog-categories.md](catalog-categories.md).
- Las abreviaturas que cambian con el género (`PEQ` → pequeño o pequeña) se ajustan a la primera palabra de la descripción.
- Las abreviaturas sin entrada en el diccionario se dejan como vienen.
- Si cambia el diccionario, se recalculan los nombres junto con la recategorización (mismo flujo que las reglas de categorías, sección 4): al sincronizar y en el `seed` del despliegue.

## 6. Imágenes

Un producto sin imagen se muestra con una **imagen genérica** y aparece igual en el catálogo.

## 7. Qué se muestra públicamente

| Dato | Catálogo público | Panel de administración |
|---|---|---|
| Nombre para mostrar | Sí | Sí |
| Descripción original de Aspel | No | Sí |
| Precio público | Sí | Sí |
| Disponible / Agotado | Sí | Sí |
| Cantidad en existencia | No | Sí |
| Categoría | Sí | Sí |
| Imagen | Sí | Sí |
| Código de artículo | No | Sí |

- **Búsqueda pública:** por nombre.
- **Búsqueda en el panel:** por nombre o por código de artículo, para asignar imágenes.

## 8. Impacto en otras partes del proyecto

**Esquema de PostgreSQL** (requiere una migración nueva):

- Quitar `last_purchase_at` y `last_sale_at` de `products`.
- Quitar `category_code` y `category_name`, que dependían de `LIN_PROD`.
- Agregar `display_name` a `products` (nombre para mostrar) y mover a esa columna el índice de búsqueda con trigramas.
- Agregar las tablas `categories` y `category_rules`, y la columna `products.category_id`.
- La sincronización guarda `public_price = 0` cuando no hay precio con `CVE_PRECIO = 1`.

**Historias:**

| Historia | Ajuste |
|---|---|
| US-06 y US-07 | Aplicar reglas de visibilidad, categorías y nombre para mostrar al importar; recategorizar cuando cambien las reglas (hecho: ver secciones 4 y 5). Portar a C# el algoritmo de `tools/catalog-analysis/catalog.py` y comprobar que da el mismo resultado. No recortar `CVE_ART`; redondear `PRECIO`. |
| US-08 | Las respuestas públicas no incluyen el código ni la cantidad en existencia. El detalle de producto usa el `id`, no el código. |
| US-10 | Mostrar nombre para mostrar, precio, Disponible / Agotado e imagen genérica. |
| US-11 | Búsqueda pública por nombre y filtro por las categorías propias. |
| US-12 | Estados Disponible y Agotado según la sección 3. |
| US-17 | Búsqueda por código en el panel. |

## 9. Pendientes

- Validar con el negocio la lista de categorías y la revisión de `01-productos-con-categoria.csv`.
- Confirmar con el negocio el límite de precio sospechoso (100 000).
- Que el negocio corrija en Aspel los productos sin precio, sin descripción y con precios imposibles.
- Verificar la consulta de la sección 1 contra la base de Aspel (US-05).
