using System.Diagnostics;

namespace SyncAgent.Diagnostics;

/// <summary>Memoria del proceso en un instante, para medir el consumo real en el servidor de la tienda.</summary>
/// <param name="PeakWorkingSetBytes">Máximo de memoria física usada por el proceso (lo que importa en un servidor de 2 GB).</param>
/// <param name="PeakPagedBytes">Máximo de memoria virtual paginable comprometida.</param>
/// <param name="ManagedHeapBytes">Tamaño actual del heap administrado de .NET.</param>
/// <param name="TotalAllocatedBytes">Total asignado por el programa durante su vida (indica cuánto trabajó el GC).</param>
/// <param name="GcAvailableBytes">Memoria que el GC cree disponible; con el tope activo debe coincidir con el límite (64 MB).</param>
public sealed record ProcessMemory(
    long PeakWorkingSetBytes,
    long PeakPagedBytes,
    long ManagedHeapBytes,
    long TotalAllocatedBytes,
    long GcAvailableBytes,
    int Gen0Collections,
    int Gen2Collections)
{
    public static ProcessMemory Capture()
    {
        using var process = Process.GetCurrentProcess();
        var gc = GC.GetGCMemoryInfo();
        return new ProcessMemory(
            process.PeakWorkingSet64,
            process.PeakPagedMemorySize64,
            GC.GetTotalMemory(forceFullCollection: false),
            GC.GetTotalAllocatedBytes(),
            gc.TotalAvailableMemoryBytes,
            GC.CollectionCount(0),
            GC.CollectionCount(2));
    }

    public static double ToMegabytes(long bytes) => bytes / 1024d / 1024d;
}
