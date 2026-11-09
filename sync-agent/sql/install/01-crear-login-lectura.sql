/* =============================================================================
   DIA DE LA INSTALACION: crea la cuenta de solo lectura que usa el agente (catalogo_lectura).
   Es el MISMO script que usa el entorno de desarrollo, asi que ya esta probado.

   COMO EJECUTARLO (con una cuenta administradora de SQL Server, por ejemplo sa o un sysadmin):

     Opcion A - sqlcmd.exe (la clave se pasa con -v):
         sqlcmd -S localhost -E -b -v LoginPassword="LaClaveNueva" -i 01-crear-login-lectura.sql

     Opcion B - SQL Server Management Studio: menu Consulta > Modo SQLCMD (activarlo) y
         definir la clave con la orden setvar de SQLCMD justo antes de ejecutar (setvar LoginPassword "LaClaveNueva",
         escrita con dos puntos delante, al inicio de una linea propia fuera de este comentario).

   LA CLAVE:
     - Debe cumplir la directiva de Windows/SQL Server (8+ caracteres, mayuscula, minuscula, numero, simbolo).
     - NO uses comillas simples (') ni el signo $ en la clave: romperian el script.
     - Anotala en el archivo appsettings.Production.json del agente; no se guarda en ningun otro lado.

   QUE HACE (todo es repetible: si ya existe, lo actualiza en vez de fallar):
     1. Crea el login catalogo_lectura (o le cambia la clave si ya existe).
     2. Crea el usuario en la base SAE70Empre01.
     3. Le quita cualquier rol de la base (db_datareader, db_datawriter, db_ddladmin, db_owner).
     4. Le da SELECT SOLO sobre dbo.INVE01 y dbo.PRECIO_X_PROD01. Nada mas.
     5. Muestra los permisos que quedaron, para revisarlos.

   Este script NO modifica datos ni tablas de Aspel; solo agrega una cuenta con permiso de lectura.
   ============================================================================= */
SET NOCOUNT ON;
GO

USE [master];
GO

IF SUSER_ID(N'catalogo_lectura') IS NULL
BEGIN
    CREATE LOGIN [catalogo_lectura]
        WITH PASSWORD = N'$(LoginPassword)',
             DEFAULT_DATABASE = [SAE70Empre01],
             CHECK_POLICY = ON,
             CHECK_EXPIRATION = OFF;   -- una cuenta de servicio no debe caducar sin aviso y dejar de sincronizar
    PRINT 'Login catalogo_lectura creado.';
END
ELSE
BEGIN
    ALTER LOGIN [catalogo_lectura]
        WITH PASSWORD = N'$(LoginPassword)',
             DEFAULT_DATABASE = [SAE70Empre01],
             CHECK_POLICY = ON,
             CHECK_EXPIRATION = OFF;
    ALTER LOGIN [catalogo_lectura] ENABLE;
    PRINT 'Login catalogo_lectura ya existia: se actualizo su clave.';
END
GO

USE [SAE70Empre01];
GO

IF USER_ID(N'catalogo_lectura') IS NULL
BEGIN
    CREATE USER [catalogo_lectura] FOR LOGIN [catalogo_lectura];
    PRINT 'Usuario catalogo_lectura creado en SAE70Empre01.';
END
ELSE
BEGIN
    ALTER USER [catalogo_lectura] WITH LOGIN = [catalogo_lectura];   -- reasocia el usuario si quedo huerfano
    PRINT 'Usuario catalogo_lectura ya existia en SAE70Empre01.';
END
GO

-- Minimo privilegio: si la cuenta perteneciera a algun rol de la base, se la saca de ahi.
IF IS_ROLEMEMBER(N'db_datareader', N'catalogo_lectura') = 1 ALTER ROLE [db_datareader] DROP MEMBER [catalogo_lectura];
IF IS_ROLEMEMBER(N'db_datawriter', N'catalogo_lectura') = 1 ALTER ROLE [db_datawriter] DROP MEMBER [catalogo_lectura];
IF IS_ROLEMEMBER(N'db_ddladmin',   N'catalogo_lectura') = 1 ALTER ROLE [db_ddladmin]   DROP MEMBER [catalogo_lectura];
IF IS_ROLEMEMBER(N'db_owner',      N'catalogo_lectura') = 1 ALTER ROLE [db_owner]      DROP MEMBER [catalogo_lectura];
GO

GRANT SELECT ON OBJECT::dbo.INVE01           TO [catalogo_lectura];
GRANT SELECT ON OBJECT::dbo.PRECIO_X_PROD01  TO [catalogo_lectura];
GO

-- Revision: solo deben aparecer dos filas SELECT / GRANT (mas CONNECT, que es lo normal de cualquier usuario).
SELECT  dp.permission_name,
        dp.state_desc,
        COALESCE(OBJECT_SCHEMA_NAME(dp.major_id) + N'.' + OBJECT_NAME(dp.major_id), dp.class_desc) AS objeto
FROM    sys.database_permissions AS dp
WHERE   dp.grantee_principal_id = USER_ID(N'catalogo_lectura')
ORDER BY objeto, dp.permission_name;
GO

/* -----------------------------------------------------------------------------
   PARA DESHACER (quitar la cuenta). Quita los comentarios y ejecuta solo este bloque:

   USE [SAE70Empre01];
   DROP USER [catalogo_lectura];
   GO
   USE [master];
   DROP LOGIN [catalogo_lectura];
   GO
   ----------------------------------------------------------------------------- */
