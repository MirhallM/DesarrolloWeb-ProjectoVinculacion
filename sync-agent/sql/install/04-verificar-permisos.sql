/* =============================================================================
   DIA DE LA INSTALACION: confirma que catalogo_lectura es de SOLO LECTURA, sin escribir nada.
   Se ejecuta CONECTADO COMO catalogo_lectura (no como administrador):

       sqlcmd -S localhost -U catalogo_lectura -P <clave> -d SAE70Empre01 -b -i 04-verificar-permisos.sql

   A diferencia de sql/dev/03, este script NO intenta INSERT, UPDATE ni DELETE: en la base real de Aspel no se
   prueba escribiendo. Solo PREGUNTA a SQL Server que permisos tiene la cuenta (HAS_PERMS_BY_NAME).
   No crea ni modifica nada en la base de Aspel (usa una variable de tabla en memoria).

   Si termina con error, la cuenta tiene mas permisos de los que debe: corregir con 01-crear-login-lectura.sql.
   ============================================================================= */
SET NOCOUNT ON;

DECLARE @problemas int = 0;

PRINT '=== Cuenta conectada ===';
SELECT SUSER_SNAME() AS login, USER_NAME() AS usuario_en_la_base, DB_NAME() AS base;

PRINT '';
PRINT '=== Permisos efectivos sobre cada tabla ===';
-- Variable de tabla: vive solo en memoria durante este script; no crea nada en la base de Aspel.
DECLARE @permisos TABLE (tabla sysname, permiso varchar(10), concedido bit, esperado bit);

INSERT INTO @permisos (tabla, permiso, concedido, esperado)
SELECT  t.tabla, p.permiso,
        CAST(ISNULL(HAS_PERMS_BY_NAME(t.tabla, 'OBJECT', p.permiso), 0) AS bit),
        CASE WHEN p.permiso = 'SELECT' THEN 1 ELSE 0 END
FROM   (VALUES ('dbo.INVE01'), ('dbo.PRECIO_X_PROD01')) AS t(tabla)
CROSS JOIN (VALUES ('SELECT'), ('INSERT'), ('UPDATE'), ('DELETE'), ('ALTER'), ('CONTROL')) AS p(permiso);

SELECT  tabla, permiso,
        CASE WHEN concedido = 1 THEN 'SI' ELSE 'no' END AS tiene_el_permiso,
        CASE WHEN concedido = esperado THEN 'OK' ELSE '<<< NO DEBERIA' END AS resultado
FROM    @permisos
ORDER BY tabla, CASE permiso WHEN 'SELECT' THEN 0 ELSE 1 END, permiso;

SELECT @problemas = COUNT(*) FROM @permisos WHERE concedido <> esperado;

PRINT '';
PRINT '=== Pertenencia a roles ===';
SELECT  IS_ROLEMEMBER('db_datareader') AS db_datareader,
        IS_ROLEMEMBER('db_datawriter') AS db_datawriter,
        IS_ROLEMEMBER('db_ddladmin')   AS db_ddladmin,
        IS_ROLEMEMBER('db_owner')      AS db_owner,
        IS_SRVROLEMEMBER('sysadmin')   AS sysadmin;

IF ISNULL(IS_ROLEMEMBER('db_datareader'), 0) = 1 OR ISNULL(IS_ROLEMEMBER('db_datawriter'), 0) = 1
   OR ISNULL(IS_ROLEMEMBER('db_ddladmin'), 0) = 1 OR ISNULL(IS_ROLEMEMBER('db_owner'), 0) = 1
   OR ISNULL(IS_SRVROLEMEMBER('sysadmin'), 0) = 1
BEGIN
    PRINT 'PROBLEMA: la cuenta pertenece a un rol que le da mas permisos de los necesarios.';
    SET @problemas += 1;
END

PRINT '';
IF @problemas > 0
    THROW 50003, 'catalogo_lectura tiene permisos de mas: NO es de solo lectura. Ejecutar sql/install/01-crear-login-lectura.sql.', 1;

PRINT 'RESULTADO: catalogo_lectura solo tiene SELECT sobre dbo.INVE01 y dbo.PRECIO_X_PROD01.';
GO
