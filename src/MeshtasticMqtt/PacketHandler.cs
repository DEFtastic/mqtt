namespace MeshtasticMqtt;

using MQTTnet.Server;
using MQTTnet;
using MQTTnet.Protocol;
using Meshtastic;
using Meshtastic.Crypto;
using Meshtastic.Protobufs;
using MeshtasticMqtt;
using Google.Protobuf;
using Serilog;
using System.Buffers;

public class PacketHandler
{
    private readonly ClientDatabase _clientDatabase;

    public PacketHandler(ClientDatabase clientDatabase)
    {
        _clientDatabase = clientDatabase;
    }

    public Task HandleInterceptingSubscription(InterceptingSubscriptionEventArgs args)
    {
        args.ProcessSubscription = true;
        return Task.CompletedTask;
    }

    public Task HandleValidatingConnection(ValidatingConnectionEventArgs args)
    {
        args.ReasonCode = MqttConnectReasonCode.Success;
        Log.Information("New client connected: {@ClientId}", args.ClientId);
        _clientDatabase.InsertClient(args.ClientId);
        return Task.CompletedTask;
    }

    public Task HandleInterceptingPublish(InterceptingPublishEventArgs args)
    {
        // Skip JSON/plaintext MQTT publishes
        if (!args.ApplicationMessage.Topic.Contains("/e/"))
        {
            args.ProcessPublish = true;
            return Task.CompletedTask;
        }

        var payloadBytes = args.ApplicationMessage.Payload.ToArray();

        try
        {
            if (payloadBytes.Length == 0)
            {
                Log.Warning("Empty payload on topic {@Topic} from {@ClientId}", args.ApplicationMessage.Topic, args.ClientId);
                args.ProcessPublish = false;
                return Task.CompletedTask;
            }

            var serviceEnvelope = ParseServiceEnvelope(payloadBytes);

            if (serviceEnvelope == null || !IsValidServiceEnvelope(serviceEnvelope))
            {
                Log.Warning("Service envelope or packet is malformed. Blocking packet on topic {Topic} from {ClientId}", args.ApplicationMessage.Topic, args.ClientId);
                args.ProcessPublish = false;
                return Task.CompletedTask;
            }

            if (IsRoutingAck(serviceEnvelope))
            {
                Log.Debug("Routing ACK/NACK packet confirmed. Allowing.");
                args.ProcessPublish = true;
                return Task.CompletedTask;
            }

            var data = DecryptMeshPacket(serviceEnvelope);

            if (data?.Portnum == PortNum.TextMessageApp)
            {
                _clientDatabase.InsertMessage(args.ClientId, data.Payload.ToStringUtf8());
            }

            LogReceivedMessage(args.ApplicationMessage.Topic, args.ClientId, data);
            args.ProcessPublish = true;
        }
        catch (InvalidProtocolBufferException ex)
        {
            Log.Warning("Failed to decode protobuf packet: {Exception}. Blocking", ex.Message);
            args.ProcessPublish = false;
        }
        catch (Exception ex)
        {
            Log.Error("Error processing packet on {@Topic} from {@ClientId}: {@Exception}",
                args.ApplicationMessage.Topic, args.ClientId, ex.Message);
            args.ProcessPublish = false;
        }

        return Task.CompletedTask;
    }

    private ServiceEnvelope? ParseServiceEnvelope(byte[] payload)
    {
        try
        {
            return ServiceEnvelope.Parser.ParseFrom(payload);
        }
        catch (Exception ex)
        {
            Log.Warning("Failed to parse service envelope: {Exception}", ex.Message);
            return null;
        }
    }

    private static bool IsValidServiceEnvelope(ServiceEnvelope serviceEnvelope)
    {
        List<string> issues = new List<string>();

        if (string.IsNullOrWhiteSpace(serviceEnvelope.ChannelId))
        {
            issues.Add("Invalid ChannelId");
        }
        if (string.IsNullOrWhiteSpace(serviceEnvelope.GatewayId))
        {
            issues.Add("Invalid GatewayId");
        }
        if (serviceEnvelope.Packet == null || serviceEnvelope.Packet.Id < 1)
        {
            issues.Add("Invalid Packet");
        }
        if (serviceEnvelope.Packet?.Encrypted == null || serviceEnvelope.Packet.Encrypted.Length < 1)
        {
            issues.Add("Missing Encrypted data");
        }

        if (issues.Any())
        {
            Log.Warning("Service envelope validation failed: {Issues}", string.Join(", ", issues));
            return false;
        }

        return true;
    }

    private static bool IsRoutingAck(ServiceEnvelope serviceEnvelope)
    {
        try
        {
            if (serviceEnvelope.Packet == null) return false;

            var nonce = new NonceGenerator(serviceEnvelope.Packet.From, serviceEnvelope.Packet.Id).Create();
            var decrypted = PacketEncryption.TransformPacket(serviceEnvelope.Packet.Encrypted.ToArray(), nonce, Resources.DEFAULT_PSK);
            var payload = Meshtastic.Protobufs.Data.Parser.ParseFrom(decrypted);

            return payload.Portnum == PortNum.RoutingApp &&
                   payload.Payload.Length > 0 &&
                   payload.Payload.Length <= 10;
        }
        catch (Exception ex)
        {
            Log.Warning("Error checking Routing ACK: {Exception}", ex.Message);
            return false;
        }
    }

    private static void LogReceivedMessage(string topic, string clientId, Meshtastic.Protobufs.Data? data)
    {
        if (data?.Portnum == PortNum.TextMessageApp)
        {
            Log.Information("Received text message on topic {@Topic} from {@ClientId}: {@Message}",
                topic, clientId, data.Payload.ToStringUtf8());
        }
        else
        {
            Log.Information("Received packet on topic {@Topic} from {@ClientId} with port number: {@Portnum}",
                topic, clientId, data?.Portnum);
        }
    }

    private static Meshtastic.Protobufs.Data? DecryptMeshPacket(ServiceEnvelope serviceEnvelope)
    {
        try
        {
            Log.Information("Decrypting packet from {From}, ID: {Id}", serviceEnvelope.Packet.From, serviceEnvelope.Packet.Id);

            var encryptedData = serviceEnvelope.Packet.Encrypted.ToByteArray();
            var nonce = new NonceGenerator(serviceEnvelope.Packet.From, serviceEnvelope.Packet.Id).Create();
            var decrypted = PacketEncryption.TransformPacket(encryptedData, nonce, Resources.DEFAULT_PSK);

            if (decrypted == null || decrypted.Length == 0)
            {
                Log.Warning("Decryption failed: empty data for packet ID: {Id}", serviceEnvelope.Packet.Id);
                return null;
            }

            var payload = Meshtastic.Protobufs.Data.Parser.ParseFrom(decrypted);
            if (payload.Portnum > PortNum.UnknownApp && payload.Payload.Length > 0)
                return payload;

            Log.Warning("Decrypted payload does not contain valid data.");
        }
        catch (Exception ex)
        {
            Log.Error("Error while decrypting packet. Exception: {Exception}, Stack Trace: {StackTrace}", ex.Message, ex.StackTrace);
        }

        return null;
    }
}
