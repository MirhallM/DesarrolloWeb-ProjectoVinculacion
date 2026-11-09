/* =============================================================================
   DIA DE LA INSTALACION: muestra como esta configurada la memoria de SQL Server.
   SOLO LECTURA: NO modifica la configuracion (no usa sp_configure ni RECONFIGURE).

   Por que importa: el servidor de la tienda tiene solo 2 GB de RAM y comparte SQL Server, Aspel y el agente.
   Si "max server memory" esta sin limite (valor 2147483647, el de fabrica), SQL Server puede tomar casi toda
   la memoria y dejar poco a Aspel y al agente. Este script solo MUESTRA el valor; cambiarlo, si hiciera falta,
   es una decision del administrador de la tienda y no forma parte de este proyecto.

   COMO EJECUTARLO (como administrador: las vistas de memoria piden el permiso VIEW SERVER STATE):
       sqlcmd -S localhost -E -b -i 03-configuracion-memoria.sql
   ============================================================================= */
SET NOCOUNT ON;
GO

PRINT '=== 1. Configuracion de memoria de SQL Server ===';
-- value        = lo configurado; value_in_use = lo que esta en vigor.
-- 2147483647 en "max server memory" = SIN LIMITE (valor de fabrica).
SELECT  name,
        CAST(value        AS bigint) AS configurado_mb,
        CAST(value_in_use AS bigint) AS en_uso_mb,
        CASE WHEN name = 'max server memory (MB)' AND CAST(value_in_use AS bigint) = 2147483647
             THEN 'SIN LIMITE (valor de fabrica)' ELSE '' END AS nota
FROM    sys.configurations
WHERE   name IN ('max server memory (MB)', 'min server memory (MB)');
GO

PRINT '=== 2. Memoria del servidor (Windows) ===';
SELECT  total_physical_memory_kb / 1024      AS ram_total_mb,
        available_physical_memory_kb / 1024  AS ram_libre_mb,
        total_page_file_kb / 1024            AS archivo_de_paginacion_total_mb,
        available_page_file_kb / 1024        AS archivo_de_paginacion_libre_mb,
        system_memory_state_desc             AS estado_de_la_memoria
FROM    sys.dm_os_sys_memory;
GO

PRINT '=== 3. Memoria que usa el proceso de SQL Server ahora mismo ===';
SELECT  physical_memory_in_use_kb / 1024     AS en_uso_mb,
        large_page_allocations_kb / 1024     AS paginas_grandes_mb,
        memory_utilization_percentage        AS porcentaje_de_uso,
        process_physical_memory_low          AS poca_memoria_fisica,
        process_virtual_memory_low           AS poca_memoria_virtual
FROM    sys.dm_os_process_memory;
GO

PRINT '=== 4. Objetivo de memoria de SQL Server ===';
SELECT  committed_kb / 1024                  AS comprometida_mb,
        committed_target_kb / 1024           AS objetivo_mb
FROM    sys.dm_os_sys_info;
GO

PRINT '=== Fin. Solo se hicieron consultas de lectura. ===';
GO
