// Copyright (c) marcschier. Licensed under the MIT License.

using IoTHubby;
using IoTHubby.Edge;

// IoTHubby samples. Pick a scenario by name; connection material is read from environment variables
// so nothing is hard-coded. Nothing connects unless the relevant variables are set.
//
//   dotnet run --project samples/IoTHubby.Samples -- telemetry
//   dotnet run --project samples/IoTHubby.Samples -- c2d
//   dotnet run --project samples/IoTHubby.Samples -- methods
//   dotnet run --project samples/IoTHubby.Samples -- twin
//   dotnet run --project samples/IoTHubby.Samples -- module
//   dotnet run --project samples/IoTHubby.Samples -- edge

var scenario = args.Length > 0 ? args[0] : "help";

switch (scenario)
{
    case "telemetry":
        await Samples.TelemetryAsync();
        break;
    case "c2d":
        await Samples.CloudToDeviceAsync();
        break;
    case "methods":
        await Samples.MethodsAsync();
        break;
    case "twin":
        await Samples.TwinAsync();
        break;
    case "module":
        await Samples.ModuleAsync();
        break;
    case "edge":
        await Samples.EdgeAsync();
        break;
    default:
        Console.WriteLine("Scenarios: telemetry | c2d | methods | twin | module | edge");
        break;
}

internal static class Samples
{
    private static string DeviceConnectionString =>
        Environment.GetEnvironmentVariable("IOTHUB_DEVICE_CONNECTION_STRING")
        ?? throw new InvalidOperationException("Set IOTHUB_DEVICE_CONNECTION_STRING.");

    private static string ModuleConnectionString =>
        Environment.GetEnvironmentVariable("IOTHUB_MODULE_CONNECTION_STRING")
        ?? throw new InvalidOperationException("Set IOTHUB_MODULE_CONNECTION_STRING.");

    public static async Task TelemetryAsync()
    {
        await using var client = IoTHubDeviceClient.CreateFromConnectionString(DeviceConnectionString);
        await client.ConnectAsync();

        for (var i = 0; i < 5; i++)
        {
            var message = TelemetryMessage.FromString($"{{\"count\":{i}}}");
            message.ContentType = "application/json";
            message.ContentEncoding = "utf-8";
            message.Properties["priority"] = "high";
            await client.SendTelemetryAsync(message);
            Console.WriteLine($"Sent telemetry #{i}.");
            await Task.Delay(1000);
        }
    }

    public static async Task CloudToDeviceAsync()
    {
        await using var client = IoTHubDeviceClient.CreateFromConnectionString(DeviceConnectionString);
        await client.ConnectAsync();

        Console.WriteLine("Waiting for cloud-to-device messages (Ctrl+C to stop)...");
        await foreach (var message in client.ReceiveCloudToDeviceMessagesAsync())
        {
            Console.WriteLine($"C2D: {message.PayloadAsString} (messageId={message.MessageId})");
        }
    }

    public static async Task MethodsAsync()
    {
        await using var client = IoTHubDeviceClient.CreateFromConnectionString(DeviceConnectionString);
        await client.ConnectAsync();

        await client.SetMethodHandlerAsync((request, _) =>
        {
            Console.WriteLine($"Method '{request.Name}' called with {request.PayloadAsString}");
            var response = DirectMethodResponse.FromString(200, "{\"result\":\"ok\"}");
            return new ValueTask<DirectMethodResponse>(response);
        });

        Console.WriteLine("Handling direct methods (Ctrl+C to stop)...");
        await Task.Delay(Timeout.Infinite);
    }

    public static async Task TwinAsync()
    {
        await using var client = IoTHubDeviceClient.CreateFromConnectionString(DeviceConnectionString);
        await client.ConnectAsync();

        var twin = await client.GetTwinAsync();
        Console.WriteLine($"Desired (v{twin.Desired.Version}): {twin.Desired.ToJsonString()}");

        var version = await client.UpdateReportedPropertiesAsync("{\"status\":\"online\"}");
        Console.WriteLine($"Reported updated to version {version}.");

        await foreach (var update in client.ReceiveDesiredPropertyUpdatesAsync())
        {
            Console.WriteLine($"Desired changed (v{update.Properties.Version}): {update.Properties.ToJsonString()}");
        }
    }

    public static async Task ModuleAsync()
    {
        await using var client = IoTHubModuleClient.CreateFromConnectionString(ModuleConnectionString);
        await client.ConnectAsync();

        await client.SendToOutputAsync("output1", TelemetryMessage.FromString("{\"hello\":\"world\"}"));
        Console.WriteLine("Sent to output1.");

        await foreach (var message in client.ReceiveInputMessagesAsync("input1"))
        {
            Console.WriteLine($"Input1: {message.PayloadAsString}");
        }
    }

    public static async Task EdgeAsync()
    {
        // Runs inside the IoT Edge runtime: identity + trust are taken from IOTEDGE_* variables and
        // the Workload API. No connection string required.
        await using var client = await EdgeModuleClient.CreateFromEnvironmentAsync();
        await client.ConnectAsync();
        await client.SendTelemetryAsync(TelemetryMessage.FromString("{\"edge\":true}"));
        Console.WriteLine("Edge module connected and sent telemetry.");
    }
}
