namespace SyncAgent;

/// <summary>Sección <c>Agent</c> de la configuración.</summary>
public sealed class AgentOptions
{
    public const string SectionName = "Agent";

    /// <summary>Carpeta del estado, el candado y los logs. Si es relativa, cuelga de la carpeta del programa.</summary>
    public string DataDirectory { get; set; } = "data";

    /// <summary>Días que se conservan los logs (el servidor de la tienda tiene poco disco).</summary>
    public int LogRetentionDays { get; set; } = 14;

    /// <summary>
    /// Ruta absoluta de la carpeta de datos. Se resuelve contra la carpeta del programa y no contra el directorio
    /// de trabajo, porque el Programador de tareas arranca el proceso en System32.
    /// </summary>
    public string ResolveDataDirectory(string baseDirectory) =>
        Path.IsPathRooted(DataDirectory) ? DataDirectory : Path.Combine(baseDirectory, DataDirectory);

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(DataDirectory)) errors.Add("Agent:DataDirectory no puede estar vacío.");
        if (LogRetentionDays < 1) errors.Add("Agent:LogRetentionDays debe ser 1 o mayor.");
        return errors;
    }
}
