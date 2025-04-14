using System.Net;
using System.Net.Http.Headers;

namespace MultipartDownloader.Core;

public class DownloadOptions
{
    #region Private fields

    private long _maximumBytesPerSecond;

    #endregion Private fields

    #region Properties

    public string Url { get; set; }
    public bool SupportsContentLength { get; internal set; }
    public bool SupportsAcceptRanges { get; internal set; }
    public bool SupportsContentRange { get; internal set; }
    public long TotalFileSize { get; internal set; }
    public List<DownloadPart> DownloadParts { get; } = [];
    public string FilePath { get; set; }
    public string DownloadPartOutputDirectory { get; set; }
    public int StreamBufferSize { get; set; } = 8192; // 8 Kilobyte

    public int CountOfActiveDownloadParts => DownloadParts.Count(part => part.IsActive);
    public long MaximumBytesPerSecondsPerDownloadPart => _maximumBytesPerSecond <= 0 ? long.MaxValue : _maximumBytesPerSecond / CountOfActiveDownloadParts;

    #endregion Properties

    public DownloadOptions(
        string url,
        string filePath,
        string downloadPartOutputDirectory,
        long maximumBytesPerSecond)
    {
        Url = url.Trim();
        FilePath = filePath.Trim();
        DownloadPartOutputDirectory = downloadPartOutputDirectory.Trim();

        _maximumBytesPerSecond = maximumBytesPerSecond;
    }

    public void ChangeMaximumBytesPerSecond(long value)
    {
        _maximumBytesPerSecond = value;
    }

    public static bool CheckUrlValidation(string? url)
    {
        if (string.IsNullOrEmpty(url))
            return false;

        // For now only supports HTTP
        return Uri.TryCreate(url, UriKind.Absolute, out var uriResult) && (uriResult.Scheme == Uri.UriSchemeHttp || uriResult.Scheme == Uri.UriSchemeHttps);
    }

    public async Task GetRequestInfoAsync(string url)
    {
        // Send request for getting Content-Length
        var response = await SendHeadRequestAsync(url, false);
        // Calculate total file size and check Content-Length support
        SetContentLength(response);

        // Send request for getting Accept-Ranges and Content-Range
        response = await SendHeadRequestAsync(url, true);
        // Check Accept-Ranges support
        CheckSupportsAcceptRanges(response);
        // Check Content-Range support
        CheckSupportsContentRange(response);
    }

    #region Helpers

    private static async Task<HttpResponseMessage> SendHeadRequestAsync(string url, bool checkRange)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        using var httpClient = new HttpClient();
        // Add range for check range support
        if (checkRange)
            request.Headers.Range = new RangeHeaderValue(0, 0);

        // Only get headers from requested url
        var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        // Make sure response is successful
        response.EnsureSuccessStatusCode();

        return response;
    }

    private void SetContentLength(HttpResponseMessage response)
    {
        SupportsContentLength = response.Content.Headers.ContentLength != null;
        TotalFileSize = response.Content.Headers.ContentLength ?? 0;
    }

    private void CheckSupportsAcceptRanges(HttpResponseMessage response)
    {
        // Check for Accept-Ranges header
        if (response.Headers.Contains("Accept-Ranges"))
        {
            var acceptRanges = response.Headers.GetValues("Accept-Ranges");
            if (acceptRanges.Contains("bytes"))
            {
                SupportsAcceptRanges = true;
                return;
            }
        }

        // Some servers don't include Accept-Ranges but still support partial content.
        // If Range request succeeds with Partial Content status:
        if (response.StatusCode == HttpStatusCode.PartialContent)
            SupportsAcceptRanges = true;
    }

    private void CheckSupportsContentRange(HttpResponseMessage response)
    {
        // Check if Content-Range header is present in the response
        if (response.Content.Headers.Contains("Content-Range"))
        {
            var contentRange = response.Content.Headers.ContentRange;
            if (contentRange?.HasRange == true && contentRange.Length.HasValue)
            {
                SupportsContentRange = true;
                return;
            }
        }

        // If no Content-Range header or invalid format, assume no support
        SupportsContentRange = false;
    }

    #endregion Helpers
}