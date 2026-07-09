// Copyright (c) marcschier. Licensed under the MIT License.

using System.Text;

namespace IoTHubby;

/// <summary>
/// Encodes and decodes the URL-encoded <c>key=value&amp;key=value</c> property bag that IoT Hub
/// appends to (and parses from) MQTT topics for telemetry, cloud-to-device, and edge input/output
/// messages.
/// </summary>
/// <remarks>
/// Both keys and values are percent-encoded with <see cref="Uri.EscapeDataString(string)"/> and
/// decoded with <see cref="Uri.UnescapeDataString(string)"/>, matching the Azure device SDK. A
/// key present with no <c>=</c> decodes to an empty-string value; an empty bag encodes to an empty
/// string.
/// </remarks>
internal static class PropertyBagCodec
{
    /// <summary>
    /// Appends the encoded property bag for <paramref name="properties"/> onto
    /// <paramref name="builder"/>. Nothing is written when the sequence is empty. The caller is
    /// responsible for any leading separator (the telemetry topic already ends in <c>/</c>).
    /// </summary>
    public static void Encode(StringBuilder builder, IEnumerable<KeyValuePair<string, string?>> properties)
    {
        if (properties is null)
        {
            return;
        }

        var first = true;
        foreach (var pair in properties)
        {
            if (string.IsNullOrEmpty(pair.Key))
            {
                continue;
            }

            if (!first)
            {
                builder.Append('&');
            }
            first = false;

            builder.Append(Uri.EscapeDataString(pair.Key));
            if (!string.IsNullOrEmpty(pair.Value))
            {
                builder.Append('=').Append(Uri.EscapeDataString(pair.Value!));
            }
        }
    }

    /// <summary>Encodes <paramref name="properties"/> to a standalone property-bag string.</summary>
    public static string Encode(IEnumerable<KeyValuePair<string, string?>> properties)
    {
        var builder = new StringBuilder(64);
        Encode(builder, properties);
        return builder.ToString();
    }

    /// <summary>
    /// Decodes a property bag from <paramref name="bag"/> (the portion of a topic after the trailing
    /// component, without a leading separator). Returns an empty dictionary for an empty span.
    /// </summary>
    public static Dictionary<string, string> Decode(ReadOnlySpan<char> bag)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        DecodeInto(bag, result);
        return result;
    }

    /// <summary>Decodes a property bag into an existing dictionary (values overwrite on duplicate keys).</summary>
    public static void DecodeInto(ReadOnlySpan<char> bag, IDictionary<string, string> destination)
    {
        while (!bag.IsEmpty)
        {
            var amp = bag.IndexOf('&');
            var pair = amp < 0 ? bag : bag.Slice(0, amp);
            bag = amp < 0 ? default : bag.Slice(amp + 1);

            if (pair.IsEmpty)
            {
                continue;
            }

            var eq = pair.IndexOf('=');
            string key, value;
            if (eq < 0)
            {
                key = Uri.UnescapeDataString(pair.ToString());
                value = string.Empty;
            }
            else
            {
                key = Uri.UnescapeDataString(pair.Slice(0, eq).ToString());
                value = Uri.UnescapeDataString(pair.Slice(eq + 1).ToString());
            }

            if (key.Length != 0)
            {
                destination[key] = value;
            }
        }
    }
}
