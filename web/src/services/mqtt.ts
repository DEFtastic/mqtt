import mqtt from 'mqtt';

interface Client {
  id: string;
  connected: boolean;
  lastSeen: number;
}

class MQTTService {
  private client: mqtt.MqttClient | null = null;
  private messageHandlers: ((topic: string, message: string) => void)[] = [];
  private errorHandlers: ((error: Error) => void)[] = [];
  private clientHandlers: ((clients: Client[]) => void)[] = [];
  private statsHandlers: ((stats: { messagesPerSecond: number; totalConnections: number }) => void)[] = [];
  private connectedClients: Map<string, Client> = new Map();
  private messageCount = 0;
  private lastMessageTime = Date.now();

  connect() {
    try {
      const brokerUrl = import.meta.env.VITE_MQTT_BROKER_URL || 'wss://mqtt.deftastic.com:8883';
      console.log('Connecting to MQTT broker:', brokerUrl);
      
      this.client = mqtt.connect(brokerUrl, {
        clientId: `web-dashboard-${Math.random().toString(16).substr(2, 8)}`,
        clean: true,
        rejectUnauthorized: true,
        username: 'public',
        password: '31337'
      });

      this.client.on('connect', () => {
        console.log('Connected to MQTT broker');
        // Subscribe to all topics
        this.client?.subscribe('#');
        // Subscribe to client connection events
        this.client?.subscribe('$SYS/broker/clients/connected');
        this.client?.subscribe('$SYS/broker/clients/disconnected');
      });

      this.client.on('error', (error) => {
        console.error('MQTT Connection Error:', error);
        this.errorHandlers.forEach(handler => handler(error));
      });

      this.client.on('message', (topic, message) => {
        const now = Date.now();
        this.messageCount++;
        
        // Update stats every second
        if (now - this.lastMessageTime >= 1000) {
          const messagesPerSecond = this.messageCount;
          this.messageCount = 0;
          this.lastMessageTime = now;
          this.statsHandlers.forEach(handler => 
            handler({
              messagesPerSecond,
              totalConnections: this.connectedClients.size
            })
          );
        }

        // Handle client connection events
        if (topic === '$SYS/broker/clients/connected') {
          const clientId = message.toString();
          this.connectedClients.set(clientId, {
            id: clientId,
            connected: true,
            lastSeen: now
          });
          this.notifyClientUpdate();
        } else if (topic === '$SYS/broker/clients/disconnected') {
          const clientId = message.toString();
          this.connectedClients.delete(clientId);
          this.notifyClientUpdate();
        }

        // Notify message handlers
        this.messageHandlers.forEach(handler => 
          handler(topic, message.toString())
        );
      });
    } catch (error) {
      console.error('MQTT Connection Error:', error);
      this.errorHandlers.forEach(handler => 
        handler(error instanceof Error ? error : new Error(String(error)))
      );
    }
  }

  private notifyClientUpdate() {
    const clients = Array.from(this.connectedClients.values());
    this.clientHandlers.forEach(handler => handler(clients));
  }

  onMessage(handler: (topic: string, message: string) => void) {
    this.messageHandlers.push(handler);
  }

  onError(handler: (error: Error) => void) {
    this.errorHandlers.push(handler);
  }

  onClientsUpdate(handler: (clients: Client[]) => void) {
    this.clientHandlers.push(handler);
    // Send initial state
    handler(Array.from(this.connectedClients.values()));
  }

  onStatsUpdate(handler: (stats: { messagesPerSecond: number; totalConnections: number }) => void) {
    this.statsHandlers.push(handler);
    // Send initial state
    handler({
      messagesPerSecond: 0,
      totalConnections: this.connectedClients.size
    });
  }

  disconnect() {
    this.client?.end();
    this.messageHandlers = [];
    this.errorHandlers = [];
    this.clientHandlers = [];
    this.statsHandlers = [];
    this.connectedClients.clear();
  }
}

export const mqttService = new MQTTService(); 