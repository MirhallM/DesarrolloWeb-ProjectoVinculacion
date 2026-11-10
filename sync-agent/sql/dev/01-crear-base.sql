/* =============================================================================
   SOLO PARA DESARROLLO: crea una base SAE70Empre01 de mentira para probar el agente.
   NO se ejecuta en la tienda (alli la base ya existe y es la real de Aspel).

   Se ejecuta como administrador (sa) del SQL Server de Docker.
   Es repetible: si la base ya existe, la BORRA y la crea de nuevo.

   TIPOS SUPUESTOS (a confirmar en la tienda con sql/install/02-inspeccionar-columnas.sql):
     INVE01.CVE_ART          varchar(16) NOT NULL    (hasta 16 caracteres; puede llevar espacios al inicio)
     INVE01.DESCR            varchar(40) NULL        (hasta 40 caracteres)
     INVE01.EXIST            float       NULL        (decimales, negativos y valores inflados)
     INVE01.STATUS           varchar(1)  NULL        ('A' activo, 'B' baja)
     PRECIO_X_PROD01.CVE_ART    varchar(16) NOT NULL
     PRECIO_X_PROD01.CVE_PRECIO int         NOT NULL (lista de precios; la regla comercial usa la 1)
     PRECIO_X_PROD01.PRECIO     float       NULL

   Solo se crean las columnas que usa el agente. Las tablas reales de Aspel tienen muchas mas.
   ============================================================================= */
SET NOCOUNT ON;

IF DB_ID(N'SAE70Empre01') IS NOT NULL
BEGIN
    PRINT 'La base SAE70Empre01 ya existe: se borra y se crea de nuevo.';
    ALTER DATABASE [SAE70Empre01] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
    DROP DATABASE [SAE70Empre01];
END
GO

-- La colacion es un supuesto razonable para una base en espanol; se confirma en la tienda.
CREATE DATABASE [SAE70Empre01] COLLATE Modern_Spanish_CI_AS;
GO

USE [SAE70Empre01];
GO

CREATE TABLE dbo.INVE01
(
    CVE_ART varchar(16) NOT NULL CONSTRAINT PK_INVE01 PRIMARY KEY,
    DESCR   varchar(40) NULL,
    EXIST   float       NULL,
    STATUS  varchar(1)  NULL
);
GO

CREATE TABLE dbo.PRECIO_X_PROD01
(
    CVE_ART    varchar(16) NOT NULL,
    CVE_PRECIO int         NOT NULL,
    PRECIO     float       NULL,
    CONSTRAINT PK_PRECIO_X_PROD01 PRIMARY KEY (CVE_ART, CVE_PRECIO)
);
GO

PRINT 'Base SAE70Empre01 creada con dbo.INVE01 y dbo.PRECIO_X_PROD01.';
GO
