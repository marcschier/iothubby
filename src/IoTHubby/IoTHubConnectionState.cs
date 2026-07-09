// Copyright (c) marcschier. Licensed under the MIT License.

namespace IoTHubby;

/// <summary>
/// The client's connection state.
/// </summary>
public enum IoTHubConnectionState
{
    /// <summary>Not connected and not attempting to connect.</summary>
    Disconnected = 0,

    /// <summary>Establishing the initial connection.</summary>
    Connecting = 1,

    /// <summary>Connected and ready.</summary>
    Connected = 2,

    /// <summary>Connection was lost and the client is retrying automatically.</summary>
    Reconnecting = 3,

    /// <summary>The client has been disposed.</summary>
    Disposed = 4,
}

/// <summary>
/// Event payload describing a connection-state transition.
/// </summary>
public sealed class IoTHubConnectionStateChangedEventArgs : EventArgs
{
    internal IoTHubConnectionStateChangedEventArgs(IoTHubConnectionState state, string? reason = null)
    {
        State = state;
        Reason = reason;
    }

    /// <summary>The new state.</summary>
    public IoTHubConnectionState State { get; }

    /// <summary>An optional human-readable reason for the transition.</summary>
    public string? Reason { get; }
}
