// Copyright (c) marcschier. Licensed under the MIT License.

namespace IoTHubby;

/// <summary>
/// An inbound direct-method invocation from IoT Hub.
/// </summary>
public sealed class DirectMethodRequest
{
    internal DirectMethodRequest(string name, ReadOnlyMemory<byte> payload)
    {
        Name = name;
        Payload = payload;
    }

    /// <summary>The method name.</summary>
    public string Name { get; }

    /// <summary>The raw request payload (JSON bytes, possibly empty).</summary>
    public ReadOnlyMemory<byte> Payload { get; }

    /// <summary>The request payload decoded as a UTF-8 string.</summary>
    public string PayloadAsString => System.Text.Encoding.UTF8.GetString(Payload.ToArray());
}

/// <summary>
/// The response a method handler returns for a <see cref="DirectMethodRequest"/>.
/// </summary>
public sealed class DirectMethodResponse
{
    private DirectMethodResponse(int status, ReadOnlyMemory<byte> payload)
    {
        Status = status;
        Payload = payload;
    }

    /// <summary>Application-defined status code returned to the caller.</summary>
    public int Status { get; }

    /// <summary>Response payload (JSON bytes, possibly empty).</summary>
    public ReadOnlyMemory<byte> Payload { get; }

    /// <summary>Creates a response with the given status and no payload.</summary>
    public static DirectMethodResponse FromStatus(int status)
        => new(status, ReadOnlyMemory<byte>.Empty);

    /// <summary>Creates a response with the given status and raw payload bytes.</summary>
    public static DirectMethodResponse FromBytes(int status, ReadOnlyMemory<byte> payload)
        => new(status, payload);

    /// <summary>Creates a response with the given status and a UTF-8 (typically JSON) string payload.</summary>
    public static DirectMethodResponse FromString(int status, string payload)
        => new(status, System.Text.Encoding.UTF8.GetBytes(payload ?? string.Empty));
}
