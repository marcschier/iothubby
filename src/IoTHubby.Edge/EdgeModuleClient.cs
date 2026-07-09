// Copyright (c) marcschier. Licensed under the MIT License.

#if NET8_0_OR_GREATER
using System.Diagnostics.CodeAnalysis;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using IoTHubby.Edge.Workload;
#endif

namespace IoTHubby.Edge;

/// <summary>
/// Bootstraps an <see cref="IoTHubModuleClient"/> for a module running inside the Azure IoT Edge
/// runtime, using the injected environment variables and the Edge Workload API for SAS signing and
/// the CA trust bundle. Requires .NET 8 or later.
/// </summary>
public static class EdgeModuleClient
{
    /// <summary>
    /// Creates and configures a module client from the IoT Edge runtime environment
    /// (<c>IOTEDGE_*</c> variables) or, for local development, an <c>EdgeHubConnectionString</c>.
    /// The returned client is not yet connected — call
    /// <see cref="IoTHubModuleClient.ConnectAsync(CancellationToken)"/>.
    /// </summary>
    /// <param name="configure">Optional hook to tune client options.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    public static Task<IoTHubModuleClient> CreateFromEnvironmentAsync(
        Action<IoTHubClientOptions>? configure = null, CancellationToken cancellationToken = default)
        => CreateFromEnvironmentAsync(configure, workloadHandler: null, cancellationToken);

    /// <summary>
    /// Test seam: as <see cref="CreateFromEnvironmentAsync(Action{IoTHubClientOptions}, CancellationToken)"/>
    /// but allows injecting an <see cref="HttpMessageHandler"/> for the Edge Workload API so the
    /// bootstrap path can run without a live edge runtime.
    /// </summary>
    internal static async Task<IoTHubModuleClient> CreateFromEnvironmentAsync(
        Action<IoTHubClientOptions>? configure,
        HttpMessageHandler? workloadHandler,
        CancellationToken cancellationToken = default)
    {
        var debugConnectionString = Environment.GetEnvironmentVariable("EdgeHubConnectionString")
            ?? Environment.GetEnvironmentVariable("IotHubConnectionString");
        if (!string.IsNullOrEmpty(debugConnectionString))
        {
            // Local development outside the edge runtime: a full module connection string is provided.
            return IoTHubModuleClient.CreateFromConnectionString(debugConnectionString!, configure);
        }

#if NET8_0_OR_GREATER
        return await CreateFromWorkloadAsync(configure, workloadHandler, cancellationToken).ConfigureAwait(false);
#else
        _ = workloadHandler;
        await Task.CompletedTask.ConfigureAwait(false);
        throw new PlatformNotSupportedException(
            "Bootstrapping an edge module from the Workload API requires .NET 8 or later.");
#endif
    }

#if NET8_0_OR_GREATER
    private static async Task<IoTHubModuleClient> CreateFromWorkloadAsync(
        Action<IoTHubClientOptions>? configure,
        HttpMessageHandler? workloadHandler,
        CancellationToken cancellationToken)
    {
        var iotHubHost = Require("IOTEDGE_IOTHUBHOSTNAME");
        var gatewayHost = Environment.GetEnvironmentVariable("IOTEDGE_GATEWAYHOSTNAME");
        var deviceId = Require("IOTEDGE_DEVICEID");
        var moduleId = Require("IOTEDGE_MODULEID");
        var generationId = Require("IOTEDGE_MODULEGENERATIONID");

        var workload = WorkloadApiClient.FromEnvironment(workloadHandler);

        var options = new IoTHubClientOptions();
        configure?.Invoke(options);

        var topics = new IoTHubTopics(deviceId, moduleId);
        var username = topics.BuildUsername(iotHubHost, options.ProductInfo, options.ModelId);
        var resourceUri = SasTokenGenerator.ModuleResourceUri(iotHubHost, deviceId, moduleId);

        options.CredentialsProviderOverride = new WorkloadSasCredentialsProvider(
            username, resourceUri, workload, moduleId, generationId,
            options.SasTokenLifetime, options.SasTokenRenewalFraction);

        // Trust the edge gateway's CA via the workload trust bundle.
        var trustBundle = await workload.GetTrustBundleCertificatesAsync(cancellationToken).ConfigureAwait(false);
        var userConfigureTls = options.ConfigureTls;
        options.ConfigureTls = tls =>
        {
            ApplyTrustBundle(tls, trustBundle);
            userConfigureTls?.Invoke(tls);
        };

        var connection = IoTHubConnectionString.ForEdgeModule(iotHubHost, deviceId, moduleId, gatewayHost);
        return IoTHubModuleClient.Create(connection, options);
    }

    [ExcludeFromCodeCoverage(Justification =
        "Runs only during a live TLS handshake against the edge gateway; not exercisable in-process.")]
    private static void ApplyTrustBundle(SslClientAuthenticationOptions tls, X509Certificate2Collection trustBundle)
    {
        tls.RemoteCertificateValidationCallback = (_, certificate, _, errors) =>
        {
            if (errors == SslPolicyErrors.None)
            {
                return true;
            }
            if (certificate is null)
            {
                return false;
            }

            using var chain = new X509Chain();
            chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
            chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
            chain.ChainPolicy.CustomTrustStore.AddRange(trustBundle);
            return chain.Build(new X509Certificate2(certificate));
        };
    }

    private static string Require(string name)
        => Environment.GetEnvironmentVariable(name)
            ?? throw new InvalidOperationException($"Required IoT Edge environment variable '{name}' is not set.");
#endif
}
