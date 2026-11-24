using backend.Sync;

namespace backend.Tests.Sync;

public class SyncSafetyBrakeTests
{
    private static readonly SyncOptions Defaults = new(); // 10 % y más de 20

    [Fact]
    public void Una_corrida_sin_articulos_siempre_frena() =>
        Assert.True(SyncSafetyBrake.Evaluate(0, 0, 0, Defaults).Blocked);

    [Fact]
    public void La_primera_carga_no_frena() =>
        Assert.False(SyncSafetyBrake.Evaluate(18_460, 0, 0, Defaults).Blocked);

    [Theory]
    [InlineData(18_000, 1_800, false)] // justo el 10 %: no es "más del"
    [InlineData(18_000, 1_801, true)]
    [InlineData(18_000, 5, false)]
    [InlineData(100, 20, false)]       // 20 % pero no más de 20
    [InlineData(100, 21, true)]
    [InlineData(33, 33, true)]         // todos los activos desaparecen
    public void Frena_si_se_desactivarian_mas_del_10_por_ciento_y_mas_de_20(int active, int missing, bool blocked)
    {
        var decision = SyncSafetyBrake.Evaluate(14, active, missing, Defaults);

        Assert.Equal(blocked, decision.Blocked);
        if (blocked)
        {
            Assert.Contains($"{missing} de {active}", decision.Reason);
        }
    }

    [Fact]
    public void Con_el_umbral_en_uno_permite_una_baja_masiva_real()
    {
        var options = new SyncOptions { MaxDeactivationRatio = 1 };
        Assert.False(SyncSafetyBrake.Evaluate(14, 33, 33, options).Blocked);
    }
}
