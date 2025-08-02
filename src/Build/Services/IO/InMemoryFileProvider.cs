using System.Text;

namespace Coding4Fun.Etl.Build.Services.IO;

internal class InMemoryFileProvider : IFileSystemProvider
{
    private readonly Dictionary<string, byte[]> _files = [];

    /// <inheritdoc />
    public void CreateDirectory(string path)
    {
    }

    /// <inheritdoc />
    public string ReadAllText(string path) => Encoding.UTF8.GetString(_files[path]);

    /// <inheritdoc />
    public void WriteAllText(string path, string? contents) => _files.Add(path, Encoding.UTF8.GetBytes(contents ?? ""));
}