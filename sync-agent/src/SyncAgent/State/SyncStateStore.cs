using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace SyncAgent.State;

/// <summary>Lee y escribe <c>sync-state.json</c> dentro de la carpeta de datos del agente.</summary>
public sealed class SyncStateStore(string directory, ILogger<SyncStateStore> logger)
{
    public const string FileName = "sync-state.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    public string FilePath => Path.Combine(directory, FileName);

    /// <summary>Estado guardado. Si el archivo no existe o está dañado, devuelve un estado vacío (nunca falla).</summary>
    public SyncState Load()
    {
        if (!File.Exists(FilePath)) return new SyncState();

        try
        {
            var json = File.ReadAllText(FilePath, Encoding.UTF8);
            return JsonSerializer.Deserialize<SyncState>(json, JsonOptions) ?? new SyncState();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // Solo el tipo de error: el contenido del archivo no se repite en el log.
            logger.LogWarning("No se pudo leer {File} ({Reason}). Se empieza con un estado vacío.", FileName, ex.GetType().Name);
            return new SyncState();
        }
    }

    /// <summary>
    /// Guarda el estado de forma atómica: escribe un archivo temporal y lo mueve encima del definitivo, así un
    /// corte de luz a mitad de la escritura no deja un JSON a medias.
    /// </summary>
    public void Save(SyncState state)
    {
        Directory.CreateDirectory(directory);

        var temp = FilePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(state, JsonOptions), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        File.Move(temp, FilePath, overwrite: true);
    }
}
