/* =============================================================================
   SOLO PARA DESARROLLO: comprueba que la cuenta catalogo_lectura NO puede modificar datos.
   Se ejecuta CONECTADO COMO catalogo_lectura (no como sa), sobre la base de MENTIRA:

       sqlcmd -C -S localhost,14330 -U catalogo_lectura -P <clave> -d SAE70Empre01 -b -i 03-verificar-solo-lectura.sql

   NUNCA se ejecuta contra la base real de la tienda (alli se usa install/04, que no escribe nada).

   Intenta INSERT, UPDATE y DELETE sobre las dos tablas. Cada uno debe fallar con el error 229
   (permiso denegado). Si alguno funciona, el script termina con error y, ademas, la prueba falla.
   ============================================================================= */
SET NOCOUNT ON;

DECLARE @fallos int = 0;
DECLARE @n int;

-- 1) Lo que SI debe poder hacer: leer.
SELECT @n = COUNT(*) FROM dbo.INVE01;
IF @n = 0 THROW 50001, 'catalogo_lectura no puede leer dbo.INVE01 (o la tabla esta vacia).', 1;
SELECT @n = COUNT(*) FROM dbo.PRECIO_X_PROD01;
PRINT 'OK  SELECT en las dos tablas.';

-- 2) Minimo privilegio: no debe pertenecer a ningun rol de la base.
IF IS_ROLEMEMBER('db_datareader') = 1 OR IS_ROLEMEMBER('db_datawriter') = 1
   OR IS_ROLEMEMBER('db_ddladmin') = 1 OR IS_ROLEMEMBER('db_owner') = 1
BEGIN
    PRINT 'ERROR  catalogo_lectura pertenece a un rol fijo de la base (deberia tener solo SELECT sobre dos tablas).';
    SET @fallos += 1;
END
ELSE PRINT 'OK  No pertenece a db_datareader, db_datawriter, db_ddladmin ni db_owner.';

-- 3) Lo que NO debe poder hacer: escribir. Cada intento debe fallar con 229.
BEGIN TRY
    INSERT INTO dbo.INVE01 (CVE_ART, DESCR, EXIST, STATUS) VALUES ('ZZ-NO-DEBE-EXISTIR', 'X', 0, 'A');
    PRINT 'ERROR  INSERT en INVE01 NO fue bloqueado.'; SET @fallos += 1;
END TRY
BEGIN CATCH
    IF ERROR_NUMBER() <> 229 THROW;
    PRINT 'OK  INSERT en INVE01 bloqueado (error 229).';
END CATCH

BEGIN TRY
    UPDATE dbo.INVE01 SET DESCR = 'MODIFICADO';
    PRINT 'ERROR  UPDATE en INVE01 NO fue bloqueado.'; SET @fallos += 1;
END TRY
BEGIN CATCH
    IF ERROR_NUMBER() <> 229 THROW;
    PRINT 'OK  UPDATE en INVE01 bloqueado (error 229).';
END CATCH

BEGIN TRY
    DELETE FROM dbo.INVE01;
    PRINT 'ERROR  DELETE en INVE01 NO fue bloqueado.'; SET @fallos += 1;
END TRY
BEGIN CATCH
    IF ERROR_NUMBER() <> 229 THROW;
    PRINT 'OK  DELETE en INVE01 bloqueado (error 229).';
END CATCH

BEGIN TRY
    INSERT INTO dbo.PRECIO_X_PROD01 (CVE_ART, CVE_PRECIO, PRECIO) VALUES ('ZZ-NO-DEBE-EXISTIR', 1, 1);
    PRINT 'ERROR  INSERT en PRECIO_X_PROD01 NO fue bloqueado.'; SET @fallos += 1;
END TRY
BEGIN CATCH
    IF ERROR_NUMBER() <> 229 THROW;
    PRINT 'OK  INSERT en PRECIO_X_PROD01 bloqueado (error 229).';
END CATCH

BEGIN TRY
    UPDATE dbo.PRECIO_X_PROD01 SET PRECIO = 0;
    PRINT 'ERROR  UPDATE en PRECIO_X_PROD01 NO fue bloqueado.'; SET @fallos += 1;
END TRY
BEGIN CATCH
    IF ERROR_NUMBER() <> 229 THROW;
    PRINT 'OK  UPDATE en PRECIO_X_PROD01 bloqueado (error 229).';
END CATCH

BEGIN TRY
    DELETE FROM dbo.PRECIO_X_PROD01;
    PRINT 'ERROR  DELETE en PRECIO_X_PROD01 NO fue bloqueado.'; SET @fallos += 1;
END TRY
BEGIN CATCH
    IF ERROR_NUMBER() <> 229 THROW;
    PRINT 'OK  DELETE en PRECIO_X_PROD01 bloqueado (error 229).';
END CATCH

IF @fallos > 0 THROW 50002, 'La cuenta catalogo_lectura tiene permisos de escritura: NO es de solo lectura.', 1;
PRINT 'RESULTADO: catalogo_lectura es de solo lectura.';
GO
