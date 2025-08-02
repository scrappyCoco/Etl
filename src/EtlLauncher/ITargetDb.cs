using System.Data;

/// <summary>
/// Defines the interface for the target database.
/// </summary>
public interface ITargetDb
{
    /// <summary>
    /// Opens the connection to the target database.
    /// </summary>
    Task OpenConnectionAsync();

    /// <summary>
    /// Closes the connection to the target database.
    /// </summary>
    /// <returns></returns>
    Task CloseConnectionAsync();

    /// <summary>
    /// Gets the last offset from the target database.
    /// </summary>
    /// <param name="etlName"></param>
    /// <returns></returns>
    Task<DateTime?> GetLastOffsetAsync(string etlName);

    /// <summary>
    /// Records or updates the last synchronization timestamp for an ETL process.
    /// </summary>
    /// <param name="etlName">Identifier for the ETL process.</param>
    /// <param name="lastOffset">Timestamp to record as the last synchronization point.</param>
    Task SaveLastOffsetAsync(string etlName, DateTime lastOffset);

    /// <summary>
    /// Executes a SQL command against the database.
    /// </summary>
    /// <param name="sql">SQL statement to execute.</param>
    Task ExecuteAsync(string sql);

    /// <summary>
    /// Performs a bulk insert operation from a data reader to the specified table.
    /// </summary>
    /// <param name="tableName">Name of the destination table.</param>
    /// <param name="dataReader">Data source containing rows to insert.</param>
    /// <returns>Number of rows successfully copied.</returns>
    Task<int> BulkInsertAsync(string tableName, IDataReader dataReader);

    /// <summary>
    /// Executes a SQL query and processes results via an asynchronous consumer delegate.
    /// </summary>
    /// <param name="sql">SQL query to execute.</param>
    /// <param name="asyncConsumer">Delegate to process returned data reader results.</param>
    Task ExecuteReaderAsync(string sql, Func<IDataReader, Task> asyncConsumer);
}