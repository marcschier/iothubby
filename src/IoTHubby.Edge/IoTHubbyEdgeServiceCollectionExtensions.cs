// Copyright (c) marcschier. Licensed under the MIT License.

using IoTHubby;
using IoTHubby.Edge;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Dependency-injection registration for an IoT Edge module client bootstrapped from the IoT Edge
/// runtime environment (the <c>IOTEDGE_*</c> variables and the Edge Workload API).
/// </summary>
public static class IoTHubbyEdgeServiceCollectionExtensions
{
    /// <summary>
    /// Registers a singleton <see cref="IoTHubModuleClient"/> built from the IoT Edge environment via
    /// <see cref="EdgeModuleClient"/>.<c>CreateFromEnvironmentAsync</c>. An
    /// <see cref="Microsoft.Extensions.Logging.ILoggerFactory"/> from the container is wired in.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Optional hook to tune client options.</param>
    /// <param name="connectOnStart">
    /// When <c>true</c>, adds a hosted service that connects the client on host start and disconnects
    /// it on stop.
    /// </param>
    /// <remarks>
    /// The workload bootstrap is asynchronous; it is completed synchronously when the singleton is
    /// first resolved (safe under the generic host, which has no synchronization context).
    /// </remarks>
    public static IServiceCollection AddIoTHubEdgeModuleClient(
        this IServiceCollection services,
        Action<IoTHubClientOptions>? configure = null,
        bool connectOnStart = false)
    {
        if (services is null)
        {
            throw new ArgumentNullException(nameof(services));
        }

        services.AddSingleton(sp => EdgeModuleClient
            .CreateFromEnvironmentAsync(IoTHubbyServiceCollectionExtensions.ApplyDiOptions(sp, configure))
            .GetAwaiter()
            .GetResult());

        if (connectOnStart)
        {
            services.AddHostedService<IoTHubClientHostedService<IoTHubModuleClient>>();
        }
        return services;
    }
}
