// Copyright (c) marcschier. Licensed under the MIT License.

using System.Text;

namespace IoTHubby.Tests;

public sealed class IoTHubWireCodecTests
{
    [Test]
    public async Task Telemetry_topic_has_prefix_and_no_bag_when_no_properties()
    {
        var topics = new IoTHubTopics("dev1");
        var topic = IoTHubWireCodec.BuildTelemetryTopic(topics, new TelemetryMessage(), outputName: null);
        await Assert.That(topic).IsEqualTo("devices/dev1/messages/events/");
    }

    [Test]
    public async Task Telemetry_topic_encodes_system_and_app_properties()
    {
        var topics = new IoTHubTopics("dev1");
        var message = new TelemetryMessage
        {
            MessageId = "m1",
            ContentType = "application/json",
        };
        message.Properties["zone"] = "north";

        var topic = IoTHubWireCodec.BuildTelemetryTopic(topics, message, outputName: null);

        await Assert.That(topic).StartsWith("devices/dev1/messages/events/");
        await Assert.That(topic).Contains("%24.mid=m1");
        await Assert.That(topic).Contains("%24.ct=application%2Fjson");
        await Assert.That(topic).Contains("zone=north");
    }

    [Test]
    public async Task Telemetry_topic_includes_output_name_for_module()
    {
        var topics = new IoTHubTopics("dev1", "mod1");
        var topic = IoTHubWireCodec.BuildTelemetryTopic(topics, new TelemetryMessage(), "route1");
        await Assert.That(topic).StartsWith("devices/dev1/modules/mod1/messages/events/");
        await Assert.That(topic).Contains("%24.on=route1");
    }

    [Test]
    public async Task Maps_c2d_message_splitting_system_and_app_properties()
    {
        const string topic = "devices/dev1/messages/devicebound/%24.mid=abc&color=red";
        var msg = IoTHubWireCodec.MapInbound(topic, "devices/dev1/messages/devicebound/", "hi"u8.ToArray());

        await Assert.That(msg.MessageId).IsEqualTo("abc");
        await Assert.That(msg.Properties["color"]).IsEqualTo("red");
        await Assert.That(Encoding.UTF8.GetString(msg.Payload.ToArray())).IsEqualTo("hi");
    }

    [Test]
    public async Task Parses_full_twin_document()
    {
        var json = Encoding.UTF8.GetBytes(
            "{\"desired\":{\"interval\":30,\"$version\":4},\"reported\":{\"temp\":21,\"$version\":9}}");
        var twin = IoTHubWireCodec.ParseTwin(json);

        await Assert.That(twin.Desired.Version).IsEqualTo(4L);
        await Assert.That(twin.Reported.Version).IsEqualTo(9L);
        await Assert.That(twin.Desired.RootElement.GetProperty("interval").GetInt32()).IsEqualTo(30);
        await Assert.That(twin.Reported.RootElement.GetProperty("temp").GetInt32()).IsEqualTo(21);
    }

    [Test]
    public async Task Twin_missing_sections_default_to_empty()
    {
        var twin = IoTHubWireCodec.ParseTwin(Encoding.UTF8.GetBytes("{}"));
        await Assert.That(twin.Desired.Version).IsNull();
        await Assert.That(twin.Desired.ToJsonString()).IsEqualTo("{}");
    }
}
