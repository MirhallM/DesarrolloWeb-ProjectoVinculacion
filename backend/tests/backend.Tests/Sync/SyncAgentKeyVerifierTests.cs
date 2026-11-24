using backend.Sync;

namespace backend.Tests.Sync;

public class SyncAgentKeyVerifierTests
{
    // Llaves de prueba evidentes: nunca se usan fuera de estas pruebas.
    private const string Key = "llave-de-ejemplo-no-usar-0000000000000000000";
    private const string OtherKey = "otra-llave-de-ejemplo-no-usar-11111111111111";

    private static SyncAgentKeyVerifier Create(string? configured, out string? problem) =>
        SyncAgentKeyVerifier.Create(configured, out problem);

    [Fact]
    public void Acepta_la_llave_cuyo_hash_esta_configurado()
    {
        var verifier = Create(SyncAgentKeyVerifier.ComputeHash(Key), out var problem);

        Assert.Null(problem);
        Assert.True(verifier.IsConfigured);
        Assert.True(verifier.Verify(Key));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("llave-de-ejemplo-no-usar-000000000000000000")] // un carácter menos
    [InlineData("LLAVE-DE-EJEMPLO-NO-USAR-0000000000000000000")] // distingue mayúsculas
    public void Rechaza_llaves_ausentes_o_distintas(string? candidate)
    {
        var verifier = Create(SyncAgentKeyVerifier.ComputeHash(Key), out _);
        Assert.False(verifier.Verify(candidate));
    }

    [Fact]
    public void Rechaza_llaves_demasiado_largas_sin_calcular_nada()
    {
        var huge = new string('a', SyncAgentKeyVerifier.MaxKeyLength + 1);
        var verifier = Create(SyncAgentKeyVerifier.ComputeHash(huge), out _);

        Assert.False(verifier.Verify(huge));
    }

    [Fact]
    public void Con_dos_hashes_acepta_las_dos_llaves_para_rotar()
    {
        var configured = $"{SyncAgentKeyVerifier.ComputeHash(Key)} , {SyncAgentKeyVerifier.ComputeHash(OtherKey).ToUpperInvariant()}";
        var verifier = Create(configured, out var problem);

        Assert.Null(problem);
        Assert.True(verifier.Verify(Key));
        Assert.True(verifier.Verify(OtherKey));
        Assert.False(verifier.Verify("cualquier-otra-cosa"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    [InlineData("no-es-hexadecimal")]
    [InlineData("abcd")] // corto
    [InlineData("zz00000000000000000000000000000000000000000000000000000000000000")] // 64 pero no hexadecimal
    public void Sin_configuracion_valida_rechaza_todo_y_explica_el_problema(string? configured)
    {
        var verifier = Create(configured, out var problem);

        Assert.False(verifier.IsConfigured);
        Assert.False(verifier.Verify(Key));
        Assert.NotNull(problem);
        if (!string.IsNullOrWhiteSpace(configured))
        {
            Assert.DoesNotContain(configured, problem); // el mensaje no repite el valor configurado
        }
    }

    [Fact]
    public void Mas_de_dos_hashes_es_configuracion_invalida()
    {
        var h = SyncAgentKeyVerifier.ComputeHash(Key);
        var verifier = Create($"{h},{h},{h}", out var problem);

        Assert.False(verifier.IsConfigured);
        Assert.NotNull(problem);
    }

    [Fact]
    public void El_hash_es_sha256_en_hexadecimal_minuscula()
    {
        // SHA-256 de "abc" (vector de prueba estándar).
        Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", SyncAgentKeyVerifier.ComputeHash("abc"));
    }
}
