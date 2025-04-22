namespace MeshtasticMqtt;

using Microsoft.Data.Sqlite;
using System.IO;

public class ClientDatabase
{
    private const string DatabaseFile = "data/broker.db";

    public ClientDatabase()
    {
        InitializeDatabase();
    }

    private void InitializeDatabase()
    {
        // Ensure 'data' directory exists
        Directory.CreateDirectory("data");

        using var conn = new SqliteConnection($"Data Source={DatabaseFile}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
        @"
            CREATE TABLE IF NOT EXISTS Clients (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                ClientId TEXT NOT NULL UNIQUE,
                ConnectedAt TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS Messages (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                ClientId TEXT NOT NULL,
                Content TEXT NOT NULL,
                Timestamp TEXT NOT NULL,
                FOREIGN KEY (ClientId) REFERENCES Clients(ClientId),
                UNIQUE (ClientId, Timestamp)
            );

            CREATE INDEX IF NOT EXISTS idx_messages_timestamp ON Messages(Timestamp);
        ";
        cmd.ExecuteNonQuery();
    }

    public void InsertMessage(string clientId, string content)
    {
        using var conn = new SqliteConnection($"Data Source={DatabaseFile}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
        @"
            INSERT OR IGNORE INTO Messages (ClientId, Content, Timestamp)
            VALUES ($clientId, $content, $timestamp);
        ";
        cmd.Parameters.AddWithValue("$clientId", clientId);
        cmd.Parameters.AddWithValue("$content", content);
        cmd.Parameters.AddWithValue("$timestamp", DateTime.UtcNow.ToString("o"));
        cmd.ExecuteNonQuery();
    }

    public void InsertClient(string clientId)
    {
        using var conn = new SqliteConnection($"Data Source={DatabaseFile}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
        @"
            INSERT INTO Clients (ClientId, ConnectedAt)
            VALUES ($clientId, $connectedAt)
            ON CONFLICT(ClientId) DO UPDATE SET ConnectedAt = $connectedAt;
        ";
        cmd.Parameters.AddWithValue("$clientId", clientId);
        cmd.Parameters.AddWithValue("$connectedAt", DateTime.UtcNow.ToString("o"));
        cmd.ExecuteNonQuery();
    }
}