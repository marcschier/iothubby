// Copyright (c) marcschier. Licensed under the MIT License.

using System.Text;

namespace IoTHubby.FuzzTests;

/// <summary>
/// libFuzzer harness for <see cref="PropertyBagCodec.Decode"/>. Contract: arbitrary property bags must not throw.
/// </summary>
internal static class PropertyBagHarness
{
    public static void Run(ReadOnlySpan<byte> data)
    {
        var bag = Encoding.UTF8.GetString(data);
        PropertyBagCodec.Decode(bag.AsSpan());
    }
}
