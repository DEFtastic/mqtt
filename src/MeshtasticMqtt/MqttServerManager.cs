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
        _packetHandler = new PacketHandler(clientDatabase);
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var factory = new MqttServerFactory();
        var options = BuildOptions();
        _mqttServer = factory.CreateMqttServer(options);

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
        string certPath;
        string keyPath;

        var letsEncryptCert = "/app/certs/fullchain.pem";
        var letsEncryptKey = "/app/certs/privkey.pem";

        if (File.Exists(letsEncryptCert) && File.Exists(letsEncryptKey))
        {
            certPath = letsEncryptCert;
            keyPath = letsEncryptKey;
            Console.WriteLine("Using Let's Encrypt certificate from /app/certs");
        }
        else
        {
            certPath = Path.Combine(AppContext.BaseDirectory, "data", "cert.pem");
            keyPath = Path.Combine(AppContext.BaseDirectory, "data", "key.pem");
            Console.WriteLine("Using auto-generated certificate from /app/data");
        }

        var cert = X509Certificate2.CreateFromPemFile(certPath, keyPath);

        return new MqttServerOptionsBuilder()
            .WithDefaultEndpoint()
            .WithDefaultEndpointPort(1883)
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