namespace MultipartDownloader.Core.Exceptions;

public class DownloadFileException : Exception
{
    public DownloadFileException()
    {
    }

    public DownloadFileException(string? message) : base(message)
    {
    }

    public DownloadFileException(string? message, Exception? innerException) : base(message, innerException)
    {
    }
}