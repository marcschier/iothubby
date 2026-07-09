// Copyright (c) marcschier. Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Source-generated log messages for the IoTHubby DI hosted service.</summary>
[ExcludeFromCodeCoverage]
internal static partial class IoTHubbyDiLog
{
    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Error,
        Message = "IoTHubby initial connect failed; auto-reconnect will retry.")]
    public static partial void InitialConnectFailed(ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = 2,
        Level = LogLevel.Debug,
        Message = "IoTHubby disconnect on shutdown failed.")]
    public static partial void DisconnectFailed(ILogger logger, Exception exception);
}
