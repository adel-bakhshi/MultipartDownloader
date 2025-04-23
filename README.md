# MultipartDownloader

## Overview

MultipartDownloader is a modified version of bezzad/Downloader, a fast and reliable multipart downloading library for .NET applications. This project was developed to address specific issues encountered in the original `bezzad/Downloader`, particularly with large file downloads, where the downloaded file size did not always match the expected size, yet the download was considered complete.

The primary goal of MultipartDownloader is to provide a more robust and reliable downloading mechanism by implementing a different algorithm. Instead of writing downloaded data directly to the final file during the download process, this project downloads each file chunk into a separate temporary file. Once all chunks are successfully downloaded, they are merged to create the final file. This approach minimizes the risk of file corruption, as multiple threads do not write to a single file simultaneously. Additionally, the size of each temporary chunk file is verified against the expected size, and any mismatch triggers a re-download of that chunk.

## Features

- **Chunk-Based Downloading**: Downloads files in parts (chunks), with each chunk stored in a separate temporary file to prevent concurrent writing issues.
- **Size Verification**: Ensures the size of each downloaded chunk matches the expected size, re-downloading the chunk if necessary.
- **Merge Operation**: Combines all chunks into the final file after successful download, with dedicated events for tracking merge progress.
- **Enhanced Memory Management**: Uses `MemoryBufferedStream` to control memory usage, limiting the amount of data held in RAM based on the `MaximumMemoryBufferBytes` property divided by the number of chunks.
- **Event-Driven Design**:
  - `MergeStarted`: Triggered when the merge operation begins.
  - `MergeProgressChanged`: Reports the progress percentage of the merge operation.
  - `ChunkDownloadRestarted`: Fired when a chunk download is restarted due to a size mismatch or other issues.
- **Resumable Downloads**: Supports resuming downloads by synchronizing `FilePosition` and `ChunkFilePath` properties in the `Chunk` class, allowing continuation from existing temporary files.
- **Temporary File Management**: Stores chunk files in a user-specified directory defined by the `ChunkFilesOutputDirectory` property in `DownloadConfiguration`.

## Changes from bezzad/Downloader

MultipartDownloader introduces several improvements and modifications over the original `bezzad/Downloader`:

- **Chunk Storage**: Each chunk is saved to a separate temporary file instead of writing directly to the final file, reducing the risk of file corruption.
- **Merge Operation**: A dedicated merge step combines chunks after all downloads are complete, with progress reported via `MergeStarted` and `MergeProgressChanged` events.
- **Memory Management**: Replaced the original memory buffer logic with `MemoryBufferedStream`, which uses a queue of `Packet` objects to manage data in memory until the `MaximumMemoryBufferBytes` limit is reached, then writes to disk.
- **Chunk Verification**: Verifies the size of each chunk file against the expected size, restarting the download if they do not match.
- **Removed Classes**: Eliminated `ConcurrentPacketBuffer` and `ConcurrentStream` as they were no longer needed due to the new chunk-based approach.
- **Updated Packet Usage**: The `Packet` class is still used but now stored in a queue within `MemoryBufferedStream` for sequential writing to disk.
- **New Properties**:
  - `ChunkFilesOutputDirectory` in `DownloadConfiguration` to specify the temporary file storage location.
  - `FilePosition` and `ChunkFilePath` in `Chunk` for managing temporary files and resuming downloads.
- **Removed Interface**: The `ISizeableObject` interface was removed as it was no longer required.
- **Memory-Only Downloads**: Support for downloading directly to memory (as in `bezzad/Downloader`) was removed; all downloads are now disk-based.
- **HttpWebRequest**: Continues to use `HttpWebRequest` for HTTP requests, as in the original project.

## Requirements

- .NET 8.0 or .NET 9.0
- `System.Net.Http` for HTTP requests
- `Newtonsoft.Json` for package serialization
- A valid directory path for the `ChunkFilesOutputDirectory` property in `DownloadConfiguration`

## Installation

1. Clone the repository:

   ```bash
   git clone https://github.com/adel-bakhshi/MultipartDownloader.git
   ```

2. Build the project:

   ```bash
   dotnet build
   ```

3. Run the application:

   ```bash
   dotnet run
   ```

## Usage

The usage of MultipartDownloader is similar to `bezzad/Downloader`, with the key requirement that the `ChunkFilesOutputDirectory` property must be set to a valid directory path in `DownloadConfiguration`.

```csharp
using MultipartDownloader.Core;

var config = new DownloadConfiguration
{
    ChunkCount = 4, // Number of parallel downloads
    MaxTryAgainOnFailover = 3, // Retry attempts
    ParallelDownload = true, // Enable parallel downloads
    ChunkFilesOutputDirectory = "path/to/temp/files" // Required: Directory for temporary chunk files
};

var downloader = new DownloadService(config);
downloader.DownloadStarted += (s, e) => Console.WriteLine($"Started downloading {e.FileName}");
downloader.DownloadProgressChanged += (s, e) => Console.WriteLine($"Progress: {e.ProgressPercentage}%");
downloader.ChunkDownloadRestarted += (s, e) => Console.WriteLine($"Chunk {e.ChunkId} restarted");
downloader.MergeStarted += (s, e) => Console.WriteLine("Merging chunks started");
downloader.MergeProgressChanged += (s, e) => Console.WriteLine($"Merge progress: {e.ProgressPercentage}%");
downloader.DownloadFileCompleted += (s, e) => Console.WriteLine(e.Error == null ? "Download completed!" : $"Download failed: {e.Error.Message}");

string url = "https://example.com/largefile.zip";
string filePath = "largefile.zip";
await downloader.DownloadFileTaskAsync(url, filePath);
```

## Credits

MultipartDownloader is built upon bezzad/Downloader. Special thanks to Behzad Khosravifar for creating the original library, which served as the foundation for this project.

## License

This project is licensed under the MIT License, consistent with the original `bezzad/Downloader` project. See the LICENSE file for details.
