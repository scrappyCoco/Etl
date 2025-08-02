using Microsoft.SqlServer.Dac.Model;

namespace Coding4Fun.Etl.Build.Services.Dacpac;

/// <summary>
/// Defines a contract for loading SQL model data from a file.
/// </summary>
internal interface ISqlModelLoader
{
    /// <summary>
    /// Loads a SQL model from the specified file path.
    /// </summary>
    /// <param name="path">The full path to the file containing the SQL model.</param>
    /// <returns>A new <see cref="TSqlModel"/> instance populated with data from the specified file.</returns>
    TSqlModel Load(string path);
}