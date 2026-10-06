using Dapper;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;

namespace ILAPI.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration config)
    {
        var cs = config.GetConnectionString("PostgreSQL");
        if (string.IsNullOrWhiteSpace(cs) || cs.Contains("__SET_VIA_ENV_OR_USER_SECRETS__"))
            throw new InvalidOperationException(
                "ConnectionStrings:PostgreSQL is not configured. Use dotnet user-secrets, the ConnectionStrings__PostgreSQL env var, or appsettings.Development.local.json.");

        DefaultTypeMap.MatchNamesWithUnderscores = true;
        services.AddSingleton(NpgsqlDataSource.Create(cs));
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<ITokenRepository, TokenRepository>();
        services.AddScoped<IProductRepository, ProductRepository>();
        services.AddScoped<IQuoteRepository, QuoteRepository>();
        services.AddHealthChecks().AddCheck<PostgresHealthCheck>("postgres");
        return services;
    }
}

public class PostgresHealthCheck(NpgsqlDataSource db) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        try
        {
            await using var c = await db.OpenConnectionAsync(ct);
            await c.ExecuteScalarAsync<int>("SELECT 1");
            return HealthCheckResult.Healthy("Connected to ILPortal");
        }
        catch (Exception ex)
        {
            // message only: never include the connection string
            return HealthCheckResult.Unhealthy("Cannot connect to ILPortal: " + ex.GetType().Name);
        }
    }
}
