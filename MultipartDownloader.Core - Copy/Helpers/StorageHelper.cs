using MultipartDownloader.Core.Exceptions;

namespace MultipartDownloader.Core.Helpers;

public static class StorageHelper
{
    public static void ReserveStorageBeforeDownload(string? filePath, long fileSize)
    {
        // Make sure file path has value
        if (string.IsNullOrEmpty(filePath))
            throw new DownloadFileException();

        // Get available free space
        var availableFreeSpace = GetAvailableFreeSpace(Path.GetDirectoryName(filePath)!);
        if (availableFreeSpace < fileSize)
            throw new DownloadFileException($"Not enough space in the disk. Required: {fileSize} byte(s), Available: {availableFreeSpace} byte(s).");

        // Create an empty file with specific size
        using var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
        fileStream.SetLength(fileSize);
        fileStream.Flush();
    }

    public static void CreateOutputDirectory(string? directoryPath)
    {
        // Make sure file path has value
        if (string.IsNullOrEmpty(directoryPath))
            throw new DownloadFileException();

        // Create directory if not exists
        if (!Directory.Exists(directoryPath))
            Directory.CreateDirectory(directoryPath);
    }

    public static void CreateOutputDirectoryByFilePath(string? filePath)
    {
        // Make sure file path has value
        if (string.IsNullOrEmpty(filePath))
            throw new DownloadFileException();

        // Find directory path
        var directoryPath = Path.GetDirectoryName(filePath);
        // Make sure directory path is not null or empty
        if (string.IsNullOrEmpty(directoryPath))
            throw new DownloadFileException();

        CreateOutputDirectory(directoryPath);
    }

    public static void RemoveDirectory(string? directoryPath)
    {
        // Make sure file path has value
        if (string.IsNullOrEmpty(directoryPath))
            return;

        // Create directory if not exists
        if (Directory.Exists(directoryPath))
            Directory.Delete(directoryPath, recursive: true);
    }

    public static FileStream CreateDownloadPartFile(string? filePath)
    {
        // Make sure file path has value
        if (string.IsNullOrEmpty(filePath))
            throw new DownloadFileException();

        // Remove previous file
        if (File.Exists(filePath))
            File.Delete(filePath);

        return new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
    }

    #region Helpers

    private static long GetAvailableFreeSpace(string path)
    {
        var driveInfo = new DriveInfo(Path.GetPathRoot(path)!);
        return driveInfo == null ? -1 : driveInfo.AvailableFreeSpace;
    }

    #endregion Helpers
}