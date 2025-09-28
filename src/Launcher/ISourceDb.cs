using System.Data;
using Microsoft.Data.SqlClient;

/// <summary>
/// Interface for source database
/// </summary>
interface ISourceDb
{
    /// <summary>
    /// Executes a SQL query and consumes the results asynchronously.
    /// </summary>
    /// <param name="sql">The SQL query to execute.</param>
    /// <param name="asyncConsumer">The asynchronous consumer function to process the results.</param>
    /// <param name="commandModifier">An optional action to modify the SQL command before execution.</param>
    Task ExecuteReaderAsync(string sql, Func<IDataReader, Task> asyncConsumer, Action<SqlCommand>? commandModifier = null);
}