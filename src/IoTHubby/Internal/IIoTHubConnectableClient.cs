// Copyright (c) marcschier. Licensed under the MIT License.

namespace IoTHubby;

/// <summary>
/// Common connect/disconnect surface shared by the device and module clients, used by the
/// dependency-injection hosted service.
/// </summary>
internal interface IIoTHubConnectableClient
{
    /// <summary>Connects to IoT Hub / the edge gateway.</summary>
    Task ConnectAsync(CancellationToken cancellationToken = default);

    /// <summary>Disconnects gracefully.</summary>
    Task DisconnectAsync(CancellationToken cancellationToken = default);
}
