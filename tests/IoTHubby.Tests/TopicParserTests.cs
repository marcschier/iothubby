// Copyright (c) marcschier. Licensed under the MIT License.

namespace IoTHubby.Tests;

public sealed class TopicParserTests
{
    [Test]
    public async Task Parses_method_request()
    {
        var ok = TopicParser.TryParseMethodRequest("$iothub/methods/POST/reboot/?$rid=42", out var name, out var rid);
        await Assert.That(ok).IsTrue();
        await Assert.That(name).IsEqualTo("reboot");
        await Assert.That(rid).IsEqualTo("42");
    }

    [Test]
    public async Task Rejects_non_method_topic()
    {
        var ok = TopicParser.TryParseMethodRequest("devices/d/messages/events/", out _, out _);
        await Assert.That(ok).IsFalse();
    }

    [Test]
    public async Task Parses_twin_response_with_version()
    {
        var ok = TopicParser.TryParseTwinResponse(
            "$iothub/twin/res/204/?$rid=7&$version=12", out var status, out var rid, out var query);
        await Assert.That(ok).IsTrue();
        await Assert.That(status).IsEqualTo(204);
        await Assert.That(rid).IsEqualTo("7");
        await Assert.That(query["$version"]).IsEqualTo("12");
    }

    [Test]
    public async Task Parses_twin_response_error_status()
    {
        var ok = TopicParser.TryParseTwinResponse(
            "$iothub/twin/res/429/?$rid=3", out var status, out var rid, out _);
        await Assert.That(ok).IsTrue();
        await Assert.That(status).IsEqualTo(429);
        await Assert.That(rid).IsEqualTo("3");
    }

    [Test]
    public async Task Parses_desired_version()
    {
        var version = TopicParser.ParseDesiredVersion("$iothub/twin/PATCH/properties/desired/?$version=99");
        await Assert.That(version).IsEqualTo(99L);
    }

    [Test]
    public async Task Parses_input_topic()
    {
        var ok = TopicParser.TryParseInput(
            "devices/d/modules/m/inputs/route1/%24.cdid=other",
            "devices/d/modules/m/inputs/",
            out var input,
            out var bag);
        await Assert.That(ok).IsTrue();
        await Assert.That(input).IsEqualTo("route1");
        await Assert.That(bag).IsEqualTo("%24.cdid=other");
    }
}
