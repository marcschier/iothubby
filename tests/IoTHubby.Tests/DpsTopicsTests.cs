// Copyright (c) marcschier. Licensed under the MIT License.

using IoTHubby.Provisioning;

namespace IoTHubby.Tests;

public sealed class DpsTopicsTests
{
    [Test]
    public async Task Builds_register_topic()
    {
        await Assert.That(DpsTopics.RegisterTopic("1"))
            .IsEqualTo("$dps/registrations/PUT/iotdps-register/?$rid=1");
    }

    [Test]
    public async Task Builds_operation_status_topic()
    {
        await Assert.That(DpsTopics.OperationStatusTopic("2", "op-1"))
            .IsEqualTo("$dps/registrations/GET/iotdps-get-operationstatus/?$rid=2&operationId=op-1");
    }

    [Test]
    public async Task Parses_assigning_response_with_retry_after()
    {
        var ok = DpsTopics.TryParseResponse(
            "$dps/registrations/res/202/?$rid=1&retry-after=3",
            out var status,
            out var rid,
            out var retryAfter);

        await Assert.That(ok).IsTrue();
        await Assert.That(status).IsEqualTo(202);
        await Assert.That(rid).IsEqualTo("1");
        await Assert.That(retryAfter).IsEqualTo(3);
    }

    [Test]
    public async Task Parses_terminal_response_without_retry_after()
    {
        var ok = DpsTopics.TryParseResponse(
            "$dps/registrations/res/200/?$rid=7",
            out var status,
            out var rid,
            out var retryAfter);

        await Assert.That(ok).IsTrue();
        await Assert.That(status).IsEqualTo(200);
        await Assert.That(rid).IsEqualTo("7");
        await Assert.That(retryAfter).IsNull();
    }
}
