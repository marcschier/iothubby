// Copyright (c) marcschier. Licensed under the MIT License.

#if NET8_0_OR_GREATER
using System.IO.Pipes;
using System.Net.Sockets;
#endif
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;

namespace IoTHubby.Edge.Workload;

/// <summary>
/// Calls the local Azure IoT Edge Workload API for module signing and trust-bundle retrieval.
/// </summary>
public sealed class WorkloadApiClient : IDisposable
{
    private const string WorkloadUriEnvironmentVariable = "IOTEDGE_WORKLOADURI";
    private const string ApiVersionEnvironmentVariable = "IOTEDGE_APIVERSION";

    private readonly string _apiVersion;
    private readonly HttpClient _client;
    private readonly Uri _requestBaseUri;

    /// <summary>
    /// Initializes a new instance of the <see cref="WorkloadApiClient"/> class.
    /// </summary>
    /// <param name="workloadUri">The workload API URI from the IoT Edge runtime environment.</param>
    /// <param name="apiVersion">The workload API version query value.</param>
    /// <param name="handler">
    /// Optional HTTP message handler. When omitted, a platform transport for the workload URI is
    /// created; injecting a handler is intended for tests.
    /// </param>
    public WorkloadApiClient(Uri workloadUri, string apiVersion, HttpMessageHandler? handler = null)
    {
        ThrowIfNull(workloadUri, nameof(workloadUri));

        if (string.IsNullOrEmpty(apiVersion))
        {
            throw new ArgumentException("The workload API version is required.", nameof(apiVersion));
        }

        _apiVersion = apiVersion;
        _requestBaseUri = CreateRequestBaseUri(workloadUri);
        _client = new HttpClient(handler ?? CreateTransportHandler(workloadUri), disposeHandler: true);
    }

    /// <summary>
    /// Creates a workload API client from the IoT Edge workload environment variables.
    /// </summary>
    /// <param name="handler">Optional HTTP message handler to inject, primarily for tests.</param>
    /// <returns>A workload API client configured from the current process environment.</returns>
    public static WorkloadApiClient FromEnvironment(HttpMessageHandler? handler = null)
    {
        var workloadUriText = Environment.GetEnvironmentVariable(WorkloadUriEnvironmentVariable);
        if (string.IsNullOrEmpty(workloadUriText))
        {
            throw new InvalidOperationException($"{WorkloadUriEnvironmentVariable} is not set.");
        }

        var apiVersion = Environment.GetEnvironmentVariable(ApiVersionEnvironmentVariable);
        if (string.IsNullOrEmpty(apiVersion))
        {
            throw new InvalidOperationException($"{ApiVersionEnvironmentVariable} is not set.");
        }

        if (!TryCreateWorkloadUri(workloadUriText, out var workloadUri))
        {
            throw new InvalidOperationException($"{WorkloadUriEnvironmentVariable} is not a valid URI.");
        }

        return new WorkloadApiClient(workloadUri!, apiVersion, handler);
    }

    /// <summary>
    /// Asks the Edge Workload API to sign data with the module's primary symmetric key.
    /// </summary>
    /// <param name="moduleId">The IoT Edge module identifier.</param>
    /// <param name="generationId">The module identity generation identifier.</param>
    /// <param name="data">The data to HMAC-sign.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The raw signature digest returned by the workload API.</returns>
    public async Task<byte[]> SignAsync(
        string moduleId,
        string generationId,
        byte[] data,
        CancellationToken ct)
    {
        if (string.IsNullOrEmpty(moduleId))
        {
            throw new ArgumentException("The module ID is required.", nameof(moduleId));
        }
        if (string.IsNullOrEmpty(generationId))
        {
            throw new ArgumentException("The generation ID is required.", nameof(generationId));
        }
        ThrowIfNull(data, nameof(data));

        var requestBody = new WorkloadSignRequest("primary", "HMACSHA256", Convert.ToBase64String(data));
        var requestJson = JsonSerializer.Serialize(requestBody, WorkloadJsonContext.Default.WorkloadSignRequest);
        var requestPath =
            $"modules/{Uri.EscapeDataString(moduleId)}/genid/{Uri.EscapeDataString(generationId)}/sign";

        using var request = new HttpRequestMessage(HttpMethod.Post, CreateRequestUri(requestPath))
        {
            Content = new StringContent(requestJson, Encoding.UTF8, "application/json"),
        };

        using var response = await _client.SendAsync(request, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var stream = await ReadAsStreamAsync(response.Content, ct).ConfigureAwait(false);
        var signResponse = await JsonSerializer.DeserializeAsync(
            stream,
            WorkloadJsonContext.Default.WorkloadSignResponse,
            ct).ConfigureAwait(false);

        if (signResponse is null || string.IsNullOrEmpty(signResponse.Digest))
        {
            throw new InvalidOperationException("The workload sign response did not contain a digest.");
        }

        return Convert.FromBase64String(signResponse.Digest);
    }

    /// <summary>
    /// Gets the IoT Edge trust bundle PEM from the workload API.
    /// </summary>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The trust bundle PEM string returned by the workload API.</returns>
    public async Task<string> GetTrustBundleAsync(CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, CreateRequestUri("trust-bundle"));
        using var response = await _client.SendAsync(request, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var stream = await ReadAsStreamAsync(response.Content, ct).ConfigureAwait(false);
        var trustBundleResponse = await JsonSerializer.DeserializeAsync(
            stream,
            WorkloadJsonContext.Default.WorkloadTrustBundleResponse,
            ct).ConfigureAwait(false);

        if (trustBundleResponse is null || string.IsNullOrEmpty(trustBundleResponse.Certificate))
        {
            throw new InvalidOperationException("The workload trust-bundle response did not contain a certificate.");
        }

        return trustBundleResponse.Certificate;
    }

    /// <summary>
    /// Gets and parses the IoT Edge trust bundle certificates.
    /// </summary>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The certificates parsed from the trust bundle PEM.</returns>
    /// <exception cref="PlatformNotSupportedException">
    /// Thrown on target frameworks that do not provide PEM certificate parsing APIs.
    /// </exception>
    public Task<X509Certificate2Collection> GetTrustBundleCertificatesAsync(CancellationToken ct)
    {
#if NET8_0_OR_GREATER
        return GetTrustBundleCertificatesCoreAsync(ct);
#else
        _ = _client;
        _ = ct;
        throw new PlatformNotSupportedException("PEM certificate parsing requires .NET 8 or later.");
#endif
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _client.Dispose();
    }

    private Uri CreateRequestUri(string relativePath)
        => new(_requestBaseUri, $"{relativePath}?api-version={Uri.EscapeDataString(_apiVersion)}");

    private static Uri CreateRequestBaseUri(Uri workloadUri)
    {
        if (IsTcpUri(workloadUri))
        {
            return EnsureTrailingSlash(workloadUri);
        }

        return new Uri("http://localhost/");
    }

    private static Uri EnsureTrailingSlash(Uri uri)
    {
        var pathUri = new Uri(uri.GetLeftPart(UriPartial.Path));
        if (pathUri.AbsoluteUri[pathUri.AbsoluteUri.Length - 1] == '/')
        {
            return pathUri;
        }

        return new Uri(pathUri.AbsoluteUri + "/");
    }

    private static bool IsTcpUri(Uri uri)
        => uri.IsAbsoluteUri
            && (uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                || uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase));

    private static bool IsNamedPipeUri(Uri uri)
        => uri.IsAbsoluteUri && uri.Scheme.Equals("npipe", StringComparison.OrdinalIgnoreCase);

    private static bool TryCreateWorkloadUri(string workloadUriText, out Uri? workloadUri)
    {
        if (workloadUriText.Length > 0 && workloadUriText[0] == '/')
        {
            workloadUri = new Uri("unix://" + workloadUriText);
            return true;
        }

        if (Uri.TryCreate(workloadUriText, UriKind.Absolute, out workloadUri))
        {
            return true;
        }

        return false;
    }

    private static void ThrowIfNull(object? value, string paramName)
    {
#if NET8_0_OR_GREATER
        ArgumentNullException.ThrowIfNull(value, paramName);
#else
        if (value is null)
        {
            throw new ArgumentNullException(paramName);
        }
#endif
    }

#if NET8_0_OR_GREATER
    private async Task<X509Certificate2Collection> GetTrustBundleCertificatesCoreAsync(
        CancellationToken ct)
    {
        var pem = await GetTrustBundlePemForParsingAsync(ct).ConfigureAwait(false);
        var certificates = new X509Certificate2Collection();
        const string endCertificateMarker = "-----END CERTIFICATE-----";
        var nextBlockStart = 0;

        while (nextBlockStart < pem.Length)
        {
            var blockEnd = pem.IndexOf(
                endCertificateMarker,
                nextBlockStart,
                StringComparison.Ordinal);
            if (blockEnd < 0)
            {
                break;
            }

            blockEnd += endCertificateMarker.Length;
            var certificatePem = pem.Substring(nextBlockStart, blockEnd - nextBlockStart).Trim();
            if (certificatePem.Length > 0)
            {
                certificates.Add(X509Certificate2.CreateFromPem(certificatePem));
            }

            nextBlockStart = blockEnd;
        }

        return certificates;
    }

    [ExcludeFromCodeCoverage(Justification =
        "Builds a real UDS/named-pipe HTTP transport; exercised only against a live edge runtime.")]
    private static HttpMessageHandler CreateTransportHandler(Uri workloadUri)
    {
        if (IsTcpUri(workloadUri))
        {
            return new HttpClientHandler();
        }

        if (!IsNamedPipeUri(workloadUri) && !IsUnixSocketUri(workloadUri))
        {
            throw new NotSupportedException($"The workload URI scheme '{workloadUri.Scheme}' is not supported.");
        }

        return new SocketsHttpHandler
        {
            ConnectCallback = (_, cancellationToken) => ConnectAsync(workloadUri, cancellationToken),
        };
    }

    [ExcludeFromCodeCoverage(Justification =
        "Opens a real UDS/named-pipe connection; exercised only against a live edge runtime.")]
    private static async ValueTask<Stream> ConnectAsync(Uri workloadUri, CancellationToken ct)
    {
        if (IsNamedPipeUri(workloadUri))
        {
            return await ConnectNamedPipeAsync(workloadUri, ct).ConfigureAwait(false);
        }

        return await ConnectUnixSocketAsync(GetUnixSocketPath(workloadUri), ct).ConfigureAwait(false);
    }

    [ExcludeFromCodeCoverage(Justification =
        "Opens a real Unix-domain socket; exercised only against a live edge runtime.")]
    private static async ValueTask<Stream> ConnectUnixSocketAsync(string socketPath, CancellationToken ct)
    {
        var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        try
        {
            await socket.ConnectAsync(new UnixDomainSocketEndPoint(socketPath), ct).ConfigureAwait(false);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    [ExcludeFromCodeCoverage(Justification =
        "Opens a real named pipe; exercised only against a live edge runtime.")]
    private static async ValueTask<Stream> ConnectNamedPipeAsync(Uri workloadUri, CancellationToken ct)
    {
        var (serverName, pipeName) = GetNamedPipeParts(workloadUri);
        var stream = new NamedPipeClientStream(
            serverName,
            pipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous);
        try
        {
            await stream.ConnectAsync(ct).ConfigureAwait(false);
            return stream;
        }
        catch
        {
            await stream.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    [ExcludeFromCodeCoverage(Justification = "Parses the UDS workload URI; reached only from the real transport.")]
    private static string GetUnixSocketPath(Uri uri)
    {
        if (!uri.IsAbsoluteUri)
        {
            return uri.OriginalString;
        }

        var path = Uri.UnescapeDataString(uri.AbsolutePath);
        var host = Uri.UnescapeDataString(uri.Host);

        if (string.IsNullOrEmpty(path) || path == "/")
        {
            return host;
        }

        if (!string.IsNullOrEmpty(host) && host[0] == '/')
        {
            return host + path;
        }

        return path;
    }

    [ExcludeFromCodeCoverage(Justification =
        "Parses the named-pipe workload URI; reached only from the real transport.")]
    private static (string ServerName, string PipeName) GetNamedPipeParts(Uri uri)
    {
        var serverName = string.IsNullOrEmpty(uri.Host) ? "." : uri.Host;
        if (serverName.Equals("localhost", StringComparison.OrdinalIgnoreCase))
        {
            serverName = ".";
        }

        var pipeName = Uri.UnescapeDataString(uri.AbsolutePath).TrimStart('/');
        const string pipePrefix = "pipe/";
        if (pipeName.StartsWith(pipePrefix, StringComparison.OrdinalIgnoreCase))
        {
            pipeName = pipeName.Substring(pipePrefix.Length);
        }

        pipeName = pipeName.Replace('/', '\\');
        if (string.IsNullOrEmpty(pipeName))
        {
            throw new ArgumentException("The named pipe workload URI does not contain a pipe name.", nameof(uri));
        }

        return (serverName, pipeName);
    }

    private static bool IsUnixSocketUri(Uri uri)
        => uri.IsAbsoluteUri && uri.Scheme.Equals("unix", StringComparison.OrdinalIgnoreCase)
            || uri.OriginalString.Length > 0 && uri.OriginalString[0] == '/';

    private async Task<string> GetTrustBundlePemForParsingAsync(CancellationToken ct)
        => await GetTrustBundleAsync(ct).ConfigureAwait(false);
#else
    // netstandard fallback: the Edge Workload API requires .NET 8 or later; this always throws.
    [ExcludeFromCodeCoverage]
    private static HttpMessageHandler CreateTransportHandler(Uri workloadUri)
    {
        _ = workloadUri;
        throw new PlatformNotSupportedException("The Edge Workload API requires .NET 8 or later.");
    }
#endif

    private static async Task<Stream> ReadAsStreamAsync(HttpContent content, CancellationToken ct)
    {
#if NET8_0_OR_GREATER
        return await content.ReadAsStreamAsync(ct).ConfigureAwait(false);
#else
        _ = ct;
        return await content.ReadAsStreamAsync().ConfigureAwait(false);
#endif
    }
}
