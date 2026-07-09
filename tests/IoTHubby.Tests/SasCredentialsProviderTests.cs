// Copyright (c) marcschier. Licensed under the MIT License.

using System.Text;
using Mqtt.Client;

namespace IoTHubby.Tests;

public sealed class SasCredentialsProviderTests
{
    [Test]
    public async Task Supplies_username_and_sas_password()
    {
        using var provider = new SasCredentialsProvider(
            "h/dev1/?api-version=2021-04-12",
            "h/devices/dev1",
            "AAAAAAAAAAAAAAAAAAAAAA==",
            TimeSpan.FromHours(1),
            0.85);

        var credentials = await ((IMqttCredentialsProvider)provider).GetCredentialsAsync(CancellationToken.None);

        await Assert.That(credentials.Username).IsEqualTo("h/dev1/?api-version=2021-04-12");
        var password = Encoding.UTF8.GetString(credentials.Password!);
        await Assert.That(password).StartsWith("SharedAccessSignature sr=h%2Fdevices%2Fdev1&sig=");
        await Assert.That(password).Contains("&se=");
    }

    [Test]
    public async Task Raises_credentials_changed_on_renewal()
    {
        using var provider = new SasCredentialsProvider(
            "u", "h/devices/dev1", "AAAAAAAAAAAAAAAAAAAAAA==", TimeSpan.FromSeconds(1), 0.001);

        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        ((IMqttCredentialsChangeNotifier)provider).CredentialsChanged += (_, _) => tcs.TrySetResult(true);

        // First fetch arms the (very short) renewal timer.
        _ = await ((IMqttCredentialsProvider)provider).GetCredentialsAsync(CancellationToken.None);

        var raised = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(5)));
        await Assert.That(raised).IsEqualTo((Task)tcs.Task);
    }
}
