using Microsoft.SqlServer.Dac.Model;

namespace Coding4Fun.Etl.Build.Services.Dacpac;

/// <summary>
/// Provides functionality to load SQL model data from a DACPAC file.
/// </summary>
/// <remarks>
/// This loader is specifically designed for internal use within the DACPAC processing pipeline.
/// </remarks>
internal class DacPacModelLoader : ISqlModelLoader
{
    /// <summary>
    /// Loads a SQL model from the specified file path.
    /// </summary>
    /// <param name="path">The full path to the DACPAC file containing the SQL model.</param>
    /// <returns>
    /// A new <see cref="TSqlModel"/> instance populated with data from the specified DACPAC file.
    /// </returns>
    public TSqlModel Load(string path) => new(path);
}