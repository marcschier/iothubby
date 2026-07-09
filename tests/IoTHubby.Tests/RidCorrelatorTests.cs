// Copyright (c) marcschier. Licensed under the MIT License.

using System.Text;

namespace IoTHubby.Tests;

public sealed class RidCorrelatorTests
{
    private static readonly TimeSpan ShortTimeout = TimeSpan.FromSeconds(5);

    [Test]
    public async Task Allocates_monotonically_increasing_ids()
    {
        var correlator = new RidCorrelator();
        var first = correlator.Next();
        var second = correlator.Next();
        await Assert.That(first).IsEqualTo("1");
        await Assert.That(second).IsEqualTo("2");
    }

    [Test]
    public async Task Completes_pending_request_with_payload()
    {
        var correlator = new RidCorrelator();
        var rid = correlator.Next();

        var waiter = correlator.WaitAsync(rid, ShortTimeout, CancellationToken.None);
        var completed = correlator.TryComplete(rid, 200, "hi"u8);

        await Assert.That(completed).IsTrue();
        var response = await waiter;
        await Assert.That(response.Status).IsEqualTo(200);
        await Assert.That(response.IsSuccess).IsTrue();
        await Assert.That(Encoding.UTF8.GetString(response.Payload)).IsEqualTo("hi");
    }

    [Test]
    public async Task TryComplete_returns_false_for_unknown_rid()
    {
        var correlator = new RidCorrelator();
        await Assert.That(correlator.TryComplete("999", 200, default)).IsFalse();
    }

    [Test]
    public async Task Times_out_when_no_response()
    {
        var correlator = new RidCorrelator();
        var rid = correlator.Next();
        await Assert.That(async () =>
            await correlator.WaitAsync(rid, TimeSpan.FromMilliseconds(50), CancellationToken.None))
            .Throws<TimeoutException>();
    }

    [Test]
    public async Task Cancellation_propagates()
    {
        var correlator = new RidCorrelator();
        var rid = correlator.Next();
        using var cts = new CancellationTokenSource();
        var waiter = correlator.WaitAsync(rid, ShortTimeout, cts.Token);
        cts.Cancel();
        await Assert.That(async () => await waiter).Throws<OperationCanceledException>();
    }

    [Test]
    public async Task FailAll_faults_pending_requests()
    {
        var correlator = new RidCorrelator();
        var rid = correlator.Next();
        var waiter = correlator.WaitAsync(rid, ShortTimeout, CancellationToken.None);
        correlator.FailAll(new InvalidOperationException("disconnected"));
        await Assert.That(async () => await waiter).Throws<InvalidOperationException>();
    }
}
