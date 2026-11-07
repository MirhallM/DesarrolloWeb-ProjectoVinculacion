# Categorías y nombres del catálogo

**Catálogo Web – Mini Super Nettes**  
Complementa la sección 4 y 5 de [business-rules.md](business-rules.md). Resultado del análisis del inventario de Aspel SAE exportado en noviembre de 2026 (18 460 artículos).

> **Estado:** propuesta del equipo, **pendiente de validación del negocio**. El negocio revisa `01-productos-con-categoria.csv` y marca los productos mal ubicados.

## 1. Resumen

| Dato | Valor |
|---|---|
| Artículos en `INVE01` | 18 460 |
| Visibles en el catálogo (activos, con precio válido y con descripción) | 18 271 |
| Con categoría asignada por regla | 15 565 (85.2 %) |
| En **Otros** | 2 706 (14.8 %) |
| Precisión estimada en muestras revisadas a mano | ~90–95 % |
| Visibles agotados (`EXIST <= 0`) | 1 610 |

La precisión se midió revisando muestras al azar de cada categoría. Los errores que quedan son casi siempre palabras con dos sentidos (`PALETA` dulce o de broca, `AEROSOL` insecticida o pintura), marcas desconocidas y descripciones mal escritas en Aspel (`ELADO`, `GALLLETA`).

## 2. Categorías propuestas

| Categoría | Productos | Reglas | Ejemplos (nombre para mostrar) |
|---|---:|---:|---|
| Abarrotes | 1252 | 141 | Atún Desmenuzado en Aceite Ahumado · Atún en Aceite Bumble Bee · Gcerezas Maraschino 170 g |
| Lácteos y huevos | 229 | 41 | Cuajada 1/2 lb · Leche Entera Leyde, 473 ml · Malteada de Fresa Leyde 473 ml |
| Carnes y embutidos | 179 | 28 | Jamón Bavaria, Toledo · Pollo Teritaki Pr · Chorizo Cervecero, el ´porteño |
| Panadería y repostería | 148 | 28 | Pan Toñita Bolsa · Pan Chapin Batida de Azúcar · Semita Mazorca |
| Bebidas | 677 | 88 | Coca Morada · Big Cola 250 ml Narnjan · Jugo Enjoy Manzana Pera |
| Licores y cigarrillos | 158 | 53 | Encendedor Multiusoso · Encendedor Grande Clipper · Elado Tipo Casero Ron con Pasa 70 g |
| Snacks y dulces | 1271 | 134 | Ranchita Plateado 70 g · Panditas Acidas, 70 g · Galleta Black Out |
| Limpieza del hogar | 792 | 101 | Papel Scott 12 Rollos Esencial Deluxe · Suavitel Completo 80 ml Primaver · Desengrasante Multiuso 1-GLN Abro PD-620 |
| Cuidado personal y belleza | 1883 | 171 | Juego de Colas 20 piezas · Sedal Shampoo, Rizos Definidos, 190 ml · Calceta de Algodón |
| Bebés | 307 | 28 | Mameluco para Bebe · Pepe Smile · Pañal Adulto Plenitud Clas Talla M 1X8 |
| Farmacia y botiquín | 232 | 61 | Jarabe Sudagrip · Alcohol Clinico Droop Azul 250 ml · Condon Durex Real Feel |
| Mascotas | 230 | 37 | Correa Perro Grande · Shampoo Perro Blanco · Jugue Zapato Ruido para Perro |
| Hogar y cocina | 1274 | 133 | Super Bolsas 10 unidades · Sacacorcho Metal · Bolsa Basura Hercules |
| Ferretería y herramientas | 2402 | 217 | Tee PVC S40 1.1/2 X 1/2 · Tape Eléctrico PVC Abro · Baigon Aerosol Hogar Total, 400 ml |
| Electricidad e iluminación | 446 | 65 | Fusible Vidrio Tubito 10 A 6.3X32MM YC212 · Ext Elec 50"C/PORTAFOCO WD-4 · Tomacorriente Estufa Sup 50 A Aguil 112 |
| Celulares y electrónica | 297 | 65 | Moiuse Unno USB · Calculadora Casio, 12DIGITS, DX-12B · Reloj Somostel H8ULTR Max |
| Bicicletas, motos y autos | 478 | 76 | Bujia Cobre Champion · Calcamonia Bici V/Estilos · Neumatico Bici 24X2.125 de V Kenda Bolsa" |
| Papelería y útiles | 1166 | 104 | Sobre Manila 10X13 · Marcador Sharpie Negro Tank · Bolígrafo Pentel, 1.0 mm, Violeta |
| Fiestas y regalos | 941 | 69 | Banner Feliz Cumpleaños, Princesa · Globo Purpura #7 · Campanita 6 Velas |
| Navidad | 251 | 20 | Adorno Navideño Oso Polar Colg 2 piezas 15X8CM Blanco · Chongo Navideño Rojo · Glade Destellas de Navidad, 275 ml |
| Juguetes | 520 | 58 | Juguete Pista para Parqueo JQ97074/P4188 · Peluche Miky Mau · Juguete de Carros |
| Ropa, calzado y accesorios | 432 | 64 | Chancleta Croc · Calcetas Tiro Bajo para a · Chancletas Mujer 30-41 |
| Otros | 2706 | 0 | Pastel Preferida · Kit Viaje V.v Love Mellow Grove · Colorante Rojo para Piso |


El orden de la tabla es el orden en que se muestran en el catálogo (`position`). **Otros** siempre va al final.

## 3. Cómo se asigna la categoría

Las reglas están en [`backend/Data/Seed/categories.json`](../backend/Data/Seed/categories.json). Cada regla es una palabra clave con una prioridad.

1. **Normalizar** la descripción: mayúsculas, sin acentos (`Ñ` → `N`).
2. **Separar en palabras** por cualquier carácter que no sea letra o número (`P/PERRO` → `P`, `PERRO`).
3. **Coincidencia:** una palabra clave coincide si aparece como palabras completas y seguidas. Cada palabra puede venir en plural con `S` o `ES` (`GLOBO` coincide con `GLOBOS`; `LLAVE DE JARDIN` con `LLAVES DE JARDIN`). `PAN` **no** coincide con `PANAL`.
4. **Desempate** si coinciden varias reglas:
   1. mayor prioridad;
   2. palabra clave más larga (sin espacios);
   3. categoría con menor `position`.
5. **Sin coincidencia:** categoría `otros`.

Guía de prioridades:

| Prioridad | Uso | Ejemplo |
|---|---|---|
| 10–15 | Palabras genéricas o ambiguas | `TE`, `PASTA`, `ENERGY` |
| 20–25 | Tipo de producto | `LECHE`, `GALLETA`, `DETERGENTE` |
| 30 | Marcas y productos muy específicos | `SUAVITEL`, `TINTE` |
| 45–50 | Frases que corrigen un conflicto | `ACEITE MOTOR`, `LLAVE DE JARDIN`, `CAMBIADOR DE PANAL` |

Una palabra clave pertenece a **una sola** categoría.

### Decisiones tomadas con el inventario real

- `CAMBIADOR` → bicicletas (en la tienda son cambiadores de velocidad). `CAMBIADOR DE PANAL` → bebés.
- `PEPE` → bebés (pacha). `COLAS` → cuidado personal (para el cabello). `BOMBA` → fiestas (globos).
- `ENCENDEDOR` → licores y cigarrillos.
- `ACEITE` → abarrotes, pero `ACEITE MOT` / `ACEITE MOTOR` → vehículos.
- `SUEROX` → farmacia.
- Los sabores (`FRESA`, `MANGO`, `LIMON`) y colores **no** son reglas: aparecen en productos de cualquier categoría.
- `CHICHARRON` queda en carnes; los chicharrones de bolsa con marca de snack caen en snacks por su marca.

## 4. Nombre para mostrar

El diccionario está en [`backend/Data/Seed/display-dictionary.json`](../backend/Data/Seed/display-dictionary.json).

| Descripción en Aspel | Nombre en el catálogo |
|---|---|
| `LECHE ENT 1LT` | Leche Entera 1 L |
| `LECHE ENTERA LEYDE , 473ML` | Leche Entera Leyde, 473 ml |
| `PINTURA COLORAMICA ACEITE 1/4_GLN AMA TR` | Pintura Coloramica Aceite 1/4 gal Amarilla Tr |
| `CEPILLO DE DIENTE NIÑO` | Cepillo de Diente Niño |
| `BROCA HIERRO 5/32" COBAL DEWALT DW1210` | Broca Hierro 5/32" Cobal Dewalt DW1210 |
| `MINI COCA/BANANA/UVA/SPRITE` | Mini Coca/Banana/Uva/Sprite |

Pasos:

1. Limpiar: `_` → espacio; quitar `V/C` y `V/D`; `S/R` → sin rosca; `P/`, `C/`, `S/`, `D/` al inicio de palabra → para, con, sin, de (`C/U` se respeta).
2. Separar número y unidad (`1LT` → `1 L`, `1/2LIBRA` → `1/2 lb`, `6PC` → `6 piezas`).
3. Por palabra, en este orden:
   1. abreviatura (`abbreviations`): si tiene dos formas, se elige masculino o femenino según la primera palabra (`VELA PEQ` → Vela Pequeña);
   2. caso especial (`special`: `IPHONE` → iPhone);
   3. sigla o palabra con números → tal cual (`PVC`, `DW1210`);
   4. palabra con acento conocido (`accents`: `AZUCAR` → Azúcar);
   5. resto → tipo título.
4. Artículos y preposiciones (`lowerWords`) en minúscula salvo al inicio.

Femenino: la primera palabra termina en `A` (salvo `feminineExceptions`: DIA, MAPA…) o está en `feminineWords` (LECHE, LLAVE…).

**Límites conocidos:** los errores de escritura de Aspel se conservan (`Moliddo`, `Proctertor`); solo se corrigen si se agregan al diccionario. No se agregaron abreviaturas ambiguas como `ELEC` (eléctrico / eléctrica) ni `COLG` (colgante / Colgate).

## 5. Cómo modificar las reglas

1. Editar `categories.json` o `display-dictionary.json` en una rama `chore/<issue>-...`.
2. Probar contra un inventario reciente:

   ```bash
   cd tools/catalog-analysis
   python analyze.py ruta/al/inventario.csv
   ```

   Muestra la cobertura por categoría y las palabras más comunes en Otros, y genera `resultados/productos-categorizados.csv`.
3. Abrir el PR con el antes y el después de la cobertura.
4. Después del despliegue, ejecutar la recategorización (business-rules.md, sección 4).

El backend en C# implementa el mismo algoritmo (US-06). [`tools/catalog-analysis/catalog.py`](../tools/catalog-analysis/catalog.py) es la implementación de referencia: si los resultados difieren, el comportamiento correcto es el de `catalog.py` hasta que el equipo decida otra cosa.

## 6. Pendientes

- El negocio valida la lista de categorías y revisa `01-productos-con-categoria.csv`.
- Bajar los 2 706 productos de Otros con nuevas reglas a partir de las palabras que reporta `analyze.py`.
- Probar en C# que la categorización da el mismo resultado que `catalog.py` con el mismo inventario.
