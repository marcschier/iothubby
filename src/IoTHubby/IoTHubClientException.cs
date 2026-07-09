// Copyright (c) marcschier. Licensed under the MIT License.

namespace IoTHubby;

/// <summary>
/// The exception thrown for IoT Hub client errors (connection rejected, operation failed with a
/// non-success status, or a protocol violation).
/// </summary>
public class IoTHubClientException : Exception
{
    /// <summary>Creates an exception with a message.</summary>
    public IoTHubClientException(string message) : base(message)
    {
    }

    /// <summary>Creates an exception with a message and inner cause.</summary>
    public IoTHubClientException(string message, Exception innerException) : base(message, innerException)
    {
    }

    /// <summary>Creates an exception with a message and an IoT Hub status code.</summary>
    public IoTHubClientException(string message, int statusCode) : base(message)
        => StatusCode = statusCode;

    /// <summary>The IoT Hub status code associated with the failure, when applicable.</summary>
    public int? StatusCode { get; }
}
