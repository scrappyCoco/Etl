using Microsoft.SqlServer.Dac.Model;

namespace Coding4Fun.Etl.Build.Services.Dacpac;

/// <summary>
/// Loads SQL model data from .sql files in a specified directory.
/// </summary>
/// <remarks>
/// This loader processes all .sql files recursively from the provided path using SQL Server 2016 (Sql160) compatibility mode.
/// </remarks>
internal class SqlModelLoader : ISqlModelLoader
{
    /// <summary>
    /// Loads a SQL model by parsing all .sql files in the specified directory.
    /// </summary>
    /// <param name="path">The root directory path containing .sql files to process.</param>
    /// <returns>
    /// A <see cref="TSqlModel"/> instance populated with objects from all found .sql files.
    /// </returns>
    public TSqlModel Load(string path)
    {
        TSqlModel sqlModel = new(SqlServerVersion.Sql160, new TSqlModelOptions());
        TSqlObjectOptions sqlObjectOptions = new();
        string[] sqlFiles = Directory.GetFiles(path, "*.sql", SearchOption.AllDirectories);
        foreach (string sqlFilePath in sqlFiles)
        {
            string fileName = Path.GetFileName(sqlFilePath);
            string sqlContent = File.ReadAllText(sqlFilePath);
            sqlModel.AddOrUpdateObjects(sqlContent, fileName, sqlObjectOptions);
        }
        return sqlModel;
    }
}