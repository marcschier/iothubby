// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers;
using System.Text;

namespace IoTHubby.Tests;

public sealed class SequencePayloadTests
{
    /// <summary>Builds a multi-segment <see cref="ReadOnlySequence{T}"/> from the given chunks.</summary>
    internal static ReadOnlySequence<byte> MultiSegment(params byte[][] chunks)
    {
        var first = new Segment(chunks[0]);
        var last = first;
        for (var i = 1; i < chunks.Length; i++)
        {
            last = last.Append(chunks[i]);
        }
        return new ReadOnlySequence<byte>(first, 0, last, last.Memory.Length);
    }

    [Test]
    public async Task Telemetry_accepts_multi_segment_payload()
    {
        var seq = MultiSegment("{\"a\":1,"u8.ToArray(), "\"b\":2}"u8.ToArray());
        var message = new TelemetryMessage(seq);

        await Assert.That(message.Payload.IsSingleSegment).IsFalse();
        await Assert.That(message.Payload.Length).IsEqualTo(13L);
        // PayloadMemory gives a contiguous view (concatenated).
        await Assert.That(Encoding.UTF8.GetString(message.PayloadMemory.ToArray())).IsEqualTo("{\"a\":1,\"b\":2}");
    }

    [Test]
    public async Task Telemetry_memory_ctor_is_single_segment()
    {
        var message = new TelemetryMessage("hi"u8.ToArray().AsMemory());
        await Assert.That(message.Payload.IsSingleSegment).IsTrue();
        await Assert.That(Encoding.UTF8.GetString(message.PayloadMemory.ToArray())).IsEqualTo("hi");
    }

    [Test]
    public async Task DirectMethodResponse_from_sequence()
    {
        var seq = MultiSegment("{\"ok\":"u8.ToArray(), "true}"u8.ToArray());
        var response = DirectMethodResponse.FromSequence(200, seq);
        await Assert.That(response.Status).IsEqualTo(200);
        await Assert.That(response.Payload.IsSingleSegment).IsFalse();
        await Assert.That(Encoding.UTF8.GetString(response.PayloadMemory.ToArray())).IsEqualTo("{\"ok\":true}");
    }

    [Test]
    public async Task Inbound_message_exposes_sequence_and_memory()
    {
        var msg = IoTHubWireCodec.MapInbound(
            "devices/d/messages/devicebound/%24.mid=1",
            "devices/d/messages/devicebound/",
            "payload"u8.ToArray());

        await Assert.That(msg.Payload.IsSingleSegment).IsTrue();
        await Assert.That(Encoding.UTF8.GetString(msg.PayloadMemory.ToArray())).IsEqualTo("payload");
        await Assert.That(msg.PayloadAsString).IsEqualTo("payload");
    }

    [Test]
    public async Task TwinProperties_raw_sequence()
    {
        var props = new TwinProperties(Encoding.UTF8.GetBytes("{\"x\":1}"), 3);
        await Assert.That(props.RawSequence.IsSingleSegment).IsTrue();
        await Assert.That(Encoding.UTF8.GetString(props.RawSequence.ToArray())).IsEqualTo("{\"x\":1}");
    }

    private sealed class Segment : ReadOnlySequenceSegment<byte>
    {
        public Segment(ReadOnlyMemory<byte> memory) => Memory = memory;

        public Segment Append(ReadOnlyMemory<byte> memory)
        {
            var next = new Segment(memory) { RunningIndex = RunningIndex + Memory.Length };
            Next = next;
            return next;
        }
    }
}
