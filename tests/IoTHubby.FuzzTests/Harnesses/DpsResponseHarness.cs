// Copyright (c) marcschier. Licensed under the MIT License.

using IoTHubby.Provisioning;
using System.Text;

namespace IoTHubby.FuzzTests;

/// <summary>
/// libFuzzer harness for DPS response topic parsing. Contract: arbitrary topic strings must not throw.
/// </summary>
internal static class DpsResponseHarness
{
    public static void Run(ReadOnlySpan<byte> data)
    {
        var topic = Encoding.UTF8.GetString(data);
        _ = DpsTopics.TryParseResponse(topic, out _, out _, out _);
    }
}
