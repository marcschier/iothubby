// Copyright (c) marcschier. Licensed under the MIT License.

using System.Globalization;

namespace IoTHubby;

/// <summary>
/// Parses the inbound IoT Hub MQTT topics (method requests, twin responses, desired-property
/// updates, cloud-to-device, and edge inputs) using span slicing to avoid intermediate allocations.
/// </summary>
internal static class TopicParser
{
    /// <summary>
    /// Parses a method-request topic <c>$iothub/methods/POST/{name}/?$rid={rid}</c>.
    /// </summary>
    public static bool TryParseMethodRequest(string topic, out string name, out string rid)
    {
        name = string.Empty;
        rid = string.Empty;

        const string prefix = "$iothub/methods/POST/";
        if (!topic.StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }

        var rest = topic.AsSpan(prefix.Length);
        var q = rest.IndexOf('/');
        if (q < 0)
        {
            return false;
        }

        name = rest.Slice(0, q).ToString();
        var query = rest.Slice(q + 1);          // "?$rid=..."
        var qmark = query.IndexOf('?');
        if (qmark >= 0)
        {
            query = query.Slice(qmark + 1);
        }
        var q2 = ParseQuery(query);
        return q2.TryGetValue("$rid", out rid!) && name.Length > 0;
    }

    /// <summary>
    /// Parses a twin-response topic <c>$iothub/twin/res/{status}/?$rid={rid}[&amp;$version={v}]</c>.
    /// </summary>
    public static bool TryParseTwinResponse(
        string topic,
        out int status,
        out string rid,
        out IReadOnlyDictionary<string, string> query)
    {
        status = 0;
        rid = string.Empty;
        query = EmptyQuery;

        const string prefix = "$iothub/twin/res/";
        if (!topic.StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }

        var rest = topic.AsSpan(prefix.Length);
        var slash = rest.IndexOf('/');
        var statusSpan = slash < 0 ? rest : rest.Slice(0, slash);
        if (!int.TryParse(statusSpan.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out status))
        {
            return false;
        }

        if (slash >= 0)
        {
            var q = rest.Slice(slash + 1);
            var qmark = q.IndexOf('?');
            if (qmark >= 0)
            {
                q = q.Slice(qmark + 1);
            }
            var parsed = ParseQuery(q);
            query = parsed;
            parsed.TryGetValue("$rid", out rid!);
        }

        return true;
    }

    /// <summary>
    /// Extracts the <c>$version</c> from a desired-property topic
    /// <c>$iothub/twin/PATCH/properties/desired/?$version={v}</c>.
    /// </summary>
    public static long? ParseDesiredVersion(string topic)
    {
        var qmark = topic.IndexOf('?');
        if (qmark < 0)
        {
            return null;
        }
        var query = ParseQuery(topic.AsSpan(qmark + 1));
        return query.TryGetValue("$version", out var v)
            && long.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var version)
            ? version
            : null;
    }

    /// <summary>Returns the property-bag portion of <paramref name="topic"/> after <paramref name="prefix"/>.</summary>
    public static ReadOnlySpan<char> PropertyBagAfter(string topic, string prefix)
        => topic.StartsWith(prefix, StringComparison.Ordinal)
            ? topic.AsSpan(prefix.Length)
            : default;

    /// <summary>
    /// Parses an edge input topic <c>devices/{d}/modules/{m}/inputs/{input}/{propBag}</c>, returning
    /// the input name and the property-bag span.
    /// </summary>
    public static bool TryParseInput(string topic, string inputPrefix, out string input, out string propertyBag)
    {
        input = string.Empty;
        propertyBag = string.Empty;
        if (!topic.StartsWith(inputPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        var rest = topic.AsSpan(inputPrefix.Length);
        var slash = rest.IndexOf('/');
        if (slash < 0)
        {
            input = rest.ToString();
            return input.Length > 0;
        }

        input = rest.Slice(0, slash).ToString();
        propertyBag = rest.Slice(slash + 1).ToString();
        return input.Length > 0;
    }

    private static Dictionary<string, string> ParseQuery(ReadOnlySpan<char> query)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        while (!query.IsEmpty)
        {
            var amp = query.IndexOf('&');
            var pair = amp < 0 ? query : query.Slice(0, amp);
            query = amp < 0 ? default : query.Slice(amp + 1);
            if (pair.IsEmpty)
            {
                continue;
            }
            var eq = pair.IndexOf('=');
            if (eq < 0)
            {
                result[pair.ToString()] = string.Empty;
            }
            else
            {
                result[pair.Slice(0, eq).ToString()] = Uri.UnescapeDataString(pair.Slice(eq + 1).ToString());
            }
        }
        return result;
    }

    private static readonly IReadOnlyDictionary<string, string> EmptyQuery =
        new Dictionary<string, string>(0);
}
