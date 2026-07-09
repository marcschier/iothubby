// Copyright (c) marcschier. Licensed under the MIT License.

using System.Globalization;

namespace IoTHubby.Provisioning;

internal static class DpsTopics
{
    public const string ResponseSubscribe = "$dps/registrations/res/#";

    public static string RegisterTopic(string rid)
        => "$dps/registrations/PUT/iotdps-register/?$rid=" + Uri.EscapeDataString(rid);

    public static string OperationStatusTopic(string rid, string operationId)
        => "$dps/registrations/GET/iotdps-get-operationstatus/?$rid="
            + Uri.EscapeDataString(rid)
            + "&operationId="
            + Uri.EscapeDataString(operationId);

    public static bool TryParseResponse(string topic, out int status, out string rid, out int? retryAfterSeconds)
    {
        status = 0;
        rid = string.Empty;
        retryAfterSeconds = null;

        const string prefix = "$dps/registrations/res/";
        if (!topic.StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }

        var rest = topic.AsSpan(prefix.Length);
        var slash = rest.IndexOf('/');
        if (slash < 0)
        {
            return false;
        }

        if (!int.TryParse(
                rest.Slice(0, slash).ToString(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out status))
        {
            return false;
        }

        var query = rest.Slice(slash + 1);
        var question = query.IndexOf('?');
        if (question >= 0)
        {
            query = query.Slice(question + 1);
        }

        ParseResponseQuery(query, out rid, out retryAfterSeconds);
        return rid.Length > 0;
    }

    private static void ParseResponseQuery(ReadOnlySpan<char> query, out string rid, out int? retryAfterSeconds)
    {
        rid = string.Empty;
        retryAfterSeconds = null;

        while (!query.IsEmpty)
        {
            var amp = query.IndexOf('&');
            var pair = amp < 0 ? query : query.Slice(0, amp);
            query = amp < 0 ? default : query.Slice(amp + 1);
            if (pair.IsEmpty)
            {
                continue;
            }

            var equals = pair.IndexOf('=');
            var key = equals < 0 ? pair : pair.Slice(0, equals);
            var value = equals < 0 ? default : pair.Slice(equals + 1);
            if (key.SequenceEqual("$rid".AsSpan()))
            {
                rid = Uri.UnescapeDataString(value.ToString());
            }
            else if (key.SequenceEqual("retry-after".AsSpan())
                && int.TryParse(
                    value.ToString(),
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var retryAfter))
            {
                retryAfterSeconds = retryAfter;
            }
        }
    }
}
