# Categorías y nombres del catálogo

**Catálogo Web – Mini Super Nettes**  
Complementa la sección 4 y 5 de [business-rules.md](business-rules.md). Resultado del análisis del inventario de Aspel SAE exportado en noviembre de 2026 (18 460 artículos).

> **Estado:** propuesta del equipo, **pendiente de validación del negocio**. El negocio revisa `01-productos-con-categoria.csv` y marca los productos mal ubicados.

## 1. Resumen

| Dato | Valor |
|---|---|
| Artículos en `INVE01` | 18 460 |
| Visibles en el catálogo (activos, con precio válido y con descripción) | 18 271 |
| Con categoría asignada por regla | 18 247 (99.9 %) |
| En **Otros** | 24 (0.1 %) |
| Precisión estimada en muestras revisadas a mano | ~90–95 % (reglas originales); ~97 % en los productos que se sacaron de Otros |
| Visibles agotados (`EXIST <= 0`) | 1 610 |

La precisión se midió revisando muestras al azar de cada categoría. Los errores que quedan son casi siempre palabras con dos sentidos (`PALETA` dulce o de broca, `AEROSOL` insecticida o pintura), marcas desconocidas y descripciones mal escritas en Aspel (`ELADO`, `GALLLETA`).

## 2. Categorías propuestas

| Categoría | Productos | Reglas | Ejemplos (nombre para mostrar) |
|---|---:|---:|---|
| Abarrotes | 1399 | 277 | Sopa Menudo Maggi 50 g · Fetuccinne Carozi · Sopa Pollo Fideos Maggi 50 g |
| Frutas y verduras | 78 | 74 | Guayaba Unidad · Malanga · Culantro ANCHO1/2MAZO |
| Lácteos y huevos | 233 | 49 | Sulita Yogurt Fresa 100 ml · Leche Semidescremada Deslactosada Sula · Crujitos Queso y Chile |
| Carnes y embutidos | 212 | 49 | Milanesa Pollo Rey · Rica Sula Yuca con Chicharrón 16 g · Costilla Res Pesada |
| Panadería y repostería | 189 | 57 | Pan Chapin Guayaba · Torta Piña Hawit Unidad · Rosquillas Mixta |
| Bebidas | 819 | 176 | Alcien Maracuyá 330 ml · Crush Pineaplple · Cutter 170 g Aloe |
| Licores y cigarrillos | 273 | 104 | Smirnoff Limón · Malboro Gold · Vusego Watermelon |
| Snacks y dulces | 1631 | 365 | Elotito Chiile Diana · Chokis Chocobase (roja) · Zambos Picositos Limón y Sal |
| Limpieza del hogar | 924 | 194 | Ensueño Turquesa 740 ml · Escobita Bimba Flamingo · PACK4 Barra Mr.maxx Podersuavizante |
| Cuidado personal y belleza | 2151 | 335 | Desodorante Piedra Alumbre 70 g · Listerine Freshburst 500 ml · Terso Finos Detalles 850 ml |
| Bebés | 327 | 46 | Contendor para Leche de Bebe · Espamo Tropin Bebe Gotos 30 ml · Huggies Clasico Jumbotalla Xxg 1X50 |
| Farmacia y botiquín | 420 | 182 | Agua Oxigenada Rk 3%, 120 ml · Vitaflenaco -farmamedica · Atribion Unidad |
| Mascotas | 263 | 55 | Dogui Vegetal · Collar para Perros/Gatos · Cadena para Perro |
| Hogar y cocina | 1533 | 271 | Sarten con Espatula 20 cm Paris · Platos Foam #6 Pequeño · Espatulas Cocina |
| Ferretería y herramientas | 2591 | 368 | Pintura Aceite Amarillo Brillante/Gln · Masking 18MMX 10M 4 piezas Multi Uso Abro · Thinner Laca 100 Corriente 1/4-GAL |
| Electricidad e iluminación | 483 | 94 | Foco Mano 6LED Recarg Vikingo Vk 6LED · Tapadera de Interruptor Eagle · Conector 3/4 |
| Celulares y electrónica | 328 | 97 | Cable USB Unno V8 · Audifonos Wireles SMS-J22 · Cargador para Phone |
| Bicicletas, motos y autos | 559 | 113 | Pedal Bici Plástico 9/16"C/R Sp 300 · Aceite Atf Dexron & Mercon 1QT Abro · Manecilla Bici PLAST/12 20"HS3 |
| Papelería y útiles | 1374 | 243 | Marcador Morado Pentel · Folder Plástico Espandible · Papel Canson Iris Piel |
| Fiestas y regalos | 1004 | 122 | Bolsas Metalicas Cumpleaños · Globo Plateado M40 · Mantel Rayados Plateado |
| Navidad | 266 | 31 | Bolas Navideñas Escarchadas Rosadas 12 unidades · Pinguinos Edicion Navideña 80 g · Adorno Navideño Vincha/Reno 12*25 Cmrojo |
| Juguetes | 621 | 143 | Naipes Plasticos · Pelota de Cuero · Juguete Juego Pesca 5 piezas 42" 1-18717-668-5 |
| Ropa, calzado y accesorios | 490 | 102 | Calcetas Sport Largas 3 piezas · Diamond Betun Past NO.4 Begro 70 g · Calcetas Unidades Par Azul |
| Deportes y piscina | 54 | 38 | NO:6001(SOPORTE Pantorrilla) · Hula Hoo Mediano · Rodillera Alfamedic |
| Servicios | 25 | 13 | Paquetigo Voz 35MIN · Facebook Ilimitado · Copias Legal Color Pagina Completa |
| Otros | 24 | 0 | L · Himan · Yi Xin Yan |


El orden de la tabla es el orden en que se muestran en el catálogo (`position`). **Otros** siempre va al final.

## 3. Cómo se asigna la categoría

Las reglas están en [`backend/src/backend/Data/Seed/categories.json`](../backend/src/backend/Data/Seed/categories.json). Cada regla es una palabra clave con una prioridad.

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
| 8 | Palabras sueltas que también son sabores o ingredientes | `CEBOLLA`, `MELON`, `SANDWICH` |
| 10–15 | Palabras genéricas o ambiguas | `TE`, `PASTA`, `ENERGY` |
| 16 | Marcas y productos agregados al vaciar Otros: solo ganan si nada más coincide | `RAPTOR`, `GLASS`, `CORREA` |
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
- **Frutas y verduras**, **Deportes y piscina** y **Servicios** se agregaron después de revisar Otros con el equipo.
- Las palabras sueltas de verduras (`CEBOLLA`, `AGUACATE`, `PEPINO`) tienen prioridad 8, como los sabores: "Pringles crema y cebolla" sigue en snacks y "shampoo de aguacate" en cuidado personal.
- `RECARGAS NORMALES` (recargas telefónicas) → Servicios. `COPIAS` (fotocopias) → Servicios.
- `PLATA`, `XL` (Botrán XL) y `ULTRA` (Michelob Ultra) → licores. `ROLLING` (papel para fumar) → licores y cigarrillos.
- `CHINOLA` (betún) → ropa y calzado. `BAÑO` (tinas para bebé) → bebés. `PINITO` (aromatizante de carro) → vehículos.
- Las reglas agregadas para vaciar Otros tienen prioridad 16: solo toman productos que ninguna otra regla reconoce.
- Respuestas del equipo sobre productos desconocidos: `GLASS` y los colores `GRNADE` son vasos de vidrio (hogar), `FURY` es energizante, `PALILLONA` es el bastón de desfiles (fiestas), `GOTAS MAGICAS`, `COSTREX`, `SANADORA` y `FROTAL` son de farmacia, `CORREA` son correas para perro, `CHEMICOL` es tinte para cuero (calzado), `DENTAL` es pasta o enjuague y `DOROSA` es removedor de esmalte (cuidado personal). `CABALLO ROJO` son fósforos, y los fósforos pasaron de limpieza a hogar y cocina. `GUACHAS` son arandelas, `HULE DE PEPITO` es el chupón del biberón, `NUMERA` es vela de número para pastel y `DINA PACK` son platos desechables.
- Varias descripciones de Aspel tienen errores que cambian el producto: `HONGO GRANDE ROJO` es un chongo (moño) y `DUREZ` es Durex.
- `JUEGO`, `FRITO`, `ESTRELLA`, `MELON` y otras palabras que aparecen en muchos productos tienen prioridad 8: solo deciden cuando no hay ninguna otra coincidencia.
- Después de cada cambio se compara la categoría de todos los productos contra la versión anterior para detectar reglas que le quitan productos a otra categoría.
- Los sabores (`FRESA`, `MANGO`, `LIMON`) y colores **no** son reglas: aparecen en productos de cualquier categoría.
- `CHICHARRON` queda en carnes; los chicharrones de bolsa con marca de snack caen en snacks por su marca.

## 4. Nombre para mostrar

El diccionario está en [`backend/src/backend/Data/Seed/display-dictionary.json`](../backend/src/backend/Data/Seed/display-dictionary.json).

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
- Los 24 productos que siguen en Otros tienen descripciones que nadie del equipo reconoce (`L`, `ÑÑÑ`, `FHJJGGJGG`, `CAYAMO`). Conviene que el negocio corrija esas descripciones en Aspel con nuevas reglas a partir de las palabras que reporta `analyze.py`.
- Probar en C# que la categorización da el mismo resultado que `catalog.py` con el mismo inventario.
