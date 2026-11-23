using backend.Catalog;
using backend.Data;
using backend.Data.Entities;
using backend.Data.Seed;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
var builder = WebApplication.CreateBuilder(args);

// Railway indica el puerto en PORT. "*" escucha en IPv4 e IPv6, que es lo que usa la red privada (railway.internal).
var port = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrWhiteSpace(port))
{
    builder.WebHost.UseUrls($"http://*:{port}");
}

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

// Llaves en PostgreSQL y no en el disco: cada despliegue arranca un contenedor nuevo y, con llaves en disco,
// todas las cookies de Identity quedarían inválidas. El nombre fijo evita depender de la ruta de la aplicación.
// Sin cifrado de las llaves en reposo: PostgreSQL solo es accesible por la red privada (ver docs/deployment.md).
builder.Services.AddDataProtection()
    .SetApplicationName("nettes-backend")
    .PersistKeysToDbContext<AppDbContext>();

// Con los comandos de consola EF registraría cada sentencia; solo interesan los avisos y errores.
if (MigrateCommand.IsRequested(args)
    || CatalogSeedCommand.IsRequested(args)
    || DevCatalogSeedCommand.IsRequested(args)
    || DataProtectionCheckCommand.IsRequested(args))
{
    builder.Logging.AddFilter("Microsoft.EntityFrameworkCore", LogLevel.Warning);
}

builder.Services.AddCatalogRules();
builder.Services.AddScoped<CatalogSeeder>();
builder.Services.AddScoped<DevCatalogSeeder>();

builder.Services.AddControllers();
builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks().AddDbContextCheck<AppDbContext>("postgresql");
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var app = builder.Build();

// "dotnet backend.dll migrate": aplica las migraciones y termina (pre-deploy de Railway, antes de "seed").
if (MigrateCommand.IsRequested(args))
{
    return await MigrateCommand.RunAsync(app.Services);
}

// "dotnet backend.dll dp-check protect|unprotect <texto>": diagnóstico de las llaves de Data Protection.
if (DataProtectionCheckCommand.IsRequested(args))
{
    return DataProtectionCheckCommand.Run(app.Services, args);
}

// "dotnet backend.dll seed": siembra categorías y reglas y termina, sin levantar el servidor (pre-deploy de Railway).
if (CatalogSeedCommand.IsRequested(args))
{
    return await CatalogSeedCommand.RunAsync(app.Services);
}

// "dotnet run -- seed-dev": siembra productos de prueba (solo en Development).
if (DevCatalogSeedCommand.IsRequested(args))
{
    return await DevCatalogSeedCommand.RunAsync(app.Services, app.Environment);
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/openapi/v1.json", "Mini Super Nettes API v1");
    });

    // En Railway el TLS termina en la plataforma y el backend recibe HTTP por la red privada.
    app.UseHttpsRedirection();
}
else
{
    app.UseExceptionHandler();
}

app.UseStatusCodePages();

app.UseAuthorization();

// 200 si PostgreSQL responde, 503 si no. Healthcheck de Railway y, por el reenvío del frontend, /api/health público.
app.MapHealthChecks("/api/health");

app.MapControllers();

app.Run();
return 0;

public partial class Program;
