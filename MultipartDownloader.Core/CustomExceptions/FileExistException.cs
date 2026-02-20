namespace MultipartDownloader.Core.CustomExceptions;

public class FileExistException(string filePath) : IOException
{
    public string Name { get; } = filePath;
}