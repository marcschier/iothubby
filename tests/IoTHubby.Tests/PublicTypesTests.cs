// Copyright (c) marcschier. Licensed under the MIT License.

using System.Text;
using System.Text.Json.Serialization;

namespace IoTHubby.Tests;

public sealed class PublicTypesTests
{
    [Test]
    public async Task TelemetryMessage_from_string_and_properties()
    {
        var m = TelemetryMessage.FromString("hi");
        m.MessageId = "id";
        m.CorrelationId = "cid";
        m.UserId = "uid";
        m.ContentType = "application/json";
        m.ContentEncoding = "utf-8";
        m.MessageSchema = "schema";
        m.OutputName = "out";
        m.CreationTimeUtc = DateTimeOffset.UnixEpoch;
        m.ExpiryTimeUtc = DateTimeOffset.UnixEpoch.AddHours(1);
        m.QoS = IoTHubQoS.AtMostOnce;
        m.Properties["k"] = "v";

        await Assert.That(Encoding.UTF8.GetString(m.Payload.ToArray())).IsEqualTo("hi");
        await Assert.That(m.MessageId).IsEqualTo("id");
        await Assert.That(m.QoS).IsEqualTo(IoTHubQoS.AtMostOnce);
        await Assert.That(m.Properties["k"]).IsEqualTo("v");
        await Assert.That(new TelemetryMessage("x"u8.ToArray().AsMemory()).Payload.Length).IsEqualTo(1);
    }

    [Test]
    public async Task CloudToDeviceMessage_exposes_system_properties()
    {
        var system = new Dictionary<string, string>
        {
            [SystemProperties.MessageId] = "m",
            [SystemProperties.CorrelationId] = "c",
            [SystemProperties.UserId] = "u",
            [SystemProperties.ContentType] = "ct",
            [SystemProperties.ContentEncoding] = "ce",
            [SystemProperties.InputName] = "in",
        };
        var app = new Dictionary<string, string> { ["a"] = "b" };
        var msg = new CloudToDeviceMessage("data"u8.ToArray().AsMemory(), system, app);

        await Assert.That(msg.MessageId).IsEqualTo("m");
        await Assert.That(msg.CorrelationId).IsEqualTo("c");
        await Assert.That(msg.UserId).IsEqualTo("u");
        await Assert.That(msg.ContentType).IsEqualTo("ct");
        await Assert.That(msg.ContentEncoding).IsEqualTo("ce");
        await Assert.That(msg.InputName).IsEqualTo("in");
        await Assert.That(msg.Properties["a"]).IsEqualTo("b");
        await Assert.That(msg.PayloadAsString).IsEqualTo("data");
    }

    [Test]
    public async Task DirectMethod_request_and_responses()
    {
        var request = new DirectMethodRequest("reboot", "{\"x\":1}"u8.ToArray().AsMemory());
        await Assert.That(request.Name).IsEqualTo("reboot");
        await Assert.That(request.PayloadAsString).IsEqualTo("{\"x\":1}");

        await Assert.That(DirectMethodResponse.FromStatus(204).Status).IsEqualTo(204);
        await Assert.That(DirectMethodResponse.FromStatus(204).Payload.Length).IsEqualTo(0);
        await Assert.That(DirectMethodResponse.FromString(200, "ok").Status).IsEqualTo(200);
        var bytes = DirectMethodResponse.FromBytes(500, "e"u8.ToArray().AsMemory());
        await Assert.That(bytes.Status).IsEqualTo(500);
        await Assert.That(bytes.Payload.Length).IsEqualTo(1);
    }

    [Test]
    public async Task TwinProperties_expose_json_version_and_deserialize()
    {
        var props = new TwinProperties(Encoding.UTF8.GetBytes("{\"interval\":30}"), 5);
        await Assert.That(props.Version).IsEqualTo(5L);
        await Assert.That(props.ToJsonString()).IsEqualTo("{\"interval\":30}");
        await Assert.That(props.RawJson.Length).IsGreaterThan(0);
        await Assert.That(props.RootElement.GetProperty("interval").GetInt32()).IsEqualTo(30);

        var typed = props.Deserialize(TestJsonContext.Default.TestModel);
        await Assert.That(typed!.Interval).IsEqualTo(30);

        var twin = new Twin(props, new TwinProperties(Encoding.UTF8.GetBytes("{}"), null));
        await Assert.That(twin.Desired.Version).IsEqualTo(5L);
        await Assert.That(twin.Reported.Version).IsNull();

        var update = new DesiredPropertyUpdate(props);
        await Assert.That(update.Properties.Version).IsEqualTo(5L);
    }

    [Test]
    public async Task IoTHubClientException_carries_status()
    {
        await Assert.That(new IoTHubClientException("m").StatusCode).IsNull();
        await Assert.That(new IoTHubClientException("m", 429).StatusCode).IsEqualTo(429);
        await Assert.That(new IoTHubClientException("m", new InvalidOperationException()).InnerException)
            .IsNotNull();
    }

    [Test]
    public async Task ConnectionStateChangedEventArgs_holds_state()
    {
        var args = new IoTHubConnectionStateChangedEventArgs(IoTHubConnectionState.Reconnecting, "dropped");
        await Assert.That(args.State).IsEqualTo(IoTHubConnectionState.Reconnecting);
        await Assert.That(args.Reason).IsEqualTo("dropped");
    }

    [Test]
    public async Task Options_have_sensible_defaults()
    {
        var o = new IoTHubClientOptions();
        await Assert.That(o.OperationTimeout).IsEqualTo(TimeSpan.FromSeconds(30));
        await Assert.That(o.SasTokenLifetime).IsEqualTo(TimeSpan.FromHours(1));
        await Assert.That(o.AutoReconnect).IsTrue();
        await Assert.That(o.ReceiveChannelCapacity).IsEqualTo(1024);
    }
}

internal sealed class TestModel
{
    public int Interval { get; set; }
}

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(TestModel))]
internal sealed partial class TestJsonContext : JsonSerializerContext
{
}
