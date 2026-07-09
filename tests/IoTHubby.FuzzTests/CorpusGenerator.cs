// Copyright (c) marcschier. Licensed under the MIT License.

using System.Text;

namespace IoTHubby.FuzzTests;

/// <summary>
/// Writes a small set of valid wire-format inputs to corpus directories so libFuzzer starts with
/// meaningful coverage instead of pure random bytes. Invoke with <c>--seed-corpus</c>.
/// </summary>
internal static class CorpusGenerator
{
    public static void GenerateAll(string corpusRoot)
    {
        var topics = Path.Combine(corpusRoot, "topics");
        var codec = Path.Combine(corpusRoot, "codec");
        var propertyBag = Path.Combine(corpusRoot, "propertybag");
        var connectionString = Path.Combine(corpusRoot, "connstring");
        var twinJson = Path.Combine(corpusRoot, "twinjson");
        var dpsResponse = Path.Combine(corpusRoot, "dpsresponse");

        Directory.CreateDirectory(topics);
        Directory.CreateDirectory(codec);
        Directory.CreateDirectory(propertyBag);
        Directory.CreateDirectory(connectionString);
        Directory.CreateDirectory(twinJson);
        Directory.CreateDirectory(dpsResponse);

        WriteTopicsSeeds(topics);
        WritePropertyBagSeeds(codec);
        WritePropertyBagSeeds(propertyBag);
        WriteConnectionStringSeeds(connectionString);
        WriteTwinJsonSeeds(twinJson);
        WriteDpsResponseSeeds(dpsResponse);
    }

    private static void WriteTopicsSeeds(string dir)
    {
        WriteText(dir, "method-request.txt", "$iothub/methods/POST/reboot/?$rid=42");
        WriteText(dir, "twin-response.txt", "$iothub/twin/res/200/?$rid=abc&$version=7");
        WriteText(dir, "desired-version.txt", "$iothub/twin/PATCH/properties/desired/?$version=12");
        WriteText(dir, "module-input.txt", "devices/d/modules/m/inputs/input1/$.ct=application%2Fjson&k=v");
    }

    private static void WritePropertyBagSeeds(string dir)
    {
        WriteText(dir, "empty.txt", string.Empty);
        WriteText(dir, "app-properties.txt", "temperature=21&unit=C");
        WriteText(dir, "system-properties.txt", "$.mid=m1&$.ct=application%2Fjson&$.ce=utf-8");
        WriteText(dir, "encoded.txt", "space=a%20b&slash=a%2Fb&unicode=%E2%9C%93");
    }

    private static void WriteConnectionStringSeeds(string dir)
    {
        WriteText(dir, "device-key.txt",
            "HostName=my-hub.azure-devices.net;DeviceId=device1;SharedAccessKey=ZmFrZUtleQ==");
        WriteText(dir, "module-key.txt",
            "HostName=my-hub.azure-devices.net;DeviceId=device1;ModuleId=module1;SharedAccessKey=ZmFrZUtleQ==");
        WriteText(dir, "x509.txt", "HostName=my-hub.azure-devices.net;DeviceId=device1;X509=true");
    }

    private static void WriteTwinJsonSeeds(string dir)
    {
        WriteText(dir, "empty-object.json", "{}");
        WriteText(dir, "desired-reported.json",
            "{\"desired\":{\"targetTemperature\":21,\"$version\":4},\"reported\":{\"status\":\"ok\",\"$version\":9}}");
    }

    private static void WriteDpsResponseSeeds(string dir)
    {
        WriteText(dir, "register-accepted.txt", "$dps/registrations/res/202/?$rid=1&retry-after=3");
        WriteText(dir, "operation-complete.txt", "$dps/registrations/res/200/?$rid=2");
    }

    private static void WriteText(string dir, string fileName, string value)
        => File.WriteAllBytes(Path.Combine(dir, fileName), Encoding.UTF8.GetBytes(value));
}
