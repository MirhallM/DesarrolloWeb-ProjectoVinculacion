using System.Net;
using backend.Catalog;
using backend.Data.Entities;
using Microsoft.AspNetCore.Mvc.Testing;

namespace backend.Tests.Catalog;

public sealed class CatalogQueryValidationTests : IClassFixture<CatalogQueryValidationTests.DummyHostFactory>
{
    private readonly HttpClient _client;

    public CatalogQueryValidationTests(DummyHostFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Theory]
    [InlineData("/api/products?page=0")]
    [InlineData("/api/products?page=-1")]
    [InlineData("/api/products?page=abc")]
    [InlineData("/api/products?pageSize=0")]
    [InlineData("/api/products?pageSize=-5")]
    [InlineData("/api/products?pageSize=101")]
    [InlineData("/api/products?sort=code")]
    [InlineData("/api/products?sort=stock")]
    [InlineData("/api/products?sort=")]
    [InlineData("/api/products?order=random")]
    [InlineData("/api/products?order=")]
    public async Task GetProducts_con_parametros_invalidos_responde_400(string url)
    {
        var response = await _client.GetAsync(url, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task GetProductById_con_id_no_guid_responde_404_problem_details()
    {
        var response = await _client.GetAsync("/api/products/no-es-guid", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public void WhereVisible_aplica_exactamente_las_reglas_de_negocio()
    {
        var category = new Category { Id = Guid.NewGuid(), Name = "Abarrotes", Slug = "abarrotes", Position = 1 };
        var products = new[]
        {
            new Product { Id = Guid.NewGuid(), IsActive = true, PublicPrice = 10.50m, DisplayName = "Arroz", Category = category },
            new Product { Id = Guid.NewGuid(), IsActive = true, PublicPrice = 99_999.99m, DisplayName = "Producto Límite", Category = category },
            new Product { Id = Guid.NewGuid(), IsActive = false, PublicPrice = 10.50m, DisplayName = "Inactivo", Category = category },
            new Product { Id = Guid.NewGuid(), IsActive = true, PublicPrice = 0m, DisplayName = "Sin Precio", Category = category },
            new Product { Id = Guid.NewGuid(), IsActive = true, PublicPrice = -5m, DisplayName = "Precio Negativo", Category = category },
            new Product { Id = Guid.NewGuid(), IsActive = true, PublicPrice = 100_000m, DisplayName = "Precio Sospechoso Límite", Category = category },
            new Product { Id = Guid.NewGuid(), IsActive = true, PublicPrice = 150_000m, DisplayName = "Precio Sospechoso Alto", Category = category },
            new Product { Id = Guid.NewGuid(), IsActive = true, PublicPrice = 20m, DisplayName = "", Category = category },
            new Product { Id = Guid.NewGuid(), IsActive = true, PublicPrice = 20m, DisplayName = "   ", Category = category },
        };

        var visible = products.AsQueryable().WhereVisible().Select(p => p.DisplayName).ToList();

        Assert.Equal(["Arroz", "Producto Límite"], visible);
    }

    public sealed class DummyHostFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
        {
            // La validación de parámetros responde 400 antes de consultar PostgreSQL.
            builder.UseSetting("ConnectionStrings:DefaultConnection",
                "Host=localhost;Database=pruebas;Username=pruebas;Password=pruebas");
        }
    }
}
