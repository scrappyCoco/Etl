namespace Coding4Fun.Etl.Build.Services.IO;

internal class DefaultFileSystemProvider : IFileSystemProvider
{
    /// <inheritdoc />
    public void CreateDirectory(string path) => Directory.CreateDirectory(path);

    /// <inheritdoc />
    public string ReadAllText(string path) => File.ReadAllText(path);

    /// <inheritdoc />
    public void WriteAllText(string path, string? contents) => File.WriteAllText(path, contents);
}