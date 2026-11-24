using SyncAgent.Diagnostics;

namespace SyncAgent.Tests.Unit;

public class ConnectionStringChecksTests
{
    [Theory]
    [InlineData("Server=localhost;Database=SAE70Empre01;User Id=catalogo_lectura;Password=CAMBIAR;TrustServerCertificate=True;")]
    [InlineData("Server=localhost;Database=SAE70Empre01;User Id=catalogo_lectura;Password=cambiar;")]
    [InlineData("Server=localhost;Database=SAE70Empre01;User Id=catalogo_lectura;Password= CAMBIAR ;")]
    [InlineData("Server=localhost;Password=CAMBIAR")]
    public void Detecta_la_contrasena_de_ejemplo(string connectionString) =>
        Assert.True(ConnectionStringChecks.HasPlaceholderPassword(connectionString));

    [Theory]
    [InlineData("Server=localhost;Database=SAE70Empre01;User Id=catalogo_lectura;Password=Cl4ve-Real-12345;")]
    [InlineData("Server=localhost;Database=SAE70Empre01;User Id=catalogo_lectura;Password=CAMBIAR-por-otra-cosa;")]
    [InlineData("Server=localhost;Database=SAE70Empre01;Integrated Security=True;")]
    public void No_confunde_una_contrasena_real(string connectionString) =>
        Assert.False(ConnectionStringChecks.HasPlaceholderPassword(connectionString));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("esto no es una cadena;;; {{")]
    public void Una_cadena_vacia_o_invalida_no_es_la_de_ejemplo(string? connectionString) =>
        Assert.False(ConnectionStringChecks.HasPlaceholderPassword(connectionString));

    [Fact]
    public void El_ejemplo_que_se_instala_en_la_tienda_se_detecta()
    {
        // La cadena exacta de appsettings.Production.example.json: si alguien la cambia, esta prueba lo avisa.
        var example = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "appsettings.Production.example.json"));
        var connectionString = System.Text.Json.JsonDocument.Parse(example).RootElement
            .GetProperty("ConnectionStrings").GetProperty("AspelSae").GetString();

        Assert.True(ConnectionStringChecks.HasPlaceholderPassword(connectionString));
    }
}
