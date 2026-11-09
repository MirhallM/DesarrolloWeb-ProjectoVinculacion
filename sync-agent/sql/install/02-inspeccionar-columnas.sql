/* =============================================================================
   DIA DE LA INSTALACION: confirma los tipos y datos REALES de las dos tablas de Aspel que lee el agente.
   SOLO LECTURA: este script solo hace SELECT. No modifica nada.

   Sirve para confirmar las suposiciones con las que se desarrollo el agente:
     - CVE_ART: varchar o char? de que largo? (si fuera char, vendria con espacios de relleno al final)
     - DESCR: varchar(40)?
     - EXIST y PRECIO: float? (puede haber decimales de mas, p. ej. 34.999999)
     - La base usa aislamiento READ COMMITTED normal o SNAPSHOT? (afecta a los bloqueos; ver mas abajo)

   COMO EJECUTARLO (como administrador para ver todo, o como catalogo_lectura para ver lo que ella ve):
       sqlcmd -S localhost -E -b -i 02-inspeccionar-columnas.sql
   o, con la cuenta de solo lectura:
       sqlcmd -S localhost -U catalogo_lectura -P <clave> -b -i 02-inspeccionar-columnas.sql
   Guarda la salida y envia el resultado al equipo.

   LAS CIFRAS SON APROXIMADAS frente a la exportacion de noviembre de 2026 (18 460 articulos, 121 activos
   sin precio, 14 pares de codigos con espacios): el inventario cambia. Lo importante es la forma de los datos.
   ============================================================================= */
SET NOCOUNT ON;
USE [SAE70Empre01];
GO

PRINT '=== 1. Servidor y base ===';
SELECT  @@SERVERNAME                               AS servidor,
        CAST(SERVERPROPERTY('ProductVersion') AS nvarchar(40)) AS version_sql,
        CAST(SERVERPROPERTY('Edition')        AS nvarchar(100)) AS edicion,
        DB_NAME()                                  AS base,
        CAST(DATABASEPROPERTYEX(DB_NAME(), 'Collation') AS nvarchar(100)) AS colacion_de_la_base;
GO

PRINT '=== 2. Aislamiento de la base (READ COMMITTED normal = 0 y OFF) ===';
-- is_read_committed_snapshot_on = 1 significa que las lecturas NO esperan a las escrituras (lo ideal para no estorbar a Aspel).
-- Con 0 (lo habitual), el SELECT del agente toma bloqueos compartidos muy breves; con 18 mil filas dura milisegundos.
-- NO se cambia nada aqui: es solo informacion.
SELECT  name,
        is_read_committed_snapshot_on,
        snapshot_isolation_state_desc,
        state_desc,
        recovery_model_desc
FROM    sys.databases
WHERE   name = DB_NAME();
GO

PRINT '=== 3. Columnas reales (INFORMATION_SCHEMA.COLUMNS) ===';
SELECT  TABLE_NAME            AS tabla,
        COLUMN_NAME           AS columna,
        DATA_TYPE             AS tipo,
        CHARACTER_MAXIMUM_LENGTH AS largo,
        NUMERIC_PRECISION     AS precision_num,
        IS_NULLABLE           AS acepta_nulos,
        COLLATION_NAME        AS colacion
FROM    INFORMATION_SCHEMA.COLUMNS
WHERE   TABLE_SCHEMA = 'dbo'
  AND   TABLE_NAME IN ('INVE01', 'PRECIO_X_PROD01')
  AND   COLUMN_NAME IN ('CVE_ART', 'DESCR', 'EXIST', 'STATUS', 'CVE_PRECIO', 'PRECIO')
ORDER BY TABLE_NAME, ORDINAL_POSITION;
GO

PRINT '=== 4. Conteos ===';
SELECT  (SELECT COUNT(*) FROM dbo.INVE01)                                  AS articulos,
        (SELECT COUNT(*) FROM dbo.INVE01 WHERE STATUS = 'A')               AS activos,
        (SELECT COUNT(*) FROM dbo.INVE01 WHERE STATUS = 'B')               AS de_baja,
        (SELECT COUNT(*) FROM dbo.INVE01 WHERE STATUS IS NULL OR STATUS NOT IN ('A','B')) AS otro_estado,
        (SELECT COUNT(*) FROM dbo.PRECIO_X_PROD01)                         AS filas_de_precio,
        (SELECT COUNT(DISTINCT CVE_PRECIO) FROM dbo.PRECIO_X_PROD01)       AS listas_de_precios;
GO

PRINT '=== 5. La consulta del agente: filas y activos sin precio en la lista 1 ===';
-- El total debe ser IGUAL al numero de articulos: si es mayor, el JOIN duplica articulos
-- (habria varias filas de precio para la misma lista) y hay que revisarlo antes de seguir.
SELECT  COUNT(*)                                                       AS filas_de_la_consulta,
        (SELECT COUNT(*) FROM dbo.INVE01)                              AS articulos,
        SUM(CASE WHEN i.STATUS = 'A' AND p.PRECIO IS NULL THEN 1 ELSE 0 END) AS activos_sin_precio_en_lista_1,
        SUM(CASE WHEN i.STATUS = 'A' AND p.PRECIO = 0     THEN 1 ELSE 0 END) AS activos_con_precio_cero
FROM    dbo.INVE01 AS i
LEFT JOIN dbo.PRECIO_X_PROD01 AS p
        ON p.CVE_ART = i.CVE_ART AND p.CVE_PRECIO = 1;
GO

PRINT '=== 6. Forma de CVE_ART (espacios) ===';
-- DATALENGTH cuenta los espacios finales; LEN no. Si "con_espacios_al_final" es alto o igual al total,
-- la columna es CHAR (con relleno): el agente los entrega tal cual y la API decide como tratarlos.
SELECT  COUNT(*)                                                         AS articulos,
        SUM(CASE WHEN CVE_ART LIKE ' %'                    THEN 1 ELSE 0 END) AS con_espacios_al_inicio,
        SUM(CASE WHEN DATALENGTH(CVE_ART) > LEN(CVE_ART)
                  AND LEN(CVE_ART) > 0                     THEN 1 ELSE 0 END) AS con_espacios_al_final,
        MAX(DATALENGTH(CVE_ART))                                          AS largo_maximo_en_bytes,
        MIN(DATALENGTH(CVE_ART))                                          AS largo_minimo_en_bytes
FROM    dbo.INVE01;
GO

PRINT '=== 7. Codigos que solo difieren por espacios al inicio (se entregan como productos distintos) ===';
SELECT  COUNT(*) AS grupos_de_codigos_que_chocan_al_recortar
FROM (  SELECT LTRIM(RTRIM(CVE_ART)) AS codigo_recortado
        FROM   dbo.INVE01
        GROUP BY LTRIM(RTRIM(CVE_ART))
        HAVING COUNT(*) > 1 ) AS g;
GO

PRINT '=== 8. Descripciones ===';
SELECT  SUM(CASE WHEN DESCR IS NULL                            THEN 1 ELSE 0 END) AS nulas,
        SUM(CASE WHEN LTRIM(RTRIM(DESCR)) = ''                 THEN 1 ELSE 0 END) AS vacias,
        SUM(CASE WHEN LTRIM(RTRIM(DESCR)) = 'NULL'             THEN 1 ELSE 0 END) AS con_el_texto_NULL,
        MAX(LEN(DESCR))                                                          AS largo_maximo
FROM    dbo.INVE01;
GO

PRINT '=== 9. Existencias y precios fuera de lo normal ===';
SELECT  SUM(CASE WHEN EXIST < 0                         THEN 1 ELSE 0 END) AS existencia_negativa,
        SUM(CASE WHEN EXIST <> FLOOR(EXIST)             THEN 1 ELSE 0 END) AS existencia_con_decimales,
        SUM(CASE WHEN EXIST >= 1000000                  THEN 1 ELSE 0 END) AS existencia_de_un_millon_o_mas
FROM    dbo.INVE01;
SELECT  SUM(CASE WHEN PRECIO >= 100000                  THEN 1 ELSE 0 END) AS precios_de_100000_o_mas,
        SUM(CASE WHEN PRECIO <> ROUND(PRECIO, 2)        THEN 1 ELSE 0 END) AS precios_con_mas_de_2_decimales
FROM    dbo.PRECIO_X_PROD01
WHERE   CVE_PRECIO = 1;
GO

PRINT '=== 10. Duplicados de precio por articulo y lista (debe dar 0 filas) ===';
SELECT TOP (20) CVE_ART, CVE_PRECIO, COUNT(*) AS veces
FROM    dbo.PRECIO_X_PROD01
GROUP BY CVE_ART, CVE_PRECIO
HAVING  COUNT(*) > 1;
GO

PRINT '=== 11. Indices de las dos tablas (el JOIN va por CVE_ART + CVE_PRECIO) ===';
-- Requiere permiso de ver definiciones: con catalogo_lectura puede salir vacio; ejecutado como administrador sale completo.
SELECT  OBJECT_NAME(i.object_id)   AS tabla,
        i.name                     AS indice,
        i.type_desc                AS tipo,
        i.is_unique                AS unico,
        STUFF((SELECT ', ' + c.name
               FROM sys.index_columns AS ic
               JOIN sys.columns AS c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
               WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.is_included_column = 0
               ORDER BY ic.key_ordinal
               FOR XML PATH('')), 1, 2, '') AS columnas
FROM    sys.indexes AS i
WHERE   i.object_id IN (OBJECT_ID('dbo.INVE01'), OBJECT_ID('dbo.PRECIO_X_PROD01'))
  AND   i.type > 0
ORDER BY tabla, indice;
GO

PRINT '=== Fin. Solo se hicieron consultas de lectura. ===';
GO
