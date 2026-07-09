// Copyright (c) marcschier. Licensed under the MIT License.

using System.Text;

namespace IoTHubby.FuzzTests;

/// <summary>
/// libFuzzer harness for <see cref="IoTHubConnectionString.Parse"/>. Contract: only documented
/// validation exceptions may be thrown for arbitrary connection strings.
/// </summary>
internal static class ConnectionStringHarness
{
    public static void Run(ReadOnlySpan<byte> data)
    {
        var connectionString = Encoding.UTF8.GetString(data);
        try
        {
            IoTHubConnectionString.Parse(connectionString);
        }
        catch (FormatException)
        {
        }
        catch (ArgumentException)
        {
        }
    }
}
