using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;

namespace MultipartDownloader.Core.Extensions.Helpers;

internal static class ExceptionHelper
{
    internal static bool IsRedirectError(this Exception error)
    {
        return error is HttpRequestException { StatusCode: not null } responseException && responseException.StatusCode.Value.IsRedirectStatus();
    }

    internal static bool IsRedirectStatus(this HttpStatusCode statusCode)
    {
        return statusCode is
            HttpStatusCode.Moved or
            HttpStatusCode.Redirect or
            HttpStatusCode.RedirectMethod or
            HttpStatusCode.TemporaryRedirect or
            HttpStatusCode.PermanentRedirect;
    }

    internal static bool IsRequestedRangeNotSatisfiable(this Exception error)
    {
        return error is HttpRequestException { StatusCode: HttpStatusCode.RequestedRangeNotSatisfiable };
    }

    internal static bool IsMomentumError(this Exception error)
    {
        return error switch
        {
            ObjectDisposedException => true, // when stream reader cancel/timeout occurred
            TaskCanceledException => true, // when cancel/timeout occurred
            SocketException or WebException { Status: WebExceptionStatus.Timeout } => true, // acceptable errors for retry
            HttpRequestException { StatusCode: HttpStatusCode.InternalServerError or HttpStatusCode.BadGateway } => false,
            HttpRequestException
            {
                StatusCode: HttpStatusCode.Ambiguous
                or HttpStatusCode.TooManyRequests
                or HttpStatusCode.ServiceUnavailable
                or HttpStatusCode.GatewayTimeout
                or HttpStatusCode.RequestTimeout
                or HttpStatusCode.Moved
                or HttpStatusCode.Redirect
                or HttpStatusCode.RedirectMethod
                or HttpStatusCode.TemporaryRedirect
                or HttpStatusCode.PermanentRedirect
            } => true,
            _ => error.HasSource("System.Net.Http", "System.Net.Sockets", "System.Net.Security")
        };
    }

    internal static bool HasTypeOf(this Exception error, params Type[] types)
    {
        Exception? innerException = error;
        while (innerException != null)
        {
            if (types.Any(type => innerException.GetType() == type))
                return true;

            innerException = innerException.InnerException;
        }

        return false;
    }

    internal static bool HasSource(this Exception error, params string[] sources)
    {
        Exception? innerException = error;
        while (innerException != null)
        {
            if (sources.Any(src => src.Equals(innerException.Source, StringComparison.OrdinalIgnoreCase)))
                return true;

            innerException = innerException.InnerException;
        }

        return false;
    }

    /// <summary>
    /// Sometime a server get certificate validation error
    /// https://stackoverflow.com/questions/777607/the-remote-certificate-is-invalid-according-to-the-validation-procedure-using
    /// </summary>
    internal static bool CertificateValidationCallBack(object sender,
        X509Certificate? certificate,
        X509Chain? chain,
        SslPolicyErrors sslPolicyErrors)
    {
        // If the certificate is a valid, signed certificate, return true.
        if (sslPolicyErrors == SslPolicyErrors.None)
            return true;

        // If there are errors in the certificate chain, look at each error to determine the cause.
        if ((sslPolicyErrors & SslPolicyErrors.RemoteCertificateChainErrors) != 0)
        {
            if (chain?.ChainStatus != null)
            {
                foreach (X509ChainStatus status in chain.ChainStatus)
                {
                    if (status.Status == X509ChainStatusFlags.NotTimeValid)
                    {
                        // If the error is for certificate expiration then it can be continued
                        return true;
                    }

                    if (status.Status == X509ChainStatusFlags.UntrustedRoot && certificate.Subject == certificate.Issuer)
                    {
                        // Self-signed certificates with an untrusted root are valid.
                    }
                    else if (status.Status != X509ChainStatusFlags.NoError)
                    {
                        // If there are any other errors in the certificate chain, the certificate is invalid,
                        // so the method returns false.
                        return false;
                    }
                }
            }

            // When processing reaches this line, the only errors in the certificate chain are
            // untrusted root errors for self-signed certificates. These certificates are valid
            // for default Exchange server installations, so return true.
            return true;
        }

        // In all other cases, return false.
        return false;
    }
}