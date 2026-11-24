using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace backend.Sync;

public static class SyncServiceCollectionExtensions
{
    /// <summary>Registra la recepción de la sincronización del agente (US-06).</summary>
    public static IServiceCollection AddSync(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<SyncOptions>().Bind(configuration.GetSection(SyncOptions.SectionName));
        services.TryAddSingleton(TimeProvider.System);

        services.AddSingleton(sp =>
        {
            var options = sp.GetRequiredService<IOptions<SyncOptions>>().Value;
            var verifier = SyncAgentKeyVerifier.Create(options.AgentKeyHashes, out var problem);
            if (problem is not null)
            {
                // La API arranca igual: un error en esta variable no debe tumbar el catálogo público.
                sp.GetRequiredService<ILoggerFactory>().CreateLogger("backend.Sync").LogWarning("{Problem}", problem);
            }

            return verifier;
        });

        return services;
    }
}
