// Copyright (c) marcschier. Licensed under the MIT License.

using System.Text.Json;

namespace IoTHubby.FuzzTests;

/// <summary>
/// libFuzzer harness for <see cref="IoTHubWireCodec.ParseTwin"/>. Contract: malformed JSON may
/// throw <see cref="JsonException"/>; all other exceptions are fuzz findings.
/// </summary>
internal static class TwinJsonHarness
{
    public static void Run(ReadOnlySpan<byte> data)
    {
        try
        {
            IoTHubWireCodec.ParseTwin(data.ToArray());
        }
        catch (JsonException)
        {
        }
    }
}
