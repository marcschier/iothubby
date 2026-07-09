// Copyright (c) marcschier. Licensed under the MIT License.

using IoTHubby;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Hosted service that connects a registered IoTHubby client when the host starts and disconnects it
/// when the host stops. Registered by the <c>connectOnStart</c> option of the DI extensions.
/// </summary>
/// <typeparam name="TClient">The concrete client type (device or module).</typeparam>
internal sealed class IoTHubClientHostedService<TClient> : IHostedService
    where TClient : class, IIoTHubConnectableClient
{
    private readonly TClient _client;
    private readonly ILogger<IoTHubClientHostedService<TClient>>? _logger;

    public IoTHubClientHostedService(TClient client, ILogger<IoTHubClientHostedService<TClient>>? logger = null)
    {
        _client = client;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _client.ConnectAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Auto-reconnect (on by default) recovers the connection; don't fail host startup.
            if (_logger is not null)
            {
                IoTHubbyDiLog.InitialConnectFailed(_logger, ex);
            }
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _client.DisconnectAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            if (_logger is not null)
            {
                IoTHubbyDiLog.DisconnectFailed(_logger, ex);
            }
        }
    }
}
