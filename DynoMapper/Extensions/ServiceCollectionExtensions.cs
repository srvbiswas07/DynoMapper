using DynoMapper.Abstractions;
using DynoMapper.Core;
using DynoMapper.SqlLayer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DynoMapper.Extensions;

/// <summary>
/// DynoMapper DI registration.
/// Call once in Program.cs — connection string is stored internally,
/// never needs to be passed again per query.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Register DynoMapper with full configuration.
    ///
    /// <code>
    /// // Program.cs
    /// builder.Services.AddDynoMapper(options =>
    /// {
    ///     options.ConnectionString = builder.Configuration.GetConnectionString("Default")!;
    ///     options.Provider         = DbProvider.SqlServer;   // or PostgreSQL / MySQL
    ///     options.CommandTimeoutSeconds = 30;
    /// });
    /// </code>
    ///
    /// Then inject ISqlHelper anywhere:
    /// <code>
    /// public class UserRepository(ISqlHelper sql)
    /// {
    ///     public async Task&lt;DynoResult&gt; GetAllAsync()
    ///         => await sql.QueryListAsync("SELECT * FROM Users");
    /// }
    /// </code>
    /// </summary>
    public static IServiceCollection AddDynoMapper(
        this IServiceCollection services,
        Action<DynoOptions> configure)
    {
        var options = new DynoOptions();
        configure(options);

        ValidateOptions(options);

        // Register options as singleton — connection string lives here
        services.AddSingleton(options);

        // Register connection factory — creates right connection per provider
        services.AddSingleton<IDbConnectionFactory, DbConnectionFactory>();

        // Register ISqlHelper as scoped — one per request
        services.AddScoped<ISqlHelper, SqlHelper>();

        return services;
    }

    /// <summary>
    /// Register DynoMapper using appsettings.json section.
    ///
    /// appsettings.json:
    /// <code>
    /// {
    ///   "DynoMapper": {
    ///     "ConnectionString": "Server=...;Database=...;",
    ///     "Provider": "SqlServer",
    ///     "CommandTimeoutSeconds": 30
    ///   }
    /// }
    /// </code>
    ///
    /// Program.cs:
    /// <code>
    /// builder.Services.AddDynoMapper(builder.Configuration);
    /// </code>
    /// </summary>
    public static IServiceCollection AddDynoMapper(
        this IServiceCollection services,
        IConfiguration configuration,
        string sectionName = "DynoMapper")
    {
        var options = new DynoOptions();
        configuration.GetSection(sectionName).Bind(options);

        ValidateOptions(options);

        services.AddSingleton(options);
        services.AddSingleton<IDbConnectionFactory, DbConnectionFactory>();
        services.AddScoped<ISqlHelper, SqlHelper>();

        return services;
    }

    private static void ValidateOptions(DynoOptions options)
    {
        if (options.Provider != DbProvider.EFCore &&
            string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            throw new InvalidOperationException(
                "DynoMapper: ConnectionString is required when not using EFCore provider. " +
                "Set options.ConnectionString in AddDynoMapper(...).");
        }
    }
}
