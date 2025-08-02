using System.Data;
using Microsoft.Data.SqlClient;

/// <summary>
/// Provides SQL Server database connectivity and execution capabilities for source database operations.
/// </summary>
/// <remarks>
/// This implementation uses ADO.NET with asynchronous operations for efficient data retrieval.
/// </remarks>
public class SqlServerTargetDb(string connectionString) : ITargetDb
{
    /// <summary>
    /// Underlying SQL connection used for all database operations.
    /// </summary>
    private SqlConnection? _connection;

    /// <summary>
    /// Transaction context for maintaining atomic operations across multiple database calls.
    /// </summary>
    private SqlTransaction? _transaction;

    /// <inheritdoc />
    public async Task<int> BulkInsertAsync(string tableName, IDataReader dataReader)
    {
        using SqlBulkCopy bulkCopyToTempTable = new(_connection, SqlBulkCopyOptions.Default, _transaction)
        {
            DestinationTableName = tableName
        };
        await bulkCopyToTempTable.WriteToServerAsync(dataReader);
        return bulkCopyToTempTable.RowsCopied;
    }

    /// <inheritdoc />
    public async Task CloseConnectionAsync()
    {
        await Task.CompletedTask;
        _transaction.Commit();
        _connection.Close();
        _connection.Dispose();
        _connection = null;
        _transaction = null;
    }

    /// <inheritdoc />
    public async Task ExecuteAsync(string sql)
    {
        SqlCommand sqlCommand = new(sql, _connection, _transaction);
        await sqlCommand.ExecuteNonQueryAsync();
    }

    /// <inheritdoc />
    public async Task ExecuteReaderAsync(string sql, Func<IDataReader, Task> asyncConsumer)
    {
        using SqlCommand sourceCommand = new(sql, _connection, _transaction);
        using IDataReader sourceDataReader = await sourceCommand.ExecuteReaderAsync();
        await asyncConsumer.Invoke(sourceDataReader);
    }

    /// <inheritdoc />
    public async Task<DateTime?> GetLastOffsetAsync(string etlName)
    {
        using SqlCommand selectLastOffsetCommand = new(
            @"SELECT TOP (1) Value
            FROM dbo.TableSync
            WHERE EtlName = @EtlName;", _connection, _transaction);

        selectLastOffsetCommand.Parameters.AddWithValue("@EtlName", etlName);

        var val = await selectLastOffsetCommand.ExecuteScalarAsync();
        DateTime? offset = val == DBNull.Value ? null : (DateTime?)val;

        return offset;
    }

    /// <inheritdoc />
    public async Task OpenConnectionAsync()
    {
        _connection = new SqlConnection(connectionString);
        _connection.Open();
        _transaction = _connection.BeginTransaction();
        await Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task SaveLastOffsetAsync(string etlName, DateTime lastOffset)
    {
        using SqlCommand insertLastOffsetCommand = new(
            @"MERGE dbo.TableSync AS Target
              USING (SELECT @EtlName AS EtlName,
                            @LastOffset AS [Value],
                            SYSDATETIME() AS CommitTime) AS Source
              ON Source.EtlName = Target.EtlName
              WHEN NOT MATCHED THEN INSERT (EtlName, [Value], [CommitTime])
                                    VALUES (EtlName, [Value], [CommitTime])
              WHEN MATCHED THEN UPDATE SET [Value]      = Source.[Value],
                                           [CommitTime] = Source.[CommitTime];",
            _connection, _transaction);

        insertLastOffsetCommand.Parameters.AddWithValue("@EtlName", etlName);
        insertLastOffsetCommand.Parameters.AddWithValue("@LastOffset", lastOffset);

        await insertLastOffsetCommand.ExecuteNonQueryAsync();
    }
}