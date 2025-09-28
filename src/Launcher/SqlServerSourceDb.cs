using System.Data;
using Microsoft.Data.SqlClient;

/// <summary>
/// Provides SQL Server database connectivity and execution capabilities for source database operations.
/// </summary>
/// <remarks>
/// This implementation uses ADO.NET with asynchronous operations for efficient data retrieval.
/// </remarks>
public class SqlServerSourceDb(string connectionString) : ISourceDb
{
    /// <inheritdoc />
    public async Task ExecuteReaderAsync(string sql, Func<IDataReader, Task> asyncConsumer, Action<SqlCommand>? commandModifier = null)
    {
        using SqlConnection sourceConnection = new(connectionString);
        await sourceConnection.OpenAsync();
        using SqlCommand sourceCommand = new(sql, sourceConnection);
        commandModifier?.Invoke(sourceCommand);
        using IDataReader sourceDataReader = await sourceCommand.ExecuteReaderAsync();
        await asyncConsumer.Invoke(sourceDataReader);
    }
}