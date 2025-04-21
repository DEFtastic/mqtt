using MQTTnet.Server;
using Meshtastic.Protobufs;
using Google.Protobuf;
using Serilog;
using MQTTnet.Protocol;
using System.Runtime.Loader;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog.Formatting.Compact;
using Meshtastic.Crypto;
using Meshtastic;
using System.IO;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Reflection;

await RunMqttServer(args);

void InitializeDatabase()
{
    if (!File.Exists("clients.db"))
    {
        using var connection = new SqliteConnection("Data Source=clients.db");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText =
        @"
            CREATE TABLE Clients (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                ClientId TEXT NOT NULL,
                ConnectedAt TEXT NOT NULL
            );
        ";
        command.ExecuteNonQuery();
    }
}

async Task RunMqttServer(string[] args)
{
    InitializeDatabase();
    
    Log.Logger = new LoggerConfiguration()
        .MinimumLevel.Debug()
        .WriteTo.Console(new RenderedCompactJsonFormatter())
        // .WriteTo.File(new RenderedCompactJsonFormatter(), "log.json", rollingInterval: RollingInterval.Hour)
        .CreateLogger();

    using var mqttServer = new MqttServerFactory()
        .CreateMqttServer(BuildMqttServerOptions());
    ConfigureMqttServer(mqttServer);

    // Set up host
    using var host = CreateHostBuilder(args).Build();
    await host.StartAsync();
    var lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();

    await mqttServer.StartAsync();

    // Configure graceful shutdown
    await SetupGracefulShutdown(mqttServer, lifetime, host);
}

MqttServerOptions BuildMqttServerOptions()
{
    var currentPath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;

    #pragma warning disable SYSLIB0057 // Type or member is obsolete
    var certificate = new X509Certificate2(
        Path.Combine(currentPath, "certificate.pfx"),
        "large4cats",
        X509KeyStorageFlags.Exportable);
    #pragma warning restore SYSLIB0057

    var options = new MqttServerOptionsBuilder()
        .WithoutDefaultEndpoint()
        .WithEncryptedEndpoint()
        .WithEncryptedEndpointPort(8883)
        .WithEncryptionCertificate(certificate.Export(X509ContentType.Pfx))
        .WithEncryptionSslProtocol(SslProtocols.Tls12)
        .Build();

    Log.Logger.Information("Using SSL certificate for MQTT server");
    return options;
}

void ConfigureMqttServer(MqttServer mqttServer)
{
    mqttServer.InterceptingPublishAsync += HandleInterceptingPublish;
    mqttServer.InterceptingSubscriptionAsync += HandleInterceptingSubscription;
    mqttServer.ValidatingConnectionAsync += HandleValidatingConnection;
}

async Task HandleInterceptingPublish(InterceptingPublishEventArgs args)
{
    try 
    {
        if (args.ApplicationMessage.Payload.Length == 0)
        {
            Log.Logger.Warning("Received empty payload on topic {@Topic} from {@ClientId}", args.ApplicationMessage.Topic, args.ClientId);
            args.ProcessPublish = false;
            return;
        }

        var serviceEnvelope = ServiceEnvelope.Parser.ParseFrom(args.ApplicationMessage.Payload);

        if (IsRoutingAck(serviceEnvelope))
        {
            Log.Logger.Debug("Confirmed routing ACK/NACK packet. Allowing through.");
            args.ProcessPublish = true;
            return;
        }

        if (!IsValidServiceEnvelope(serviceEnvelope))
        {
            Log.Logger.Warning("Service envelope or packet is malformed. Blocking packet on topic {@Topic} from {@ClientId}",
                args.ApplicationMessage.Topic, args.ClientId);
            args.ProcessPublish = false;
            return;
        }

        // Spot for any async operations we might want to perform
        await Task.FromResult(0);

        var data = DecryptMeshPacket(serviceEnvelope);

        // Uncomment to block unrecognized packets
        // if (data == null)
        // {
        //     Log.Logger.Warning("Service envelope does not contain a valid packet. Blocking packet");
        //     args.ProcessPublish = false;
        //     return;
        // }

        LogReceivedMessage(args.ApplicationMessage.Topic, args.ClientId, data);
        args.ProcessPublish = true;
    }
    catch (InvalidProtocolBufferException)
    {
        Log.Logger.Warning("Failed to decode presumed protobuf packet. Blocking");
        args.ProcessPublish = false;
    }
    catch (Exception ex)
    {
        Log.Logger.Error("Exception occurred while processing packet on {@Topic} from {@ClientId}: {@Exception}",
            args.ApplicationMessage.Topic, args.ClientId, ex.Message);
        args.ProcessPublish = false;
    }
}

Task HandleInterceptingSubscription(InterceptingSubscriptionEventArgs args)
{
    // Add filtering logic here if needed
    args.ProcessSubscription = true;
    return Task.CompletedTask;
}

Task HandleValidatingConnection(ValidatingConnectionEventArgs args)
{
    // Add connection / authentication logic here if needed
    args.ReasonCode = MqttConnectReasonCode.Success;

    Log.Logger.Information("New client connected: {@ClientId}", args.ClientId);

    using var connection = new SqliteConnection("Data Source=clients.db");
    connection.Open();

    using var command = connection.CreateCommand();
    command.CommandText =
    @"
        INSERT INTO Clients (ClientId, ConnectedAt)
        VALUES ($clientId, $connectedAt);
    ";
    command.Parameters.AddWithValue("$clientId", args.ClientId);
    command.Parameters.AddWithValue("$connectedAt", DateTime.UtcNow.ToString("o"));
    command.ExecuteNonQuery();

    return Task.CompletedTask;
}

bool IsValidServiceEnvelope(ServiceEnvelope serviceEnvelope)
{
    return !(String.IsNullOrWhiteSpace(serviceEnvelope.ChannelId) ||
            String.IsNullOrWhiteSpace(serviceEnvelope.GatewayId) ||
            serviceEnvelope.Packet == null ||
            serviceEnvelope.Packet.Id < 1 ||
            serviceEnvelope.Packet.From < 1 ||
            serviceEnvelope.Packet.Encrypted == null ||
            serviceEnvelope.Packet.Encrypted.Length < 1 ||
            serviceEnvelope.Packet.Decoded != null);
}

bool IsRoutingAck(ServiceEnvelope serviceEnvelope)
{
    try
    {
        if (serviceEnvelope.Packet == null)
            return false;

        var nonce = new NonceGenerator(serviceEnvelope.Packet.From, serviceEnvelope.Packet.Id).Create();
        var decrypted = PacketEncryption.TransformPacket(serviceEnvelope.Packet.Encrypted.ToByteArray(), nonce, Resources.DEFAULT_PSK);
        var payload = Data.Parser.ParseFrom(decrypted);

        return payload.Portnum == PortNum.RoutingApp &&
               payload.Payload.Length > 0 &&
               payload.Payload.Length <= 10;
    }
    catch
    {
        return false;
    }
}

void LogReceivedMessage(string topic, string clientId, Data? data)
{
    if (data?.Portnum == PortNum.TextMessageApp)
    {
        Log.Logger.Information("Received text message on topic {@Topic} from {@ClientId}: {@Message}",
            topic, clientId, data.Payload.ToStringUtf8());
    }
    else
    {
        Log.Logger.Information("Received packet on topic {@Topic} from {@ClientId} with port number: {@Portnum}",
            topic, clientId, data?.Portnum);
    }
}

static Data? DecryptMeshPacket(ServiceEnvelope serviceEnvelope)
{
    var nonce = new NonceGenerator(serviceEnvelope.Packet.From, serviceEnvelope.Packet.Id).Create();
    var decrypted = PacketEncryption.TransformPacket(serviceEnvelope.Packet.Encrypted.ToByteArray(), nonce, Resources.DEFAULT_PSK);
    var payload = Data.Parser.ParseFrom(decrypted);

    if (payload.Portnum > PortNum.UnknownApp && payload.Payload.Length > 0)
        return payload;

    return null;
}

async Task SetupGracefulShutdown(MqttServer mqttServer, IHostApplicationLifetime lifetime, IHost host)
{
    var ended = new ManualResetEventSlim();
    var starting = new ManualResetEventSlim();

    AssemblyLoadContext.Default.Unloading += ctx =>
    {
        starting.Set();
        Log.Logger.Debug("Waiting for completion");
        ended.Wait();
    };

    starting.Wait();

    Log.Logger.Debug("Received signal gracefully shutting down");
    await mqttServer.StopAsync();
    Thread.Sleep(500);
    ended.Set();

    lifetime.StopApplication();
    await host.WaitForShutdownAsync();
}

static IHostBuilder CreateHostBuilder(string[] args)
{
    return Host.CreateDefaultBuilder(args)
        .UseConsoleLifetime()
        .ConfigureServices((hostContext, services) =>
        {
            services.AddSingleton(Console.Out);
        });
}
