using System.Security.Cryptography;
using System.Text;

namespace backend.Sync;

/// <summary>
/// Comprueba la llave del agente de sincronización. El backend solo conoce el hash SHA-256 de la llave:
/// calcula el hash de lo recibido y lo compara en tiempo constante.
/// </summary>
public sealed class SyncAgentKeyVerifier
{
    public const string HeaderName = "X-Sync-Agent-Key";

    /// <summary>Las llaves generadas miden 43 caracteres; algo mucho más largo no se procesa.</summary>
    public const int MaxKeyLength = 256;

    public const int MaxHashes = 2;

    private readonly byte[][] _hashes;

    private SyncAgentKeyVerifier(byte[][] hashes) => _hashes = hashes;

    public bool IsConfigured => _hashes.Length > 0;

    /// <summary>
    /// Crea el verificador a partir de <c>Sync:AgentKeyHashes</c>. Si falta o es inválido, el verificador rechaza
    /// toda llave y <paramref name="problem"/> explica por qué (sin repetir el valor configurado).
    /// </summary>
    public static SyncAgentKeyVerifier Create(string? configured, out string? problem)
    {
        problem = null;
        if (string.IsNullOrWhiteSpace(configured))
        {
            problem = "Sync:AgentKeyHashes no está configurado: /api/sync rechazará todas las solicitudes (401).";
            return new SyncAgentKeyVerifier([]);
        }

        var parts = configured.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length is 0 or > MaxHashes)
        {
            problem = $"Sync:AgentKeyHashes debe tener uno o {MaxHashes} hashes separados por coma: /api/sync rechazará todas las solicitudes (401).";
            return new SyncAgentKeyVerifier([]);
        }

        var hashes = new List<byte[]>();
        foreach (var part in parts)
        {
            if (part.Length != SHA256.HashSizeInBytes * 2 || !part.All(Uri.IsHexDigit))
            {
                problem = "Sync:AgentKeyHashes tiene un valor que no es un SHA-256 en hexadecimal (64 caracteres): " +
                          "/api/sync rechazará todas las solicitudes (401).";
                return new SyncAgentKeyVerifier([]);
            }

            hashes.Add(Convert.FromHexString(part));
        }

        return new SyncAgentKeyVerifier([.. hashes]);
    }

    public bool Verify(string? key)
    {
        if (_hashes.Length == 0 || string.IsNullOrEmpty(key) || key.Length > MaxKeyLength)
        {
            return false;
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        var matches = false;
        foreach (var expected in _hashes)
        {
            // Se comparan todos los hashes, sin cortar al primero que coincida.
            matches |= CryptographicOperations.FixedTimeEquals(hash, expected);
        }

        return matches;
    }

    /// <summary>Hash en el formato de <c>Sync:AgentKeyHashes</c> (lo usan las pruebas y el script de la llave).</summary>
    public static string ComputeHash(string key) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
}
