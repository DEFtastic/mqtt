namespace MeshtasticMqtt;

using Meshtastic;
using Meshtastic.Protobufs;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using MQTTnet.Server;
using Serilog;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using System.Security.Authentication;

public class MqttServerManager : IHostedService
{
    private MqttServer _mqttServer;
    private readonly ClientDatabase _clientDatabase;
    private readonly PacketHandler _packetHandler;

    public MqttServerManager(ClientDatabase clientDatabase)
    {
        _clientDatabase = clientDatabase;
        _packetHandler = new PacketHandler(clientDatabase); // <-- NEW

        var factory = new MqttServerFactory();
        var options = BuildOptions();
        _mqttServer = factory.CreateMqttServer(options);
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        ConfigureServer(_mqttServer);
        await _mqttServer.StartAsync();
        Log.Information("MQTT server started successfully.");
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        Log.Information("Stopping MQTT server...");
        if (_mqttServer != null)
        {
            await _mqttServer.StopAsync();
        }
    }

    private MqttServerOptions BuildOptions()
    {
        var path = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;

#pragma warning disable SYSLIB0057
        var cert = new X509Certificate2(
            Path.Combine(path, "certificate.pfx"),
            "large4cats",
            X509KeyStorageFlags.Exportable
        );
#pragma warning restore SYSLIB0057

        return new MqttServerOptionsBuilder()
            .WithoutDefaultEndpoint()
            .WithEncryptedEndpoint()
            .WithEncryptedEndpointPort(8883)
            .WithEncryptionCertificate(cert.Export(X509ContentType.Pfx))
            .WithEncryptionSslProtocol(SslProtocols.Tls12)
            .Build();
    }

    private void ConfigureServer(MqttServer server)
    {
        server.InterceptingPublishAsync += _packetHandler.HandleInterceptingPublish;
        server.InterceptingSubscriptionAsync += _packetHandler.HandleInterceptingSubscription;
        server.ValidatingConnectionAsync += _packetHandler.HandleValidatingConnection;
    }
}