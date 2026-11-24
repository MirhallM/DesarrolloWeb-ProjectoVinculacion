using System.Globalization;

namespace backend.Sync;

public sealed record SyncBrakeDecision(bool Blocked, string? Reason);

/// <summary>
/// Freno de seguridad: impide que una lectura vacía o parcial de Aspel desactive el catálogo.
/// Se mide con los activos que la corrida desactivaría por no venir, no con el total recibido: los artículos de baja
/// siguen llegando de Aspel, así que lo normal es que casi ningún activo desaparezca.
/// </summary>
public static class SyncSafetyBrake
{
    public static SyncBrakeDecision Evaluate(int received, int currentlyActive, int activeMissing, SyncOptions options)
    {
        if (received == 0)
        {
            return new(true, "Freno de seguridad: la corrida no trajo artículos. No se cambió nada.");
        }

        if (activeMissing > options.MaxDeactivationCount && activeMissing > currentlyActive * options.MaxDeactivationRatio)
        {
            var ratio = options.MaxDeactivationRatio.ToString("P0", CultureInfo.InvariantCulture);
            return new(true,
                $"Freno de seguridad: se desactivarían {activeMissing} de {currentlyActive} productos activos " +
                $"(más del {ratio} y más de {options.MaxDeactivationCount}). No se cambió nada. " +
                "Si la baja es real, suba Sync:MaxDeactivationRatio temporalmente.");
        }

        return new(false, null);
    }
}
