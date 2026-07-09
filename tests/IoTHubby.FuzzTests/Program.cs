// Copyright (c) marcschier. Licensed under the MIT License.

using SharpFuzz;

namespace IoTHubby.FuzzTests;

/// <summary>
/// libFuzzer entry point. The first command-line argument selects the harness:
/// <c>topics</c> | <c>codec</c> | <c>propertybag</c> | <c>connstring</c> | <c>twinjson</c> |
/// <c>dpsresponse</c>. Selection can also be made via the <c>FUZZ_HARNESS</c> environment variable.
/// </summary>
public static class Program
{
    public static int Main(string[] args)
    {
        if (args.Length >= 2 && args[0] == "--seed-corpus")
        {
            CorpusGenerator.GenerateAll(args[1]);
            Console.WriteLine($"Seeded corpus at {args[1]}");
            return 0;
        }

        var name = args.Length > 0
            ? args[0]
            : Environment.GetEnvironmentVariable("FUZZ_HARNESS") ?? "topics";
        ReadOnlySpanAction action = name.ToLowerInvariant() switch
        {
            "topics" => TopicsHarness.Run,
            "codec" => PropertyBagHarness.Run,
            "propertybag" => PropertyBagHarness.Run,
            "connstring" => ConnectionStringHarness.Run,
            "twinjson" => TwinJsonHarness.Run,
            "dpsresponse" => DpsResponseHarness.Run,
            _ => throw new ArgumentException($"Unknown harness: {name}"),
        };
        Fuzzer.LibFuzzer.Run(action);
        return 0;
    }
}
