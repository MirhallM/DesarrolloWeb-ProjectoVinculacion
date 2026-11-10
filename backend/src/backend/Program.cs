using backend.Catalog;
using backend.Data;
using backend.Data.Entities;
using backend.Data.Seed;
using Microsoft.EntityFrameworkCore;
var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException(
        "Falta ConnectionStrings:DefaultConnection. Configúrala con dotnet user-secrets (ver README).");

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(connectionString));

builder.Services
    .AddIdentityCore<AppUser>(options =>
    {
        options.User.RequireUniqueEmail = true;
    })
    .AddRoles<AppRole>()
    .AddEntityFrameworkStores<AppDbContext>();

// Con "seed" EF registraría cada una de las miles de inserciones; solo interesan los avisos y errores.
if (CatalogSeedCommand.IsRequested(args))
{
    builder.Logging.AddFilter("Microsoft.EntityFrameworkCore", LogLevel.Warning);
}

builder.Services.AddCatalogRules();
builder.Services.AddScoped<CatalogSeeder>();

builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var app = builder.Build();

// "dotnet backend.dll seed": siembra categorías y reglas y termina, sin levantar el servidor (pre-deploy de Railway).
if (CatalogSeedCommand.IsRequested(args))
{
    return await CatalogSeedCommand.RunAsync(app.Services);
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
return 0;

public partial class Program;
