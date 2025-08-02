namespace Coding4Fun.Etl.Build.Services.IO;

/// <summary>
/// A file system provider.
/// </summary>
internal interface IFileSystemProvider
{
    /// <summary>
    /// Creates a new file, writes the specified string to the file, and then closes the file. If the target file already exists, it is overwritten.
    /// </summary>
    void WriteAllText(string path, string? contents);

    /// <summary>
    /// Creates all directories and subdirectories in the specified path unless they already exist.
    /// </summary>
    void CreateDirectory(string path);

    /// <summary>
    /// Opens a text file, reads all the text in the file, and then closes the file.
    /// </summary>
    string ReadAllText(string path);
}