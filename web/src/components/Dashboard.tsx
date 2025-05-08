import React, { useEffect, useState } from 'react';
import { mqttService } from '../services/mqtt';

interface Message {
  topic: string;
  payload: string;
  timestamp: number;
}

interface Client {
  id: string;
  connected: boolean;
  lastSeen: number;
}

export const Dashboard: React.FC = () => {
  const [messages, setMessages] = useState<Message[]>([]);
  const [clients, setClients] = useState<Client[]>([]);
  const [stats, setStats] = useState({
    messagesPerSecond: 0,
    totalConnections: 0,
  });
  const [isConnected, setIsConnected] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    try {
      mqttService.connect();
      setIsConnected(true);
      
      mqttService.onMessage((topic, message) => {
        setMessages(prev => [{
          topic,
          payload: message,
          timestamp: Date.now()
        }, ...prev].slice(0, 100)); // Keep last 100 messages
      });

      mqttService.onClientsUpdate((updatedClients) => {
        setClients(updatedClients);
      });

      mqttService.onStatsUpdate((updatedStats) => {
        setStats(updatedStats);
      });

      mqttService.onError((err) => {
        setError(err.message);
        setIsConnected(false);
      });

      return () => {
        mqttService.disconnect();
      };
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to connect to MQTT broker');
      setIsConnected(false);
    }
  }, []);

  return (
    <div className="min-h-screen bg-black text-white">
      <header className="border-b border-green-500 p-4">
        <h1 className="text-2xl font-mono text-green-500">
          DEFtastic MQTT Dashboard
        </h1>
      </header>
      
      {error && (
        <div className="bg-red-900 text-white p-4 m-4 rounded-lg">
          <p>Error: {error}</p>
        </div>
      )}

      {!isConnected && !error && (
        <div className="bg-yellow-900 text-white p-4 m-4 rounded-lg">
          <p>Connecting to MQTT broker...</p>
        </div>
      )}
      
      <main className="container mx-auto p-4">
        <div className="grid grid-cols-1 md:grid-cols-3 gap-4">
          {/* Statistics Panel */}
          <div className="bg-gray-900 p-4 rounded-lg">
            <h2 className="text-xl font-mono text-green-500 mb-4">Statistics</h2>
            <div className="space-y-2">
              <p>Messages/sec: {stats.messagesPerSecond}</p>
              <p>Total Connections: {stats.totalConnections}</p>
              <p>Connection Status: {isConnected ? 'Connected' : 'Disconnected'}</p>
            </div>
          </div>

          {/* Message Feed */}
          <div className="md:col-span-2 bg-gray-900 p-4 rounded-lg">
            <h2 className="text-xl font-mono text-green-500 mb-4">Message Feed</h2>
            <div className="space-y-2 max-h-[600px] overflow-y-auto">
              {messages.length === 0 ? (
                <p className="text-gray-500">No messages received yet...</p>
              ) : (
                messages.map((msg, index) => (
                  <div key={index} className="border border-green-500 p-2 rounded">
                    <p className="text-blue-400">{msg.topic}</p>
                    <p className="text-gray-300">{msg.payload}</p>
                    <p className="text-sm text-gray-500">
                      {new Date(msg.timestamp).toLocaleTimeString()}
                    </p>
                  </div>
                ))
              )}
            </div>
          </div>

          {/* Connected Clients */}
          <div className="md:col-span-3 bg-gray-900 p-4 rounded-lg">
            <h2 className="text-xl font-mono text-green-500 mb-4">Connected Clients</h2>
            <div className="grid grid-cols-1 md:grid-cols-3 gap-4">
              {clients.length === 0 ? (
                <p className="text-gray-500">No clients connected...</p>
              ) : (
                clients.map((client) => (
                  <div key={client.id} className="border border-green-500 p-2 rounded">
                    <p className="text-blue-400">{client.id}</p>
                    <p className="text-sm text-gray-500">
                      Last seen: {new Date(client.lastSeen).toLocaleTimeString()}
                    </p>
                  </div>
                ))
              )}
            </div>
          </div>
        </div>
      </main>
    </div>
  );
}; 