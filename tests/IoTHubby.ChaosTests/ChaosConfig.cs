// Copyright (c) marcschier. Licensed under the MIT License.

using System.Globalization;

namespace IoTHubby.ChaosTests;

internal sealed class ChaosConfig
{
    public TimeSpan Duration { get; private set; } = TimeSpan.FromHours(1);

    public int Clients { get; private set; } = 4;

    public int Seed { get; private set; } = Environment.TickCount;

    public string ReportDir { get; private set; } = "chaos-report";

    public static ChaosConfig Parse(string[] args)
    {
        var config = new ChaosConfig();

        for (var i = 0; i < args.Length; i++)
        {
            var key = args[i];
            string? Next() => i + 1 < args.Length ? args[++i] : null;

            switch (key)
            {
                case "--duration":
                    config.Duration = ParseDuration(Next());
                    break;
                case "--clients":
                    config.Clients = Math.Max(1, ParseInt(Next(), config.Clients));
                    break;
                case "--seed":
                    config.Seed = ParseInt(Next(), config.Seed);
                    break;
                case "--report":
                    config.ReportDir = Next() ?? config.ReportDir;
                    break;
                default:
                    throw new ArgumentException($"Unknown argument: {key}");
            }
        }

        return config;
    }

    public override string ToString()
        => string.Create(
            CultureInfo.InvariantCulture,
            $"duration={Duration} clients={Clients} seed={Seed} report={ReportDir}");

    private static TimeSpan ParseDuration(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return TimeSpan.FromHours(1);
        }

        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds))
        {
            return TimeSpan.FromSeconds(seconds);
        }

        if (TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out var duration))
        {
            return duration;
        }

        throw new ArgumentException($"Invalid --duration: {value}");
    }

    private static int ParseInt(string? value, int fallback)
        => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : fallback;
}
