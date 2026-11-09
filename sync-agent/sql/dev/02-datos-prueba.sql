/* =============================================================================
   SOLO PARA DESARROLLO: datos de prueba para la base SAE70Empre01 de mentira.
   Se ejecuta como administrador (sa) despues de 01-crear-base.sql.

   Cada fila representa un caso real del inventario de la tienda (docs/business-rules.md).
   RESULTADO ESPERADO de `SyncAgent --test` con estos datos (las pruebas de integracion lo comprueban):

     Filas leidas ........................... 14      (una por articulo: el LEFT JOIN no duplica)
     Codigos distintos ...................... 14
     Activos / de baja / otro estado ........ 13 / 1 / 0
     Activos sin precio en la lista 1 ....... 2       (articulos 3 y 14)
     Activos con precio 0 ................... 1       (articulo 13)
     Descripcion nula / vacia / "NULL" ...... 1 / 1 / 1
     Activos sin descripcion utilizable ..... 3
     Codigos con espacios al inicio ......... 1       (articulo 6)
     Grupos que solo difieren por espacios .. 1       (articulos 5 y 6)
     Existencias negativas / con decimales .. 1 / 1
     Existencias >= 10 000 / >= 1 000 000 ... 1 / 1
     Longitud maxima de CVE_ART ............. 15      ("  2050000117121" = 2 espacios + 13 digitos)
   ============================================================================= */
SET NOCOUNT ON;
USE [SAE70Empre01];
GO

-- (CVE_ART, DESCR, EXIST, STATUS)
INSERT INTO dbo.INVE01 (CVE_ART, DESCR, EXIST, STATUS) VALUES
 ('7501000100011', 'LECHE ENT 1LT',            12,           'A'),  --  1 producto normal (precio en listas 1 y 4)
 ('7501000100028', 'ARROZ BLANCO 1LB',         2.75,         'A'),  --  2 existencia con decimales (producto por libra)
 ('7501000100035', 'SIN PRECIO EN LISTA 1',    5,            'A'),  --  3 activo sin precio en la lista 1, pero con precio en la 4
 ('7501000100042', 'PRODUCTO DE BAJA',         0,            'B'),  --  4 de baja
 ('2050000117121', 'PAN BLANCO BOLSA',         8,            'A'),  --  5 codigo sin espacios...
 ('  2050000117121', 'PAN INTEGRAL BOLSA',     3,            'A'),  --  6 ...y el mismo con espacios al inicio: son DOS productos distintos
 ('7501000100059', '',                         4,            'A'),  --  7 descripcion vacia
 ('7501000100066', 'NULL',                     4,            'A'),  --  8 descripcion con el texto literal NULL
 ('7501000100073', NULL,                       4,            'A'),  --  9 descripcion nula (NULL de la base)
 ('7501000100080', 'EXISTENCIA NEGATIVA',     -3,            'A'),  -- 10 existencia negativa
 ('7501000100097', 'EXISTENCIA INFLADA',       500000000000, 'A'),  -- 11 existencia inflada (valor de relleno)
 ('7501000100103', 'PRECIO CON DECIMALES',    1,             'A'),  -- 12 precio con decimales de mas (34.999999)
 ('7501000100110', 'PRECIO CERO',             1,             'A'),  -- 13 precio 0
 ('7501000100127', 'SIN NINGUN PRECIO',       1,             'A');  -- 14 activo sin ninguna fila de precio
GO

-- (CVE_ART, CVE_PRECIO, PRECIO)
INSERT INTO dbo.PRECIO_X_PROD01 (CVE_ART, CVE_PRECIO, PRECIO) VALUES
 ('7501000100011', 1, 34.5),
 ('7501000100011', 4, 30),            -- otras listas: el agente solo lee la lista configurada (1)
 ('7501000100028', 1, 25),
 ('7501000100035', 4, 18),            -- 3: solo tiene precio en la lista 4 -> en la lista 1 queda NULL
 ('7501000100042', 1, 12),
 ('2050000117121', 1, 40),
 ('  2050000117121', 1, 55),
 ('7501000100059', 1, 10),
 ('7501000100066', 1, 10),
 ('7501000100073', 1, 10),
 ('7501000100080', 1, 9),
 ('7501000100097', 1, 9),
 ('7501000100103', 1, 34.999999),
 ('7501000100110', 1, 0);
 -- 14: sin filas de precio
GO

PRINT 'Datos de prueba cargados: 14 articulos.';
GO
