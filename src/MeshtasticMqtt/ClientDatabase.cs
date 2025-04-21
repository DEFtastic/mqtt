using Microsoft.Data.Sqlite;
using System.IO;

namespace MeshtasticMqtt
{
    public class ClientDatabase
    {
        private const string DatabaseFile = "broker.db";

        public void InitializeDatabase()
        {
            if (File.Exists(DatabaseFile))
                return;

            using var conn = new SqliteConnection($"Data Source={DatabaseFile}");
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText =
            @"
                CREATE TABLE Clients (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    ClientId TEXT NOT NULL,
                    ConnectedAt TEXT NOT NULL
                );
                CREATE TABLE Messages (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    ClientId TEXT NOT NULL,
                    Content TEXT NOT NULL,
                    Timestamp TEXT NOT NULL,
                    FOREIGN KEY (ClientId) REFERENCES Clients(ClientId)
                );
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
                INSERT INTO Messages (ClientId, Content, Timestamp)
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
                VALUES ($clientId, $connectedAt);
            ";
            cmd.Parameters.AddWithValue("$clientId", clientId);
            cmd.Parameters.AddWithValue("$connectedAt", DateTime.UtcNow.ToString("o"));
            cmd.ExecuteNonQuery();
        }
    }
}