// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers;

namespace IoTHubby;

/// <summary>
/// An inbound direct-method invocation from IoT Hub.
/// </summary>
public sealed class DirectMethodRequest
{
    internal DirectMethodRequest(string name, ReadOnlySequence<byte> payload)
    {
        Name = name;
        Payload = payload;
    }

    /// <summary>The method name.</summary>
    public string Name { get; }

    /// <summary>The raw request payload (JSON bytes, possibly empty). May span multiple segments.</summary>
    public ReadOnlySequence<byte> Payload { get; }

    /// <summary>Contiguous view of <see cref="Payload"/> (zero-copy when single-segment).</summary>
    public ReadOnlyMemory<byte> PayloadMemory => Payload.IsSingleSegment ? Payload.First : Payload.ToArray();

    /// <summary>The request payload decoded as a UTF-8 string.</summary>
    public string PayloadAsString => System.Text.Encoding.UTF8.GetString(PayloadMemory.ToArray());
}

/// <summary>
/// The response a method handler returns for a <see cref="DirectMethodRequest"/>.
/// </summary>
public sealed class DirectMethodResponse
{
    private DirectMethodResponse(int status, ReadOnlySequence<byte> payload)
    {
        Status = status;
        Payload = payload;
    }

    /// <summary>Application-defined status code returned to the caller.</summary>
    public int Status { get; }

    /// <summary>Response payload (JSON bytes, possibly empty). May span multiple segments.</summary>
    public ReadOnlySequence<byte> Payload { get; }

    /// <summary>Contiguous view of <see cref="Payload"/> (zero-copy when single-segment).</summary>
    public ReadOnlyMemory<byte> PayloadMemory => Payload.IsSingleSegment ? Payload.First : Payload.ToArray();

    /// <summary>Creates a response with the given status and no payload.</summary>
    public static DirectMethodResponse FromStatus(int status)
        => new(status, ReadOnlySequence<byte>.Empty);

    /// <summary>Creates a response with the given status and contiguous payload bytes.</summary>
    public static DirectMethodResponse FromBytes(int status, ReadOnlyMemory<byte> payload)
        => new(status, new ReadOnlySequence<byte>(payload));

    /// <summary>
    /// Creates a response with the given status and a payload that may span multiple buffer segments
    /// (sent without being concatenated).
    /// </summary>
    public static DirectMethodResponse FromSequence(int status, ReadOnlySequence<byte> payload)
        => new(status, payload);

    /// <summary>Creates a response with the given status and a UTF-8 (typically JSON) string payload.</summary>
    public static DirectMethodResponse FromString(int status, string payload)
        => new(status, new ReadOnlySequence<byte>(System.Text.Encoding.UTF8.GetBytes(payload ?? string.Empty)));
}
