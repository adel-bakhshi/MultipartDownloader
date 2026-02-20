namespace MultipartDownloader.Core.CustomExceptions;

public class DownloadException : Exception
{
    #region Properties

    public int? ErrorCode { get; set; }

    #endregion Properties

    public DownloadException() : base()
    {
    }

    public DownloadException(int? errorCode) : this(errorCode, null, null)
    {
    }

    public DownloadException(string? message) : this(null, message, null)
    {
    }

    public DownloadException(int? errorCode, string? message) : this(errorCode, message, null)
    {
    }

    public DownloadException(int? errorCode, string? message, Exception? innerException) : base(message, innerException)
    {
        ErrorCode = errorCode;
    }
}