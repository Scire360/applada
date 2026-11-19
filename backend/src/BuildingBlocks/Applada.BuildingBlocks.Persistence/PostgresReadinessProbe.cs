using Npgsql;

namespace Applada.BuildingBlocks.Persistence;

public sealed class PostgresReadinessProbe : IAsyncDisposable
{
    private readonly NpgsqlDataSource _dataSource;

    public PostgresReadinessProbe(string connectionString)
    {
        _dataSource = NpgsqlDataSource.Create(connectionString);
    }

    public async Task<bool> IsReadyAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
            await using var command = new NpgsqlCommand("SELECT 1", connection);
            await command.ExecuteScalarAsync(cancellationToken);
            return true;
        }
        catch (NpgsqlException)
        {
            return false;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _dataSource.DisposeAsync();
    }
}
