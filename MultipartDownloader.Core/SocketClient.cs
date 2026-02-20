using MultipartDownloader.Core.Extensions.Helpers;
using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Security.Authentication;
using System.Text.RegularExpressions;

namespace MultipartDownloader.Core;

/// <summary>
/// Represents a client for making HTTP requests.
/// </summary>
public partial class SocketClient : IDisposable
{
    #region Constants

    private const string FilenameStartPointKey = "filename=";

    #endregion Constants

    #region Private Fields

    private readonly Regex _contentRangePattern = RangePatternRegex();
    private readonly ConcurrentDictionary<string, string> _responseHeaders = new(StringComparer.OrdinalIgnoreCase);
    private readonly HttpClient _client;
    private bool _isDisposed;
    private bool? _isSupportDownloadInRange;

    #endregion Private Fields

    /// <summary>
    /// Initializes a new instance of the <see cref="SocketClient"/> class with the specified configuration.
    /// </summary>
    public SocketClient(DownloadConfiguration config)
    {
        _client = GetHttpClientWithSocketHandler(config);
    }

    private static SocketsHttpHandler GetSocketsHttpHandler(RequestConfiguration config)
    {
        var handler = new SocketsHttpHandler()
        {
            AllowAutoRedirect = config.AllowAutoRedirect,
            MaxAutomaticRedirections = config.MaximumAutomaticRedirections,
            AutomaticDecompression = config.AutomaticDecompression,
            PreAuthenticate = config.PreAuthenticate,
            UseCookies = config.CookieContainer != null,
            UseProxy = config.Proxy != null,
            MaxConnectionsPerServer = 1000,
            PooledConnectionIdleTimeout = config.KeepAliveTimeout,
            PooledConnectionLifetime = Timeout.InfiniteTimeSpan,
            EnableMultipleHttp2Connections = true,
            ConnectTimeout = TimeSpan.FromMilliseconds(config.ConnectTimeout)
        };

        // Set up the SslClientAuthenticationOptions for custom certificate validation
        if (config.ClientCertificates.Count > 0)
            handler.SslOptions.ClientCertificates = config.ClientCertificates;

        handler.SslOptions.EnabledSslProtocols = SslProtocols.Tls13 | SslProtocols.Tls12;
        handler.SslOptions.RemoteCertificateValidationCallback = ExceptionHelper.CertificateValidationCallBack;

        // Configure keep-alive
        if (config.KeepAlive)
        {
            handler.KeepAlivePingTimeout = config.KeepAliveTimeout;
            handler.KeepAlivePingPolicy = HttpKeepAlivePingPolicy.WithActiveRequests;
        }

        // Configure credentials
        if (config.Credentials != null)
        {
            handler.Credentials = config.Credentials;
            handler.PreAuthenticate = config.PreAuthenticate;
        }

        // Configure cookies
        if (handler.UseCookies && config.CookieContainer != null)
            handler.CookieContainer = config.CookieContainer;

        // Configure proxy
        if (handler.UseProxy && config.Proxy != null)
            handler.Proxy = config.Proxy;

        // Add expect header
        if (!string.IsNullOrWhiteSpace(config.Expect))
            handler.Expect100ContinueTimeout = TimeSpan.FromSeconds(1);

        return handler;
    }

    private static HttpClient GetHttpClientWithSocketHandler(DownloadConfiguration downloadConfig)
    {
        var requestConfig = downloadConfig.RequestConfiguration;
        var handler = GetSocketsHttpHandler(requestConfig);
        HttpClient client = new(handler);

        // Apply HttpClientTimeout
        client.Timeout = TimeSpan.FromMilliseconds(downloadConfig.HttpClientTimeout);

        client.DefaultRequestHeaders.Clear();

        // Add standard headers
        AddHeaderIfNotEmpty(client.DefaultRequestHeaders, "Accept", requestConfig.Accept);
        AddHeaderIfNotEmpty(client.DefaultRequestHeaders, "User-Agent", requestConfig.UserAgent);

        client.DefaultRequestHeaders.Add("Connection", requestConfig.KeepAlive ? "keep-alive" : "close");
        client.DefaultRequestHeaders.CacheControl = new CacheControlHeaderValue { NoCache = true };

        // Add custom headers
        if (requestConfig.Headers?.Count > 0)
        {
            foreach (string key in requestConfig.Headers.AllKeys.Where(k => !string.IsNullOrWhiteSpace(k)).ToList())
                AddHeaderIfNotEmpty(client.DefaultRequestHeaders, key, requestConfig.Headers[key]);
        }

        // Add optional headers
        if (!string.IsNullOrWhiteSpace(requestConfig.Referer))
            client.DefaultRequestHeaders.Referrer = new Uri(requestConfig.Referer);

        if (!string.IsNullOrWhiteSpace(requestConfig.ContentType))
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue(requestConfig.ContentType));

        if (!string.IsNullOrWhiteSpace(requestConfig.TransferEncoding))
        {
            client.DefaultRequestHeaders.AcceptEncoding.Add(new StringWithQualityHeaderValue(requestConfig.TransferEncoding));
            client.DefaultRequestHeaders.TransferEncoding.Add(new TransferCodingHeaderValue(requestConfig.TransferEncoding));
        }

        AddHeaderIfNotEmpty(client.DefaultRequestHeaders, "Expect", requestConfig.Expect);
        return client;
    }

    /// <summary>
    /// Adds the header to the request headers if it is not empty.
    /// </summary>
    /// <param name="headers">The request headers.</param>
    /// <param name="key">The key of the header.</param>
    /// <param name="value">The value of the header.</param>
    private static void AddHeaderIfNotEmpty(HttpRequestHeaders headers, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            headers.Add(key, value);
    }

    /// <summary>
    /// Fetches the response headers asynchronously.
    /// </summary>
    /// <param name="request">The request of client</param>
    /// <param name="addRange">Indicates whether to add a range header to the request.</param>
    /// <param name="cancelToken">Cancel request token</param>
    private async Task FetchResponseHeadersAsync(Request request, bool addRange, CancellationToken cancelToken = default)
    {
        try
        {
            if (!_responseHeaders.IsEmpty)
                return;

            var requestMsg = request.GetRequest();
            if (addRange)
                requestMsg.Headers.Range = new RangeHeaderValue(0, 0);

            using var response = await SendRequestAsync(requestMsg, cancelToken).ConfigureAwait(false);
            if (!EnsureResponseAddressIsSameWithOrigin(request, response))
                await FetchResponseHeadersAsync(request, true, cancelToken).ConfigureAwait(false);
        }
        catch (HttpRequestException exp)
        {
            if (addRange && exp.IsRequestedRangeNotSatisfiable())
            {
                await FetchResponseHeadersAsync(request, false, cancelToken).ConfigureAwait(false);
            }
            else if (request.Configuration.AllowAutoRedirect &&
                exp.IsRedirectError() &&
                _responseHeaders.TryGetValue(HttpHeaderNames.Location, out var redirectedUrl) &&
                !string.IsNullOrWhiteSpace(redirectedUrl) &&
                !request.Address.ToString().Equals(redirectedUrl, StringComparison.OrdinalIgnoreCase))
            {
                request.Address = new Uri(redirectedUrl);
                await FetchResponseHeadersAsync(request, addRange, cancelToken).ConfigureAwait(false);
            }
            else
            {
                throw;
            }
        }
    }

    /// <summary>
    /// Ensures that the response address is the same as the original address.
    /// </summary>
    /// <param name="request">The request of client</param>
    /// <param name="response">The web response to check.</param>
    /// <returns>True if the response address is the same as the original address; otherwise, false.</returns>
    private static bool EnsureResponseAddressIsSameWithOrigin(Request request, HttpResponseMessage response)
    {
        var redirectUri = GetRedirectUrl(response);
        if (redirectUri != null && request.Address != redirectUri)
        {
            request.Address = redirectUri;
            return false;
        }

        return true;
    }

    /// <summary>
    /// Gets the redirect URL from the web response.
    /// </summary>
    /// <param name="response">The web response to get the redirect URL from.</param>
    /// <returns>The redirect URL.</returns>
    internal static Uri? GetRedirectUrl(HttpResponseMessage response)
    {
        // https://github.com/dotnet/runtime/issues/23264
        var redirectLocation = response?.Headers.Location;
        return redirectLocation ?? (response?.RequestMessage?.RequestUri);
    }

    /// <summary>
    /// Gets the file size asynchronously.
    /// </summary>
    /// <param name="request">The request instance containing the request information.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains the file size.</returns>
    public async ValueTask<long> GetFileSizeAsync(Request request)
    {
        if (await IsSupportDownloadInRangeAsync(request).ConfigureAwait(false))
            return GetTotalSizeFromContentRange(_responseHeaders.ToDictionary());

        return GetTotalSizeFromContentLength(_responseHeaders.ToDictionary());
    }

    /// <summary>
    /// Gets total size from content length.
    /// </summary>
    /// <param name="headers">The headers of the request.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    internal static long GetTotalSizeFromContentLength(Dictionary<string, string> headers)
    {
        // gets the total size from the content length headers.
        if (headers.TryGetValue(HttpHeaderNames.ContentLength, out var contentLengthText) &&
            long.TryParse(contentLengthText, out long contentLength))
        {
            return contentLength;
        }

        return -1L;
    }

    /// <summary>
    /// Throws an exception if the download in range is not supported.
    /// </summary>
    /// <param name="request">The request instance containing the request information.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public async ValueTask ThrowIfIsNotSupportDownloadInRangeAsync(Request request)
    {
        bool isSupport = await IsSupportDownloadInRangeAsync(request).ConfigureAwait(false);
        if (!isSupport)
            throw new NotSupportedException("The downloader cannot continue downloading because the network or server failed to download in range.");
    }

    /// <summary>
    /// Checks if the download in range is supported.
    /// </summary>
    /// <param name="request">The request instance containing the request information.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains a boolean indicating whether the download in range is supported.</returns>
    public async ValueTask<bool> IsSupportDownloadInRangeAsync(Request request)
    {
        if (_isSupportDownloadInRange.HasValue)
            return _isSupportDownloadInRange.Value;

        await FetchResponseHeadersAsync(request, addRange: true).ConfigureAwait(false);

        // https://www.w3.org/Protocols/rfc2616/rfc2616-sec14.html#sec14.5
        if (_responseHeaders.TryGetValue(HttpHeaderNames.AcceptRanges, out var acceptRanges) &&
            acceptRanges.Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            _isSupportDownloadInRange = false;
            return false;
        }

        // https://www.w3.org/Protocols/rfc2616/rfc2616-sec14.html#sec14.16
        if (_responseHeaders.TryGetValue(HttpHeaderNames.ContentRange, out var contentRange) && !string.IsNullOrWhiteSpace(contentRange))
        {
            _isSupportDownloadInRange = true;
            return true;
        }

        _isSupportDownloadInRange = false;
        return false;
    }

    /// <summary>
    /// Gets the total size from the content range headers.
    /// </summary>
    /// <param name="headers">The headers to get the total size from.</param>
    /// <returns>The total size of the content.</returns>
    internal long GetTotalSizeFromContentRange(Dictionary<string, string> headers)
    {
        if (headers.TryGetValue(HttpHeaderNames.ContentRange, out var contentRange) &&
            !string.IsNullOrWhiteSpace(contentRange) && _contentRangePattern.IsMatch(contentRange))
        {
            var match = _contentRangePattern.Match(contentRange);
            var size = match.Groups["size"].Value;

            return long.TryParse(size, out long totalSize) ? totalSize : -1L;
        }

        return -1L;
    }

    /// <summary>
    /// Gets the file name asynchronously.
    /// </summary>
    /// <param name="request">The request instance containing the request information.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains the file name.</returns>
    public async Task<string> SetRequestFileNameAsync(Request request)
    {
        if (!string.IsNullOrWhiteSpace(request.FileName))
            return request.FileName;

        var filename = await GetUrlDispositionFilenameAsync(request).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(filename))
        {
            filename = request.GetFileNameFromUrl();
            if (string.IsNullOrWhiteSpace(filename))
                filename = Guid.NewGuid().ToString("N");
        }

        request.FileName = filename;
        return filename;
    }

    /// <summary>
    /// Gets the file name from the URL disposition header asynchronously.
    /// </summary>
    /// <param name="request">The request instance containing the request information.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains the file name.</returns>
    internal async Task<string?> GetUrlDispositionFilenameAsync(Request request)
    {
        try
        {
            // Validate URL format
            if (request.Address?.IsAbsoluteUri != true ||
                !(request.Address.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase) ||
                request.Address.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase)) ||
                string.IsNullOrWhiteSpace(request.Address.Host) ||
                string.IsNullOrWhiteSpace(request.Address.AbsolutePath) ||
                request.Address.AbsolutePath == "/" ||
                request.Address.Segments.Length <= 1)
            {
                return null;
            }

            // Fetch headers if validations pass
            await FetchResponseHeadersAsync(request, true).ConfigureAwait(false);

            if (_responseHeaders.TryGetValue(HttpHeaderNames.ContentDisposition, out var disposition))
            {
                var filename = request.ToUnicode(disposition)?
                    .Split(';')
                    .FirstOrDefault(part => part.Trim().StartsWith(FilenameStartPointKey, StringComparison.OrdinalIgnoreCase))?
                    .Replace(FilenameStartPointKey, "")
                    .Replace("\"", "")
                    .Trim();

                return string.IsNullOrWhiteSpace(filename) ? null : filename;
            }
        }
        catch
        {
            // Ignore exceptions
        }

        return null;
    }

    /// <summary>
    /// Gets the response stream asynchronously.
    /// </summary>
    /// <param name="request">The request instance containing the request information.</param>
    /// <param name="cancelToken">The cancel token to cancel the operation.</param>
    /// <exception cref="HttpRequestException">Throw if the status code of the request was not satisfied.</exception>
    public async Task<HttpResponseMessage> SendRequestAsync(HttpRequestMessage request, CancellationToken cancelToken = default)
    {
        var response = await _client
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancelToken)
            .ConfigureAwait(false);

        // Copy all response headers to our dictionary
        _responseHeaders.Clear();
        foreach (var header in response.Headers)
            _responseHeaders.TryAdd(header.Key, header.Value.FirstOrDefault() ?? string.Empty);

        foreach (var header in response.Content.Headers)
            _responseHeaders.TryAdd(header.Key, header.Value.FirstOrDefault() ?? string.Empty);

        // throws an HttpRequestException error if the response status code isn't within the 200-299 range.
        response.EnsureSuccessStatusCode();
        return response;
    }

    /// <summary>
    /// Disposes of the resources (if any) used by the <see cref="SocketClient"/>.
    /// </summary>
    public void Dispose()
    {
        if (_isDisposed)
            return;

        _isDisposed = true;
        _client?.Dispose();
        GC.SuppressFinalize(this);
    }

    #region Generated regex

    [GeneratedRegex(@"bytes\s*((?<from>\d*)\s*-\s*(?<to>\d*)|\*)\s*\/\s*(?<size>\d+|\*)", RegexOptions.Compiled)]
    private static partial Regex RangePatternRegex();

    #endregion Generated regex
}