// Copyright (c) marcschier. Licensed under the MIT License.

using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using IoTHubby.Edge;

namespace IoTHubby.IntegrationTests;

[NotInParallel("edge-environment")]
public sealed class EdgeBootstrapIntegrationTests
{
    [Test]
    public async Task Edge_module_bootstraps_from_workload_and_connects()
    {
        await using var host = await TestHost.StartAsync();

        var vars = new (string Key, string Value)[]
        {
            ("IOTEDGE_IOTHUBHOSTNAME", "edge-hub.azure-devices.net"),
            ("IOTEDGE_GATEWAYHOSTNAME", "gateway"),
            ("IOTEDGE_DEVICEID", "edge-dev"),
            ("IOTEDGE_MODULEID", "edge-mod"),
            ("IOTEDGE_MODULEGENERATIONID", "gen-1"),
            ("IOTEDGE_WORKLOADURI", "unix:///tmp/iotedge/workload.sock"),
            ("IOTEDGE_APIVERSION", "2019-01-30"),
        };
        // EdgeHubConnectionString must be absent so the workload path runs.
        Environment.SetEnvironmentVariable("EdgeHubConnectionString", null);
        Environment.SetEnvironmentVariable("IotHubConnectionString", null);
        foreach (var (k, v) in vars)
        {
            Environment.SetEnvironmentVariable(k, v);
        }

        try
        {
            using var handler = new FakeWorkloadHandler();
            await using var module = await EdgeModuleClient.CreateFromEnvironmentAsync(
                o =>
                {
                    o.EndpointHostOverride = "127.0.0.1";
                    o.EndpointPortOverride = host.Broker.Port;
                    o.DisableTls = true;
                },
                handler);

            await module.ConnectAsync();
            await Assert.That(module.State).IsEqualTo(IoTHubConnectionState.Connected);

            await module.SendTelemetryAsync(TelemetryMessage.FromString("{\"edge\":true}"));
            var captured = await host.Hub.WaitForTelemetryAsync(TimeSpan.FromSeconds(10));
            await Assert.That(captured.Topic).StartsWith("devices/edge-dev/modules/edge-mod/messages/events/");

            await Assert.That(handler.SignCalled).IsTrue();
            await Assert.That(handler.TrustBundleCalled).IsTrue();
        }
        finally
        {
            foreach (var (k, _) in vars)
            {
                Environment.SetEnvironmentVariable(k, null);
            }
        }
    }

    /// <summary>Fake Edge Workload API: answers the sign and trust-bundle endpoints.</summary>
    private sealed class FakeWorkloadHandler : HttpMessageHandler
    {
        private static readonly string TrustBundlePem = CreateSelfSignedPem();

        public bool SignCalled { get; private set; }

        public bool TrustBundleCalled { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
            var path = request.RequestUri!.AbsolutePath;
            if (path.Contains("/sign", StringComparison.Ordinal))
            {
                SignCalled = true;
                var digest = Convert.ToBase64String(new byte[32]);
                return Json($"{{\"digest\":\"{digest}\"}}");
            }
            if (path.Contains("trust-bundle", StringComparison.Ordinal))
            {
                TrustBundleCalled = true;
                var escaped = TrustBundlePem.Replace("\n", "\\n", StringComparison.Ordinal);
                return Json($"{{\"certificate\":\"{escaped}\"}}");
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        private static HttpResponseMessage Json(string body)
            => new(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };

        private static string CreateSelfSignedPem()
        {
            using var rsa = RSA.Create(2048);
            var request = new CertificateRequest(
                "CN=edge-test-ca", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            using var cert = request.CreateSelfSigned(
                DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
            return cert.ExportCertificatePem();
        }
    }
}
