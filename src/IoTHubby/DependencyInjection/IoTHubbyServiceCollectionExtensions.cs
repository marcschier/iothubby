// Copyright (c) marcschier. Licensed under the MIT License.

using IoTHubby;
using Microsoft.Extensions.Logging;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Dependency-injection registration for IoTHubby device and module clients. The client is registered
/// as a singleton and, when available, an <see cref="ILoggerFactory"/> from the container is wired in
/// automatically. Optionally connects on host start via a hosted service.
/// </summary>
public static class IoTHubbyServiceCollectionExtensions
{
    /// <summary>
    /// Registers a singleton <see cref="IoTHubDeviceClient"/> built from the given connection string.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="connectionString">An IoT Hub device connection string.</param>
    /// <param name="configure">Optional hook to tune client options.</param>
    /// <param name="connectOnStart">
    /// When <c>true</c>, adds a hosted service that connects the client on host start and disconnects
    /// it on stop.
    /// </param>
    public static IServiceCollection AddIoTHubDeviceClient(
        this IServiceCollection services,
        string connectionString,
        Action<IoTHubClientOptions>? configure = null,
        bool connectOnStart = false)
    {
        if (services is null)
        {
            throw new ArgumentNullException(nameof(services));
        }
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException("Connection string is required.", nameof(connectionString));
        }

        services.AddSingleton(sp => IoTHubDeviceClient.CreateFromConnectionString(
            connectionString, ApplyDiOptions(sp, configure)));

        if (connectOnStart)
        {
            services.AddHostedService<IoTHubClientHostedService<IoTHubDeviceClient>>();
        }
        return services;
    }

    /// <summary>
    /// Registers a singleton <see cref="IoTHubModuleClient"/> built from the given connection string.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="connectionString">An IoT Hub / IoT Edge module connection string.</param>
    /// <param name="configure">Optional hook to tune client options.</param>
    /// <param name="connectOnStart">
    /// When <c>true</c>, adds a hosted service that connects the client on host start and disconnects
    /// it on stop.
    /// </param>
    public static IServiceCollection AddIoTHubModuleClient(
        this IServiceCollection services,
        string connectionString,
        Action<IoTHubClientOptions>? configure = null,
        bool connectOnStart = false)
    {
        if (services is null)
        {
            throw new ArgumentNullException(nameof(services));
        }
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException("Connection string is required.", nameof(connectionString));
        }

        services.AddSingleton(sp => IoTHubModuleClient.CreateFromConnectionString(
            connectionString, ApplyDiOptions(sp, configure)));

        if (connectOnStart)
        {
            services.AddHostedService<IoTHubClientHostedService<IoTHubModuleClient>>();
        }
        return services;
    }

    /// <summary>
    /// Wraps a user options callback so the container's <see cref="ILoggerFactory"/> (if any) is
    /// applied first, then the user's configuration.
    /// </summary>
    internal static Action<IoTHubClientOptions> ApplyDiOptions(
        IServiceProvider serviceProvider, Action<IoTHubClientOptions>? configure)
        => options =>
        {
            options.LoggerFactory ??= serviceProvider.GetService<ILoggerFactory>();
            configure?.Invoke(options);
        };
}
