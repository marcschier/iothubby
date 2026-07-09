// Copyright (c) marcschier. Licensed under the MIT License.

using System.Text;

namespace IoTHubby.FuzzTests;

/// <summary>
/// libFuzzer harness for IoT Hub MQTT topic parsers. Contract: arbitrary topic strings must not throw.
/// </summary>
internal static class TopicsHarness
{
    public static void Run(ReadOnlySpan<byte> data)
    {
        var topic = Encoding.UTF8.GetString(data);

        _ = TopicParser.TryParseMethodRequest(topic, out _, out _);
        _ = TopicParser.TryParseTwinResponse(topic, out _, out _, out _);
        TopicParser.ParseDesiredVersion(topic);
        _ = TopicParser.TryParseInput(topic, "devices/d/modules/m/inputs/", out _, out _);
    }
}
