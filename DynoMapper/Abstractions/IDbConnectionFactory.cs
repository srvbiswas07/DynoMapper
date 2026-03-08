using System.Data.Common;
using DynoMapper.Core;
using Microsoft.Data.SqlClient;
using MySqlConnector;
using Npgsql;

namespace DynoMapper.Abstractions;

/// <summary>
/// Abstracts database connection creation across all providers.
/// DynoMapper uses this internally — you never touch it directly.
/// </summary>
public interface IDbConnectionFactory
{
    DbConnection CreateConnection();
    DbProvider Provider { get; }
}

/// <summary>
/// Default implementation — creates the right connection based on DbProvider.
/// Registered automatically when you call services.AddDynoMapper(...).
/// </summary>
internal sealed class DbConnectionFactory(DynoOptions options) : IDbConnectionFactory
{
    private readonly DynoOptions _options = options;
    public DbProvider Provider => _options.Provider;

    public DbConnection CreateConnection()
    {
        return _options.Provider switch
        {
            DbProvider.SqlServer  => new SqlConnection(_options.ConnectionString),
            DbProvider.PostgreSQL => new NpgsqlConnection(_options.ConnectionString),
            DbProvider.MySQL      => new MySqlConnection(_options.ConnectionString),
            DbProvider.EFCore     => throw new InvalidOperationException(
                "EFCore provider: inject DbContext directly and use DynoMapper.FromContext(dbContext)."),
            _ => throw new NotSupportedException($"Provider '{_options.Provider}' is not supported.")
        };
    }
}
