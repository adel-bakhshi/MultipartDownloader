namespace MultipartDownloader.Core.Enums;

public enum FileExistPolicy : byte
{
    IgnoreDownload = 0,
    Delete = 1,
    Exception = 2,
    Rename = 3
}