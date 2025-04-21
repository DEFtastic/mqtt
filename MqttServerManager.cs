using Microsoft.Extensions.Hosting;
using MQTTnet.Server;
using Serilog;
using Meshtastic.Protobufs;
using Meshtastic;
using Google.Protobuf;
using Microsoft.Data.Sqlite;
using System.Security.Cryptography.X509Certificates;
using System.Security.Authentication;
using System.Reflection;
using System.Runtime.Loader;

namespace MeshtasticMqtt;

public class MqttServerManager : IHostedService
{
    private readonly ClientDatabase _db;
    private IMqttServer? _mqttServer;

    public MqttServerManager(ClientDatabase db)
    {
        _db = db;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        Log.Logger.Information("Starting MQTT server...");

        _db.InitializeDatabase();

        var mqttServer = new MqttServerFactory().CreateMqttServer(BuildOptions());
        ConfigureServer(mqttServer);

        _mqttServer = mqttServer;
        await _mqttServer.StartAsync();
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        Log.Logger.Information("Stopping MQTT server...");
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

    private void ConfigureServer(IMqttServer server)
    {
        server.InterceptingPublishAsync += HandlePublishAsync;
        server.ValidatingConnectionAsync += HandleConnectionAsync;
        server.InterceptingSubscriptionAsync += args =>
        {
            args.ProcessSubscription = true;
            return Task.CompletedTask;
        };
    }

    private async Task HandlePublishAsync(InterceptingPublishEventArgs args)
    {
        try
        {
            if (args.ApplicationMessage.Payload.Length == 0)
            {
                Log.Warning("Empty payload on {@Topic} from {@ClientId}", args.ApplicationMessage.Topic, args.ClientId);
                args.ProcessPublish = false;
                return;
            }

            var envelope = ServiceEnvelope.Parser.ParseFrom(args.ApplicationMessage.Payload);

            if (!IsValidEnvelope(envelope))
            {
                Log.Warning("Malformed packet on {@Topic} from {@ClientId}", args.ApplicationMessage.Topic, args.ClientId);
                args.ProcessPublish = false;
                return;
            }

            args.ProcessPublish = true;
        }
        catch (InvalidProtocolBufferException)
        {
            Log.Warning("Failed to parse protobuf packet.");
            args.ProcessPublish = false;
        }
        catch (Exception ex)
        {
            Log.Error("Error during publish handling: {@Error}", ex.Message);
            args.ProcessPublish = false;
        }
    }

    private async Task HandleConnectionAsync(ValidatingConnectionEventArgs args)
    {
        args.ReasonCode = MqttConnectReasonCode.Success;
        _db.InsertClient(args.ClientId ?? "unknown");

        Log.Information("New client connected: {@ClientId}", args.ClientId);
    }

    private bool IsValidEnvelope(ServiceEnvelope env)
    {
        return !(string.IsNullOrWhiteSpace(env.ChannelId) ||
                 string.IsNullOrWhiteSpace(env.GatewayId) ||
                 env.Packet == null ||
                 env.Packet.Id < 1 ||
                 env.Packet.From < 1 ||
                 env.Packet.Encrypted == null ||
                 env.Packet.Encrypted.Length < 1 ||
                 env.Packet.Decoded != null);
    }
}