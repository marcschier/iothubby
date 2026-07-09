// Copyright (c) marcschier. Licensed under the MIT License.

namespace IoTHubby.Tests;

public sealed class PropertyBagCodecTests
{
    [Test]
    public async Task Encodes_single_property()
    {
        var bag = PropertyBagCodec.Encode(new[] { Kv("temp", "42") });
        await Assert.That(bag).IsEqualTo("temp=42");
    }

    [Test]
    public async Task Encodes_multiple_properties_joined_with_ampersand()
    {
        var bag = PropertyBagCodec.Encode(new[] { Kv("a", "1"), Kv("b", "2") });
        await Assert.That(bag).IsEqualTo("a=1&b=2");
    }

    [Test]
    public async Task Encodes_valueless_property_as_key_only()
    {
        var bag = PropertyBagCodec.Encode(new[] { Kv("flag", null) });
        await Assert.That(bag).IsEqualTo("flag");
    }

    [Test]
    public async Task Percent_encodes_special_characters()
    {
        var bag = PropertyBagCodec.Encode(new[] { Kv("$.ct", "application/json") });
        await Assert.That(bag).IsEqualTo("%24.ct=application%2Fjson");
    }

    [Test]
    public async Task Round_trips_through_decode()
    {
        var bag = PropertyBagCodec.Encode(new[] { Kv("$.mid", "id-1"), Kv("custom", "a b"), Kv("flag", null) });
        var decoded = PropertyBagCodec.Decode(bag.AsSpan());

        await Assert.That(decoded["$.mid"]).IsEqualTo("id-1");
        await Assert.That(decoded["custom"]).IsEqualTo("a b");
        await Assert.That(decoded["flag"]).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task Decodes_empty_bag_to_empty_dictionary()
    {
        var decoded = PropertyBagCodec.Decode(default);
        await Assert.That(decoded.Count).IsEqualTo(0);
    }

    private static KeyValuePair<string, string?> Kv(string k, string? v) => new(k, v);
}
