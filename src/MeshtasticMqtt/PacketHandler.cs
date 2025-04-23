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

public class PacketHandler
{
    private readonly ClientDatabase _clientDatabase;

    public PacketHandler(ClientDatabase clientDatabase)
    {
        _clientDatabase = clientDatabase;
    }

    public async Task HandleInterceptingPublish(InterceptingPublishEventArgs args)
    {
        try
        {
            Log.Debug("Received payload (hex): {Payload}", BitConverter.ToString(args.ApplicationMessage.Payload));

            if (args.ApplicationMessage.Payload.Length == 0)
            {
                Log.Warning("Received empty payload on topic {@Topic} from {@ClientId}", args.ApplicationMessage.Topic, args.ClientId);
                args.ProcessPublish = false;
                return;
            }

            var serviceEnvelope = ServiceEnvelope.Parser.ParseFrom(args.ApplicationMessage.Payload);

            if (IsRoutingAck(serviceEnvelope))
            {
                Log.Debug("Confirmed routing ACK/NACK packet. Allowing through.");
                args.ProcessPublish = true;
                return;
            }

            if (!IsValidServiceEnvelope(serviceEnvelope))
            {
                Log.Warning("Service envelope or packet is malformed. Blocking packet on topic {@Topic} from {@ClientId}",
                    args.ApplicationMessage.Topic, args.ClientId);
                args.ProcessPublish = false;
                return;
            }

            var data = DecryptMeshPacket(serviceEnvelope);

            if (data?.Portnum == PortNum.TextMessageApp)
            {
                _clientDatabase.InsertMessage(args.ClientId, data.Payload.ToStringUtf8());
            }

            LogReceivedMessage(args.ApplicationMessage.Topic, args.ClientId, data);
            args.ProcessPublish = true;
        }
        catch (InvalidProtocolBufferException)
        {
            Log.Warning("Failed to decode presumed protobuf packet. Blocking");
            args.ProcessPublish = false;
        }
        catch (Exception ex)
        {
            Log.Error("Exception occurred while processing packet on {@Topic} from {@ClientId}: {@Exception}",
                args.ApplicationMessage.Topic, args.ClientId, ex.Message);
            args.ProcessPublish = false;
        }
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

    private static bool IsValidServiceEnvelope(ServiceEnvelope serviceEnvelope)
    {
        bool valid = true;

        if (string.IsNullOrWhiteSpace(serviceEnvelope.ChannelId))
        {
            Log.Warning("Missing or invalid ChannelId in ServiceEnvelope");
            valid = false;
        }
        if (string.IsNullOrWhiteSpace(serviceEnvelope.GatewayId))
        {
            Log.Warning("Missing or invalid GatewayId in ServiceEnvelope");
            valid = false;
        }
        if (serviceEnvelope.Packet == null || serviceEnvelope.Packet.Id < 1)
        {
            Log.Warning("Missing or invalid Packet in ServiceEnvelope");
            valid = false;
        }
        if (serviceEnvelope.Packet.Encrypted == null || serviceEnvelope.Packet.Encrypted.Length < 1)
        {
            Log.Warning("Missing or invalid Encrypted data in ServiceEnvelope");
            valid = false;
        }
        if (serviceEnvelope.Packet.Decoded != null)
        {
            Log.Warning("Unexpected Decoded data in ServiceEnvelope");
            valid = false;
        }

        return valid;
    }

    private static bool IsRoutingAck(ServiceEnvelope serviceEnvelope)
    {
        try
        {
            if (serviceEnvelope.Packet == null)
                return false;

            var nonce = new NonceGenerator(serviceEnvelope.Packet.From, serviceEnvelope.Packet.Id).Create();
            var decrypted = PacketEncryption.TransformPacket(serviceEnvelope.Packet.Encrypted.ToByteArray(), nonce, Resources.DEFAULT_PSK);
            var payload = Meshtastic.Protobufs.Data.Parser.ParseFrom(decrypted);

            return payload.Portnum == PortNum.RoutingApp &&
                    payload.Payload.Length > 0 &&
                    payload.Payload.Length <= 10;
        }
        catch
        {
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
        var nonce = new NonceGenerator(serviceEnvelope.Packet.From, serviceEnvelope.Packet.Id).Create();
        var decrypted = PacketEncryption.TransformPacket(serviceEnvelope.Packet.Encrypted.ToByteArray(), nonce, Resources.DEFAULT_PSK);

        // Log the decrypted data to ensure decryption is happening
        Log.Debug("Decrypted packet: {Decrypted}", BitConverter.ToString(decrypted));

        var payload = Meshtastic.Protobufs.Data.Parser.ParseFrom(decrypted);

        if (payload.Portnum > PortNum.UnknownApp && payload.Payload.Length > 0)
            return payload;

        return null;
    }
}

