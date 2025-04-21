using MQTTnet.Server;
using Meshtastic;
using Meshtastic.Crypto;
using Meshtastic.Protobufs;
using Google.Protobuf;
using Serilog;
using Microsoft.Data.Sqlite;

namespace Meshtastic.Mqtt
{
    public static class PacketHandler
    {
        public static async Task HandleInterceptingPublish(InterceptingPublishEventArgs args)
        {
            try
            {
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

        public static Task HandleInterceptingSubscription(InterceptingSubscriptionEventArgs args)
        {
            args.ProcessSubscription = true;
            return Task.CompletedTask;
        }

        public static Task HandleValidatingConnection(ValidatingConnectionEventArgs args)
        {
            args.ReasonCode = MqttConnectReasonCode.Success;
            Log.Information("New client connected: {@ClientId}", args.ClientId);

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

        private static bool IsValidServiceEnvelope(ServiceEnvelope serviceEnvelope)
        {
            return !(string.IsNullOrWhiteSpace(serviceEnvelope.ChannelId) ||
                     string.IsNullOrWhiteSpace(serviceEnvelope.GatewayId) ||
                     serviceEnvelope.Packet == null ||
                     serviceEnvelope.Packet.Id < 1 ||
                     serviceEnvelope.Packet.From < 1 ||
                     serviceEnvelope.Packet.Encrypted == null ||
                     serviceEnvelope.Packet.Encrypted.Length < 1 ||
                     serviceEnvelope.Packet.Decoded != null);
        }

        private static bool IsRoutingAck(ServiceEnvelope serviceEnvelope)
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

        private static void LogReceivedMessage(string topic, string clientId, Data? data)
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

        private static Data? DecryptMeshPacket(ServiceEnvelope serviceEnvelope)
        {
            var nonce = new NonceGenerator(serviceEnvelope.Packet.From, serviceEnvelope.Packet.Id).Create();
            var decrypted = PacketEncryption.TransformPacket(serviceEnvelope.Packet.Encrypted.ToByteArray(), nonce, Resources.DEFAULT_PSK);
            var payload = Data.Parser.ParseFrom(decrypted);

            if (payload.Portnum > PortNum.UnknownApp && payload.Payload.Length > 0)
                return payload;

            return null;
        }
    }
}
